using InventoryApp.Api.Data;
using InventoryApp.Api.Dtos;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryApp.Api.Controllers
{
    [Route("api/platform")]
    [ApiController]
    public class PlatformController : ControllerBase
    {
        private readonly InventoryDbContext _context;
        private readonly TenantContext _tenantContext;

        public PlatformController(InventoryDbContext context, TenantContext tenantContext)
        {
            _context = context;
            _tenantContext = tenantContext;
        }

        private ObjectResult Refuse(int status, string title) =>
            StatusCode(status, new ProblemDetails { Status = status, Title = title });

        [HttpGet("tenants")]
        [Authorize(Policy = AdminPolicies.PlatformTenantsRead)]
        public async Task<ActionResult<IReadOnlyList<TenantSummaryDto>>> GetTenants()
        {
            var tenants = await _context.Tenants.OrderBy(t => t.Name).ToListAsync();

            return Ok(tenants.Select(TenantSummaryDto.FromEntity).ToList());
        }

        [HttpPost("tenants")]
        [Authorize(Policy = AdminPolicies.PlatformTenantsManage)]
        public async Task<ActionResult<TenantDto>> CreateTenant(NameRequest request)
        {
            // The new tenant's roles are written in the context of the new tenant. A scope belongs to one
            // tenant for its whole life, so an identity that is also a Tenant Admin can't do this.
            if (_tenantContext.TenantId is not null)
                return Refuse(StatusCodes.Status409Conflict,
                    "This identity is also a Tenant Admin of a tenant. Use an account that is only a Platform Admin.");

            Tenant tenant;

            try
            {
                tenant = new Tenant(request.Name);
            }
            catch (ArgumentException ex)
            {
                return Refuse(StatusCodes.Status400BadRequest, ex.Message);
            }

            if (await _context.Tenants.AnyAsync(t => t.Name == tenant.Name))
                return Refuse(StatusCodes.Status409Conflict, "A tenant with this name already exists.");

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _context.Tenants.Add(tenant);
                await _context.SaveChangesAsync();

                _tenantContext.Set(tenant.Id);
                _context.Roles.AddRange(DefaultRoles.CreateFor(tenant.Id));
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch (DbUpdateException)
            {
                return Refuse(StatusCodes.Status409Conflict, "A tenant with this name already exists.");
            }

            return Ok(TenantDto.FromEntity(tenant));
        }

        [HttpPut("tenants/{id:int}")]
        [Authorize(Policy = AdminPolicies.PlatformTenantsManage)]
        public async Task<ActionResult<TenantDto>> RenameTenant(int id, NameRequest request)
        {
            var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == id);

            if (tenant is null)
                return Refuse(StatusCodes.Status404NotFound, "Tenant not found.");

            try
            {
                tenant.Rename(request.Name);
            }
            catch (ArgumentException ex)
            {
                return Refuse(StatusCodes.Status400BadRequest, ex.Message);
            }

            if (await _context.Tenants.AnyAsync(t => t.Name == tenant.Name && t.Id != id))
                return Refuse(StatusCodes.Status409Conflict, "A tenant with this name already exists.");

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Refuse(StatusCodes.Status409Conflict, "A tenant with this name already exists.");
            }

            return Ok(TenantDto.FromEntity(tenant));
        }

        [HttpPost("tenants/{id:int}/activate")]
        [Authorize(Policy = AdminPolicies.PlatformTenantsManage)]
        public Task<ActionResult<TenantDto>> ActivateTenant(int id) => SetTenantActive(id, true);

        [HttpPost("tenants/{id:int}/deactivate")]
        [Authorize(Policy = AdminPolicies.PlatformTenantsManage)]
        public Task<ActionResult<TenantDto>> DeactivateTenant(int id) => SetTenantActive(id, false);

        private async Task<ActionResult<TenantDto>> SetTenantActive(int id, bool active)
        {
            var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == id);

            if (tenant is null)
                return Refuse(StatusCodes.Status404NotFound, "Tenant not found.");

            if (active)
                tenant.Activate();
            else
                tenant.Deactivate();

            await _context.SaveChangesAsync();

            return Ok(TenantDto.FromEntity(tenant));
        }
    }
}
