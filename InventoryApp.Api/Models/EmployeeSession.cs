using System.ComponentModel.DataAnnotations;

namespace InventoryApp.Api.Models
{
    /// <summary>
    /// A badge login on a device. The JWT only points at it (claim "sid"); whether a request is
    /// still allowed is decided here, on every request, so a session can be revoked immediately.
    /// A device has at most one active session (enforced by a unique index).
    /// </summary>
    public class EmployeeSession : ITenantOwned
    {
        private EmployeeSession() { }

        public EmployeeSession(Employee employee, Device device, DateTimeOffset now, TimeSpan absoluteLifetime)
        {
            ArgumentNullException.ThrowIfNull(employee);
            ArgumentNullException.ThrowIfNull(device);

            if (employee.Id <= 0 || device.Id <= 0)
                throw new ArgumentException("The employee and the device must be saved first.");

            if (employee.TenantId != device.TenantId)
                throw new ArgumentException("The employee and the device belong to different tenants.");

            if (absoluteLifetime <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(absoluteLifetime));

            Id = Guid.NewGuid();
            TenantId = employee.TenantId;
            EmployeeId = employee.Id;
            DeviceId = device.Id;
            CreatedAt = now;
            LastSeenAt = now;
            ExpiresAt = now + absoluteLifetime;
        }

        public Guid Id { get; private set; }

        public int TenantId { get; private set; }

        public int EmployeeId { get; private set; }

        public int DeviceId { get; private set; }

        public Employee? Employee { get; private set; }

        public Device? Device { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        /// <summary>Last request, used for the idle timeout.</summary>
        public DateTimeOffset LastSeenAt { get; private set; }

        /// <summary>Absolute end of the session, whatever the activity.</summary>
        public DateTimeOffset ExpiresAt { get; private set; }

        public DateTimeOffset? RevokedAt { get; private set; }

        [MaxLength(50)]
        public string? RevokedReason { get; private set; }

        public bool IsActive(DateTimeOffset now, TimeSpan idleTimeout) =>
            RevokedAt is null && now < ExpiresAt && now - LastSeenAt < idleTimeout;

        public void Touch(DateTimeOffset now)
        {
            if (now > LastSeenAt)
                LastSeenAt = now;
        }

        public void Revoke(DateTimeOffset now, string reason)
        {
            if (RevokedAt is not null)
                return;

            RevokedAt = now;
            RevokedReason = reason;
        }
    }
}
