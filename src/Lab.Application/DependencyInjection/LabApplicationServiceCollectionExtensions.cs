using Lab.Application.Abstractions;
using Lab.Application.Security;
using Lab.Application.Sites;
using Lab.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Lab.Application.DependencyInjection;

/// <summary>Реєстрація сервісів прикладного шару.</summary>
public static class LabApplicationServiceCollectionExtensions
{
    /// <summary>Додає сервіси прикладного шару в контейнер залежностей.</summary>
    public static IServiceCollection AddLabApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPasswordHasher<UserCredential>, PasswordHasher<UserCredential>>();
        services.AddScoped<ISiteService, SiteService>();
        return services;
    }
}
