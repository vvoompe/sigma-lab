namespace Lab.Contracts.Experiments;

public sealed record MeasurementDto(int Id, int ExperimentId, Guid ClientId, double Value,
    DateTime MeasuredAt, long Version, string Unit);
