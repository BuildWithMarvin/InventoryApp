using System.Net;
using System.Net.Http.Json;
using System.Text;
using InventoryApp.Api.Dtos;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

// Administrators sign in with Microsoft Entra ID. These tests go through the real HTTP pipeline with the real Entra
// scheme; only the signing key is the tests' own (see ApiFactory). A token that is not acceptable is a 401. A valid
// Entra identity that is not an active administrator here is signed in but not authorized: 403.
[Trait("Category", "Integration")]
[Collection("Integration Tests")]
public class EntraAuthenticationTests : EntraTestBase
{
    private const string AdminMe = "/api/admin/me";
    private const string AdminTenant = "/api/admin/tenant";
    private const string PlatformTenants = "/api/platform/tenants";

    public EntraAuthenticationTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private string Token(
        Guid? objectId = null,
        Guid? directoryId = null,
        string? issuer = null,
        string? audience = null,
        TimeSpan? lifetime = null,
        IDictionary<string, object?>? claims = null) =>
        EntraTestTokens.Create(
            Time.GetUtcNow(), objectId, directoryId, issuer, audience, lifetime: lifetime, claims: claims);

    [Fact]
    public async Task ATenantAdministrator_IsRecognised_WithItsTenantAndPermissions()
    {
        var admin = await CreateTenantAdminAsync();

        var response = await GetAsync(AdminMe, TokenFor(admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = (await response.Content.ReadFromJsonAsync<AdminProfileDto>())!;
        Assert.Equal(new[] { "tenant" }, profile.Kinds);
        Assert.Equal(TenantId, profile.Tenant!.Id);
        Assert.Equal(Permissions.Of(PermissionScope.Admin).Order(), profile.Permissions);
        Assert.Empty(profile.PlatformPermissions);
    }

    [Fact]
    public async Task ATenantAdministrator_CanReadItsTenant()
    {
        var admin = await CreateTenantAdminAsync();

        var response = await GetAsync(AdminTenant, TokenFor(admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tenant = (await response.Content.ReadFromJsonAsync<TenantDto>())!;
        Assert.Equal(new TenantDto(TenantId, "Test tenant", true, false), tenant);
    }

    [Fact]
    public async Task APlatformAdministrator_NeedsNoMapping_ButHasNoTenantOfItsOwn()
    {
        await CreateTenantAsync("Second tenant");
        var token = PlatformAdminToken();

        var tenants = await GetAsync(PlatformTenants, token);
        var profile = await GetAsync(AdminMe, token);

        Assert.Equal(HttpStatusCode.OK, tenants.StatusCode);
        Assert.Equal(new[] { "Second tenant", "Test tenant" },
            (await tenants.Content.ReadFromJsonAsync<List<TenantSummaryDto>>())!.Select(t => t.Name));

        var me = (await profile.Content.ReadFromJsonAsync<AdminProfileDto>())!;
        Assert.Equal(new[] { "platform" }, me.Kinds);
        Assert.Null(me.Tenant);
        Assert.Empty(me.Permissions);
        Assert.Equal(PlatformPermissions.All.Order(), me.PlatformPermissions);

        // Tenant settings are a tenant administrator's business, not a platform one.
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(AdminTenant, token));
    }

    [Fact]
    public async Task ATenantAdministrator_IsNotAPlatformAdministrator()
    {
        var admin = await CreateTenantAdminAsync();

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(PlatformTenants, TokenFor(admin)));
    }

    [Fact]
    public async Task OnlyTheAppRoleMakesAPlatformAdministrator_NotAClaimAboutIt()
    {
        // Same words in other claims: not the app role.
        var token = Token(claims: new Dictionary<string, object?>
        {
            ["role"] = EntraTestTokens.PlatformAdminRole,
            ["groups"] = new[] { EntraTestTokens.PlatformAdminRole },
            ["inv_admin_kind"] = "platform",
            ["platform_permission"] = PlatformPermissions.TenantsRead,
        });

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(PlatformTenants, token));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(AdminMe, token));
    }

