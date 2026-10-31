using System.Net;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;

// The token is only a pointer to a session; the database decides on every request.
[Trait("Category", "Integration")]
[Collection("Integration Tests")]
public class SessionTests : ApiTestBase
{
    private Employee _anna = null!;
    private Pda _pda = null!;

    public SessionTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        _anna = await CreateEmployeeAsync("Anna", "BADGE-1");
        _pda = await EnrollPdaAsync();
    }

    private async Task<HttpStatusCode> RequestAsync(string token) =>
        (await GetAsync("/api/products", token)).StatusCode;

    // ---- One session per device ------------------------------------------------------

    [Fact]
    public async Task ANewLoginOnTheSameDevice_EndsThePreviousSession()
    {
        await CreateEmployeeAsync("Ben", "BADGE-2");
        var annasToken = await LoginTokenAsync(_pda, "BADGE-1");
        Assert.Equal(HttpStatusCode.OK, await RequestAsync(annasToken));

        var bensToken = await LoginTokenAsync(_pda, "BADGE-2");

        Assert.Equal(HttpStatusCode.Unauthorized, await RequestAsync(annasToken));
        Assert.Equal(HttpStatusCode.OK, await RequestAsync(bensToken));

        await using var context = CreateContextFor(Tenant);
        Assert.Equal(1, await context.EmployeeSessions.CountAsync(s => s.RevokedAt == null));
        Assert.Equal("replaced", (await context.EmployeeSessions.SingleAsync(s => s.RevokedAt != null)).RevokedReason);
    }

    [Fact]
    public async Task DifferentDevices_KeepSeparateSessions()
    {
        var otherPda = await EnrollPdaAsync("PDA 2");
        await CreateEmployeeAsync("Ben", "BADGE-2");

        var first = await LoginTokenAsync(_pda, "BADGE-1");
        var second = await LoginTokenAsync(otherPda, "BADGE-2");

        Assert.Equal(HttpStatusCode.OK, await RequestAsync(first));
        Assert.Equal(HttpStatusCode.OK, await RequestAsync(second));
    }

    [Fact]
    public async Task TheDatabase_AllowsOnlyOneActiveSessionPerDevice()
    {
        await LoginTokenAsync(_pda, "BADGE-1");

        await using var context = CreateContextFor(Tenant);
        var device = await context.Devices.SingleAsync();
        context.EmployeeSessions.Add(new EmployeeSession(_anna, device, Time.GetUtcNow(), TimeSpan.FromHours(1)));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    // ---- Timeouts --------------------------------------------------------------------

    [Fact]
    public async Task ASession_EndsAfter30MinutesWithoutARequest()
    {
        var token = await LoginTokenAsync(_pda, "BADGE-1");

        Time.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, await RequestAsync(token));

        Time.Advance(TimeSpan.FromMinutes(29)); // 29 minutes after the last request: still fine
        Assert.Equal(HttpStatusCode.OK, await RequestAsync(token));

        Time.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(HttpStatusCode.Unauthorized, await RequestAsync(token));
    }

    [Fact]
    public async Task ASession_EndsAfter12Hours_NoMatterHowActive()
    {
        var token = await LoginTokenAsync(_pda, "BADGE-1");

        for (var elapsed = TimeSpan.Zero; elapsed < TimeSpan.FromHours(11) + TimeSpan.FromMinutes(40); elapsed += TimeSpan.FromMinutes(20))
        {
            Time.Advance(TimeSpan.FromMinutes(20));
            Assert.Equal(HttpStatusCode.OK, await RequestAsync(token));
        }

        Time.Advance(TimeSpan.FromMinutes(20)); // now 12 hours after login
        Assert.Equal(HttpStatusCode.Unauthorized, await RequestAsync(token));
    }

    [Fact]
    public async Task AnExpiredSession_CannotBeResurrectedByTheDeviceLoggingInAgain()
    {
        var oldToken = await LoginTokenAsync(_pda, "BADGE-1");
        Time.Advance(TimeSpan.FromHours(1));

        var newToken = await LoginTokenAsync(_pda, "BADGE-1");

        Assert.Equal(HttpStatusCode.Unauthorized, await RequestAsync(oldToken));
        Assert.Equal(HttpStatusCode.OK, await RequestAsync(newToken));
    }

    // ---- Revocation takes effect on the next request ---------------------------------

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        var token = await LoginTokenAsync(_pda, "BADGE-1");

        var logout = await SendAsync(HttpMethod.Post, "/api/auth/logout", token: token);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await RequestAsync(token));
        await using var context = CreateContextFor(Tenant);
        Assert.Equal("logout", (await context.EmployeeSessions.SingleAsync()).RevokedReason);
    }

    [Fact]
    public async Task RevokingTheSession_InTheDatabase_EndsItImmediately()
    {
        var token = await LoginTokenAsync(_pda, "BADGE-1");

        await using (var context = CreateContextFor(Tenant))
        {
            (await context.EmployeeSessions.SingleAsync()).Revoke(Time.GetUtcNow(), "admin");
            await context.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await RequestAsync(token));
    }

    [Fact]
    public async Task DeactivatingTheEmployee_EndsTheirSessionImmediately()
    {
        var token = await LoginTokenAsync(_pda, "BADGE-1");

        await using (var context = CreateContextFor(Tenant))
        {
            (await context.Employees.SingleAsync()).Deactivate();
            await context.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await RequestAsync(token));
    }

    [Fact]
    public async Task DeactivatingTheDevice_EndsItsSessionImmediately()
    {
        var token = await LoginTokenAsync(_pda, "BADGE-1");

        await using (var context = CreateContextFor(Tenant))
        {
            (await context.Devices.SingleAsync()).Deactivate();
            await context.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await RequestAsync(token));
    }

    [Fact]
    public async Task ReEnrollingTheDevice_EndsItsSessionImmediately()
    {
        var token = await LoginTokenAsync(_pda, "BADGE-1");

        await using (var context = CreateContextFor(Tenant))
        {
            (await context.Devices.SingleAsync()).IssueEnrollmentCode(Time.GetUtcNow());
            await context.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await RequestAsync(token));
    }

    [Fact]
    public async Task DeactivatingTheTenant_EndsAllItsSessionsImmediately()
    {
        var token = await LoginTokenAsync(_pda, "BADGE-1");

        await using (var context = CreateContextWithoutTenant())
        {
            (await context.Tenants.SingleAsync(t => t.Id == TenantId)).Deactivate();
            await context.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await RequestAsync(token));
    }

    [Fact]
    public async Task ChangingARole_TakesEffectOnTheNextRequest_WithTheSameToken()
    {
        var token = await LoginTokenAsync(_pda, "BADGE-1");
        Assert.Equal(HttpStatusCode.OK, await RequestAsync(token));

        await using (var context = CreateContextFor(Tenant))
        {
            var role = await context.Roles.Include(r => r.Permissions)
                .SingleAsync(r => r.Name == DefaultRoles.WarehouseEmployee);
            role.SetPermissions([Permissions.StockRead]); // no more Products.Read
            await context.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden, await RequestAsync(token));
    }

    // ---- Tokens that don't come from a login -----------------------------------------

    [Fact]
    public async Task ARequest_WithoutAToken_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync("/api/products", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await RequestAsync("not.a.token"));
    }

    [Fact]
    public async Task ATokenSignedWithAnotherKey_IsRejected()
    {
        var session = await LoginSessionAsync();

        var forged = ForgeToken(session, key: "another-signing-key-another-signing-key!!");

        Assert.Equal(HttpStatusCode.Unauthorized, await RequestAsync(forged));
    }

    [Fact]
    public async Task ATokenWithTheRightKeyButAnotherTenant_IsRejected()
    {
        var other = await CreateTenantAsync("Other tenant");
        var session = await LoginSessionAsync();

        // Even someone holding the signing key can't move a session to another tenant: the
        // tenant of the stored session is what counts, and it must match the token.
        var forged = ForgeToken(session, tenantId: other.Id);

        Assert.Equal(HttpStatusCode.Unauthorized, await RequestAsync(forged));
        Assert.Equal(HttpStatusCode.OK, await RequestAsync(ForgeToken(session)));
    }

    [Fact]
    public async Task ATokenForAnotherEmployeeOrDevice_IsRejected()
    {
        var ben = await CreateEmployeeAsync("Ben", "BADGE-2");
        var session = await LoginSessionAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await RequestAsync(ForgeToken(session, employeeId: ben.Id)));
        Assert.Equal(HttpStatusCode.Unauthorized, await RequestAsync(ForgeToken(session, deviceId: session.DeviceId + 1000)));
    }

    [Fact]
    public async Task ATokenForASessionThatDoesNotExist_IsRejected()
    {
        var session = await LoginSessionAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await RequestAsync(ForgeToken(session, sessionId: Guid.NewGuid())));
    }

    [Fact]
    public async Task ATokenWithoutTheSessionClaims_IsRejected()
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "InventoryApp",
            Audience = "InventoryApp.Employee",
            Claims = new Dictionary<string, object> { ["sub"] = _anna.Id.ToString() },
            Expires = Time.GetUtcNow().AddHours(1).UtcDateTime,
            SigningCredentials = Credentials(ApiFactory.SigningKey),
        };

        Assert.Equal(HttpStatusCode.Unauthorized, await RequestAsync(new JsonWebTokenHandler().CreateToken(descriptor)));
    }

    // ---- Helpers ---------------------------------------------------------------------

    private async Task<EmployeeSession> LoginSessionAsync()
    {
        await LoginTokenAsync(_pda, "BADGE-1");
        await using var context = CreateContextFor(Tenant);
        return await context.EmployeeSessions.AsNoTracking().SingleAsync(s => s.RevokedAt == null);
    }

    /// <summary>A token with valid signature and lifetime, built from a real session with some claims replaced.</summary>
    private string ForgeToken(
        EmployeeSession session,
        string key = ApiFactory.SigningKey,
        Guid? sessionId = null,
        int? tenantId = null,
        int? employeeId = null,
        int? deviceId = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "InventoryApp",
            Audience = "InventoryApp.Employee",
            Claims = new Dictionary<string, object>
            {
                ["sub"] = (employeeId ?? session.EmployeeId).ToString(),
                ["inv_tenant"] = (tenantId ?? session.TenantId).ToString(),
                ["sid"] = (sessionId ?? session.Id).ToString(),
                ["did"] = (deviceId ?? session.DeviceId).ToString(),
            },
            NotBefore = Time.GetUtcNow().UtcDateTime,
            Expires = Time.GetUtcNow().AddHours(1).UtcDateTime,
            SigningCredentials = Credentials(key),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static SigningCredentials Credentials(string key) =>
        new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
}
