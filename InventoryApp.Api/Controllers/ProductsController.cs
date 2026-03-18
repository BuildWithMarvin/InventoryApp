using InventoryApp.Api.Data;
using InventoryApp.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public ProductsController(InventoryDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Retrieves a specific product by its barcode.
        /// </summary>
        [HttpGet("barcode/{barcode}")]
        public async Task<ActionResult<Product>> GetProductByBarcode(string barcode)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Barcode == barcode);

            if (product == null)
            {
                return NotFound();
            }

            return product;
        }

        /// <summary>
        /// Retrieves all products from the inventory.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Product>>> GetProducts()
        {
            // Note: For future scaling (e.g., > 50,000 items), consider implementing pagination (Skip/Take).
            return await _context.Products.ToListAsync();
        }

        /// <summary>
        /// Creates a new product in the database.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<Product>> CreateProduct(Product product)
        {
            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            return Ok(product);
        }

        /// <summary>
        /// Updates an existing product. Includes concurrency handling to prevent data loss 
        /// when multiple workers edit the same product simultaneously.
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(int id, Product product)
        {
            if (id != product.Id)
            {
                return BadRequest("Product ID mismatch.");
            }

            _context.Entry(product).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ProductExists(id))
                {
                    return NotFound($"Product with ID {id} not found.");
                }
                else
                {
                    // The product exists, but was modified by someone else simultaneously.
                    // Throwing the exception or returning Conflict() is best practice here.
                    throw;
                }
            }

            return NoContent();
        }

        private bool ProductExists(int id)
        {
            return _context.Products.Any(e => e.Id == id);
        }
    }
}
