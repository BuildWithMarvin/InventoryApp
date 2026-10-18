using InventoryApp.Api.Data;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;

    // Every test starts on an empty database (Respawn), so a context from
    // DatabaseFixture.CreateContext() or an HTTP call to the API sees the same data.
    protected InventoryDbContext Context { get; private set; } = null!;

    protected IntegrationTestBase(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
    }

    public virtual async Task InitializeAsync()
    {
        await _databaseFixture.ResetDatabaseAsync();

        Context = _databaseFixture.CreateContext();
    }

    public async Task DisposeAsync()
    {
        await Context.DisposeAsync();
    }
}
