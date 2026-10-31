using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

/// <summary>
/// The real application, in-process, on the shared test database. The clock is fake, so session
/// timeouts can be tested by moving time instead of waiting.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string SigningKey = "test-signing-key-0123456789-abcdefghij";

    private readonly IReadOnlyDictionary<string, string> _settings;

    public ApiFactory(IReadOnlyDictionary<string, string>? settings = null)
    {
        _settings = settings ?? new Dictionary<string, string>();
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Auth:SigningKey", SigningKey);

        // Tests make many requests from one address; the rate limit has its own tests.
        builder.UseSetting("Auth:LoginRequestsPerMinute", "10000");
        builder.UseSetting("Auth:EnrollRequestsPerMinute", "10000");

        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }
}
