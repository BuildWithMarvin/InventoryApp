using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using InventoryApp.Api.Dtos;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

/// <summary>A device that went through enrollment: its public code and the secret cookie value.</summary>
public record Pda(int DeviceId, string DeviceCode, string Cookie);

/// <summary>Base for tests that go through the real HTTP pipeline (authentication, authorization, filters).</summary>
public abstract class ApiTestBase : IntegrationTestBase
{
    protected ApiFactory Factory { get; private set; } = null!;

    protected HttpClient Client { get; private set; } = null!;

    protected FakeTimeProvider Time => Factory.Time;

    protected ApiTestBase(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    /// <summary>Configuration overrides for the application under test.</summary>
    protected virtual IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        Factory = new ApiFactory(Settings);
        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            // https, because the device cookie is Secure. Cookies are handled by hand so tests decide
            // exactly which one a request carries.
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        });
    }

    public override async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await base.DisposeAsync();
    }

    // ---- Seeding ---------------------------------------------------------------------

    protected async Task<(Device Device, string EnrollmentCode)> CreateDeviceAsync(string name = "PDA 1", Tenant? tenant = null)
    {
        await using var context = CreateContextFor(tenant ?? Tenant);

        var device = new Device((tenant ?? Tenant).Id, name);
        var code = device.IssueEnrollmentCode(Time.GetUtcNow());
        context.Devices.Add(device);
        await context.SaveChangesAsync();

        return (device, code);
    }

    /// <summary>Creates a device and enrolls it through the API, like a PDA at setup.</summary>
    protected async Task<Pda> EnrollPdaAsync(string name = "PDA 1", Tenant? tenant = null)
    {
        var (device, code) = await CreateDeviceAsync(name, tenant);

        var response = await Client.PostAsJsonAsync("/api/auth/enroll", new EnrollRequest(code));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return new Pda(device.Id, device.DeviceCode, CookieValue(response)!);
    }

    protected async Task<Employee> CreateEmployeeAsync(
        string name,
        string badge,
        string role = DefaultRoles.WarehouseEmployee,
        string? pin = null,
        bool mustChangePin = false,
        Tenant? tenant = null)
    {
        await using var context = CreateContextFor(tenant ?? Tenant);

        var employee = new Employee((tenant ?? Tenant).Id, name, badge);
        employee.AssignRole(await context.Roles.SingleAsync(r => r.Name == role));

        if (pin is not null)
            employee.SetPin(PinHasher.Hash(pin), mustChangePin);

        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        return employee;
    }

    protected async Task RequirePinAsync(bool required = true, Tenant? tenant = null)
    {
        await using var context = CreateContextWithoutTenant();
        var row = await context.Tenants.SingleAsync(t => t.Id == (tenant ?? Tenant).Id);
        row.SetRequirePin(required);
        await context.SaveChangesAsync();
    }

    // ---- Requests --------------------------------------------------------------------

    protected Task<HttpResponseMessage> LoginAsync(Pda pda, string badge, string? pin = null, string? cookie = null, string? deviceCode = null) =>
        SendAsync(HttpMethod.Post, "/api/auth/login",
            new LoginRequest(deviceCode ?? pda.DeviceCode, badge, pin),
            cookie: cookie ?? pda.Cookie);

    protected async Task<string> LoginTokenAsync(Pda pda, string badge, string? pin = null)
    {
        var response = await LoginAsync(pda, badge, pin);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    protected Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, object? body = null, string? token = null, string? cookie = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (body is not null)
            request.Content = JsonContent.Create(body);

        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (cookie is not null)
            request.Headers.Add("Cookie", $"{AuthControllerCookie}={cookie}");

        return Client.SendAsync(request);
    }

    protected Task<HttpResponseMessage> GetAsync(string url, string? token) =>
        SendAsync(HttpMethod.Get, url, token: token);

    /// <summary>Cookie value from a Set-Cookie header, or null.</summary>
    protected static string? CookieValue(HttpResponseMessage response)
    {
        var header = SetCookieHeader(response);
        return header?.Split(';')[0].Split('=', 2)[1];
    }

    protected static string? SetCookieHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith($"{AuthControllerCookie}="))
            : null;

    private const string AuthControllerCookie = "inv_device";
}
