using InventoryApp.Api.Data;
using InventoryApp.Api.Dtos;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InventoryApp.Api.Controllers
{
    /// <summary>
    /// Device enrollment and employee login. Three separate things are checked, in this order:
    /// the device (its public code plus the secret cookie), then the tenant it belongs to (which
    /// decides everything after), then the employee (badge, and PIN if the tenant requires one).
    /// Every refusal looks the same to the caller so the endpoint can't be used to find out which
    /// devices, badges or tenants exist.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        public const string DeviceCookieName = "inv_device";

        private const int MaxSaveAttempts = 5;

        // A hash to spend the same time on when there is no real one to check, so response time
        // doesn't tell an unknown badge from a known one.
        private static readonly string DummyPinHash = PinHasher.Hash("000000");

        private readonly InventoryDbContext _context;
        private readonly TenantContext _tenant;
        private readonly TimeProvider _time;
        private readonly AuthOptions _options;
        private readonly JwtTokenService _tokens;

        public AuthController(
            InventoryDbContext context,
            TenantContext tenant,
            TimeProvider time,
            IOptions<AuthOptions> options,
            JwtTokenService tokens)
        {
            _context = context;
            _tenant = tenant;
            _time = time;
            _options = options.Value;
            _tokens = tokens;
        }

        /// <summary>
        /// Turns a one-time enrollment code into the device credential, delivered as an HttpOnly
        /// cookie. The cookie is scoped to /api/auth and sent with login only.
        /// </summary>
        [HttpPost("enroll")]
        [AllowAnonymous]
        [EnableRateLimiting(EmployeeAuthentication.EnrollRateLimit)]
        public async Task<ActionResult<EnrollResponse>> Enroll(EnrollRequest request)
        {
            var code = DeviceCredentials.NormalizeEnrollmentCode(request.EnrollmentCode);

            if (code.Length == 0)
                return Denied();

            var codeHash = DeviceCredentials.Hash(code);

            // The tenant isn't known yet: the code itself is what finds the device.
            var device = await _context.Devices
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(d => d.EnrollmentCodeHash == codeHash);

            if (device == null || !await IsTenantActiveAsync(device.TenantId))
                return Denied();

            _tenant.Set(device.TenantId);

            var credential = device.CompleteEnrollment(_time.GetUtcNow());

            if (credential == null)
                return Denied();

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // The same code was redeemed at the same moment; only one request may win.
                return Denied();
            }

            Response.Cookies.Append(DeviceCookieName, credential, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/api/auth",
                MaxAge = TimeSpan.FromDays(400),
                IsEssential = true,
            });

            return new EnrollResponse(device.DeviceCode, device.Name);
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [EnableRateLimiting(EmployeeAuthentication.LoginRateLimit)]
        public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
        {
            var now = _time.GetUtcNow();

            // 1. The device: its public code names it, the cookie proves it.
            var deviceCode = request.DeviceCode.Trim().ToUpperInvariant();

            var device = await _context.Devices
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(d => d.DeviceCode == deviceCode);

            var credentialMatches = DeviceCredentials.Matches(
                Request.Cookies[DeviceCookieName], device?.CredentialHash);

            if (device == null || !credentialMatches || !device.IsActive)
                return Denied();

            // 2. The tenant comes from the device, never from the request.
            var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == device.TenantId);

            if (tenant == null || !tenant.IsActive)
                return Denied();

            _tenant.Set(device.TenantId);

            // 3. The employee.
            var badge = request.BadgeBarcode.Trim();

            var employee = await _context.Employees
                .Include(e => e.EmployeeRoles).ThenInclude(er => er.Role)
                .FirstOrDefaultAsync(e => e.BadgeBarcode == badge && e.IsActive);

            if (employee == null)
            {
                if (tenant.RequirePin)
                    PinHasher.Verify("000000", DummyPinHash);

                return Denied();
            }

            if (tenant.RequirePin)
            {
                // Telling a locked-out employee why is worth more than hiding it: the caller already
                // holds an enrolled device, and the lockout only ever follows wrong PINs.
                if (employee.IsLockedOut(now))
                    return LockedOut(employee, now);

                if (!employee.HasPin)
                {
                    // An admin has to set a PIN first.
                    PinHasher.Verify("000000", DummyPinHash);
                    return Denied();
                }

                if (string.IsNullOrEmpty(request.Pin))
                    return Denied("pin_required");

                if (!PinHasher.Verify(request.Pin, employee.PinHash))
                {
                    await RegisterFailedPinAsync(employee, now);
                    return Denied();
                }

                if (!await RegisterSuccessfulPinAsync(employee, now))
                    return LockedOut(employee, now);
            }

            // 4. One session per device: the new login ends the previous one.
            var session = new EmployeeSession(employee, device, now, _options.AbsoluteLifetime);

            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();

                await _context.EmployeeSessions
                    .Where(s => s.DeviceId == device.Id && s.RevokedAt == null)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(s => s.RevokedAt, now)
                        .SetProperty(s => s.RevokedReason, "replaced"));

                _context.EmployeeSessions.Add(session);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch (DbUpdateException)
            {
                // Two logins on the same device at the same moment; the unique index let one through.
                return Conflict(new ProblemDetails { Status = 409, Title = "Another login is in progress on this device. Try again." });
            }

            return new LoginResponse(
                _tokens.Create(session),
                session.ExpiresAt,
                EmployeeDto.FromEntity(employee),
                employee.MustChangePin);
        }

        [HttpPost("logout")]
        [Authorize(Policy = Policies.EmployeeSession)]
        public async Task<IActionResult> Logout()
        {
            var sessionId = User.GetSessionId();
            var now = _time.GetUtcNow();

            await _context.EmployeeSessions
                .Where(s => s.Id == sessionId && s.RevokedAt == null)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.RevokedAt, now)
                    .SetProperty(s => s.RevokedReason, "logout"));

            return NoContent();
        }

        /// <summary>
        /// Lets an employee choose their own PIN. Allowed while an admin-set PIN is still pending
        /// (that is the point of it). The current PIN is checked and counted like a login attempt.
        /// </summary>
        [HttpPost("change-pin")]
        [Authorize(Policy = Policies.EmployeeSession)]
        [EnableRateLimiting(EmployeeAuthentication.LoginRateLimit)]
        public async Task<IActionResult> ChangePin(ChangePinRequest request)
        {
            var now = _time.GetUtcNow();

            if (!PinHasher.IsValidFormat(request.NewPin))
                return BadRequest(new ProblemDetails { Status = 400, Title = $"A PIN must consist of exactly {PinHasher.PinLength} digits." });

            var employee = await _context.Employees.FirstAsync(e => e.Id == User.GetEmployeeId());

            if (employee.HasPin)
            {
                if (employee.IsLockedOut(now))
                    return LockedOut(employee, now);

                if (!PinHasher.Verify(request.CurrentPin, employee.PinHash))
                {
                    await RegisterFailedPinAsync(employee, now);
                    return BadRequest(new ProblemDetails { Status = 400, Title = "The current PIN is wrong." });
                }

                if (request.CurrentPin == request.NewPin)
                    return BadRequest(new ProblemDetails { Status = 400, Title = "The new PIN must differ from the current one." });
            }

            employee.SetPin(PinHasher.Hash(request.NewPin), mustChangePin: false);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new ProblemDetails { Status = 409, Title = "The employee was changed at the same moment. Try again." });
            }

            // A new PIN ends every other session of this employee (another device, a lost token).
            var currentSession = User.GetSessionId();
            await _context.EmployeeSessions
                .Where(s => s.EmployeeId == employee.Id && s.Id != currentSession && s.RevokedAt == null)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.RevokedAt, now)
                    .SetProperty(s => s.RevokedReason, "pin changed"));

            return NoContent();
        }

        // ---- PIN accounting ----------------------------------------------------------
        //
        // Parallel guesses must all be counted, or they would get past the lockout. Employee has a row
        // version: a conflicting update is reloaded and applied again on top of the newer state.

        private async Task RegisterFailedPinAsync(Employee employee, DateTimeOffset now)
        {
            for (var attempt = 0; attempt < MaxSaveAttempts; attempt++)
            {
                employee.RegisterFailedPin(now);

                try
                {
                    await _context.SaveChangesAsync();
                    return;
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    await ex.Entries.Single().ReloadAsync();
                }
            }
        }

        /// <summary>False if the employee became locked out while the PIN was being checked.</summary>
        private async Task<bool> RegisterSuccessfulPinAsync(Employee employee, DateTimeOffset now)
        {
            for (var attempt = 0; attempt < MaxSaveAttempts; attempt++)
            {
                if (employee.IsLockedOut(now))
                    return false;

                employee.RegisterSuccessfulPin();

                try
                {
                    await _context.SaveChangesAsync();
                    return true;
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    await ex.Entries.Single().ReloadAsync();
                }
            }

            return false;
        }

        // ---- Responses ---------------------------------------------------------------

        private async Task<bool> IsTenantActiveAsync(int tenantId) =>
            await _context.Tenants.AnyAsync(t => t.Id == tenantId && t.IsActive);

        private ObjectResult Denied(string? code = null) =>
            Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Authentication failed.",
                Type = code is null ? null : $"urn:inventoryapp:{code}",
            });

        private ObjectResult LockedOut(Employee employee, DateTimeOffset now)
        {
            var retryAfter = Math.Max(1, (int)Math.Ceiling(((employee.LockoutEnd ?? now) - now).TotalSeconds));
            Response.Headers.RetryAfter = retryAfter.ToString();

            return StatusCode(StatusCodes.Status423Locked, new ProblemDetails
            {
                Status = StatusCodes.Status423Locked,
                Title = "Too many wrong PIN attempts. Try again later.",
                Type = "urn:inventoryapp:locked_out",
            });
        }
    }
}
