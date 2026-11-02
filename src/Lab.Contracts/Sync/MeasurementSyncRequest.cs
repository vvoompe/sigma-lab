using System.ComponentModel.DataAnnotations;

namespace Lab.Contracts.Sync;

/// <summary>Поля елемента перевіряються окремо, щоб одна відмова не відхиляла весь пакет.</summary>
public sealed record MeasurementSyncRequest
{
    public int? ExperimentId { get; init; }
    public Guid? ClientId { get; init; }
    public double? Value { get; init; }
    public DateTime? MeasuredAt { get; init; }
    public long? ExpectedVersion { get; init; }
}

public sealed record MeasurementSyncBatchRequest
{
    [Required, MinLength(1), MaxLength(50)]
    public IReadOnlyList<MeasurementSyncRequest?>? Items { get; init; }
}
