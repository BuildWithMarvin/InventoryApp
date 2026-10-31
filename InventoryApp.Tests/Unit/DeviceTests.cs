using InventoryApp.Api.Models;
using InventoryApp.Api.Security;

namespace InventoryApp.Tests.Unit;

[Trait("Category", "Unit")]
public class DeviceCredentialsTests
{
    [Fact]
    public void GeneratedValues_HaveTheExpectedShape()
    {
        Assert.Equal(10, DeviceCredentials.NewDeviceCode().Length);
        Assert.Equal(26, DeviceCredentials.NewEnrollmentCode().Length);

        var credential = DeviceCredentials.NewCredential();
        Assert.Equal(43, credential.Length); // 256 bits, base64url without padding
        Assert.DoesNotContain('+', credential);
        Assert.DoesNotContain('/', credential);
        Assert.DoesNotContain('=', credential);
    }

    [Fact]
    public void Codes_UseOnlyUnambiguousCharacters()
    {
        var code = DeviceCredentials.NewDeviceCode() + DeviceCredentials.NewEnrollmentCode();

        Assert.All(code, c => Assert.DoesNotContain(c, "IO01"));
        Assert.All(code, c => Assert.True(char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)));
    }

    [Fact]
    public void GeneratedValues_AreUnique()
    {
        var credentials = Enumerable.Range(0, 200).Select(_ => DeviceCredentials.NewCredential());
        var codes = Enumerable.Range(0, 200).Select(_ => DeviceCredentials.NewEnrollmentCode());

        Assert.Equal(200, credentials.Distinct().Count());
        Assert.Equal(200, codes.Distinct().Count());
    }

    [Fact]
    public void Hash_IsStableLowercaseHex_OfFixedLength()
    {
        var hash = DeviceCredentials.Hash("secret");

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, DeviceCredentials.Hash("secret"));
        Assert.Equal(hash.ToLowerInvariant(), hash);
        Assert.NotEqual(hash, DeviceCredentials.Hash("Secret"));
    }

    [Fact]
    public void Matches_ComparesAgainstTheStoredHash()
    {
        var hash = DeviceCredentials.Hash("secret");

        Assert.True(DeviceCredentials.Matches("secret", hash));
        Assert.False(DeviceCredentials.Matches("other", hash));
        Assert.False(DeviceCredentials.Matches(null, hash));
        Assert.False(DeviceCredentials.Matches("secret", null));
        Assert.False(DeviceCredentials.Matches("", hash));
    }

    [Theory]
    [InlineData("abcde-fghjk", "ABCDEFGHJK")]
    [InlineData("  ab cd-EF  ", "ABCDEF")]
    [InlineData(null, "")]
    public void NormalizeEnrollmentCode_AcceptsTheCodeAsTyped(string? input, string expected)
    {
        Assert.Equal(expected, DeviceCredentials.NormalizeEnrollmentCode(input));
    }
}

