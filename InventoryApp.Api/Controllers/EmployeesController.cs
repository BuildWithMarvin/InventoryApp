using BCrypt.Net;
using InventoryApp.Api.Data;
using InventoryApp.Api.Models; // (oder .Data)
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
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
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] string pinCode)
        {
            // Da wir keinen Benutzernamen haben, laden wir alle Mitarbeiter ins RAM
            var allEmployees = await _context.Employees.ToListAsync();

            // Wir prüfen jeden Hash. BCrypt.Verify macht die mathematische Magie!
            // Vorher: BCrypt.Verify(...)
            var employee = allEmployees.FirstOrDefault(e => BCrypt.Net.BCrypt.Verify(pinCode, e.PinCode));

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
    }
}
