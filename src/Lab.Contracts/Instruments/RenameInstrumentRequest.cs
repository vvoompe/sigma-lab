using System.ComponentModel.DataAnnotations;

namespace Lab.Contracts.Instruments;

/// <summary>Метрологічні параметри та серійний номер незмінні.</summary>
public sealed record RenameInstrumentRequest
{
    [Required, MaxLength(100)] public string Name { get; init; } = string.Empty;
}
