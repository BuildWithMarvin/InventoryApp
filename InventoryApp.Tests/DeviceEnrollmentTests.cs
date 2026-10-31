using System.Net;
using System.Net.Http.Json;
using InventoryApp.Api.Dtos;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.EntityFrameworkCore;

[Trait("Category", "Integration")]
[Collection("Integration Tests")]
public class DeviceEnrollmentTests : ApiTestBase
{
    public DeviceEnrollmentTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Enroll_SetsAnHttpOnlyCookie_AndNeverPutsTheCredentialInTheBody()
    {
        var (device, code) = await CreateDeviceAsync();

        var response = await Client.PostAsJsonAsync("/api/auth/enroll", new EnrollRequest(code));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var enrolled = await response.Content.ReadFromJsonAsync<EnrollResponse>();
        Assert.Equal(device.DeviceCode, enrolled!.DeviceCode);
        Assert.Equal("PDA 1", enrolled.DeviceName);

        var cookie = SetCookieHeader(response)!;
        var cookieValue = CookieValue(response)!;
        Assert.Equal(43, cookieValue.Length);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);

        // Client-side code can only ever see the public code, never the secret.
        Assert.DoesNotContain(cookieValue, body);
        Assert.DoesNotContain(code, body);
    }

    [Fact]
    public async Task Enroll_StoresOnlyHashes_AndBurnsTheCode()
    {
        var (device, code) = await CreateDeviceAsync();

        var response = await Client.PostAsJsonAsync("/api/auth/enroll", new EnrollRequest(code));
        var cookieValue = CookieValue(response)!;

        await using var context = CreateContextFor(Tenant);
        var stored = await context.Devices.SingleAsync(d => d.Id == device.Id);
        Assert.Equal(DeviceCredentials.Hash(cookieValue), stored.CredentialHash);
        Assert.NotEqual(cookieValue, stored.CredentialHash);
        Assert.Null(stored.EnrollmentCodeHash);
        Assert.Equal(Time.GetUtcNow(), stored.EnrolledAt);
    }

    [Fact]
    public async Task Enroll_AcceptsTheCodeAsTyped()
    {
        var (_, code) = await CreateDeviceAsync();
        var typed = $"{code[..5]}-{code[5..10]} {code[10..]}".ToLowerInvariant();

        var response = await Client.PostAsJsonAsync("/api/auth/enroll", new EnrollRequest(typed));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Enroll_CodeWorksOnlyOnce()
    {
        var (_, code) = await CreateDeviceAsync();

        var first = await Client.PostAsJsonAsync("/api/auth/enroll", new EnrollRequest(code));
        var second = await Client.PostAsJsonAsync("/api/auth/enroll", new EnrollRequest(code));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Null(SetCookieHeader(second));
    }

    [Fact]
    public async Task Enroll_CodeExpiresAfter24Hours()
    {
        var (_, code) = await CreateDeviceAsync();
        Time.Advance(Device.EnrollmentCodeLifetime);

        var response = await Client.PostAsJsonAsync("/api/auth/enroll", new EnrollRequest(code));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Enroll_RefusesUnknownCodes_AndAnswersTheSameWay()
    {
        var (_, code) = await CreateDeviceAsync();
        var expired = await Client.PostAsJsonAsync("/api/auth/enroll", new EnrollRequest("NOSUCHCODE"));
        Time.Advance(TimeSpan.FromDays(2));
        var late = await Client.PostAsJsonAsync("/api/auth/enroll", new EnrollRequest(code));

        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, late.StatusCode);
        Assert.Equal(await expired.Content.ReadAsStringAsync(), await late.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Enroll_RejectsABlankCode()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/enroll", new EnrollRequest(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Enroll_Refuses_AnInactiveDevice()
    {
        var (device, code) = await CreateDeviceAsync();
        await using (var context = CreateContextFor(Tenant))
        {
            (await context.Devices.SingleAsync(d => d.Id == device.Id)).Deactivate();
            await context.SaveChangesAsync();
        }

        var response = await Client.PostAsJsonAsync("/api/auth/enroll", new EnrollRequest(code));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Enroll_Refuses_AnInactiveTenant()
    {
        var (_, code) = await CreateDeviceAsync();
        await SetTenantActiveAsync(Tenant, active: false);

        var response = await Client.PostAsJsonAsync("/api/auth/enroll", new EnrollRequest(code));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Enroll_ARedeemedCodeOfAnotherTenantsDevice_StaysWithThatTenant()
    {
        var other = await CreateTenantAsync("Other tenant");
        var (otherDevice, otherCode) = await CreateDeviceAsync("Other PDA", other);

        var response = await Client.PostAsJsonAsync("/api/auth/enroll", new EnrollRequest(otherCode));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var context = CreateContextFor(other);
        Assert.True((await context.Devices.SingleAsync(d => d.Id == otherDevice.Id)).IsEnrolled);
    }

    [Fact]
    public async Task ReEnrolling_CutsOffTheOldCookieAtOnce()
    {
        await CreateEmployeeAsync("Anna", "BADGE-1");
        var pda = await EnrollPdaAsync();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(pda, "BADGE-1")).StatusCode);

        // An admin issues a new code (lost or replaced device).
        string newCode;
        await using (var context = CreateContextFor(Tenant))
        {
            var device = await context.Devices.SingleAsync(d => d.Id == pda.DeviceId);
            newCode = device.IssueEnrollmentCode(Time.GetUtcNow());
            await context.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(pda, "BADGE-1")).StatusCode);

        var enrolled = await Client.PostAsJsonAsync("/api/auth/enroll", new EnrollRequest(newCode));
        var newPda = pda with { Cookie = CookieValue(enrolled)! };
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(newPda, "BADGE-1")).StatusCode);
        Assert.NotEqual(pda.Cookie, newPda.Cookie);
    }

    private async Task SetTenantActiveAsync(Tenant tenant, bool active)
    {
        await using var context = CreateContextWithoutTenant();
        var row = await context.Tenants.SingleAsync(t => t.Id == tenant.Id);
        if (active) row.Activate(); else row.Deactivate();
        await context.SaveChangesAsync();
    }
}
