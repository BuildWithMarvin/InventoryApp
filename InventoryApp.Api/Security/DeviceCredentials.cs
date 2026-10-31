using System.Security.Cryptography;
using System.Text;

namespace InventoryApp.Api.Security
{
    /// <summary>
    /// Generates and hashes the random values that identify and authenticate a device.
    /// All values come from a cryptographic random generator. The credential and the enrollment
    /// code are high-entropy, so a plain SHA-256 is enough to store them (no password hashing needed).
    /// </summary>
    public static class DeviceCredentials
    {
        // No I, O, 0 or 1: the codes are read from screens and typed on small keyboards.
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        /// <summary>Public identifier, printed as the device barcode. Not a secret.</summary>
        public static string NewDeviceCode() => RandomCode(10);

        /// <summary>One-time code (130 bits) an admin hands to the person setting up a device.</summary>
        public static string NewEnrollmentCode() => RandomCode(26);

        /// <summary>The device's long-lived secret (256 bits). It only ever lives in an HttpOnly cookie.</summary>
        public static string NewCredential() =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>Lowercase hex SHA-256, 64 characters.</summary>
        public static string Hash(string value) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        /// <summary>Accepts the code as typed: any case, with or without dashes and spaces.</summary>
        public static string NormalizeEnrollmentCode(string? input) =>
            new string((input ?? string.Empty).Where(c => c != '-' && !char.IsWhiteSpace(c)).ToArray())
                .ToUpperInvariant();

        /// <summary>Compares a presented secret with a stored hash without leaking where they differ.</summary>
        public static bool Matches(string? presented, string? storedHash)
        {
            if (string.IsNullOrEmpty(presented) || string.IsNullOrEmpty(storedHash))
                return false;

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(Hash(presented)),
                Encoding.UTF8.GetBytes(storedHash));
        }

        private static string RandomCode(int length) =>
            new(RandomNumberGenerator.GetItems<char>(Alphabet, length));
    }
}
