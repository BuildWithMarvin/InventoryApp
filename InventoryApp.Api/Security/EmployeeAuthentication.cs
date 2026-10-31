using System.Security.Claims;
using System.Threading.RateLimiting;
using InventoryApp.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace InventoryApp.Api.Security
{
    /// <summary>
    /// Employee (badge) authentication: a signed JWT that is checked against the database on every
    /// request, so revoking a session, deactivating an employee, a device or a tenant, or changing a
    /// role takes effect on the very next request.
    /// </summary>
    public static class EmployeeAuthentication
    {
        public const string Scheme = "Employee";

        public const string LoginRateLimit = "login";
        public const string EnrollRateLimit = "enroll";

        // LastSeenAt is only written when it is older than this, so a burst of requests is one write.
        private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

        public static IServiceCollection AddInventorySecurity(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<AuthOptions>()
                .Bind(configuration.GetSection(AuthOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<JwtTokenService>();

            AddAuthentication(services);
            AddAuthorization(services);
            AddRateLimiting(services);

            // Azure App Service terminates TLS in front of the app and passes the client in
            // X-Forwarded-*. Without this every client would look like the proxy to the rate limiter.
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });

            return services;
        }

        private static void AddAuthentication(IServiceCollection services)
        {
            services.AddAuthentication().AddJwtBearer(Scheme, _ => { });

            services.AddOptions<JwtBearerOptions>(Scheme)
                .Configure<IOptions<AuthOptions>, TimeProvider>((jwt, authOptions, time) =>
                {
                    var auth = authOptions.Value;

                    jwt.MapInboundClaims = false;
                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = auth.Issuer,
                        ValidateAudience = true,
                        ValidAudience = auth.Audience,
                        RequireSignedTokens = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = JwtTokenService.SigningKey(auth),
                        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                        RequireExpirationTime = true,
                        ValidateLifetime = true,
                        // The server issues and checks its own tokens, and the session row is the
                        // authority on expiry, so no clock skew is tolerated. The injected clock
                        // lets tests move time.
                        LifetimeValidator = (notBefore, expires, _, _) =>
                        {
                            var now = time.GetUtcNow().UtcDateTime;
                            return expires is not null && expires > now && (notBefore is null || notBefore <= now);
                        },
                        NameClaimType = InventoryClaims.Subject,
                    };

                    jwt.Events = new JwtBearerEvents { OnTokenValidated = ValidateSessionAsync };
                });
        }

        /// <summary>
        /// Runs after the signature is verified. The token is only a pointer to a session: everything
        /// that decides whether and how it may be used comes from the database. This is also where the
        /// request's tenant is set, from the stored session and never from client input.
        /// </summary>
        private static async Task ValidateSessionAsync(TokenValidatedContext context)
        {
            var services = context.HttpContext.RequestServices;
            var db = services.GetRequiredService<InventoryDbContext>();
            var tenantContext = services.GetRequiredService<TenantContext>();
            var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
            var auth = services.GetRequiredService<IOptions<AuthOptions>>().Value;
            var cancellationToken = context.HttpContext.RequestAborted;
            var principal = context.Principal!;

            if (!Guid.TryParse(principal.FindFirstValue(InventoryClaims.Session), out var sessionId)
                || !int.TryParse(principal.FindFirstValue(InventoryClaims.Subject), out var employeeId)
                || !int.TryParse(principal.FindFirstValue(InventoryClaims.Tenant), out var tenantId)
                || !int.TryParse(principal.FindFirstValue(InventoryClaims.Device), out var deviceId))
            {
                context.Fail("The token is malformed.");
                return;
            }

            // The tenant isn't known yet, so this one lookup has to bypass the tenant filter. It is
            // keyed by the session id and its result is checked against the token below.
            var session = await db.EmployeeSessions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s => s.Id == sessionId)
                .Select(s => new
                {
                    s.TenantId,
                    s.EmployeeId,
                    s.DeviceId,
                    s.LastSeenAt,
                    s.ExpiresAt,
                    s.RevokedAt,
                    EmployeeIsActive = s.Employee!.IsActive,
                    s.Employee.MustChangePin,
                    DeviceIsActive = s.Device!.IsActive,
                    DeviceIsEnrolled = s.Device.CredentialHash != null,
                    TenantIsActive = db.Tenants.Where(t => t.Id == s.TenantId).Select(t => t.IsActive).FirstOrDefault(),
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (session is null
                || session.TenantId != tenantId
                || session.EmployeeId != employeeId
                || session.DeviceId != deviceId
                || session.RevokedAt is not null
                || now >= session.ExpiresAt
                || now - session.LastSeenAt >= auth.IdleTimeout
                || !session.EmployeeIsActive
                || !session.DeviceIsActive
                || !session.DeviceIsEnrolled
                || !session.TenantIsActive)
            {
                context.Fail("The session is not valid.");
                return;
            }

            tenantContext.Set(session.TenantId);

            // Permissions are read now, not taken from the token, so a role change applies immediately.
            var permissions = await db.EmployeeRoles
                .Where(er => er.EmployeeId == session.EmployeeId)
                .SelectMany(er => er.Role!.Permissions)
                .Select(p => p.Permission)
                .Distinct()
                .ToListAsync(cancellationToken);

            var identity = (ClaimsIdentity)principal.Identity!;
            identity.AddClaims(permissions.Select(p => new Claim(InventoryClaims.Permission, p)));

            if (session.MustChangePin)
                identity.AddClaim(new Claim(InventoryClaims.MustChangePin, "true"));

            if (now - session.LastSeenAt >= TouchInterval)
            {
                await db.EmployeeSessions
                    .Where(s => s.Id == sessionId && s.RevokedAt == null && s.LastSeenAt < now)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.LastSeenAt, now), cancellationToken);
            }
        }

        private static void AddAuthorization(IServiceCollection services)
        {
            services.AddAuthorization(options =>
            {
                // One policy per inventory permission, named after it: [Authorize(Policy = Permissions.ProductsRead)].
                // Admin permissions have no employee policy on purpose: they are never usable with a badge login.
                foreach (var permission in Permissions.Of(PermissionScope.Inventory))
                {
                    options.AddPolicy(permission, policy => policy
                        .AddAuthenticationSchemes(Scheme)
                        .RequireAuthenticatedUser()
                        // An admin-set PIN must be replaced before anything else can be done.
                        .RequireAssertion(ctx => !ctx.User.HasClaim(InventoryClaims.MustChangePin, "true"))
                        .RequireClaim(InventoryClaims.Permission, permission));
                }

                options.AddPolicy(Policies.EmployeeSession, policy => policy
                    .AddAuthenticationSchemes(Scheme)
                    .RequireAuthenticatedUser());

                options.AddPolicy(Policies.AdminOnly, policy => policy
                    .AddAuthenticationSchemes(Scheme)
                    .RequireAssertion(_ => false));

                // Default deny: an endpoint nobody marked still requires a logged-in employee.
                options.FallbackPolicy = new AuthorizationPolicyBuilder(Scheme)
                    .RequireAuthenticatedUser()
                    .Build();
            });
        }

        private static void AddRateLimiting(IServiceCollection services)
        {
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.AddPolicy(LoginRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => Window(context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.LoginRequestsPerMinute)));

                options.AddPolicy(EnrollRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => Window(context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.EnrollRequestsPerMinute)));
            });

            // The key must not be something the caller controls (a header, a cookie), or rotating it
            // would reset the limit. The IP is the real client address after ForwardedHeaders.
            static string ClientKey(HttpContext context) =>
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            static FixedWindowRateLimiterOptions Window(int permitLimit) => new()
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            };
        }
    }
}
