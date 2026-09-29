using Lab.Application.Abstractions;
using Lab.Application.Sites;
using Microsoft.Extensions.DependencyInjection;

namespace Lab.Application.DependencyInjection;

/// <summary>Реєстрація сервісів прикладного шару.</summary>
public static class LabApplicationServiceCollectionExtensions
{
    /// <summary>Додає сервіси прикладного шару в контейнер залежностей.</summary>
    public static IServiceCollection AddLabApplication(this IServiceCollection services)
    {
        services.AddScoped<ISiteService, SiteService>();
        return services;
    }
}
