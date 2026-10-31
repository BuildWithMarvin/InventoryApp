using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using InventoryApp.Api.Models;

namespace InventoryApp.Api.Security
{
    /// <summary>
    /// Issues the signed access token for a session. The token only says who the session is for and
    /// where to find it; whether it is still valid, and what the employee may do, is looked up in the
    /// database on every request (see <see cref="EmployeeAuthentication"/>).
    /// </summary>
    public class JwtTokenService
    {
        private readonly AuthOptions _options;

        public JwtTokenService(IOptions<AuthOptions> options)
        {
            _options = options.Value;
        }

        public string Create(EmployeeSession session)
        {
            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = _options.Issuer,
                Audience = _options.Audience,
                Claims = new Dictionary<string, object>
                {
                    [InventoryClaims.Subject] = session.EmployeeId.ToString(),
                    [InventoryClaims.Tenant] = session.TenantId.ToString(),
                    [InventoryClaims.Session] = session.Id.ToString(),
                    [InventoryClaims.Device] = session.DeviceId.ToString(),
                },
                NotBefore = session.CreatedAt.UtcDateTime,
                IssuedAt = session.CreatedAt.UtcDateTime,
                Expires = session.ExpiresAt.UtcDateTime,
                SigningCredentials = new SigningCredentials(SigningKey(_options), SecurityAlgorithms.HmacSha256),
            };

            return new JsonWebTokenHandler().CreateToken(descriptor);
        }

        public static SymmetricSecurityKey SigningKey(AuthOptions options) =>
            new(Encoding.UTF8.GetBytes(options.SigningKey));
    }
}
