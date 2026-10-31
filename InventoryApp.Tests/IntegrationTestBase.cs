using InventoryApp.Api.Data;
using InventoryApp.Api.Models;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;

    // Every test starts on an empty database (Respawn), so a context from
    // DatabaseFixture.CreateContext() or an HTTP call to the API sees the same data.
    // Context works for the default tenant created below.
    protected InventoryDbContext Context { get; private set; } = null!;

    // The tenant that Context works for.
    protected Tenant Tenant { get; private set; } = null!;

    protected int TenantId => Tenant.Id;

    protected IntegrationTestBase(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
    }

    public virtual async Task InitializeAsync()
    {
        await _databaseFixture.ResetDatabaseAsync();

        Tenant = await CreateTenantAsync("Test tenant");
        Context = _databaseFixture.CreateContext(Tenant.Id);
    }

    protected async Task<Tenant> CreateTenantAsync(string name)
    {
        await using var context = _databaseFixture.CreateContext();

        var tenant = new Tenant(name);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        return tenant;
    }

    // A second context for another tenant. The caller disposes it.
    protected InventoryDbContext CreateContextFor(Tenant tenant) =>
        _databaseFixture.CreateContext(tenant.Id);

    // A context that has no tenant. The caller disposes it.
    protected InventoryDbContext CreateContextWithoutTenant() =>
        _databaseFixture.CreateContext();

    public async Task DisposeAsync()
    {
        await Context.DisposeAsync();
    }
}
