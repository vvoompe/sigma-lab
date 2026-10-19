using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Lab.Application.Security;
using Lab.Contracts.Security;
using Microsoft.IdentityModel.Tokens;

namespace Lab.Api.Security;

/// <summary>Видає підписані HS256 JWT із тими самими параметрами, що й валідатор.</summary>
public sealed class JwtAccessTokenIssuer(SymmetricSecurityKey signingKey) : IAccessTokenIssuer
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    public AccessTokenResponse Create(AuthenticatedUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.Add(TokenLifetime);
        var token = new JwtSecurityToken(
            issuer: LabJwt.Issuer,
            audience: LabJwt.Audience,
            claims:
            [
                new Claim("sub", user.Id.ToString(CultureInfo.InvariantCulture)),
                new Claim("role", user.Role.ToString()),
            ],
            notBefore: issuedAt,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));

        return new AccessTokenResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            "Bearer",
            expiresAt,
            user.Id,
            user.Name,
            user.Role.ToString());
    }
}
