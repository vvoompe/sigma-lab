using Lab.Domain.Validation;
using Lab.Domain.Users;

namespace Lab.Domain.Security;

/// <summary>Облікові дані користувача, відокремлені від його профілю.</summary>
public sealed class UserCredential
{
    private UserCredential() { }

    public UserCredential(
        User user,
        string passwordHash,
        DateTime passwordUpdatedAt,
        UserRole role = UserRole.Researcher)
    {
        ArgumentNullException.ThrowIfNull(user);
        User = user;
        UserId = user.Id;
        PasswordHash = ValidatePasswordHash(passwordHash);
        DomainRules.Utc(passwordUpdatedAt, nameof(passwordUpdatedAt));
        PasswordUpdatedAt = passwordUpdatedAt;
        EnsureRole(role);
        Role = role;
    }

    public int UserId { get; private set; }
    public User? User { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public DateTime PasswordUpdatedAt { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTime? LockoutUntil { get; private set; }
    public UserRole Role { get; private set; }

    public bool IsLockedOut(DateTime nowUtc)
    {
        DomainRules.Utc(nowUtc, nameof(nowUtc));
        return LockoutUntil is { } lockoutUntil && nowUtc < lockoutUntil;
    }

    public void RecordFailedAttempt(int maximumAttempts, TimeSpan lockoutDuration, DateTime nowUtc)
    {
        DomainRules.Utc(nowUtc, nameof(nowUtc));
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lockoutDuration, TimeSpan.Zero);

        if (IsLockedOut(nowUtc))
        {
            return;
        }

        if (LockoutUntil is not null)
        {
            FailedAttempts = 0;
            LockoutUntil = null;
        }

        FailedAttempts = checked(FailedAttempts + 1);
        if (FailedAttempts >= maximumAttempts)
        {
            LockoutUntil = nowUtc.Add(lockoutDuration);
        }
    }

    public void ResetFailedAttempts()
    {
        FailedAttempts = 0;
        LockoutUntil = null;
    }

    public void UpdatePasswordHash(string passwordHash, DateTime passwordUpdatedAt)
    {
        DomainRules.Utc(passwordUpdatedAt, nameof(passwordUpdatedAt));
        PasswordHash = ValidatePasswordHash(passwordHash);
        PasswordUpdatedAt = passwordUpdatedAt;
        ResetFailedAttempts();
    }

    public bool ChangeRole(UserRole role)
    {
        EnsureRole(role);
        if (Role == role)
        {
            return false;
        }

        Role = role;
        return true;
    }

    private static string ValidatePasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        if (passwordHash.Length > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(passwordHash));
        }

        return passwordHash;
    }

    private static void EnsureRole(UserRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }
    }
}
