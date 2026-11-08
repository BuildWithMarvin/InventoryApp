using System.Data.Common;
using InventoryApp.Api.Data;
using InventoryApp.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

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

    /// <summary>While <see cref="DatabaseFailure.Fail"/> is set, every database query of the application throws.</summary>
    public DatabaseFailure DatabaseFailure { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Auth:SigningKey", SigningKey);

        // Tests make many requests from one address; the rate limit has its own tests.
        builder.UseSetting("Auth:LoginRequestsPerMinute", "10000");
        builder.UseSetting("Auth:EnrollRequestsPerMinute", "10000");

        // Entra for the tests: a made-up directory and application. A test can switch it off with empty values.
        builder.UseSetting("Entra:TenantId", EntraTestTokens.DirectoryId.ToString());
        builder.UseSetting("Entra:ClientId", EntraTestTokens.ClientId.ToString());

        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);

            // Microsoft's signing keys come from the internet in production. The tests trust their own key
            // instead; everything else about the Entra scheme (issuer, audience, lifetime, algorithm, ...) is real.
            services.PostConfigure<JwtBearerOptions>(EntraAuthentication.Scheme, options =>
            {
                if (string.IsNullOrEmpty(options.Authority))
                    return; // Entra is switched off in this test.

                var configuration = new OpenIdConnectConfiguration { Issuer = options.TokenValidationParameters.ValidIssuer };
                configuration.SigningKeys.Add(EntraTestTokens.SigningKey);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });

            services.ConfigureDbContext<InventoryDbContext>(options => options.AddInterceptors(DatabaseFailure));

            // Test-only endpoints (see InventoryProbeController); they live in the test assembly.
            services.AddControllers().AddApplicationPart(typeof(ApiFactory).Assembly);
        });
    }
}

/// <summary>Makes every database query fail on demand, to test that nothing is granted when the database is down.</summary>
public class DatabaseFailure : DbCommandInterceptor
{
    public bool Fail { get; set; }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        ThrowIfFailing();
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void ThrowIfFailing()
    {
        if (Fail)
            throw new InvalidOperationException("Simulated database failure.");
    }
}
