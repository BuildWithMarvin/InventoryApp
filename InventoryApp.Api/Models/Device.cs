using System.ComponentModel.DataAnnotations;
using InventoryApp.Api.Security;

namespace InventoryApp.Api.Models
{
    /// <summary>
    /// A shared warehouse terminal (PDA). Identification and authentication are separate:
    /// the <see cref="DeviceCode"/> is public (it is printed as a barcode and only says
    /// "this is device X"), the credential proves the device really is the enrolled one.
    /// The credential is created at enrollment and only ever leaves the server as an
    /// HttpOnly cookie, so it is never readable by client-side code. Only its hash is stored.
    /// </summary>
    public class Device : ITenantOwned
    {
        /// <summary>How long an enrollment code stays valid.</summary>
        public static readonly TimeSpan EnrollmentCodeLifetime = TimeSpan.FromHours(24);

        private Device() { }

        public Device(int tenantId, string name)
        {
            if (tenantId <= 0)
                throw new ArgumentException("A tenant is required.", nameof(tenantId));

            TenantId = tenantId;
            Name = NormalizeName(name);
            DeviceCode = DeviceCredentials.NewDeviceCode();
        }

        public int Id { get; private set; }

        public int TenantId { get; private set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; private set; } = string.Empty;

        /// <summary>Public identifier, unique across all tenants. It is what resolves the tenant at login.</summary>
        [Required]
        [MaxLength(20)]
        public string DeviceCode { get; private set; } = string.Empty;

        public bool IsActive { get; private set; } = true;

        /// <summary>SHA-256 of the credential in the device's cookie. Null until enrolled.</summary>
        [MaxLength(64)]
        public string? CredentialHash { get; private set; }

        [MaxLength(64)]
        public string? EnrollmentCodeHash { get; private set; }

        public DateTimeOffset? EnrollmentCodeExpiresAt { get; private set; }

        public DateTimeOffset? EnrolledAt { get; private set; }

        // Two requests redeeming the same enrollment code can't both succeed.
        [Timestamp]
        public byte[] RowVersion { get; private set; } = null!;

        public bool IsEnrolled => CredentialHash is not null;

        public void Rename(string name) => Name = NormalizeName(name);

        public void Deactivate() => IsActive = false;

        public void Activate() => IsActive = true;

        /// <summary>
        /// Creates a one-time enrollment code (returned in plain text, only its hash is kept).
        /// Issuing one also invalidates the current credential at once: re-enrolling a lost or
        /// replaced device cuts off the old cookie.
        /// </summary>
        public string IssueEnrollmentCode(DateTimeOffset now)
        {
            var code = DeviceCredentials.NewEnrollmentCode();

            EnrollmentCodeHash = DeviceCredentials.Hash(code);
            EnrollmentCodeExpiresAt = now + EnrollmentCodeLifetime;
            CredentialHash = null;
            EnrolledAt = null;

            return code;
        }

        /// <summary>
        /// Redeems the enrollment code: returns the new credential in plain text (for the cookie)
        /// or null if the device is inactive or has no valid code. The code is burnt on success.
        /// </summary>
        public string? CompleteEnrollment(DateTimeOffset now)
        {
            if (!IsActive || EnrollmentCodeHash is null || EnrollmentCodeExpiresAt is not { } expiresAt || expiresAt <= now)
                return null;

            var credential = DeviceCredentials.NewCredential();

            CredentialHash = DeviceCredentials.Hash(credential);
            EnrollmentCodeHash = null;
            EnrollmentCodeExpiresAt = null;
            EnrolledAt = now;

            return credential;
        }

        public bool VerifyCredential(string? presented) =>
            DeviceCredentials.Matches(presented, CredentialHash);

        /// <summary>Cuts off the enrolled browser without issuing a new code.</summary>
        public void RevokeCredential()
        {
            CredentialHash = null;
            EnrolledAt = null;
        }

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
