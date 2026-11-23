using System.ComponentModel.DataAnnotations;
using InventoryApp.Api.Models;

namespace InventoryApp.Api.Dtos
{
    // The badge barcode and the PIN (hash) are credentials and are never returned.
    public record EmployeeDto(
        int Id,
        string Name,
        bool IsActive,
        IReadOnlyList<string> Roles,
        bool HasPin,
        bool MustChangePin)
    {
        public static EmployeeDto FromEntity(Employee employee) =>
            new(
                employee.Id,
                employee.Name,
                employee.IsActive,
                employee.EmployeeRoles.Select(er => er.Role!.Name).Order().ToList(),
                employee.HasPin,
                employee.MustChangePin);
    }

    public record CreateEmployeeRequest(
        [Required, MaxLength(100)] string Name,
        [Required, MaxLength(50)] string BadgeBarcode,
        [MaxLength(50)] string? Role = null,
        // Initial PIN (six digits). The employee has to replace it at the first login. Required while the
        // tenant requires PINs.
        [MaxLength(20)] string? Pin = null);

    public record SetEmployeeRolesRequest([Required] List<string> Roles);

    public record SetEmployeePinRequest([Required, MaxLength(20)] string Pin);
}
