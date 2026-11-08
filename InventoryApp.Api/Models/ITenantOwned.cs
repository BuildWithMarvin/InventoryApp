namespace InventoryApp.Api.Models
{
    /// <summary>
    /// Marks an entity that belongs to one tenant. The DbContext filters these
    /// entities by the current tenant and refuses to write them for any other.
    /// </summary>
    public interface ITenantOwned
    {
        int TenantId { get; }
    }

    /// <summary>
    /// Marks inventory data (products, stock, storage locations). Besides the tenant filter, the
    /// DbContext hides these from, and refuses to write them for, any scope without inventory access:
    /// administrators (Platform Admin, Tenant Admin) never see them.
    /// </summary>
    public interface IInventoryData : ITenantOwned
    {
    }
}
