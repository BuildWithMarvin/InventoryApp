using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;


namespace InventoryApp.Api.Models
{
    [Index(nameof(BadgeBarcode), IsUnique = true)]
    public class Employee
    {
        private Employee() { }

        public Employee(string name, string badgeBarcode, string role = "User")
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException(
                    "Name is required.",
                    nameof(name));

            if (string.IsNullOrWhiteSpace(badgeBarcode))
                throw new ArgumentException(
                    "Badge barcode is required.",
                    nameof(badgeBarcode));

            if (string.IsNullOrWhiteSpace(role))
                throw new ArgumentException(
                    "Role is required.",
                    nameof(role));

            var normalizedName = name.Trim();

            if (normalizedName.Length > 100)
                throw new ArgumentException("Name cannot exceed 100 characters.", nameof(name));
            
            Name = normalizedName;

            var normalizedBadgeBarcode = badgeBarcode.Trim();

            if (normalizedBadgeBarcode.Length > 50)
                throw new ArgumentException("Badge barcode cannot exceed 50 characters.", nameof(badgeBarcode));

            BadgeBarcode = normalizedBadgeBarcode;

            var normalizedRole = role.Trim();

            if (normalizedRole.Length > 20)
                throw new ArgumentException("Role cannot exceed 20 characters.", nameof(role));

            Role = normalizedRole;
        }

        public int Id { get; private set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; private set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string BadgeBarcode { get; private set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string Role { get; private set; } = "User";

        public void UpdateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException(
                    "Name is required.",
                    nameof(name));

            Name = name.Trim();
        }

        public void UpdateRole(string role)
        {
            if (string.IsNullOrWhiteSpace(role))
                throw new ArgumentException(
                    "Role is required.",
                    nameof(role));

            Role = role.Trim();
        }
    }
}