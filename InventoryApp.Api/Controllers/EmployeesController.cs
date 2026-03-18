using InventoryApp.Api.Data;
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

        /// <summary>
        /// Authenticates an employee using their secret PIN.
        /// </summary>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            // Note: Since login is PIN-only (no username to filter by), we must load all hashes to verify.
            // This is acceptable for small to medium warehouse teams, but would require a 
            // username/ID identifier to scale to thousands of users efficiently.
            var allEmployees = await _context.Employees.ToListAsync();

            var employee = allEmployees.FirstOrDefault(e => BCrypt.Net.BCrypt.Verify(request.PinCode, e.PinCode));

            if (employee == null)
            {
                return Unauthorized("Invalid PIN code.");
            }

            return Ok(employee);
        }

        /// <summary>
        /// Creates a new employee and securely hashes their initial PIN.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateEmployee([FromBody] Employee newEmployee)
        {
            var allEmployees = await _context.Employees.ToListAsync();

            // Ensure the PIN is unique across the company
            if (allEmployees.Any(e => BCrypt.Net.BCrypt.Verify(newEmployee.PinCode, e.PinCode)))
            {
                return BadRequest("This PIN is already in use.");
            }

            // Hash the password before saving to the database
            newEmployee.PinCode = BCrypt.Net.BCrypt.HashPassword(newEmployee.PinCode);

            _context.Employees.Add(newEmployee);
            await _context.SaveChangesAsync();

            return Ok(newEmployee);
        }

        /// <summary>
        /// Allows an employee to change their PIN and clears the 'MustChangePin' requirement flag.
        /// </summary>
        [HttpPost("change-pin")]
        public async Task<IActionResult> ChangePin([FromBody] ChangePinRequest request)
        {
            var employee = await _context.Employees.FindAsync(request.EmployeeId);

            if (employee == null)
            {
                return NotFound("Employee not found.");
            }

            employee.PinCode = BCrypt.Net.BCrypt.HashPassword(request.NewPin);
            employee.MustChangePin = false;

            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}
