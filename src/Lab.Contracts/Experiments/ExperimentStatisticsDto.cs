namespace Lab.Contracts.Experiments;

public sealed record ExperimentStatisticsDto(int ExperimentId, long ExperimentVersion,
    int Count, string Unit, double Mean, double SampleStandardDeviation, double StandardError,
    double InstrumentAbsoluteError, ConfidenceIntervalDto ConfidenceInterval,
    IReadOnlyList<int> OutlierMeasurementIds, ReferenceComparisonDto? ReferenceComparison);

public sealed record ConfidenceIntervalDto(double Lower, double Upper, double ConfidenceLevel);
public sealed record ReferenceComparisonDto(double AbsoluteDeviation, double AbsoluteTolerance, bool WithinTolerance);
