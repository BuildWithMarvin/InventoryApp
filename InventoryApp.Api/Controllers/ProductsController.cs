using InventoryApp.Api.Data;
using InventoryApp.Api.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.AspNetCore.Authorization;

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
        [Authorize(Policy = Permissions.ProductsRead)]
        public async Task<ActionResult<ProductDto>> GetProductByBarcode(string internalBarcode)
        {
            var product = await _context.Products
                .Include(p => p.Stocks)
                .FirstOrDefaultAsync(p => p.InternalBarcode == internalBarcode);

            if (product == null)
            {
                return NotFound();
            }

            return ProductDto.FromEntity(product);
        }

        [HttpGet]
        [Authorize(Policy = Permissions.ProductsRead)]
        public async Task<ActionResult<IEnumerable<ProductDto>>> GetProducts()
        {
            // Note: For future scaling (e.g., > 50,000 items), consider implementing pagination (Skip/Take).
            var products = await _context.Products
                .Include(p => p.Stocks)
                .ToListAsync();

            return products.Select(ProductDto.FromEntity).ToList();
        }

        [HttpPost]
        [Authorize(Policy = Permissions.ProductsWrite)]
        public async Task<ActionResult<ProductDto>> CreateProduct(CreateProductRequest request)
        {
            if (await ProductExistsAsync(request.InternalBarcode))
            {
                return Conflict("Barcode already assigned.");
            }

            var employeeId = User.GetEmployeeId();

            Product product;

            try
            {
                product = new Product(
                    _context.RequireTenantId(),
                    request.InternalBarcode,
                    request.Name,
                    employeeId,
                    request.SupplierBarcode);

                product.UpdateDetails(product.Name, request.Description, employeeId);
                product.UpdatePrice(request.Price, employeeId);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }

            // SQL Server fills RowVersion on insert.
            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            return CreatedAtAction(
                nameof(GetProductByBarcode),
                new { internalBarcode = product.InternalBarcode },
                ProductDto.FromEntity(product));
        }

        [HttpPut("{internalBarcode}")]
        [Authorize(Policy = Permissions.ProductsWrite)]
        public async Task<IActionResult> UpdateProduct(string internalBarcode, UpdateProductRequest request)
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.InternalBarcode == internalBarcode);

            if (product == null)
            {
                return NotFound($"Product with Barcode {internalBarcode} not found.");
            }

            var employeeId = User.GetEmployeeId();

            // Compare against the version the client read, not the one just loaded.
            _context.Entry(product).Property(p => p.RowVersion).OriginalValue = request.RowVersion;

            try
            {
                product.UpdateDetails(request.Name, request.Description, employeeId);
                product.UpdateSupplierBarcode(request.SupplierBarcode, employeeId);
                product.UpdatePrice(request.Price, employeeId);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await ProductExistsAsync(internalBarcode))
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

        private Task<bool> ProductExistsAsync(string internalBarcode)
        {
            return _context.Products.AnyAsync(e => e.InternalBarcode == internalBarcode);
        }
    }
}
