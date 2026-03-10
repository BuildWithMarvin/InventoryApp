using InventoryApp.Api.Data;
using InventoryApp.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryApp.Api.Controllers
{
    // [controller] automaticaly changes the URL to /api/products
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public ProductsController(InventoryDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Product>>> GetProducts()
        {
            // TODO for V2: Once the warehouse has 50,000 items, we will need to incorporate pagination (skip/take) here.
            // For the start, it is perfectly sufficient to simply query everything completely.
            return await _context.Products.ToListAsync();
        }

        [HttpPost]
        public async Task<ActionResult<Product>> CreateProduct(Product product)
        {
            _context.Products.Add(product);

            // EF Core automatically generates the new ID when saving (auto-increment in the database)
            await _context.SaveChangesAsync();

            return Ok(product);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(int id, Product product)
        {
          
            if (id != product.Id)
            {
                return BadRequest();
            }

            // EF Core explicitly states: ‘Do not re-insert this record, but perform an UPDATE.’
            _context.Entry(product).State = EntityState.Modified;

            // TODO: A try-catch block could be added here later, 
            // in case two warehouse workers update the same product at exactly the same millisecond.
            await _context.SaveChangesAsync();

            return NoContent(); // 204 Success (standard response for successful PUT without return data)
        }
    }
}
