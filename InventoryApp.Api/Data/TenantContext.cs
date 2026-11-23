namespace InventoryApp.Api.Data
{
    public class TenantContext
    {
        public int? TenantId { get; private set; }

        public bool InventoryAccess { get; private set; } = true;

        public void Set(int tenantId)
        {
            if (tenantId <= 0)
                throw new ArgumentOutOfRangeException(nameof(tenantId), "Tenant id must be positive.");

            // A scope belongs to one tenant for its whole lifetime.
            if (TenantId is not null && TenantId != tenantId)
                throw new InvalidOperationException("The tenant of this scope has already been set.");

            TenantId = tenantId;
        }

        /// <summary>
        /// Takes inventory access away from this scope for good. There is deliberately no way back:
        /// a request that was authenticated as an administrator stays an administrator request.
        /// </summary>
        public void RestrictToAdministration() => InventoryAccess = false;
    }
}
