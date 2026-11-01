using Lab.Application.Abstractions;
using Lab.Domain.Security;
using Lab.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lab.Application.Security;

public sealed class InitialAdministratorSeeder(
    IAppDbContext db,
    IPasswordHasher<UserCredential> passwordHasher) : IInitialAdministratorSeeder
{
    public async Task SeedAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password))
            return;

        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length is < 12 or > 128)
            throw new ArgumentOutOfRangeException(nameof(password), "Пароль має містити від 12 до 128 символів.");

        if (await db.UserCredentials.AnyAsync(
                item => item.Role == UserRole.Administrator,
                cancellationToken))
        {
            return;
        }

        var user = new User("Initial administrator", email.Trim());
        var passwordUpdatedAt = DateTime.UtcNow;
        var hashingContext = new UserCredential(user, "hashing", passwordUpdatedAt, UserRole.Administrator);
        db.UserCredentials.Add(new UserCredential(
            user,
            passwordHasher.HashPassword(hashingContext, password),
            passwordUpdatedAt,
            UserRole.Administrator));
        await db.SaveChangesAsync(cancellationToken);
    }
}
