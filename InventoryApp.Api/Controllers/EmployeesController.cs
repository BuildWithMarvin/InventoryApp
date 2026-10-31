using InventoryApp.Api.Data;
using InventoryApp.Api.Dtos;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


namespace InventoryApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EmployeesController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public EmployeesController(InventoryDbContext context)
        {
            _context = context;
        }


        // Creating employees is an administrator operation (Entra ID, Phase 4). Until that exists the
        // policy denies everyone: badge logins can never hold the permission to do this.
        [Authorize(Policy = Policies.AdminOnly)]
        [HttpPost]
        public async Task<ActionResult<EmployeeDto>> CreateEmployee([FromBody] CreateEmployeeRequest request)
        {
            var roleName = request.Role ?? DefaultRoles.WarehouseEmployee;

            var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == roleName);

            if (role == null)
            {
                return BadRequest($"Role '{roleName}' not found.");
            }

            Employee newEmployee;

            try
            {
                newEmployee = new Employee(_context.RequireTenantId(), request.Name, request.BadgeBarcode);
                newEmployee.AssignRole(role);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }

            var barcodeExists = await _context.Employees
                .AnyAsync(e => e.BadgeBarcode == newEmployee.BadgeBarcode);

            if (barcodeExists)
            {
                return BadRequest("This barcode has already been assigned to another employee.");
            }

            _context.Employees.Add(newEmployee);
            await _context.SaveChangesAsync();

            return Ok(EmployeeDto.FromEntity(newEmployee));
        }
    }

}
