using System.ComponentModel.DataAnnotations;
using Lab.Contracts.Validation;

namespace Lab.Contracts.ReferenceValues;

/// <summary>Повне оновлення еталона.</summary>
public sealed record UpdateReferenceValueRequest
{
    /// <summary>Назва еталона.</summary>
    [Required, MaxLength(100)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Одиниця виміру.</summary>
    [Required, MaxLength(32)]
    public string Unit { get; init; } = string.Empty;

    /// <summary>Значення еталона.</summary>
    [Required, FiniteNumber]
    public double? Value { get; init; }

    /// <summary>Абсолютний допуск, не від'ємний.</summary>
    [Required, FiniteNumber, Range(0d, double.MaxValue)]
    public double? AbsoluteTolerance { get; init; }
}
