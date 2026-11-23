namespace InventoryApp.Api.Models
{
    public interface ITenantOwned
    {
        int TenantId { get; }
    }

    public interface IInventoryData : ITenantOwned
    {
    }
}
