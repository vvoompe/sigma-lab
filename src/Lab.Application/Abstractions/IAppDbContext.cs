using Lab.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Lab.Application.Abstractions;

/// <summary>
/// Мінімальна поверхня бази даних, потрібна прикладній логіці.
/// Сервіси залежать від цього інтерфейсу, тому не знають ні про СУБД, ні про рядок
/// підключення, ні про конкретний клас контексту.
/// </summary>
public interface IAppDbContext
{
    /// <summary>Майданчики.</summary>
    DbSet<Site> Sites { get; }

    /// <summary>Зберігає зміни однієї одиниці роботи.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
