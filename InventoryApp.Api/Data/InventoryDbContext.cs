using InventoryApp.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace InventoryApp.Api.Data
{

    public class InventoryDbContext : DbContext
    {
        private readonly TenantContext _tenant;

        public InventoryDbContext(DbContextOptions<InventoryDbContext> dbSetup, TenantContext tenant) : base(dbSetup)
        {
            _tenant = tenant;
        }

        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<Employee> Employees => Set<Employee>();
        public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();
        public DbSet<ProductStock> ProductStocks => Set<ProductStock>();

        /// <summary>The tenant this context works for, or null if none has been set.</summary>
        public int? CurrentTenantId => _tenant.TenantId;

        /// <summary>The current tenant. Throws if there is none, so a missing tenant never becomes a default.</summary>
        public int RequireTenantId() =>
            _tenant.TenantId ?? throw new InvalidOperationException("No tenant has been set for this request.");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Tenant isolation is enforced twice. The query filters below hide other tenants'
            // rows (and return nothing while no tenant is set). The composite keys and foreign
            // keys include TenantId, so the database itself rejects a row that references
            // another tenant's data.

            modelBuilder.Entity<Tenant>()
                .HasIndex(t => t.Name)
                .IsUnique();

            modelBuilder.Entity<Employee>(employee =>
            {
                // Target of the composite foreign keys (TenantId, EmployeeId) below.
                employee.HasAlternateKey(e => new { e.TenantId, e.Id });

                employee.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(e => e.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                employee.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            });

            modelBuilder.Entity<StorageLocation>(location =>
            {
                location.HasAlternateKey(l => new { l.TenantId, l.Id });

                location.HasIndex(l => new { l.TenantId, l.Barcode })
                    .IsUnique();

                location.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(l => l.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                location.HasQueryFilter(l => l.TenantId == CurrentTenantId);
            });

            modelBuilder.Entity<Product>(product =>
            {
                product.HasKey(p => new { p.TenantId, p.InternalBarcode });

                product.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(p => p.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                // NoAction, not SetNull: SQL Server can't null just one column of a composite key.
                // Employees are deactivated, never deleted.
                product.HasOne(p => p.LastUpdatedBy)
                    .WithMany()
                    .HasForeignKey(p => new { p.TenantId, p.LastUpdatedByEmployeeId })
                    .HasPrincipalKey(e => new { e.TenantId, e.Id })
                    .OnDelete(DeleteBehavior.NoAction);

                product.HasQueryFilter(p => p.TenantId == CurrentTenantId);
            });

            modelBuilder.Entity<ProductStock>(stock =>
            {
                stock.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(ps => ps.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                stock.HasOne(ps => ps.Product)
                    .WithMany(p => p.Stocks)
                    .HasForeignKey(ps => new { ps.TenantId, ps.ProductInternalBarcode });

                stock.HasOne(ps => ps.StorageLocation)
                    .WithMany(l => l.Stocks)
                    .HasForeignKey(ps => new { ps.TenantId, ps.StorageLocationId })
                    .HasPrincipalKey(l => new { l.TenantId, l.Id });

                stock.HasOne(ps => ps.LastUpdatedBy)
                    .WithMany()
                    .HasForeignKey(ps => new { ps.TenantId, ps.LastUpdatedByEmployeeId })
                    .HasPrincipalKey(e => new { e.TenantId, e.Id })
                    .OnDelete(DeleteBehavior.NoAction);

                stock.HasIndex(ps => new { ps.TenantId, ps.ProductInternalBarcode, ps.StorageLocationId })
                    .IsUnique();

                stock.HasQueryFilter(ps => ps.TenantId == CurrentTenantId);
            });
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            EnforceTenantBoundary();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            EnforceTenantBoundary();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        /// <summary>
        /// Last line of defence before the database: a context only writes rows of its own
        /// tenant, and a row can never be moved to another tenant.
        /// </summary>
        private void EnforceTenantBoundary()
        {
            ChangeTracker.DetectChanges();

            foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
            {
                if (entry.State is EntityState.Unchanged or EntityState.Detached)
                    continue;

                var tenantId = CurrentTenantId
                    ?? throw new InvalidOperationException(
                        "Cannot write tenant data without a tenant context.");

                if (entry.Entity.TenantId != tenantId)
                    throw new InvalidOperationException(
                        $"Cannot write a {entry.Metadata.ClrType.Name} of tenant {entry.Entity.TenantId} from the context of tenant {tenantId}.");

                if (entry.State == EntityState.Modified && entry.Property("TenantId").IsModified)
                    throw new InvalidOperationException(
                        $"The tenant of a {entry.Metadata.ClrType.Name} cannot be changed.");
            }
        }
    }
}
