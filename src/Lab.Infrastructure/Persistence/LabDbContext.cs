using Lab.Application.Abstractions;
using Lab.Domain.Audit;
using Lab.Domain.Security;
using Lab.Domain.Sites;
using Lab.Domain.Users;
using Lab.Domain.Instruments;
using Lab.Domain.Experiments;
using Lab.Domain.Measurements;
using Lab.Domain.ReferenceValues;
using Microsoft.EntityFrameworkCore;

namespace Lab.Infrastructure.Persistence;

/// <summary>
/// Контекст EF Core. Коротка одиниця роботи: завантажити стан, змінити, зберегти.
/// Реалізує <see cref="IAppDbContext"/>, тому прикладна логіка бачить лише інтерфейс.
/// </summary>
/// <param name="options">Налаштування контексту (провайдер, рядок підключення, assembly міграцій).</param>
public sealed class LabDbContext(DbContextOptions<LabDbContext> options)
    : DbContext(options), IAppDbContext
{
    /// <summary>Майданчики.</summary>
    public DbSet<Site> Sites => Set<Site>();

    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<Experiment> Experiments => Set<Experiment>();
    public DbSet<Measurement> Measurements => Set<Measurement>();
    public DbSet<ReferenceValue> ReferenceValues => Set<ReferenceValue>();

    /// <inheritdoc />
    public DbSet<User> Users => Set<User>();

    /// <inheritdoc />
    public DbSet<UserCredential> UserCredentials => Set<UserCredential>();

    /// <inheritdoc />
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Усі Fluent-конфігурації з цієї assembly застосовуються автоматично:
        // нова сутність додається без правки цього методу.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LabDbContext).Assembly);
    }
}
