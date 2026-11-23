using System.Linq.Expressions;
using InventoryApp.Api.Data;
using InventoryApp.Api.Dtos;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InventoryApp.Api.Controllers
{
    public abstract class TenantManagementController : ControllerBase
    {
        private readonly TimeProvider _time;
        private readonly AuthOptions _auth;

        protected TenantManagementController(InventoryDbContext context, TimeProvider time, IOptions<AuthOptions> auth)
        {
            Context = context;
            _time = time;
            _auth = auth.Value;
        }

        protected InventoryDbContext Context { get; }

        protected ObjectResult Refuse(int status, string title) =>
            StatusCode(status, new ProblemDetails { Status = status, Title = title });

        protected ObjectResult BadInput(string title) => Refuse(StatusCodes.Status400BadRequest, title);

        protected ObjectResult Clash(string title) => Refuse(StatusCodes.Status409Conflict, title);

        protected ObjectResult Missing(string what) => Refuse(StatusCodes.Status404NotFound, $"{what} not found.");

        private const string ChangedAtTheSameTime = "It was changed at the same moment. Reload and try again.";

        protected async Task<ActionResult<TenantDto>> SetRequirePinCore(bool required)
        {
            var tenant = await Context.Tenants.SingleAsync(t => t.Id == Context.RequireTenantId());

            // Requiring PINs while someone has none would lock that person out at the next login.
            if (required)
            {
                var withoutPin = await Context.Employees.CountAsync(e => e.IsActive && e.PinHash == null);

                if (withoutPin > 0)
                    return Clash($"{withoutPin} active employee(s) have no PIN yet. Set their PINs or deactivate them first.");
            }

            tenant.SetRequirePin(required);
            await Context.SaveChangesAsync();

            return Ok(TenantDto.FromEntity(tenant));
        }

        protected async Task<ActionResult<IReadOnlyList<DeviceDto>>> ListDevicesCore()
        {
            var devices = await Context.Devices.OrderBy(d => d.Name).ThenBy(d => d.Id).ToListAsync();

            return Ok(devices.Select(DeviceDto.FromEntity).ToList());
        }

        protected async Task<ActionResult<DeviceEnrollmentDto>> CreateDeviceCore(CreateDeviceRequest request)
        {
            Device device;

            try
            {
                device = new Device(Context.RequireTenantId(), request.Name);
            }
            catch (ArgumentException ex)
            {
                return BadInput(ex.Message);
            }

            var now = _time.GetUtcNow();
            var code = device.IssueEnrollmentCode(now);

            Context.Devices.Add(device);
            await Context.SaveChangesAsync();

            return Ok(new DeviceEnrollmentDto(DeviceDto.FromEntity(device), code, device.EnrollmentCodeExpiresAt!.Value));
        }

        protected async Task<ActionResult<DeviceEnrollmentDto>> IssueEnrollmentCodeCore(int deviceId)
        {
            var device = await Context.Devices.FirstOrDefaultAsync(d => d.Id == deviceId);

            if (device is null)
                return Missing("Device");

            if (!device.IsActive)
                return Clash("The device is deactivated. Activate it first.");

            var now = _time.GetUtcNow();
            var code = device.IssueEnrollmentCode(now);

            if (await TrySaveAsync() is { } conflict)
                return conflict;

            await RevokeSessionsAsync(s => s.DeviceId == deviceId, "device re-enrolled");

            return Ok(new DeviceEnrollmentDto(DeviceDto.FromEntity(device), code, device.EnrollmentCodeExpiresAt!.Value));
        }

        protected async Task<ActionResult<DeviceDto>> SetDeviceActiveCore(int deviceId, bool active)
        {
            var device = await Context.Devices.FirstOrDefaultAsync(d => d.Id == deviceId);

            if (device is null)
                return Missing("Device");

            if (active)
                device.Activate();
            else
                device.Deactivate();

            if (await TrySaveAsync() is { } conflict)
                return conflict;

            if (!active)
                await RevokeSessionsAsync(s => s.DeviceId == deviceId, "device deactivated");

            return Ok(DeviceDto.FromEntity(device));
        }

        private IQueryable<Employee> EmployeesWithRoles() =>
            Context.Employees.Include(e => e.EmployeeRoles).ThenInclude(er => er.Role);

        protected async Task<ActionResult<IReadOnlyList<EmployeeDto>>> ListEmployeesCore()
        {
            var employees = await EmployeesWithRoles().OrderBy(e => e.Name).ThenBy(e => e.Id).ToListAsync();

            return Ok(employees.Select(EmployeeDto.FromEntity).ToList());
        }

        protected async Task<ActionResult<EmployeeDto>> CreateEmployeeCore(CreateEmployeeRequest request)
        {
            var tenantId = Context.RequireTenantId();
            var roleName = request.Role ?? DefaultRoles.WarehouseEmployee;

            var role = await Context.Roles.FirstOrDefaultAsync(r => r.Name == roleName);

            if (role is null)
                return BadInput($"Role '{roleName}' not found.");

            if (request.Pin is not null && !PinHasher.IsValidFormat(request.Pin))
                return BadInput(PinFormatMessage);

            var tenant = await Context.Tenants.SingleAsync(t => t.Id == tenantId);

            if (tenant.RequirePin && request.Pin is null)
                return BadInput("This tenant requires PINs: give the employee an initial PIN.");

            Employee employee;

            try
            {
                employee = new Employee(tenantId, request.Name, request.BadgeBarcode);
                employee.AssignRole(role);
            }
            catch (ArgumentException ex)
            {
                return BadInput(ex.Message);
            }

            if (await Context.Employees.AnyAsync(e => e.BadgeBarcode == employee.BadgeBarcode))
                return Clash("This barcode has already been assigned to another employee.");

            if (request.Pin is not null)
                employee.SetPin(PinHasher.Hash(request.Pin), mustChangePin: true);

            Context.Employees.Add(employee);

            try
            {
                await Context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // The unique (tenant, badge) index, if two requests used the same badge at once.
                return Clash("This barcode has already been assigned to another employee.");
            }

            return Ok(EmployeeDto.FromEntity(employee));
        }

        protected async Task<ActionResult<EmployeeDto>> RenameEmployeeCore(int employeeId, NameRequest request)
        {
            var employee = await EmployeesWithRoles().FirstOrDefaultAsync(e => e.Id == employeeId);

            if (employee is null)
                return Missing("Employee");

            try
            {
                employee.UpdateName(request.Name);
            }
            catch (ArgumentException ex)
            {
                return BadInput(ex.Message);
            }

            if (employee.Name.Length > 100)
                return BadInput("Name cannot exceed 100 characters.");

            if (await TrySaveAsync() is { } conflict)
                return conflict;

            return Ok(EmployeeDto.FromEntity(employee));
        }

        protected async Task<ActionResult<EmployeeDto>> SetEmployeeRolesCore(int employeeId, SetEmployeeRolesRequest request)
        {
            var names = request.Roles.Select(n => n?.Trim() ?? string.Empty).Distinct().ToList();

            if (names.Count == 0 || names.Any(n => n.Length == 0))
                return BadInput("Give at least one role name.");

            var employee = await EmployeesWithRoles().FirstOrDefaultAsync(e => e.Id == employeeId);

            if (employee is null)
                return Missing("Employee");

            var roles = await Context.Roles.Where(r => names.Contains(r.Name)).ToListAsync();
            var unknown = names.Except(roles.Select(r => r.Name)).ToList();

            if (unknown.Count > 0)
                return BadInput($"Role(s) not found: {string.Join(", ", unknown)}.");

            var wanted = roles.Select(r => r.Id).ToHashSet();

            foreach (var roleId in employee.EmployeeRoles.Select(er => er.RoleId).Where(id => !wanted.Contains(id)).ToList())
                employee.RemoveRole(roleId);

            try
            {
                // AssignRole refuses admin roles (and the database does too) and skips roles already held.
                foreach (var role in roles)
                    employee.AssignRole(role);
            }
            catch (ArgumentException ex)
            {
                return BadInput(ex.Message);
            }

            if (await TrySaveAsync() is { } conflict)
                return conflict;

            return Ok(EmployeeDto.FromEntity(employee));
        }

        protected async Task<IActionResult> SetEmployeePinCore(int employeeId, SetEmployeePinRequest request)
        {
            if (!PinHasher.IsValidFormat(request.Pin))
                return BadInput(PinFormatMessage);

            var employee = await Context.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);

            if (employee is null)
                return Missing("Employee");

            employee.SetPin(PinHasher.Hash(request.Pin), mustChangePin: true);

            if (await TrySaveAsync() is { } conflict)
                return conflict;

            await RevokeSessionsAsync(s => s.EmployeeId == employeeId, "pin reset");

            return NoContent();
        }

        protected async Task<ActionResult<EmployeeDto>> SetEmployeeActiveCore(int employeeId, bool active)
        {
            var employee = await EmployeesWithRoles().FirstOrDefaultAsync(e => e.Id == employeeId);

            if (employee is null)
                return Missing("Employee");

            if (active)
            {
                var tenant = await Context.Tenants.SingleAsync(t => t.Id == Context.RequireTenantId());

                // Same rule as switching the requirement on: nobody active without a PIN while PINs are required.
                if (tenant.RequirePin && !employee.HasPin)
                    return Clash("This tenant requires PINs. Set a PIN for the employee before activating.");

                employee.Activate();
            }
            else
            {
                employee.Deactivate();
            }

            if (await TrySaveAsync() is { } conflict)
                return conflict;

            if (!active)
                await RevokeSessionsAsync(s => s.EmployeeId == employeeId, "employee deactivated");

            return Ok(EmployeeDto.FromEntity(employee));
        }

        protected async Task<ActionResult<IReadOnlyList<SessionDto>>> ListSessionsCore(bool includeInactive)
        {
            var now = _time.GetUtcNow();
            var idleCutoff = now - _auth.IdleTimeout;

            var query = Context.EmployeeSessions.AsQueryable();

            if (!includeInactive)
                query = query.Where(s => s.RevokedAt == null && s.ExpiresAt > now && s.LastSeenAt > idleCutoff);

            var sessions = await query
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new SessionDto(
                    s.Id,
                    s.EmployeeId,
                    s.Employee!.Name,
                    s.DeviceId,
                    s.Device!.Name,
                    s.CreatedAt,
                    s.LastSeenAt,
                    s.ExpiresAt,
                    s.RevokedAt,
                    s.RevokedReason,
                    s.RevokedAt == null && s.ExpiresAt > now && s.LastSeenAt > idleCutoff))
                .Take(500)
                .ToListAsync();

            return Ok(sessions);
        }

        protected async Task<IActionResult> RevokeSessionCore(Guid sessionId)
        {
            if (!await Context.EmployeeSessions.AnyAsync(s => s.Id == sessionId))
                return Missing("Session");

            await RevokeSessionsAsync(s => s.Id == sessionId, "revoked by administrator");

            return NoContent();
        }

        private const string PinFormatMessage = "A PIN must consist of exactly 6 digits.";

        private async Task RevokeSessionsAsync(Expression<Func<EmployeeSession, bool>> which, string reason)
        {
            var now = _time.GetUtcNow();

            await Context.EmployeeSessions
                .Where(which)
                .Where(s => s.RevokedAt == null)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.RevokedAt, now)
                    .SetProperty(s => s.RevokedReason, reason));
        }

        protected async Task<ObjectResult?> TrySaveAsync()
        {
            try
            {
                await Context.SaveChangesAsync();
                return null;
            }
            catch (DbUpdateConcurrencyException)
            {
                return Clash(ChangedAtTheSameTime);
            }
        }
    }
}