    [Fact]
    public async Task ARequest_WithoutAToken_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(AdminMe, null));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(AdminMe, "not.a.token"));
    }

    [Fact]
    public async Task ATokenFromAnotherIssuer_IsRejected()
    {
        var admin = await CreateTenantAdminAsync();
        var other = Guid.NewGuid();

        foreach (var issuer in new[]
        {
            $"https://login.microsoftonline.com/{other}/v2.0",         // another directory
            $"https://sts.windows.net/{EntraTestTokens.DirectoryId}/", // the v1 issuer of the right directory
            "https://evil.example/v2.0",
        })
        {
            var token = Token(admin.EntraObjectId, issuer: issuer);
            Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(AdminMe, token));
        }

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(AdminMe, TokenFor(admin)));
    }

    [Fact]
    public async Task ATokenForAnotherAudience_IsRejected()
    {
        var admin = await CreateTenantAdminAsync();

        var otherApi = Token(admin.EntraObjectId, audience: Guid.NewGuid().ToString());
        var graph = Token(admin.EntraObjectId, audience: "https://graph.microsoft.com");

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(AdminMe, otherApi));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(AdminMe, graph));
        // The application id URI form is accepted too.
        Assert.Equal(HttpStatusCode.OK,
            await StatusAsync(AdminMe, Token(admin.EntraObjectId, audience: $"api://{EntraTestTokens.ClientId}")));
    }

    [Fact]
    public async Task ATokenWithAForeignSignature_IsRejected()
    {
        var admin = await CreateTenantAdminAsync();

        var forged = EntraTestTokens.Create(Time.GetUtcNow(), admin.EntraObjectId, foreignSignature: true);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(AdminMe, forged));
    }

    [Fact]
    public async Task OnlyRs256IsAccepted_NoOtherAlgorithm_NoUnsignedToken()
    {
        var admin = await CreateTenantAdminAsync();
        var now = Time.GetUtcNow();

        var rs384 = EntraTestTokens.Create(now, admin.EntraObjectId, algorithm: SecurityAlgorithms.RsaSha384);
        var unsigned = EntraTestTokens.Create(now, admin.EntraObjectId, unsigned: true);

        // Algorithm confusion: an HMAC "signature" made with a symmetric key (with the right key id).
        var hmac = EntraTestTokens.Create(now, admin.EntraObjectId, credentials: new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))) { KeyId = EntraTestTokens.SigningKey.KeyId },
            SecurityAlgorithms.HmacSha256));

        Assert.Equal("none", new JsonWebToken(unsigned).Alg);
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(AdminMe, rs384));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(AdminMe, unsigned));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(AdminMe, hmac));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(AdminMe, TokenFor(admin)));
    }

    [Fact]
    public async Task AnExpiredToken_IsRejected_UsingTheApplicationClock()
    {
        var admin = await CreateTenantAdminAsync();
        var token = Token(admin.EntraObjectId, lifetime: TimeSpan.FromMinutes(5));

        Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(30));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(AdminMe, token));

        Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(AdminMe, token));
    }

    [Fact]
    public async Task ATokenWithoutExpiry_IsRejected()
    {
        var admin = await CreateTenantAdminAsync();

        var token = EntraTestTokens.Create(Time.GetUtcNow(), admin.EntraObjectId, omitExpiry: true);

        Assert.Equal(DateTime.MinValue, new JsonWebToken(token).ValidTo); // really without "exp"
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(AdminMe, token));
    }

    [Fact]
    public async Task ATokenThatIsNotValidYet_IsRejected()
    {
        var admin = await CreateTenantAdminAsync();

        var token = EntraTestTokens.Create(Time.GetUtcNow(), admin.EntraObjectId, notBefore: Time.GetUtcNow().AddMinutes(10));

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(AdminMe, token));
    }

    [Fact]
    public async Task ATokenFromAnotherDirectory_IsRejected_EvenWithTheRightIssuer()
    {
        var admin = await CreateTenantAdminAsync();

        // Issuer and signature are fine; the tid claim says another directory.
        var token = Token(admin.EntraObjectId, claims: new Dictionary<string, object?> { ["tid"] = Guid.NewGuid().ToString() });

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(AdminMe, token));
    }

    [Theory]
    [InlineData("tid", null)]
    [InlineData("tid", "")]
    [InlineData("tid", "not-a-guid")]
    [InlineData("tid", "00000000-0000-0000-0000-000000000000")]
    [InlineData("tid", "{11111111-1111-4111-8111-111111111111}")]
    [InlineData("oid", null)]
    [InlineData("oid", "")]
    [InlineData("oid", "not-a-guid")]
    [InlineData("oid", "00000000-0000-0000-0000-000000000000")]
    [InlineData("oid", "12345")]
    public async Task ATokenWithAMissingOrMalformedDirectoryOrObjectId_IsRejected(string claim, string? value)
    {
        // A mapping exists for the oid the token would otherwise carry, so only the claim decides.
        var admin = await CreateTenantAdminAsync();
        var token = Token(admin.EntraObjectId, claims: new Dictionary<string, object?> { [claim] = value });

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(AdminMe, token));
    }

    [Theory]
    [InlineData("tid")]
    [InlineData("oid")]
    public async Task ATokenWithTwoDirectoryOrObjectIds_IsRejected(string claim)
    {
        var admin = await CreateTenantAdminAsync();
        var values = claim == "tid"
            ? new[] { EntraTestTokens.DirectoryId.ToString(), Guid.NewGuid().ToString() }
            : new[] { admin.EntraObjectId.ToString(), Guid.NewGuid().ToString() };

        var token = Token(admin.EntraObjectId, claims: new Dictionary<string, object?> { [claim] = values });

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(AdminMe, token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("some_other_scope")]
    [InlineData("access_as_user_not")]
    public async Task ATokenWithoutTheRequiredScope_IsRejected(string? scope)
    {
        var admin = await CreateTenantAdminAsync();
        var token = Token(admin.EntraObjectId, claims: new Dictionary<string, object?> { ["scp"] = scope });

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(AdminMe, token));
    }

    [Fact]
    public async Task AnAppOnlyToken_IsRejected_EvenWithThePlatformAdminRole()
    {
        // Client-credentials tokens carry app roles but no delegated scope (and an app is not a person).
        var token = EntraTestTokens.Create(
            Time.GetUtcNow(), roles: [EntraTestTokens.PlatformAdminRole], claims: new Dictionary<string, object?> { ["scp"] = null });

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(PlatformTenants, token));
    }

    [Fact]
    public async Task TheRequiredScope_IsOneOfSeveral()
    {
        var admin = await CreateTenantAdminAsync();
        var token = Token(admin.EntraObjectId, claims: new Dictionary<string, object?> { ["scp"] = "openid access_as_user profile" });

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(AdminMe, token));
    }

    [Fact]
    public async Task AnUnknownIdentity_IsSignedInButNotAnAdministrator()
    {
        var token = Token();

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(AdminMe, token));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(AdminTenant, token));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(PlatformTenants, token));
    }

    [Fact]
    public async Task TheMappingKey_IsDirectoryAndObjectId_NotTheObjectIdAlone()
    {
        var objectId = Guid.NewGuid();
        // A mapping for the same oid in some other directory.
        await CreateTenantAdminAsync(objectId: objectId, entraTenantId: Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(AdminMe, Token(objectId)));
    }

    [Fact]
    public async Task AnInactiveMapping_IsRefused_AndRevivesWithTheSameToken()
    {
        var admin = await CreateTenantAdminAsync(active: false);
        var token = TokenFor(admin);

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(AdminTenant, token));

        await SetMappingActiveAsync(admin, true);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(AdminTenant, token));
    }

    [Fact]
    public async Task DeactivatingAMapping_TakesEffectOnTheNextRequest_WithTheSameToken()
    {
        var admin = await CreateTenantAdminAsync();
        var token = TokenFor(admin);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(AdminTenant, token));

        await SetMappingActiveAsync(admin, false);

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(AdminTenant, token));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(AdminMe, token));
    }

    [Fact]
    public async Task AnInactiveTenant_ClosesItsAdministrators()
    {
        var admin = await CreateTenantAdminAsync();
        var token = TokenFor(admin);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(AdminTenant, token));

        await SetTenantActiveAsync(Tenant, false);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(AdminTenant, token));

        await SetTenantActiveAsync(Tenant, true);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(AdminTenant, token));
    }

    [Fact]
    public async Task APlatformAdministrator_ThatIsAlsoMapped_StaysAPlatformAdministrator_WhenTheMappingEnds()
    {
        var objectId = Guid.NewGuid();
        var mapping = await CreateTenantAdminAsync(objectId: objectId);
        var token = PlatformAdminToken(objectId);

        var both = (await (await GetAsync(AdminMe, token)).Content.ReadFromJsonAsync<AdminProfileDto>())!;
        Assert.Equal(new[] { "platform", "tenant" }, both.Kinds);

        await SetMappingActiveAsync(mapping, false);

        var platformOnly = (await (await GetAsync(AdminMe, token)).Content.ReadFromJsonAsync<AdminProfileDto>())!;
        Assert.Equal(new[] { "platform" }, platformOnly.Kinds);
        Assert.Null(platformOnly.Tenant);
    }

    [Fact]
    public async Task ATenantAdministrator_OnlyEverSeesTheMappedTenant()
    {
        var other = await CreateTenantAsync("Other tenant");
        var adminA = await CreateTenantAdminAsync();
        var adminB = await CreateTenantAdminAsync(other);

        var a = await (await GetAsync(AdminTenant, TokenFor(adminA))).Content.ReadFromJsonAsync<TenantDto>();
        var b = await (await GetAsync(AdminTenant, TokenFor(adminB))).Content.ReadFromJsonAsync<TenantDto>();

        Assert.Equal(("Test tenant", TenantId), (a!.Name, a.Id));
        Assert.Equal(("Other tenant", other.Id), (b!.Name, b.Id));
    }

    [Fact]
    public async Task ATenantIdFromTheClient_IsIgnored()
    {
        var other = await CreateTenantAsync("Other tenant");
        var admin = await CreateTenantAdminAsync();
        var token = TokenFor(admin);

        foreach (var url in new[] { $"{AdminTenant}?tenantId={other.Id}", $"{AdminTenant}?TenantId={other.Id}" })
        {
            var tenant = await (await GetAsync(url, token)).Content.ReadFromJsonAsync<TenantDto>();
            Assert.Equal(TenantId, tenant!.Id);
        }

        var request = new HttpRequestMessage(HttpMethod.Get, AdminTenant);
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Add("X-Tenant-Id", other.Id.ToString());
        var viaHeader = await (await Client.SendAsync(request)).Content.ReadFromJsonAsync<TenantDto>();
        Assert.Equal(TenantId, viaHeader!.Id);
    }

    [Fact]
    public async Task ClaimsInTheToken_CannotMakeAnAdministrator_OrMoveOne()
    {
        var other = await CreateTenantAsync("Other tenant");
        var forged = new Dictionary<string, object?>
        {
            ["inv_admin_kind"] = "tenant",
            ["inv_tenant"] = other.Id.ToString(),
            ["permission"] = new[] { Permissions.TenantSettings, Permissions.EmployeesManage },
        };

        // Nobody here: the claims don't create an administrator.
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(AdminTenant, Token(claims: forged)));

        // A real administrator of the first tenant stays there and keeps only the permissions of its role.
        var limited = await CreateAdminRoleAsync("LimitedAdmin", Permissions.EmployeesManage);
        await using (var context = CreateContextFor(Tenant))
        {
            var role = context.Roles.Single(r => r.Id == limited.Id);
            var mapping = new TenantAdmin(role, EntraTestTokens.DirectoryId, Guid.NewGuid(), Time.GetUtcNow());
            context.TenantAdmins.Add(mapping);
            await context.SaveChangesAsync();

            var token = Token(mapping.EntraObjectId, claims: forged);
            var profile = (await (await GetAsync(AdminMe, token)).Content.ReadFromJsonAsync<AdminProfileDto>())!;

            Assert.Equal(TenantId, profile.Tenant!.Id);
            Assert.Equal(new[] { Permissions.EmployeesManage }, profile.Permissions);
            Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(AdminTenant, token));
        }
    }

    [Fact]
    public async Task AnEmployeeToken_CannotReachAdministratorEndpoints()
    {
        var pda = await EnrollPdaAsync();
        await CreateEmployeeAsync("Manager", "BADGE-M", DefaultRoles.WarehouseManager);
        var employeeToken = await LoginTokenAsync(pda, "BADGE-M");

        Assert.Equal(HttpStatusCode.OK, await StatusAsync("/api/products", employeeToken));

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(AdminMe, employeeToken));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(AdminTenant, employeeToken));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(PlatformTenants, employeeToken));
    }

    [Fact]
    public async Task AnEntraToken_CannotReachEmployeeEndpoints()
    {
        var tenantAdmin = TokenFor(await CreateTenantAdminAsync());
        var platformAdmin = PlatformAdminToken();

        foreach (var token in new[] { tenantAdmin, platformAdmin })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/products", token));
            Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/products/123", token));
            Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/products", token, HttpMethod.Post));
            Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/auth/logout", token, HttpMethod.Post));
            Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/auth/change-pin", token, HttpMethod.Post));
            Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/employees", token, HttpMethod.Post));
        }
    }

    [Fact]
    public async Task EachAdministratorPolicy_AcceptsOnlyTheEntraScheme_AndEachEmployeePolicyOnlyTheEmployeeScheme()
    {
        var provider = Factory.Services.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider>();

        foreach (var name in AdminPolicies.All)
        {
            var policy = await provider.GetPolicyAsync(name);
            Assert.True(policy is not null, $"Policy '{name}' is not registered.");
            Assert.Equal(new[] { EntraAuthentication.Scheme }, policy!.AuthenticationSchemes);
        }

        foreach (var name in Permissions.Of(PermissionScope.Inventory).Append(Policies.EmployeeSession))
        {
            var policy = await provider.GetPolicyAsync(name);
            Assert.Equal(new[] { EmployeeAuthentication.Scheme }, policy!.AuthenticationSchemes);
        }
    }
}

// With no Entra settings at all the scheme stays registered but rejects every token: administrator endpoints are
// closed, not open, and the application still starts.
[Trait("Category", "Integration")]
[Collection("Integration Tests")]
public class EntraNotConfiguredTests : EntraTestBase
{
    public EntraNotConfiguredTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>
    {
        ["Entra:TenantId"] = "",
        ["Entra:ClientId"] = "",
    };

    [Fact]
    public async Task EveryEntraToken_IsRejected()
    {
        var admin = await CreateTenantAdminAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/admin/me", TokenFor(admin)));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/platform/tenants", PlatformAdminToken()));
    }

    [Fact]
    public async Task EmployeeAuthentication_IsNotAffected()
    {
        var pda = await EnrollPdaAsync();
        await CreateEmployeeAsync("Anna", "BADGE-1");

        var token = await LoginTokenAsync(pda, "BADGE-1");

        Assert.Equal(HttpStatusCode.OK, await StatusAsync("/api/products", token));
    }
}
