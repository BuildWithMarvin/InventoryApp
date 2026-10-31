using InventoryApp.Api.Data;
using InventoryApp.Api.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

// Proves the migration path the deployment takes: Azure applies the pending migrations in
// order to whatever schema it has. Each test works on its own temporary database, never on
// the shared one.
[Trait("Category", "Integration")]
[Collection("Integration Tests")]
public class MigrationTests
{
    // The schema states a deployed database can be in before MultiTenantSchema.
    private const string InitialCreate = "20260804144404_InitialCreate";
    private const string AdjustProductPricePrecision = "20261011091051_AdjustProductPricePrecision";

    [Fact]
    public async Task Migrate_FromAnEmptyDatabase_MatchesTheModel()
    {
        await using var scratch = await ScratchDatabase.CreateAsync();

        await scratch.Context.Database.MigrateAsync();

        Assert.Empty(await scratch.Context.Database.GetPendingMigrationsAsync());
        // Fails if someone changes the model without adding a migration.
        Assert.False(scratch.Context.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData(InitialCreate)]
    [InlineData(AdjustProductPricePrecision)]
    public async Task Migrate_FromAnOldSchemaWithRows_ResetsToTheMultiTenantSchema(string oldState)
    {
        await using var scratch = await ScratchDatabase.CreateAsync();
        var context = scratch.Context;

        await context.GetService<IMigrator>().MigrateAsync(oldState);
        await InsertOldSingleTenantRowsAsync(context);

        await context.Database.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());

        // The old rows are gone ...
        Assert.Equal(0, await CountAsync(context, "Employees"));
        Assert.Equal(0, await CountAsync(context, "Products"));
        Assert.Equal(0, await CountAsync(context, "StorageLocations"));
        Assert.Equal(0, await CountAsync(context, "ProductStocks"));

        // ... and the new schema works end to end.
        var tenant = new Tenant("Acme");
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        await using var tenantContext = scratch.CreateContext(tenant.Id);
        var employee = new Employee(tenant.Id, "Anna", "BADGE-1");
        tenantContext.Employees.Add(employee);
        await tenantContext.SaveChangesAsync();
        tenantContext.Products.Add(new Product(tenant.Id, "10000001", "Product", employee.Id));
        await tenantContext.SaveChangesAsync();

        Assert.Single(await tenantContext.Products.ToListAsync());
    }

    [Fact]
    public async Task MultiTenantSchema_CannotBeReverted()
    {
        await using var scratch = await ScratchDatabase.CreateAsync();
        var context = scratch.Context;
        await context.Database.MigrateAsync();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => context.GetService<IMigrator>().MigrateAsync(AdjustProductPricePrecision));
    }

    // Rows in the shape the single-tenant schema had. Only columns that exist in both old states.
    private static async Task InsertOldSingleTenantRowsAsync(InventoryDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Employees (Name, BadgeBarcode, Role) VALUES ('Old employee', 'OLD-1', 'User')");
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO StorageLocations (LocationCode, Zone, Barcode, IsActive) VALUES ('A-01', 'A', 'LOC-1', 1)");
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Products (InternalBarcode, Name, Price) VALUES ('OLD-PRODUCT', 'Old product', 1.50)");
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO ProductStocks (ProductInternalBarcode, StorageLocationId, Quantity) " +
            "SELECT 'OLD-PRODUCT', Id, 5 FROM StorageLocations");

        Assert.Equal(1, await CountAsync(context, "ProductStocks"));
    }

    private static Task<int> CountAsync(InventoryDbContext context, string table) =>
        context.Database
            .SqlQueryRaw<int>($"SELECT COUNT(*) AS [Value] FROM [{table}]")
            .SingleAsync();

    private sealed class ScratchDatabase : IAsyncDisposable
    {
        private readonly string _connectionString;

        private ScratchDatabase(string connectionString)
        {
            _connectionString = connectionString;
            Context = CreateContext();
        }

        public InventoryDbContext Context { get; }

        public static Task<ScratchDatabase> CreateAsync()
        {
            var baseConnectionString =
                Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                ?? throw new InvalidOperationException("ConnectionStrings__DefaultConnection has not been set.");

            var builder = new SqlConnectionStringBuilder(baseConnectionString)
            {
                InitialCatalog = $"MigrationTest_{Guid.NewGuid():N}"
            };

            return Task.FromResult(new ScratchDatabase(builder.ConnectionString));
        }

        public InventoryDbContext CreateContext(int? tenantId = null)
        {
            var tenant = new TenantContext();
            if (tenantId is int id)
                tenant.Set(id);

            return new InventoryDbContext(
                new DbContextOptionsBuilder<InventoryDbContext>().UseSqlServer(_connectionString).Options,
                tenant);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.Database.EnsureDeletedAsync();
            await Context.DisposeAsync();
        }
    }
}
