using System.ComponentModel.DataAnnotations;
using Lab.Contracts.Validation;

namespace Lab.Contracts.Instruments;

/// <summary>Адміністратор призначає прилад наявному досліднику.</summary>
public sealed record CreateInstrumentRequest
{
    [Required, Range(1, int.MaxValue)] public int? OwnerId { get; init; }
    [Required, MaxLength(100)] public string Name { get; init; } = string.Empty;
    [Required, MaxLength(100)] public string SerialNumber { get; init; } = string.Empty;
    [Required, MaxLength(32)] public string Unit { get; init; } = string.Empty;
    [Required, FiniteNumber] public double? MinValue { get; init; }
    [Required, FiniteNumber] public double? MaxValue { get; init; }
    [Required, FiniteNumber] public double? AbsoluteError { get; init; }
}
