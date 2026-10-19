using System.ComponentModel.DataAnnotations;
using Lab.Contracts.Validation;

namespace Lab.Contracts.Experiments;

public sealed record AddMeasurementRequest
{
    [Required] public Guid? ClientId { get; init; }
    [Required, FiniteNumber] public double? Value { get; init; }
    [Required, UtcTimestamp] public DateTime? MeasuredAt { get; init; }
    [Required, Range(typeof(long), "1", "9223372036854775807")] public long? ExpectedVersion { get; init; }
}
