using System.ComponentModel.DataAnnotations;
using InventoryApp.Api.Security;

namespace InventoryApp.Tests.Unit;

[Trait("Category", "Unit")]
public class EntraOptionsTests
{
    private const string Directory = "11111111-1111-4111-8111-111111111111";
    private const string Client = "22222222-2222-4222-8222-222222222222";

    private static List<string> Errors(EntraOptions options) =>
        options.Validate(new ValidationContext(options)).SelectMany(r => r.MemberNames).ToList();

    [Fact]
    public void WithoutAnySettings_EntraIsOff_AndValid()
    {
        var options = new EntraOptions();

        Assert.False(options.Enabled);
        Assert.Empty(Errors(options));
    }

    [Fact]
    public void CompleteSettings_AreValid_AndDeriveTheAuthorityAndAudiences()
    {
        var options = new EntraOptions { TenantId = Directory, ClientId = Client };

        Assert.True(options.Enabled);
        Assert.Empty(Errors(options));
        Assert.Equal($"https://login.microsoftonline.com/{Directory}/v2.0", options.GetAuthority());
        Assert.Equal(new[] { Client, $"api://{Client}" }, options.GetAudiences());
        Assert.Equal(Guid.Parse(Directory), options.GetDirectoryId());
    }

    [Fact]
    public void AnExplicitAudience_ReplacesTheDefaults()
    {
        var options = new EntraOptions { TenantId = Directory, ClientId = Client, Audience = "api://inventory" };

        Assert.Equal(new[] { "api://inventory" }, options.GetAudiences());
    }

    [Theory]
    [InlineData(Directory, null)]
    [InlineData(null, Client)]
    [InlineData("not-a-guid", Client)]
    [InlineData(Directory, "not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000", Client)]
    public void PartialOrMalformedSettings_AreInvalid_SoTheApplicationDoesNotStartHalfConfigured(string? tenantId, string? clientId)
    {
        var options = new EntraOptions { TenantId = tenantId, ClientId = clientId };

        Assert.NotEmpty(Errors(options));
    }

    [Fact]
    public void OtherSettings_AreChecked()
    {
        Assert.Contains("Instance", Errors(new EntraOptions { TenantId = Directory, ClientId = Client, Instance = "http://login.example/" }));
        Assert.Contains("RequiredScope", Errors(new EntraOptions { TenantId = Directory, ClientId = Client, RequiredScope = " " }));
        Assert.Contains("PlatformAdminRole", Errors(new EntraOptions { TenantId = Directory, ClientId = Client, PlatformAdminRole = "" }));
        Assert.Contains("ClockSkew", Errors(new EntraOptions { TenantId = Directory, ClientId = Client, ClockSkew = TimeSpan.FromMinutes(30) }));
    }
}
