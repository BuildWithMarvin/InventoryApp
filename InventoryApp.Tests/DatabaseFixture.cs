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

    public InventoryDbContext CreateContext()
    {
        var options =
            new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlServer(_connectionString)
                .Options;

        return new InventoryDbContext(options);
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


