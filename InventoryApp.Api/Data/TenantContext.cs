namespace InventoryApp.Api.Data
{
    /// <summary>
    /// Holds the tenant of the current request scope. It is filled from validated
    /// credentials (never from client input) and read by <see cref="InventoryDbContext"/>.
    /// </summary>
    public class TenantContext
    {
        public int? TenantId { get; private set; }

        /// <summary>
        /// Whether this scope may read and write inventory data (products, stock, storage locations).
        /// True for warehouse employees and the pre-login flows. An administrator request (Entra) is
        /// restricted by <see cref="RestrictToAdministration"/> before anything else happens, so
        /// administrators can never reach inventory data through the shared DbContext, whatever a
        /// controller does (decision D8).
        /// </summary>
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
