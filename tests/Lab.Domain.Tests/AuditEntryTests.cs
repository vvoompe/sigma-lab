using Lab.Domain.Audit;

namespace Lab.Domain.Tests;

public sealed class AuditEntryTests
{
    [Fact]
    public void Запис_аудиту_зберігає_контекст_події()
    {
        var occurredAt = new DateTime(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc);

        var entry = new AuditEntry(2, "user.role.changed", "UserCredential", 5, occurredAt, "{\"role\":\"LabManager\"}");

        Assert.Equal(2, entry.ActorId);
        Assert.Equal("user.role.changed", entry.Action);
        Assert.Equal("UserCredential", entry.TargetType);
        Assert.Equal(5, entry.TargetId);
        Assert.Equal(occurredAt, entry.OccurredAt);
        Assert.Equal("{\"role\":\"LabManager\"}", entry.Details);
    }

    [Fact]
    public void Некоректні_ідентифікатори_або_час_аудиту_відхиляються()
    {
        var occurredAt = new DateTime(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AuditEntry(0, "action", "target", 1, occurredAt));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AuditEntry(1, "action", "target", 0, occurredAt));
        Assert.Throws<ArgumentException>(() =>
            new AuditEntry(1, "action", "target", 1, DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified)));
    }
}
