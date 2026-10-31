using System.Net;
using System.Net.Http.Json;
using InventoryApp.Api.Dtos;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.EntityFrameworkCore;

[Trait("Category", "Integration")]
[Collection("Integration Tests")]
public class LoginTests : ApiTestBase
{
    public LoginTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Login_ReturnsATokenThatWorks_ForAnEnrolledDevice()
    {
        var anna = await CreateEmployeeAsync("Anna", "BADGE-1");
        var pda = await EnrollPdaAsync();

        var response = await LoginAsync(pda, "BADGE-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(Time.GetUtcNow() + TimeSpan.FromHours(12), login!.ExpiresAt);
        Assert.Equal(anna.Id, login.Employee.Id);
        Assert.Equal("Anna", login.Employee.Name);
        Assert.Equal(new[] { DefaultRoles.WarehouseEmployee }, login.Employee.Roles);
        Assert.False(login.MustChangePin);
        Assert.DoesNotContain("BADGE-1", body); // the badge is a credential

        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/products", login.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Login_TokenCarriesOnlyReferences_NotPermissions()
    {
        await CreateEmployeeAsync("Anna", "BADGE-1");
        var pda = await EnrollPdaAsync();

        var token = await LoginTokenAsync(pda, "BADGE-1");

        var payload = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(
            new[] { "aud", "did", "exp", "iat", "inv_tenant", "iss", "nbf", "sid", "sub" },
            payload.Claims.Select(c => c.Type).Distinct().Order());
    }

    [Fact]
    public async Task Login_AcceptsTheDeviceCodeInAnyCase()
    {
        await CreateEmployeeAsync("Anna", "BADGE-1");
        var pda = await EnrollPdaAsync();

        var response = await LoginAsync(pda, "BADGE-1", deviceCode: pda.DeviceCode.ToLowerInvariant());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_Refuses_EverythingThatIsNotAValidDeviceAndEmployee_WithTheSameAnswer()
    {
        await CreateEmployeeAsync("Anna", "BADGE-1");
        await CreateEmployeeAsync("Inactive", "BADGE-OFF");
        var pda = await EnrollPdaAsync("PDA 1");
        var otherPda = await EnrollPdaAsync("PDA 2");

        await using (var context = CreateContextFor(Tenant))
        {
            (await context.Employees.SingleAsync(e => e.BadgeBarcode == "BADGE-OFF")).Deactivate();
            await context.SaveChangesAsync();
        }

        var attempts = new Dictionary<string, HttpResponseMessage>
        {
            ["no cookie"] = await SendAsync(HttpMethod.Post, "/api/auth/login", new LoginRequest(pda.DeviceCode, "BADGE-1")),
            ["wrong cookie"] = await LoginAsync(pda, "BADGE-1", cookie: "not-the-credential"),
            ["cookie of another device"] = await LoginAsync(pda, "BADGE-1", cookie: otherPda.Cookie),
            ["unknown device code"] = await LoginAsync(pda, "BADGE-1", deviceCode: "NOSUCHCODE"),
            ["unknown badge"] = await LoginAsync(pda, "NO-SUCH-BADGE"),
            ["inactive employee"] = await LoginAsync(pda, "BADGE-OFF"),
        };

        foreach (var (name, response) in attempts)
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{name}: {response.StatusCode}");

        var bodies = await Task.WhenAll(attempts.Values.Select(r => r.Content.ReadAsStringAsync()));
        Assert.Single(bodies.Distinct());
    }

    [Fact]
    public async Task Login_Refuses_AnInactiveDevice_AndAnInactiveTenant()
    {
        await CreateEmployeeAsync("Anna", "BADGE-1");
        var pda = await EnrollPdaAsync();

        await using (var context = CreateContextFor(Tenant))
        {
            (await context.Devices.SingleAsync(d => d.Id == pda.DeviceId)).Deactivate();
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(pda, "BADGE-1")).StatusCode);

        await using (var context = CreateContextFor(Tenant))
        {
            (await context.Devices.SingleAsync(d => d.Id == pda.DeviceId)).Activate();
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(pda, "BADGE-1")).StatusCode);

        await using (var context = CreateContextWithoutTenant())
        {
            (await context.Tenants.SingleAsync(t => t.Id == TenantId)).Deactivate();
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(pda, "BADGE-1")).StatusCode);
    }

    [Fact]
    public async Task Login_TheTenantComesFromTheDevice_NotFromTheRequest()
    {
        var other = await CreateTenantAsync("Other tenant");
        await CreateEmployeeAsync("Ben", "BADGE-B", tenant: other);
        var pdaOfA = await EnrollPdaAsync();

        // A body that names the other tenant is just ignored ...
        var forged = await SendAsync(HttpMethod.Post, "/api/auth/login",
            new { pdaOfA.DeviceCode, BadgeBarcode = "BADGE-B", TenantId = other.Id }, cookie: pdaOfA.Cookie);

        // ... and the other tenant's employee can't log in on this tenant's device.
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
    }

