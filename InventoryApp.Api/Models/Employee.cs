using InventoryApp.Api.Security;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;


namespace InventoryApp.Api.Models
{
    [Index(nameof(TenantId), nameof(BadgeBarcode), IsUnique = true)]
    public class Employee : ITenantOwned
    {
        /// <summary>Failed PIN attempts that trigger a lockout.</summary>
        public const int MaxFailedPinAttempts = 5;

        /// <summary>How long a lockout lasts.</summary>
        public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        private Employee() { }

        public Employee(int tenantId, string name, string badgeBarcode)
        {
            if (tenantId <= 0)
                throw new ArgumentException("A tenant is required.", nameof(tenantId));

            TenantId = tenantId;

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException(
                    "Name is required.",
                    nameof(name));

            if (string.IsNullOrWhiteSpace(badgeBarcode))
                throw new ArgumentException(
                    "Badge barcode is required.",
                    nameof(badgeBarcode));

            var normalizedName = name.Trim();

            if (normalizedName.Length > 100)
                throw new ArgumentException("Name cannot exceed 100 characters.", nameof(name));

            Name = normalizedName;

            var normalizedBadgeBarcode = badgeBarcode.Trim();

            if (normalizedBadgeBarcode.Length > 50)
                throw new ArgumentException("Badge barcode cannot exceed 50 characters.", nameof(badgeBarcode));

            BadgeBarcode = normalizedBadgeBarcode;
        }

        public int Id { get; private set; }

        public int TenantId { get; private set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; private set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string BadgeBarcode { get; private set; } = string.Empty;

        /// <summary>Inactive employees can't log in. Employees are deactivated, never deleted.</summary>
        public bool IsActive { get; private set; } = true;

        public ICollection<EmployeeRole> EmployeeRoles { get; } = new List<EmployeeRole>();

        // ---- PIN ---------------------------------------------------------------------

        /// <summary>BCrypt hash of the six-digit PIN, or null if the employee has none.</summary>
        [MaxLength(100)]
        public string? PinHash { get; private set; }

        public bool HasPin => PinHash is not null;

        /// <summary>Set after an admin chose the PIN: the employee must replace it before doing anything else.</summary>
        public bool MustChangePin { get; private set; }

        public int FailedPinAttempts { get; private set; }

        public DateTimeOffset? LockoutEnd { get; private set; }

        public void UpdateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException(
                    "Name is required.",
                    nameof(name));

            Name = name.Trim();
        }

        public void Deactivate() => IsActive = false;

        public void Activate() => IsActive = true;

        /// <summary>
        /// Gives the employee an Inventory role of the same tenant. Admin roles are refused
        /// here and, as a second line, by the database.
        /// </summary>
        public void AssignRole(Role role)
        {
            ArgumentNullException.ThrowIfNull(role);

            if (role.TenantId != TenantId)
                throw new ArgumentException("The role belongs to another tenant.", nameof(role));

            if (role.Scope != PermissionScope.Inventory)
                throw new ArgumentException("Employees can only hold inventory roles.", nameof(role));

            if (EmployeeRoles.Any(er => er.Role == role || (role.Id != 0 && er.RoleId == role.Id)))
                return;

            EmployeeRoles.Add(new EmployeeRole(this, role));
        }

        public void RemoveRole(int roleId)
        {
            var assignment = EmployeeRoles.FirstOrDefault(er => er.RoleId == roleId);

            if (assignment is not null)
                EmployeeRoles.Remove(assignment);
        }

        /// <summary>
        /// Stores a new PIN hash (see <see cref="PinHasher"/>) and clears any lockout.
        /// An admin setting the PIN passes mustChangePin: true; the employee changing their own passes false.
        /// </summary>
        public void SetPin(string pinHash, bool mustChangePin)
        {
            if (string.IsNullOrWhiteSpace(pinHash))
                throw new ArgumentException("A PIN hash is required.", nameof(pinHash));

            PinHash = pinHash;
            MustChangePin = mustChangePin;
            FailedPinAttempts = 0;
            LockoutEnd = null;
        }

        public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is { } end && end > now;

        /// <summary>
        /// Counts a wrong PIN. The fifth in a row locks the employee out for <see cref="LockoutDuration"/>.
        /// While locked out the login must not check the PIN at all, so the lockout can't be extended
        /// and a correct guess during it is not accepted.
        /// </summary>
        public void RegisterFailedPin(DateTimeOffset now)
        {
            if (IsLockedOut(now))
                return;

            FailedPinAttempts++;

            if (FailedPinAttempts >= MaxFailedPinAttempts)
            {
                LockoutEnd = now + LockoutDuration;
                FailedPinAttempts = 0;
            }
        }

        public void RegisterSuccessfulPin()
        {
            FailedPinAttempts = 0;
            LockoutEnd = null;
        }
    }
}
