using InventoryApp.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryApp.Api.Models;

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

        [HttpGet("{internalBarcode}")]
        public async Task<ActionResult<Product>> GetProductByBarcode(string internalBarcode)
        {

            var product = await _context.Products.FirstOrDefaultAsync(p => p.InternalBarcode == internalBarcode);

            if (product == null)
            {
                return NotFound();
            }

            return product;
        }


        [HttpGet]
        public async Task<ActionResult<IEnumerable<Product>>> GetProducts()
        {
            // Note: For future scaling (e.g., > 50,000 items), consider implementing pagination (Skip/Take).
            return await _context.Products.ToListAsync();
        }
        [HttpPost]
        public async Task<ActionResult<Product>> CreateProduct(Product product)
        {

            if (ProductExists(product.InternalBarcode))
            {
                return Conflict("Barcode already assigned.");
            }

            product.RowVersion = Guid.NewGuid().ToString();
            _context.Products.Add(product);
            await _context.SaveChangesAsync();
            return Ok(product);
        }


        [HttpPut("{internalBarcode}")]
        public async Task<IActionResult> UpdateProduct(string internalBarcode, Product product)
        {

            if (internalBarcode != product.InternalBarcode)
            {
                return BadRequest("Product Barcode mismatch.");
            }

            _context.Entry(product).State = EntityState.Modified;
            product.RowVersion = Guid.NewGuid().ToString();

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ProductExists(internalBarcode))
                {
                    return NotFound($"Product with Barcode {internalBarcode} not found.");
                }
                else
                {

                    return Conflict("Conflict: Another user is currently editing this article. Please refresh the page.");
                }
            }

            return NoContent();
        }

        private bool ProductExists(string internalBarcode)
        {

            return _context.Products.Any(e => e.InternalBarcode == internalBarcode);
        }
    }
}