using InventoryApp.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace InventoryApp.Api.Data
{
    // EF Core main context. 
    // Reminder: If new tables (DbSets) are added here -> don't forget to add migration!
    public class InventoryDbContext : DbContext
    {
        public InventoryDbContext(DbContextOptions<InventoryDbContext> dbSetup) : base(dbSetup) { }

        public DbSet<Product> Products { get; set; }

        // New:
        public DbSet<Employee> Employees { get; set; }

        // TODO for later: Once the app grows and we need more complex things like foreign keys 
        // (e.g. categories or storage locations), we will override OnModelCreating() here. 

    }
}
