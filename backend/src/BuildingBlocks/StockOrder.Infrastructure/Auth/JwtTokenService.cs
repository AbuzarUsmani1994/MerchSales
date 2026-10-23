using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Infrastructure.Auth;

internal sealed class JwtTokenService(ISigningCertificateProvider certificateProvider, IClock clock) : IJwtTokenService
{
    private const int ExpiresInSeconds = 15 * 60;

    public IssuedAccessToken IssueAccessToken(AccessTokenRequest request)
    {
        var credentials = new SigningCredentials(
            new X509SecurityKey(certificateProvider.Certificate), SecurityAlgorithms.RsaSha256);

        var now = clock.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.UserId.ToString()),
            new("tid", request.TenantId.ToString()),
            new("tcode", request.TenantCode),
            new("sst", request.SecurityStamp.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        if (request.DeviceId is { } deviceId)
        {
            claims.Add(new Claim("did", deviceId.ToString()));
        }

        var token = new JwtSecurityToken(
            claims: claims,
            notBefore: now,
            expires: now.AddSeconds(ExpiresInSeconds),
            signingCredentials: credentials);

        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), ExpiresInSeconds);
    }
}
