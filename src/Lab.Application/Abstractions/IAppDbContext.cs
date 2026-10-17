using Lab.Domain.Audit;
using Lab.Domain.Security;
using Lab.Domain.Sites;
using Lab.Domain.Users;
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

    /// <summary>Профілі користувачів.</summary>
    DbSet<User> Users { get; }

    /// <summary>Облікові дані користувачів.</summary>
    DbSet<UserCredential> UserCredentials { get; }

    /// <summary>Аудит подій безпеки.</summary>
    DbSet<AuditEntry> AuditEntries { get; }

    /// <summary>Зберігає зміни однієї одиниці роботи.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
