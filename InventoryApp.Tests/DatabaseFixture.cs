using InventoryApp.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Respawn;
public class DatabaseFixture : IAsyncLifetime
{
    private readonly string _connectionString;
    private Respawner _respawner = null!;

    public DatabaseFixture()
    {
        _connectionString =
            Environment.GetEnvironmentVariable(
                "ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings__DefaultConnection has not been set.");
    }

    // A context without a tenant sees no tenant data and can't write any; it can still
    // manage the Tenants table itself.
    public InventoryDbContext CreateContext(int? tenantId = null)
    {
        var options =
            new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlServer(_connectionString)
                .Options;

        var tenant = new TenantContext();
        if (tenantId is int id)
            tenant.Set(id);

        return new InventoryDbContext(options, tenant);
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();

        await context.Database.MigrateAsync();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
            TablesToIgnore = [new Respawn.Graph.Table("__EFMigrationsHistory")]
        });
    }

    public async Task ResetDatabaseAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await _respawner.ResetAsync(connection);
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();

        await context.Database.EnsureDeletedAsync();
    }
}


