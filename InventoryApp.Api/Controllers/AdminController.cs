using InventoryApp.Api.Data;
using InventoryApp.Api.Dtos;
using InventoryApp.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryApp.Api.Controllers
{
    /// <summary>
    /// Endpoints for a tenant administrator (Microsoft Entra). The tenant is whatever the administrator's
    /// mapping says, set by the authentication step; no tenant id is ever taken from the request.
    /// Every action sits behind an <see cref="AdminPolicies"/> policy, which never accepts an employee token.
    /// </summary>
    [Route("api/admin")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public AdminController(InventoryDbContext context)
        {
            _context = context;
        }

        /// <summary>Who am I, as far as this application is concerned. Any resolved administrator.</summary>
        [HttpGet("me")]
        [Authorize(Policy = AdminPolicies.Session)]
        public async Task<ActionResult<AdminProfileDto>> GetProfile()
        {
            var kinds = User.FindAll(InventoryClaims.AdminKind).Select(c => c.Value).Order().ToList();

            TenantSummaryDto? tenant = null;

            if (kinds.Contains(InventoryClaims.AdminKindTenant))
            {
                var row = await _context.Tenants.SingleAsync(t => t.Id == _context.RequireTenantId());
                tenant = TenantSummaryDto.FromEntity(row);
            }

            return Ok(new AdminProfileDto(
                kinds,
                tenant,
                Values(InventoryClaims.Permission),
                Values(InventoryClaims.PlatformPermission)));
        }

        /// <summary>The tenant this administrator administers.</summary>
        [HttpGet("tenant")]
        [Authorize(Policy = AdminPolicies.TenantSettings)]
        public async Task<ActionResult<TenantDto>> GetTenant()
        {
            var tenant = await _context.Tenants.SingleAsync(t => t.Id == _context.RequireTenantId());

            return Ok(TenantDto.FromEntity(tenant));
        }

        private List<string> Values(string claimType) =>
            User.FindAll(claimType).Select(c => c.Value).Order().ToList();
    }
}
