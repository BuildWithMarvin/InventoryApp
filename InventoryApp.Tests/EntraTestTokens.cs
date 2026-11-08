using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Tokens that look like Microsoft Entra v2.0 access tokens, signed with a key the tests own. The
/// application under test validates them with the real Entra scheme; only the key discovery
/// (OpenID metadata over the internet) is replaced by this key (see <see cref="ApiFactory"/>).
/// </summary>
public static class EntraTestTokens
{
    public static readonly Guid DirectoryId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid ClientId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    public const string Scope = "access_as_user";
    public const string PlatformAdminRole = "Platform.Admin";

    public static string Authority => $"https://login.microsoftonline.com/{DirectoryId}/v2.0";

    /// <summary>The key the application trusts (what Microsoft's published keys are in production).</summary>
    public static RsaSecurityKey SigningKey { get; } = new(RSA.Create(2048)) { KeyId = "entra-test-key" };

    // Same key id, different key: a signature made by someone else.
    private static readonly RsaSecurityKey ForeignKey = new(RSA.Create(2048)) { KeyId = "entra-test-key" };

    /// <param name="claims">Replaces a claim; a null value removes it (e.g. <c>["tid"] = null</c>).</param>
    public static string Create(
        DateTimeOffset now,
        Guid? objectId = null,
        Guid? directoryId = null,
        string? issuer = null,
        string? audience = null,
        string[]? roles = null,
        TimeSpan? lifetime = null,
        bool omitExpiry = false,
        bool foreignSignature = false,
        string? algorithm = null,
        bool unsigned = false,
        DateTimeOffset? notBefore = null,
        SigningCredentials? credentials = null,
        IDictionary<string, object?>? claims = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["tid"] = (directoryId ?? DirectoryId).ToString(),
            ["oid"] = (objectId ?? Guid.NewGuid()).ToString(),
            ["scp"] = Scope,
            ["name"] = "Test Administrator",
            ["ver"] = "2.0",
        };

        if (roles is not null)
            payload["roles"] = roles;

        foreach (var (type, value) in claims ?? new Dictionary<string, object?>())
            payload[type] = value;

        foreach (var removed in payload.Where(c => c.Value is null).Select(c => c.Key).ToList())
            payload.Remove(removed);

        var signing = unsigned
            ? null
            : credentials ?? new SigningCredentials(foreignSignature ? ForeignKey : SigningKey, algorithm ?? SecurityAlgorithms.RsaSha256);

        if (omitExpiry)
        {
            // The handler gives a descriptor without Expires a default lifetime, so write the payload ourselves.
            payload["iss"] = issuer ?? Authority;
            payload["aud"] = audience ?? ClientId.ToString();
            payload["nbf"] = (notBefore ?? now).ToUnixTimeSeconds();
            payload["iat"] = now.ToUnixTimeSeconds();

            return signing is null
                ? new JsonWebTokenHandler().CreateToken(JsonSerializer.Serialize(payload))
                : new JsonWebTokenHandler().CreateToken(JsonSerializer.Serialize(payload), signing);
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Authority,
            Audience = audience ?? ClientId.ToString(),
            Claims = payload.ToDictionary(c => c.Key, c => c.Value!),
            NotBefore = (notBefore ?? now).UtcDateTime,
            IssuedAt = now.UtcDateTime,
            Expires = (now + (lifetime ?? TimeSpan.FromHours(1))).UtcDateTime,
            SigningCredentials = signing,
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
