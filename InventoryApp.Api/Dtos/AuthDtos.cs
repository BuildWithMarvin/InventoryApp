using System.ComponentModel.DataAnnotations;

namespace InventoryApp.Api.Dtos
{
    /// <summary>The one-time code an admin gave for this device, as typed or scanned.</summary>
    public record EnrollRequest([Required, MaxLength(100)] string EnrollmentCode);

    /// <summary>
    /// The device credential itself is never in the body: it only travels in an HttpOnly cookie, out of
    /// reach of client-side code. DeviceCode is public and is what the client sends at login.
    /// </summary>
    public record EnrollResponse(string DeviceCode, string DeviceName);

    /// <summary>The device is identified by DeviceCode and authenticated by its cookie; the employee by badge (and PIN).</summary>
    public record LoginRequest(
        [Required, MaxLength(20)] string DeviceCode,
        [Required, MaxLength(50)] string BadgeBarcode,
        [MaxLength(20)] string? Pin = null);

    public record LoginResponse(
        string AccessToken,
        DateTimeOffset ExpiresAt,
        EmployeeDto Employee,
        bool MustChangePin);

    /// <summary>CurrentPin is only needed if the employee already has a PIN.</summary>
    public record ChangePinRequest([MaxLength(20)] string? CurrentPin, [Required, MaxLength(20)] string NewPin);
}
