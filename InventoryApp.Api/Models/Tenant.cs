using System.ComponentModel.DataAnnotations;

namespace InventoryApp.Api.Models
{
    /// <summary>
    /// A company using the system. Everything else belongs to exactly one tenant.
    /// </summary>
    public class Tenant
    {
        private Tenant() { }

        public Tenant(string name)
        {
            Name = NormalizeName(name);
        }

        public int Id { get; private set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; private set; } = string.Empty;

        public bool IsActive { get; private set; } = true;

        public void Rename(string name)
        {
            Name = NormalizeName(name);
        }

        public void Deactivate() => IsActive = false;

        public void Activate() => IsActive = true;

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name is required.", nameof(name));

            var normalized = name.Trim();

            if (normalized.Length > 100)
                throw new ArgumentException("Name cannot exceed 100 characters.", nameof(name));

            return normalized;
        }
    }
}
