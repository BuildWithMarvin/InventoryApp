using InventoryApp.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace InventoryApp.Api.Data
{
    public class InventoryDbContext : DbContext
    {
        public InventoryDbContext(DbContextOptions<InventoryDbContext> dbSetup) : base(dbSetup) { }

        public DbSet<Product> Products => Set<Product>();
        public DbSet<Employee> Employees => Set<Employee>();
        public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();
        public DbSet<ProductStock> ProductStocks => Set<ProductStock>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ProductStock>()
                .HasIndex(ps => new { ps.ProductInternalBarcode, ps.StorageLocationId })
                .IsUnique();

        
            modelBuilder.Entity<ProductStock>()
                .HasOne(ps => ps.Product)
                .WithMany(p => p.Stocks)
                .HasForeignKey(ps => ps.ProductInternalBarcode);

            modelBuilder.Entity<StorageLocation>()
                .HasIndex(sl => sl.Barcode)
                .IsUnique();
        }
    }
}
