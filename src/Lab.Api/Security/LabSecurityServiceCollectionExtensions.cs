using System.Text;
using Lab.Application.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Lab.Api.Security;

/// <summary>Налаштування автентифікації та політик доступу API.</summary>
public static class LabSecurityServiceCollectionExtensions
{
    /// <summary>Додає JWT-автентифікацію та захист усіх endpoint-ів API.</summary>
    public static IServiceCollection AddLabSecurity(this IServiceCollection services)
    {
        var signingKey = CreateSigningKey(Environment.GetEnvironmentVariable("Jwt__Key"));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, ClaimsCurrentUser>();
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.IncludeErrorDetails = false;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    ValidateIssuer = true,
                    ValidIssuer = LabJwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = LabJwt.Audience,
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                };
            });

        services.AddAuthorization(options =>
        {
            var roles = new[]
            {
                LabRoles.Researcher,
                LabRoles.LabManager,
                LabRoles.Administrator,
            };

            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireRole(roles)
                .Build();

            foreach (var role in roles)
            {
                options.AddPolicy(role, policy =>
                    policy.RequireAuthenticatedUser().RequireRole(role));
            }
        });

        return services;
    }

    internal static SymmetricSecurityKey CreateSigningKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "Задайте секрет підпису JWT у змінній середовища Jwt__Key.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(key);
        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                "Значення Jwt__Key має містити щонайменше 32 байти UTF-8 для HS256.");
        }

        return new SymmetricSecurityKey(keyBytes);
    }
}
