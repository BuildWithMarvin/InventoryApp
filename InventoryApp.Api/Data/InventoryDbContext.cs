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
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<EmployeeRole> EmployeeRoles => Set<EmployeeRole>();
        public DbSet<Device> Devices => Set<Device>();
        public DbSet<EmployeeSession> EmployeeSessions => Set<EmployeeSession>();

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

            modelBuilder.Entity<Role>(role =>
            {
                role.HasAlternateKey(r => new { r.TenantId, r.Id });

                // Lets EmployeeRole reference a role together with its scope, so the database
                // itself refuses to give an employee anything but an Inventory role.
                role.HasAlternateKey(r => new { r.TenantId, r.Id, r.Scope });

                role.HasIndex(r => new { r.TenantId, r.Name })
                    .IsUnique();

                role.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(r => r.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                role.HasQueryFilter(r => r.TenantId == CurrentTenantId);
            });

            modelBuilder.Entity<RolePermission>(rolePermission =>
            {
                rolePermission.HasKey(rp => new { rp.RoleId, rp.Permission });

                rolePermission.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(rp => rp.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                rolePermission.HasOne<Role>()
                    .WithMany(r => r.Permissions)
                    .HasForeignKey(rp => new { rp.TenantId, rp.RoleId })
                    .HasPrincipalKey(r => new { r.TenantId, r.Id })
                    .OnDelete(DeleteBehavior.Cascade);

                rolePermission.HasQueryFilter(rp => rp.TenantId == CurrentTenantId);
            });

            modelBuilder.Entity<EmployeeRole>(employeeRole =>
            {
                employeeRole.HasKey(er => new { er.EmployeeId, er.RoleId });

                // With the foreign key below this makes the database refuse admin roles for employees:
                // the key only matches the role's real scope, and the check only allows Inventory (0).
                employeeRole.ToTable(t => t.HasCheckConstraint("CK_EmployeeRoles_InventoryRoleOnly", "[RoleScope] = 0"));

                employeeRole.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(er => er.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                employeeRole.HasOne(er => er.Employee)
                    .WithMany(e => e.EmployeeRoles)
                    .HasForeignKey(er => new { er.TenantId, er.EmployeeId })
                    .HasPrincipalKey(e => new { e.TenantId, e.Id })
                    .OnDelete(DeleteBehavior.Restrict);

                // RoleScope is only ever Inventory (Employee.AssignRole), so this key can only
                // match Inventory roles. A role in use can't be deleted.
                employeeRole.HasOne(er => er.Role)
                    .WithMany()
                    .HasForeignKey(er => new { er.TenantId, er.RoleId, er.RoleScope })
                    .HasPrincipalKey(r => new { r.TenantId, r.Id, r.Scope })
                    .OnDelete(DeleteBehavior.Restrict);

                employeeRole.HasQueryFilter(er => er.TenantId == CurrentTenantId);
            });

            modelBuilder.Entity<Device>(device =>
            {
                device.HasAlternateKey(d => new { d.TenantId, d.Id });

                // Global on purpose: the device code is what tells the login which tenant it is for.
                device.HasIndex(d => d.DeviceCode)
                    .IsUnique();

                device.HasIndex(d => d.EnrollmentCodeHash)
                    .IsUnique()
                    .HasFilter("[EnrollmentCodeHash] IS NOT NULL");

                device.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(d => d.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                device.HasQueryFilter(d => d.TenantId == CurrentTenantId);
            });

            modelBuilder.Entity<EmployeeSession>(session =>
            {
                session.HasKey(s => s.Id);
                session.Property(s => s.Id).ValueGeneratedNever();

                session.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(s => s.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                session.HasOne(s => s.Employee)
                    .WithMany()
                    .HasForeignKey(s => new { s.TenantId, s.EmployeeId })
                    .HasPrincipalKey(e => new { e.TenantId, e.Id })
                    .OnDelete(DeleteBehavior.Restrict);

                session.HasOne(s => s.Device)
                    .WithMany()
                    .HasForeignKey(s => new { s.TenantId, s.DeviceId })
                    .HasPrincipalKey(d => new { d.TenantId, d.Id })
                    .OnDelete(DeleteBehavior.Restrict);

                // One active session per device: a new login must revoke the previous one first.
                session.HasIndex(s => s.DeviceId)
                    .IsUnique()
                    .HasFilter("[RevokedAt] IS NULL");

                session.HasQueryFilter(s => s.TenantId == CurrentTenantId);
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
