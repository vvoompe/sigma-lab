using Lab.Application.Abstractions;
using Lab.Contracts.Security;
using Lab.Domain.Audit;
using Lab.Domain.Security;
using Lab.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Lab.Application.Security;

/// <summary>Реалізація реєстрації, входу та керування ролями.</summary>
public sealed class AuthService(
    IAppDbContext db,
    IPasswordHasher<UserCredential> passwordHasher) : IAuthService
{
    private const int MaximumFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly PasswordHasher<UserCredential> DummyPasswordHasher = new();
    private static readonly Lazy<UserCredential> DummyCredential = new(CreateDummyCredential);

    /// <inheritdoc />
    public async Task<AuthenticatedUser?> RegisterAsync(
        RegisterUserRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePassword(request.Password);

        var user = new User(request.Name, request.Email);
        if (await db.Users.AnyAsync(
                existing => existing.NormalizedEmail == user.NormalizedEmail,
                cancellationToken))
        {
            return null;
        }

        var passwordUpdatedAt = DateTime.UtcNow;
        var hashingContext = new UserCredential(user, "hashing", passwordUpdatedAt);
        var credential = new UserCredential(
            user,
            passwordHasher.HashPassword(hashingContext, request.Password),
            passwordUpdatedAt);
        db.UserCredentials.Add(credential);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await db.Users.AnyAsync(
                    existing => existing.NormalizedEmail == user.NormalizedEmail,
                    cancellationToken))
            {
                return null;
            }

            throw;
        }

        return new AuthenticatedUser(user.Id, user.Name, credential.Role);
    }

    /// <inheritdoc />
    public async Task<AuthenticatedUser?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Email);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Password);

        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var credential = await db.UserCredentials
            .Include(item => item.User)
            .SingleOrDefaultAsync(
                item => item.User != null && item.User.NormalizedEmail == normalizedEmail,
                cancellationToken);
        if (credential?.User is not { } user)
        {
            var dummy = DummyCredential.Value;
            _ = DummyPasswordHasher.VerifyHashedPassword(dummy, dummy.PasswordHash, request.Password);
            return null;
        }

        var verification = passwordHasher.VerifyHashedPassword(
            credential,
            credential.PasswordHash,
            request.Password);
        var now = DateTime.UtcNow;
        if (credential.IsLockedOut(now) || verification == PasswordVerificationResult.Failed)
        {
            if (!credential.IsLockedOut(now))
            {
                credential.RecordFailedAttempt(MaximumFailedAttempts, LockoutDuration, now);
                await db.SaveChangesAsync(cancellationToken);
            }

            return null;
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            credential.UpdatePasswordHash(passwordHasher.HashPassword(credential, request.Password), now);
        }
        else
        {
            credential.ResetFailedAttempts();
        }

        await db.SaveChangesAsync(cancellationToken);
        return new AuthenticatedUser(user.Id, user.Name, credential.Role);
    }

    /// <inheritdoc />
    public async Task<RoleChangeStatus> ChangeRoleAsync(
        int actorId,
        int targetUserId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetUserId);
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        if (actorId == targetUserId)
        {
            return RoleChangeStatus.SelfChangeForbidden;
        }

        var credential = await db.UserCredentials
            .SingleOrDefaultAsync(item => item.UserId == targetUserId, cancellationToken);
        if (credential is null)
        {
            return RoleChangeStatus.TargetNotFound;
        }

        var previousRole = credential.Role;
        if (!credential.ChangeRole(role))
        {
            return RoleChangeStatus.Unchanged;
        }

        db.AuditEntries.Add(new AuditEntry(
            actorId,
            "user.role.changed",
            "UserCredential",
            targetUserId,
            DateTime.UtcNow,
            $"{previousRole} -> {credential.Role}"));
        await db.SaveChangesAsync(cancellationToken);

        return RoleChangeStatus.Changed;
    }

    private static void ValidatePassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length is < 12 or > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(password), "Пароль має містити від 12 до 128 символів.");
        }
    }

    private static UserCredential CreateDummyCredential()
    {
        var now = DateTime.UtcNow;
        var user = new User("Dummy account", "dummy@invalid.local");
        var hashingContext = new UserCredential(user, "hashing", now);
        var randomPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return new UserCredential(
            user,
            DummyPasswordHasher.HashPassword(hashingContext, randomPassword),
            now);
    }
}
