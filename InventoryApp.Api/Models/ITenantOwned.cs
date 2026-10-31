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
}
