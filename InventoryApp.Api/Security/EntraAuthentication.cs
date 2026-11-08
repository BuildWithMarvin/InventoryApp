using System.Globalization;
using System.Security.Claims;
using InventoryApp.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace InventoryApp.Api.Security
{
    /// <summary>
    /// Administrator authentication: Microsoft Entra ID access tokens for Platform Admins and Tenant
    /// Admins. Separate from the employee scheme on purpose: different issuer, key and audience, so
    /// neither kind of token is ever valid for the other.
    ///
    /// Entra only says who the person is. What they may do comes from this application's database:
    /// a Tenant Admin from the active <see cref="Models.TenantAdmin"/> row of <c>(tid, oid)</c> and the
    /// permissions of its role, looked up on every request; a Platform Admin from the app role
    /// <see cref="EntraOptions.PlatformAdminRole"/> of the validated token and the fixed
    /// <see cref="PlatformPermissions"/>. A tenant id from the client is never consulted.
    /// </summary>
    public static class EntraAuthentication
    {
        public const string Scheme = "Entra";

        private const string UnavailableKey = "inv.admin.unavailable";

        // Claims that only this application may add. A token must not be able to bring them along.
        private static readonly string[] ReservedClaims =
        [
            InventoryClaims.Permission,
            InventoryClaims.PlatformPermission,
            InventoryClaims.AdminKind,
            InventoryClaims.Tenant,
            InventoryClaims.MustChangePin,
        ];

        public static IServiceCollection AddEntraAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<EntraOptions>()
                .Bind(configuration.GetSection(EntraOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddAuthentication().AddJwtBearer(Scheme, _ => { });

            services.AddOptions<JwtBearerOptions>(Scheme)
                .Configure<IOptions<EntraOptions>, TimeProvider>((jwt, entraOptions, time) =>
                {
                    var entra = entraOptions.Value;

                    // The claims keep the names Entra gave them ("tid", "oid", "roles").
                    jwt.MapInboundClaims = false;

                    jwt.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            if (!entra.Enabled)
                            {
                                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                                    .CreateLogger(typeof(EntraAuthentication))
                                    .LogWarning("A bearer token reached the Entra scheme, but Entra is not configured (section \"{Section}\"). It was rejected.", EntraOptions.SectionName);

                                context.Fail("Entra is not configured.");
                            }

                            return Task.CompletedTask;
                        },
                        OnTokenValidated = ResolveAdministratorAsync,
                        OnChallenge = context =>
                        {
                            // The administrator could not be looked up. That is "try again later", not "sign in again".
                            if (context.HttpContext.Items.ContainsKey(UnavailableKey))
                            {
                                context.HandleResponse();
                                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                                context.Response.Headers.RetryAfter = "5";
                            }

                            return Task.CompletedTask;
                        },
                    };

                    if (!entra.Enabled)
                        return;

                    jwt.Authority = entra.GetAuthority();

                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = entra.GetAuthority(),
                        ValidateAudience = true,
                        ValidAudiences = entra.GetAudiences(),
                        RequireSignedTokens = true,
                        ValidateIssuerSigningKey = true,
                        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                        RequireExpirationTime = true,
                        ValidateLifetime = true,
                        ClockSkew = entra.ClockSkew,
                        // Same clock as the rest of the application, so tests can move time.
                        LifetimeValidator = (notBefore, expires, _, parameters) =>
                        {
                            var now = time.GetUtcNow().UtcDateTime;
                            return expires is not null
                                && expires.Value + parameters.ClockSkew > now
                                && (notBefore is null || notBefore.Value - parameters.ClockSkew <= now);
                        },
                        NameClaimType = "name",
                        RoleClaimType = InventoryClaims.EntraRoles,
                    };
                });

            return services;
        }

        /// <summary>The policies of both administrator kinds. Called from the authorization setup.</summary>
        internal static void AddAdminPolicies(AuthorizationOptions options)
        {
            options.AddPolicy(AdminPolicies.Session, policy => policy
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser()
                // Signed in to Entra is not enough: the identity must have been resolved as an administrator.
                .RequireClaim(InventoryClaims.AdminKind));

            foreach (var permission in Permissions.Of(PermissionScope.Admin))
            {
                options.AddPolicy(AdminPolicies.ForTenantAdmin(permission), policy => policy
                    .AddAuthenticationSchemes(Scheme)
                    .RequireAuthenticatedUser()
                    .RequireClaim(InventoryClaims.AdminKind, InventoryClaims.AdminKindTenant)
                    .RequireClaim(InventoryClaims.Permission, permission));
            }

            foreach (var permission in PlatformPermissions.All)
            {
                options.AddPolicy(AdminPolicies.ForPlatformAdmin(permission), policy => policy
                    .AddAuthenticationSchemes(Scheme)
                    .RequireAuthenticatedUser()
                    .RequireClaim(InventoryClaims.AdminKind, InventoryClaims.AdminKindPlatform)
                    .RequireClaim(InventoryClaims.PlatformPermission, permission));
            }
        }

        /// <summary>
        /// Runs after the signature, issuer, audience and lifetime are verified, on every request.
        /// A token that is not acceptable at all fails (401). An identity that is valid in Entra but
        /// unknown, deactivated or without a usable tenant stays authenticated but gets no administrator
        /// claims, so every administrator policy refuses it (403): the person is signed in, just not an
        /// administrator here. If the database can't be asked, nothing is granted (503).
        /// </summary>
        private static async Task ResolveAdministratorAsync(TokenValidatedContext context)
        {
            var services = context.HttpContext.RequestServices;
            var entra = services.GetRequiredService<IOptions<EntraOptions>>().Value;
            var tenantContext = services.GetRequiredService<TenantContext>();
            var principal = context.Principal!;
            var identity = (ClaimsIdentity)principal.Identity!;

            // From here on this request is an administrator request: no inventory data, ever.
            tenantContext.RestrictToAdministration();

            foreach (var claim in identity.Claims.Where(c => ReservedClaims.Contains(c.Type)).ToList())
                identity.RemoveClaim(claim);

            if (!TryGuid(principal, InventoryClaims.EntraTenant, out var directoryId) || directoryId != entra.GetDirectoryId())
            {
                context.Fail("The token is not from the expected directory.");
                return;
            }

            if (!TryGuid(principal, InventoryClaims.EntraObject, out var objectId))
            {
                context.Fail("The token has no valid object id.");
                return;
            }

            // Only tokens issued to a signed-in user carry a delegated scope. App-only tokens don't.
            var scopes = principal.FindAll(InventoryClaims.EntraScope)
                .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

            if (!scopes.Contains(entra.RequiredScope, StringComparer.Ordinal))
            {
                context.Fail("The token does not carry the required scope.");
                return;
            }

            var isPlatformAdmin = principal.FindAll(InventoryClaims.EntraRoles)
                .Any(c => string.Equals(c.Value, entra.PlatformAdminRole, StringComparison.Ordinal));

            var db = services.GetRequiredService<InventoryDbContext>();
            var cancellationToken = context.HttpContext.RequestAborted;

            try
            {
                // The tenant isn't known yet, so this lookup bypasses the tenant filters. It is keyed by the
                // identity from the validated token, and the role comes through the composite foreign key
                // of the same row, so it can only be a role of the mapped tenant.
                var assignment = await db.TenantAdmins
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(a => a.EntraTenantId == directoryId && a.EntraObjectId == objectId)
                    .Select(a => new
                    {
                        a.TenantId,
                        a.IsActive,
                        TenantIsActive = db.Tenants.Where(t => t.Id == a.TenantId).Select(t => t.IsActive).FirstOrDefault(),
                        Permissions = a.Role!.Permissions.Select(p => p.Permission).ToList(),
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                if (assignment is { IsActive: true, TenantIsActive: true })
                {
                    tenantContext.Set(assignment.TenantId);

                    identity.AddClaim(new Claim(InventoryClaims.AdminKind, InventoryClaims.AdminKindTenant));
                    identity.AddClaim(new Claim(InventoryClaims.Tenant, assignment.TenantId.ToString(CultureInfo.InvariantCulture)));

                    // Only what the catalog still knows, and only admin permissions, whatever the rows say.
                    foreach (var permission in assignment.Permissions
                        .Where(p => Permissions.IsDefined(p) && Permissions.ScopeOf(p) == PermissionScope.Admin)
                        .Distinct())
                    {
                        identity.AddClaim(new Claim(InventoryClaims.Permission, permission));
                    }
                }

                if (isPlatformAdmin)
                {
                    identity.AddClaim(new Claim(InventoryClaims.AdminKind, InventoryClaims.AdminKindPlatform));

                    foreach (var permission in PlatformPermissions.All)
                        identity.AddClaim(new Claim(InventoryClaims.PlatformPermission, permission));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Nothing has been granted at this point: the claims are only added after a successful lookup.
                services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(EntraAuthentication))
                    .LogError(exception, "The administrator could not be resolved. Access is refused.");

                context.HttpContext.Items[UnavailableKey] = true;
                context.Fail("The administrator could not be resolved.");
            }
        }

        private static bool TryGuid(ClaimsPrincipal principal, string claimType, out Guid value)
        {
            // Exactly one claim, in the plain 8-4-4-4-12 form.
            var values = principal.FindAll(claimType).Select(c => c.Value).ToList();

            if (values.Count == 1 && Guid.TryParseExact(values[0], "D", out value) && value != Guid.Empty)
                return true;

            value = Guid.Empty;
            return false;
        }
    }
}
