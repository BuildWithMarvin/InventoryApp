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

        
        public DbSet<Employee> Employees { get; set; }
 

    }
}
