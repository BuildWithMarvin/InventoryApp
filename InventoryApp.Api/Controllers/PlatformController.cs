using InventoryApp.Api.Data;
using InventoryApp.Api.Dtos;
using InventoryApp.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryApp.Api.Controllers
{
    /// <summary>
    /// Endpoints for a Platform Admin (Microsoft Entra, app role). They work across tenants and so have
    /// no tenant of their own. A Platform Admin request can't read inventory data: the DbContext hides
    /// it from every administrator request (<see cref="TenantContext.RestrictToAdministration"/>).
    /// </summary>
    [Route("api/platform")]
    [ApiController]
    public class PlatformController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public PlatformController(InventoryDbContext context)
        {
            _context = context;
        }

        [HttpGet("tenants")]
        [Authorize(Policy = AdminPolicies.PlatformTenantsRead)]
        public async Task<ActionResult<IReadOnlyList<TenantSummaryDto>>> GetTenants()
        {
            var tenants = await _context.Tenants.OrderBy(t => t.Name).ToListAsync();

            return Ok(tenants.Select(TenantSummaryDto.FromEntity).ToList());
        }
    }
}
