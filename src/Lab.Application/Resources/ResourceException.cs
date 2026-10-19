namespace Lab.Application.Resources;

public enum ResourceErrorKind { Unauthorized, Forbidden, NotFound, Conflict }

/// <summary>Прикладна помилка без залежності від HTTP або конкретної СУБД.</summary>
public sealed class ResourceException(ResourceErrorKind kind, string message) : Exception(message)
{
    public ResourceErrorKind Kind { get; } = kind;
}
