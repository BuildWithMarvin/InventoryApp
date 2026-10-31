using System.ComponentModel.DataAnnotations;
using InventoryApp.Api.Models;

namespace InventoryApp.Api.Dtos
{
    // Roles needs EmployeeRoles and their Role loaded.
    // The badge barcode is a credential and is never returned.
    public record EmployeeDto(int Id, string Name, bool IsActive, IReadOnlyList<string> Roles)
    {
        public static EmployeeDto FromEntity(Employee employee) =>
            new(
                employee.Id,
                employee.Name,
                employee.IsActive,
                employee.EmployeeRoles.Select(er => er.Role!.Name).Order().ToList());
    }

    public record CreateEmployeeRequest(
        [Required, MaxLength(100)] string Name,
        [Required, MaxLength(50)] string BadgeBarcode,
        // Name of an inventory role of the tenant. Defaults to WarehouseEmployee.
        [MaxLength(50)] string? Role = null);
}