[Trait("Category", "Unit")]
public class DeviceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_CreatesAnActiveUnenrolledDevice_WithAPublicCode()
    {
        var device = new Device(1, "  Dock 1  ");

        Assert.Equal("Dock 1", device.Name);
        Assert.Equal(10, device.DeviceCode.Length);
        Assert.True(device.IsActive);
        Assert.False(device.IsEnrolled);
    }

    [Fact]
    public void Constructor_Throws_ForABlankNameOrMissingTenant()
    {
        Assert.Throws<ArgumentException>(() => new Device(1, "  "));
        Assert.Throws<ArgumentException>(() => new Device(0, "Dock 1"));
        Assert.Throws<ArgumentException>(() => new Device(1, new string('a', 101)));
    }

    [Fact]
    public void EnrollmentCode_IsStoredHashed_AndExpiresAfter24Hours()
    {
        var device = new Device(1, "Dock 1");

        var code = device.IssueEnrollmentCode(Now);

        Assert.Equal(DeviceCredentials.Hash(code), device.EnrollmentCodeHash);
        Assert.DoesNotContain(code, device.EnrollmentCodeHash);
        Assert.Equal(Now + Device.EnrollmentCodeLifetime, device.EnrollmentCodeExpiresAt);
    }

    [Fact]
    public void CompleteEnrollment_ReturnsACredential_StoresOnlyItsHash_AndBurnsTheCode()
    {
        var device = new Device(1, "Dock 1");
        device.IssueEnrollmentCode(Now);

        var credential = device.CompleteEnrollment(Now.AddMinutes(5));

        Assert.NotNull(credential);
        Assert.True(device.IsEnrolled);
        Assert.Equal(Now.AddMinutes(5), device.EnrolledAt);
        Assert.True(device.VerifyCredential(credential));
        Assert.DoesNotContain(credential, device.CredentialHash);
        Assert.Null(device.EnrollmentCodeHash);
        Assert.Null(device.CompleteEnrollment(Now.AddMinutes(6))); // single use
    }

    [Fact]
    public void CompleteEnrollment_ReturnsNull_WhenTheCodeExpired()
    {
        var device = new Device(1, "Dock 1");
        device.IssueEnrollmentCode(Now);

        Assert.Null(device.CompleteEnrollment(Now + Device.EnrollmentCodeLifetime));
        Assert.False(device.IsEnrolled);
    }

    [Fact]
    public void CompleteEnrollment_ReturnsNull_WithoutACode_OrWhenInactive()
    {
        var withoutCode = new Device(1, "Dock 1");
        Assert.Null(withoutCode.CompleteEnrollment(Now));

        var inactive = new Device(1, "Dock 2");
        inactive.IssueEnrollmentCode(Now);
        inactive.Deactivate();
        Assert.Null(inactive.CompleteEnrollment(Now));
    }

    [Fact]
    public void IssuingANewCode_InvalidatesTheCurrentCredential()
    {
        var device = new Device(1, "Dock 1");
        device.IssueEnrollmentCode(Now);
        var oldCredential = device.CompleteEnrollment(Now)!;

        device.IssueEnrollmentCode(Now.AddDays(1));

        Assert.False(device.VerifyCredential(oldCredential));
        Assert.False(device.IsEnrolled);
    }

    [Fact]
    public void VerifyCredential_RejectsWrongMissingAndUnenrolled()
    {
        var device = new Device(1, "Dock 1");
        Assert.False(device.VerifyCredential("anything"));

        device.IssueEnrollmentCode(Now);
        var credential = device.CompleteEnrollment(Now)!;

        Assert.True(device.VerifyCredential(credential));
        Assert.False(device.VerifyCredential(credential + "x"));
        Assert.False(device.VerifyCredential(null));
    }

    [Fact]
    public void RevokeCredential_CutsOffTheEnrolledBrowser()
    {
        var device = new Device(1, "Dock 1");
        device.IssueEnrollmentCode(Now);
        var credential = device.CompleteEnrollment(Now)!;

        device.RevokeCredential();

        Assert.False(device.VerifyCredential(credential));
    }
}

[Trait("Category", "Unit")]
public class EmployeeSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Absolute = TimeSpan.FromHours(12);

    [Fact]
    public void NewSession_IsActive_UntilIdleOrAbsoluteTimeout()
    {
        var session = NewSession();

        Assert.True(session.IsActive(Now, Idle));
        Assert.True(session.IsActive(Now.AddMinutes(29), Idle));
        Assert.False(session.IsActive(Now.AddMinutes(30), Idle));
        Assert.Equal(Now + Absolute, session.ExpiresAt);
    }

    [Fact]
    public void Touch_ExtendsTheIdleTimeout_ButNeverTheAbsoluteOne()
    {
        var session = NewSession();

        for (var minutes = 20; minutes < 12 * 60; minutes += 20)
            session.Touch(Now.AddMinutes(minutes));

        Assert.True(session.IsActive(Now.AddHours(11).AddMinutes(30), Idle));
        Assert.False(session.IsActive(Now.AddHours(12), Idle));
    }

    [Fact]
    public void Touch_IgnoresAnEarlierTime()
    {
        var session = NewSession();
        session.Touch(Now.AddMinutes(10));

        session.Touch(Now.AddMinutes(5));

        Assert.Equal(Now.AddMinutes(10), session.LastSeenAt);
    }

    [Fact]
    public void Revoke_EndsTheSession_AndKeepsTheFirstReason()
    {
        var session = NewSession();

        session.Revoke(Now.AddMinutes(1), "logout");
        session.Revoke(Now.AddMinutes(2), "other");

        Assert.False(session.IsActive(Now.AddMinutes(1), Idle));
        Assert.Equal(Now.AddMinutes(1), session.RevokedAt);
        Assert.Equal("logout", session.RevokedReason);
    }

    [Fact]
    public void Constructor_Throws_ForUnsavedOrMismatchedEmployeeAndDevice()
    {
        Assert.Throws<ArgumentException>(() =>
            new EmployeeSession(new Employee(1, "Tom", "EMP-1"), new Device(1, "Dock"), Now, Absolute));

        Assert.Throws<ArgumentException>(() =>
            new EmployeeSession(Saved(new Employee(1, "Tom", "EMP-1"), 5), Saved(new Device(2, "Dock"), 7), Now, Absolute));
    }

    private static EmployeeSession NewSession() =>
        new(Saved(new Employee(1, "Tom", "EMP-1"), 5), Saved(new Device(1, "Dock"), 7), Now, Absolute);

    // Simulates what the database does on insert.
    private static T Saved<T>(T entity, int id) where T : class
    {
        typeof(T).GetProperty("Id")!.SetValue(entity, id);
        return entity;
    }
}
