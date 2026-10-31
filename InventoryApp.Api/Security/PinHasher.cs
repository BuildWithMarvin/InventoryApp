using BC = BCrypt.Net.BCrypt;

namespace InventoryApp.Api.Security
{
    /// <summary>
    /// PINs are exactly six digits and stored as BCrypt hashes. A six-digit PIN has
    /// little entropy of its own; the lockout on <c>Employee</c> and the login rate limit
    /// are what protect it, so never log or return a PIN or its hash.
    /// </summary>
    public static class PinHasher
    {
        public const int PinLength = 6;

        // Roughly a quarter of a second per hash on current hardware.
        private const int WorkFactor = 12;

        public static bool IsValidFormat(string? pin) =>
            pin is { Length: PinLength } && pin.All(char.IsAsciiDigit);

        public static string Hash(string pin)
        {
            if (!IsValidFormat(pin))
                throw new ArgumentException($"A PIN must consist of exactly {PinLength} digits.", nameof(pin));

            return BC.HashPassword(pin, WorkFactor);
        }

        /// <summary>False for a wrong PIN, a malformed PIN or a missing or corrupt hash.</summary>
        public static bool Verify(string? pin, string? hash)
        {
            if (!IsValidFormat(pin) || string.IsNullOrEmpty(hash))
                return false;

            try
            {
                return BC.Verify(pin, hash);
            }
            catch (Exception ex) when (ex is ArgumentException or BCrypt.Net.SaltParseException)
            {
                return false;
            }
        }
    }
}
