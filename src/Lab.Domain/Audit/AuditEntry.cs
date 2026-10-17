using Lab.Domain.Validation;

namespace Lab.Domain.Audit;

/// <summary>Незмінний запис аудиту дії користувача над ресурсом.</summary>
public sealed class AuditEntry
{
    public const int MaxActionLength = 100;
    public const int MaxTargetTypeLength = 100;
    public const int MaxDetailsLength = 4000;

    private AuditEntry() { }

    public AuditEntry(
        int actorId,
        string action,
        string targetType,
        int targetId,
        DateTime occurredAt,
        string? details = null)
    {
        DomainRules.PositiveId(actorId, nameof(actorId));
        DomainRules.PositiveId(targetId, nameof(targetId));
        ActorId = actorId;
        Action = DomainRules.Text(action, nameof(action), MaxActionLength);
        TargetType = DomainRules.Text(targetType, nameof(targetType), MaxTargetTypeLength);
        TargetId = targetId;
        DomainRules.Utc(occurredAt, nameof(occurredAt));
        OccurredAt = occurredAt;
        if (details is { Length: > MaxDetailsLength })
        {
            throw new ArgumentOutOfRangeException(nameof(details));
        }

        Details = details;
    }

    public int Id { get; private set; }
    public int ActorId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string TargetType { get; private set; } = string.Empty;
    public int TargetId { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public string? Details { get; private set; }
}
