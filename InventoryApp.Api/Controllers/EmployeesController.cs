using BCrypt.Net;
using InventoryApp.Api.Data;
using InventoryApp.Api.Models; // (oder .Data)
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

        // POST: api/employees/login
        // POST: api/employees/login
        // Wieder sicher als POST, PIN ist im versteckten Body!
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var allEmployees = await _context.Employees.ToListAsync();

            // Wir greifen jetzt auf request.PinCode zu
            var employee = allEmployees.FirstOrDefault(e => BCrypt.Net.BCrypt.Verify(request.PinCode, e.PinCode));

            if (employee == null)
            {
                return Unauthorized("Falscher PIN-Code.");
            }

            return Ok(employee);
        }

        // POST: api/employees
        [HttpPost]
        public async Task<IActionResult> CreateEmployee([FromBody] Employee newEmployee)
        {
            var allEmployees = await _context.Employees.ToListAsync();

            // Prüfen, ob der PIN schon belegt ist
            if (allEmployees.Any(e => BCrypt.Net.BCrypt.Verify(newEmployee.PinCode, e.PinCode)))

            // Vorher: BCrypt.HashPassword(...)
            {
                return BadRequest("Dieser PIN wird bereits verwendet!");
            }

            // HIER PASSIERT DIE MAGIE: Aus "1234" wird ein unlesbarer Hash!
            newEmployee.PinCode = BCrypt.Net.BCrypt.HashPassword(newEmployee.PinCode);

            _context.Employees.Add(newEmployee);
            await _context.SaveChangesAsync();

            return Ok(newEmployee);
        }
        [HttpPost("change-pin")]
        public async Task<IActionResult> ChangePin([FromBody] ChangePinRequest request)
        {
            var employee = await _context.Employees.FindAsync(request.EmployeeId);

            if (employee == null)
            {
                return NotFound("Mitarbeiter nicht gefunden.");
            }

            // 1. Die neue PIN sicher verschlüsseln (BCrypt hast du ja schon eingebaut!)
            employee.PinCode = BCrypt.Net.BCrypt.HashPassword(request.NewPin);

            // 2. Den Zwang aufheben, da er jetzt eine eigene PIN hat
            employee.MustChangePin = false;

            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}
