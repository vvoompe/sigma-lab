using Lab.Application.Abstractions;
using Lab.Domain.Sites;
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

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Усі Fluent-конфігурації з цієї assembly застосовуються автоматично:
        // нова сутність додається без правки цього методу.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LabDbContext).Assembly);
    }
}