    [Fact]
    public async Task Login_ABadgeCanBeUsedInTwoTenants_EachResolvesToItsOwnEmployee()
    {
        var other = await CreateTenantAsync("Other tenant");
        var annaOfA = await CreateEmployeeAsync("Anna", "SHARED");
        var annaOfB = await CreateEmployeeAsync("Bea", "SHARED", tenant: other);
        var pdaOfA = await EnrollPdaAsync("A", Tenant);
        var pdaOfB = await EnrollPdaAsync("B", other);

        var loginA = await (await LoginAsync(pdaOfA, "SHARED")).Content.ReadFromJsonAsync<LoginResponse>();
        var loginB = await (await LoginAsync(pdaOfB, "SHARED")).Content.ReadFromJsonAsync<LoginResponse>();

        Assert.Equal(annaOfA.Id, loginA!.Employee.Id);
        Assert.Equal(annaOfB.Id, loginB!.Employee.Id);
    }

    // ---- PIN -------------------------------------------------------------------------

    [Fact]
    public async Task Pin_IsRequired_WhenTheTenantRequiresOne()
    {
        await CreateEmployeeAsync("Anna", "BADGE-1", pin: "123456");
        await RequirePinAsync();
        var pda = await EnrollPdaAsync();

        var missing = await LoginAsync(pda, "BADGE-1");
        var wrong = await LoginAsync(pda, "BADGE-1", "654321");
        var malformed = await LoginAsync(pda, "BADGE-1", "12");
        var right = await LoginAsync(pda, "BADGE-1", "123456");

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Contains("pin_required", await missing.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
    }

    [Fact]
    public async Task Pin_AnEmployeeWithoutOne_CannotLogInWhereItIsRequired()
    {
        await CreateEmployeeAsync("Anna", "BADGE-1");
        await RequirePinAsync();
        var pda = await EnrollPdaAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(pda, "BADGE-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(pda, "BADGE-1", "123456")).StatusCode);
    }

    [Fact]
    public async Task Pin_IsIgnored_WhenTheTenantDoesNotRequireOne()
    {
        await CreateEmployeeAsync("Anna", "BADGE-1", pin: "123456");
        var pda = await EnrollPdaAsync();

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(pda, "BADGE-1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(pda, "BADGE-1", "000000")).StatusCode);
    }

    [Fact]
    public async Task Pin_LocksTheEmployeeOutAfterFiveWrongAttempts_EvenForTheRightPin()
    {
        await CreateEmployeeAsync("Anna", "BADGE-1", pin: "123456");
        await RequirePinAsync();
        var pda = await EnrollPdaAsync();

        for (var i = 0; i < Employee.MaxFailedPinAttempts; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(pda, "BADGE-1", "000000")).StatusCode);

        var locked = await LoginAsync(pda, "BADGE-1", "123456");

        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
        Assert.Equal("900", locked.Headers.RetryAfter!.ToString());
    }

    [Fact]
    public async Task Pin_LockoutEnds_AfterFifteenMinutes()
    {
        await CreateEmployeeAsync("Anna", "BADGE-1", pin: "123456");
        await RequirePinAsync();
        var pda = await EnrollPdaAsync();
        for (var i = 0; i < Employee.MaxFailedPinAttempts; i++)
            await LoginAsync(pda, "BADGE-1", "000000");

        Time.Advance(Employee.LockoutDuration - TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.Locked, (await LoginAsync(pda, "BADGE-1", "123456")).StatusCode);

        Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(pda, "BADGE-1", "123456")).StatusCode);
    }

    [Fact]
    public async Task Pin_ASuccessfulLogin_ResetsTheFailureCount()
    {
        await CreateEmployeeAsync("Anna", "BADGE-1", pin: "123456");
        await RequirePinAsync();
        var pda = await EnrollPdaAsync();

        for (var round = 0; round < 3; round++)
        {
            for (var i = 0; i < Employee.MaxFailedPinAttempts - 1; i++)
                await LoginAsync(pda, "BADGE-1", "000000");

            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(pda, "BADGE-1", "123456")).StatusCode);
        }
    }

    [Fact]
    public async Task Pin_TheLockoutIsPerEmployee()
    {
        await CreateEmployeeAsync("Anna", "BADGE-1", pin: "123456");
        await CreateEmployeeAsync("Ben", "BADGE-2", pin: "222222");
        await RequirePinAsync();
        var pda = await EnrollPdaAsync();
        for (var i = 0; i < Employee.MaxFailedPinAttempts; i++)
            await LoginAsync(pda, "BADGE-1", "000000");

        Assert.Equal(HttpStatusCode.Locked, (await LoginAsync(pda, "BADGE-1", "123456")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(pda, "BADGE-2", "222222")).StatusCode);
    }

    [Fact]
    public async Task Pin_ParallelWrongGuesses_AreAllCounted_AndLockTheEmployeeOut()
    {
        await CreateEmployeeAsync("Anna", "BADGE-1", pin: "123456");
        await RequirePinAsync();
        var pda = await EnrollPdaAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => LoginAsync(pda, "BADGE-1", "000000")));

        Assert.All(responses, r => Assert.True(
            r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Locked, r.StatusCode.ToString()));

        // However the requests interleaved, the lockout must have started.
        await using var context = CreateContextFor(Tenant);
        var anna = await context.Employees.SingleAsync();
        Assert.True(anna.IsLockedOut(Time.GetUtcNow()));
        Assert.Equal(HttpStatusCode.Locked, (await LoginAsync(pda, "BADGE-1", "123456")).StatusCode);
    }
}
