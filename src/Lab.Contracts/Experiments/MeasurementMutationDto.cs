namespace Lab.Contracts.Experiments;

public sealed record MeasurementMutationDto(MeasurementDto Measurement, long ExperimentVersion);
