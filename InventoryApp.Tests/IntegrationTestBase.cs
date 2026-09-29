using InventoryApp.Api.Data;
using Microsoft.EntityFrameworkCore.Storage;

// Each test runs inside its own transaction, which is rolled back afterwards,
// so nothing a test writes stays in the shared database.
// Derived classes still need [Collection("Integration Tests")] and the trait.
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;
    private IDbContextTransaction _transaction = null!;

    // Use only this context in tests: a second context would run on another
    // connection outside the transaction.
    protected InventoryDbContext Context { get; private set; } = null!;

    protected IntegrationTestBase(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
    }

    public virtual async Task InitializeAsync()
    {
        Context = _databaseFixture.CreateContext();
        _transaction = await Context.Database.BeginTransactionAsync();
    }

    public async Task DisposeAsync()
    {
        await _transaction.RollbackAsync();
        await _transaction.DisposeAsync();
        await Context.DisposeAsync();
    }
}
