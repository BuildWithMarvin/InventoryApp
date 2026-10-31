using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InventoryApp.Api.Data
{
    /// <summary>
    /// Used only by the EF Core tools. With a design-time factory the tools never run
    /// Program.cs, so they can't execute its startup migration against whatever
    /// connection string the machine happens to resolve. Commands that touch a
    /// database (database update) must pass --connection explicitly.
    /// </summary>
    public class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlServer("Server=design-time-only;Database=InventoryApp;")
                .Options;

            return new InventoryDbContext(options, new TenantContext());
        }
    }
}
