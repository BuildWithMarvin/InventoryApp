using InventoryApp.Api.Data;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryApp.Tests;

[ApiController]
[Route("test-probe")]
public class InventoryProbeController : ControllerBase
{
    private readonly InventoryDbContext _context;

    public InventoryProbeController(InventoryDbContext context)
    {
        _context = context;
    }

    public record Counts(int Products, int Stocks, int Locations, int Employees, int? TenantId);

    [HttpGet("counts")]
    [Authorize(Policy = AdminPolicies.Session)]
    public async Task<Counts> GetCounts() => new(
        await _context.Products.CountAsync(),
        await _context.ProductStocks.CountAsync(),
        await _context.StorageLocations.CountAsync(),
        await _context.Employees.CountAsync(),
        _context.CurrentTenantId);

    // Includes through a navigation must not leak either.
    [HttpGet("employee-products")]
    [Authorize(Policy = AdminPolicies.Session)]
    public async Task<int> GetProductsViaStock() =>
        await _context.ProductStocks.Include(s => s.Product).Select(s => s.Product).CountAsync();

    [HttpPost("product")]
    [Authorize(Policy = AdminPolicies.Session)]
    public async Task<IActionResult> CreateProduct()
    {
        try
        {
            var tenantId = _context.RequireTenantId();
            var employee = await _context.Employees.FirstAsync();
            _context.Products.Add(new Product(tenantId, "PROBE-1", "Probe", employee.Id));
            await _context.SaveChangesAsync();
            return Ok("written");
        }
        catch (InvalidOperationException exception)
        {
            return StatusCode(StatusCodes.Status409Conflict, exception.Message);
        }
    }
}
