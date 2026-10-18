using System.ComponentModel.DataAnnotations;
using InventoryApp.Api.Models;

namespace InventoryApp.Api.Dtos
{
    public record EmployeeDto(int Id, string Name, string BadgeBarcode, string Role)
    {
        public static EmployeeDto FromEntity(Employee employee) =>
            new(employee.Id, employee.Name, employee.BadgeBarcode, employee.Role);
    }

    public record CreateEmployeeRequest(
        [Required, MaxLength(100)] string Name,
        [Required, MaxLength(50)] string BadgeBarcode,
        [MaxLength(20)] string? Role = null);
}
