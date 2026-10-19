using Lab.Domain.Security;
using Lab.Domain.Users;

namespace Lab.Domain.Tests;

public sealed class UserCredentialTests
{
    [Fact]
    public void Нові_облікові_дані_мають_роль_дослідника_та_нуль_помилок()
    {
        var credential = CreateCredential();

        Assert.Equal(UserRole.Researcher, credential.Role);
        Assert.Equal(0, credential.FailedAttempts);
        Assert.Null(credential.LockoutUntil);
    }

    [Fact]
    public void Пята_помилка_входу_блокує_на_заданий_термін()
    {
        var credential = CreateCredential();
        var now = new DateTime(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            credential.RecordFailedAttempt(5, TimeSpan.FromMinutes(15), now);
        }

        Assert.Equal(5, credential.FailedAttempts);
        Assert.Equal(now.AddMinutes(15), credential.LockoutUntil);
        Assert.True(credential.IsLockedOut(now.AddMinutes(14)));
        Assert.False(credential.IsLockedOut(now.AddMinutes(15)));
    }

    [Fact]
    public void Невдала_спроба_під_час_блокування_не_змінює_стан()
    {
        var credential = CreateCredential();
        var now = new DateTime(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            credential.RecordFailedAttempt(5, TimeSpan.FromMinutes(15), now);
        }

        credential.RecordFailedAttempt(5, TimeSpan.FromMinutes(15), now.AddMinutes(1));

        Assert.Equal(5, credential.FailedAttempts);
        Assert.Equal(now.AddMinutes(15), credential.LockoutUntil);
    }

    [Fact]
    public void Після_закінчення_блокування_лічильник_починається_знову()
    {
        var credential = CreateCredential();
        var now = new DateTime(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            credential.RecordFailedAttempt(5, TimeSpan.FromMinutes(15), now);
        }

        credential.RecordFailedAttempt(5, TimeSpan.FromMinutes(15), now.AddMinutes(16));

        Assert.Equal(1, credential.FailedAttempts);
        Assert.Null(credential.LockoutUntil);
    }

    [Fact]
    public void Успішне_входження_скидає_помилки_та_блокування()
    {
        var credential = CreateCredential();
        var now = new DateTime(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            credential.RecordFailedAttempt(5, TimeSpan.FromMinutes(15), now);
        }

        credential.ResetFailedAttempts();

        Assert.Equal(0, credential.FailedAttempts);
        Assert.Null(credential.LockoutUntil);
    }

    [Fact]
    public void Зміна_ролі_повідомляє_чи_дійсно_роль_змінилась()
    {
        var credential = CreateCredential();

        Assert.True(credential.ChangeRole(UserRole.LabManager));
        Assert.False(credential.ChangeRole(UserRole.LabManager));
        Assert.Equal(UserRole.LabManager, credential.Role);
    }

    [Fact]
    public void Оновлення_пароля_зберігає_час_і_скидає_лічильник()
    {
        var credential = CreateCredential();
        var now = new DateTime(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc);
        credential.RecordFailedAttempt(5, TimeSpan.FromMinutes(15), now);
        var updatedAt = now.AddMinutes(1);

        credential.UpdatePasswordHash("new-password-hash", updatedAt);

        Assert.Equal("new-password-hash", credential.PasswordHash);
        Assert.Equal(updatedAt, credential.PasswordUpdatedAt);
        Assert.Equal(0, credential.FailedAttempts);
    }

    [Fact]
    public void Невалідні_облікові_дані_відхиляються()
    {
        var user = new User("Іван", "ivan@example.com");

        Assert.Throws<ArgumentException>(() =>
            new UserCredential(user, "hash", DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified)));
        Assert.Throws<ArgumentException>(() =>
            new UserCredential(user, " ", DateTime.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new UserCredential(user, "hash", DateTime.UtcNow, (UserRole)99));
    }

    private static UserCredential CreateCredential() =>
        new(new User("Іван", "ivan@example.com"), "password-hash", DateTime.UtcNow);
}
