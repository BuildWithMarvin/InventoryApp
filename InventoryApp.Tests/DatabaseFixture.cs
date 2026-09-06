using InventoryApp.Api.Data;
using Microsoft.EntityFrameworkCore;


    public class DatabaseFixture : IAsyncLifetime
    {
        private readonly string _connectionString;

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
                    .UseSqlServer(
                        _connectionString,
                        sqlOptions => sqlOptions.EnableRetryOnFailure())
                    .Options;

            return new InventoryDbContext(options);
        }

        public async Task InitializeAsync()
        {
            await using var context = CreateContext();

            await context.Database.MigrateAsync();
        }

        public async Task DisposeAsync()
        {
            await using var context = CreateContext();

            await context.Database.EnsureDeletedAsync();
        }
    }


