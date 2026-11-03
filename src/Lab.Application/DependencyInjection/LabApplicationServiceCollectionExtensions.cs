using Lab.Application.Abstractions;
using Lab.Application.Security;
using Lab.Application.Sites;
using Lab.Application.Resources;
using Lab.Application.Sync;
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
        services.AddScoped<IInitialAdministratorSeeder, InitialAdministratorSeeder>();
        services.AddScoped<ISiteService, SiteService>();
        services.AddScoped<IInstrumentService, InstrumentService>();
        services.AddScoped<IExperimentAccessService, ExperimentAccessService>();
        services.AddScoped<IExperimentService, ExperimentService>();
        services.AddScoped<IExperimentExportService, ExperimentExportService>();
        services.AddScoped<IExperimentContextService, ExperimentContextService>();
        services.AddScoped<IMeasurementSyncService, MeasurementSyncService>();
        services.AddScoped<MeasurementSyncAttempt>();
        services.AddScoped<IReferenceValueService, ReferenceValues.ReferenceValueService>();
        return services;
    }
}
