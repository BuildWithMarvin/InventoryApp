namespace InventoryApp.Api.Data
{
    /// <summary>
    /// Holds the tenant of the current request scope. It is filled from validated
    /// credentials (never from client input) and read by <see cref="InventoryDbContext"/>.
    /// </summary>
    public class TenantContext
    {
        public int? TenantId { get; private set; }

        public void Set(int tenantId)
        {
            if (tenantId <= 0)
                throw new ArgumentOutOfRangeException(nameof(tenantId), "Tenant id must be positive.");

            // A scope belongs to one tenant for its whole lifetime.
            if (TenantId is not null && TenantId != tenantId)
                throw new InvalidOperationException("The tenant of this scope has already been set.");

            TenantId = tenantId;
        }
    }
}
