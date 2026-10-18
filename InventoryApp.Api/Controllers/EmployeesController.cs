using InventoryApp.Api.Data;
using InventoryApp.Api.Dtos;
using InventoryApp.Api.Models;
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


        [HttpPost("login")]
        public async Task<ActionResult<EmployeeDto>> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.BadgeBarcode))
            {
                return BadRequest("The barcode field cannot be left blank.");
            }


            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.BadgeBarcode == request.BadgeBarcode);

            if (employee == null)
            {

                return Unauthorized("Invalid employee ID card.");
            }

            return Ok(EmployeeDto.FromEntity(employee));
        }


        [HttpPost]
        public async Task<ActionResult<EmployeeDto>> CreateEmployee([FromBody] CreateEmployeeRequest request)
        {
            Employee newEmployee;

            try
            {
                newEmployee = request.Role == null
                    ? new Employee(request.Name, request.BadgeBarcode)
                    : new Employee(request.Name, request.BadgeBarcode, request.Role);
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
