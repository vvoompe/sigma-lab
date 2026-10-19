using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Lab.Api.Security;
using Microsoft.IdentityModel.Tokens;

namespace Lab.Application.Tests;

internal static class ApiTestJwt
{
    private static readonly string SigningKey = CreateAndConfigureSigningKey();

    public static void EnsureConfigured()
    {
        _ = SigningKey;
    }

    public static void Authenticate(
        HttpClient client,
        string role = "Researcher",
        byte[]? signingKey = null)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(signingKey ?? Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: LabJwt.Issuer,
            audience: LabJwt.Audience,
            claims:
            [
                new Claim("sub", "1"),
                new Claim("role", role),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }

    private static string CreateAndConfigureSigningKey()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Environment.SetEnvironmentVariable("Jwt__Key", key);
        return key;
    }
}
