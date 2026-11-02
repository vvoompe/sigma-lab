using Lab.Contracts.Experiments;

namespace Lab.Contracts.Sync;

public sealed record MeasurementSyncError(string Title);

public sealed record MeasurementSyncResult(
    Guid? ClientId, int Status, bool Duplicate, MeasurementDto? Measurement,
    long? ExperimentVersion, MeasurementSyncError? Error);

public sealed record MeasurementSyncBatchResult(IReadOnlyList<MeasurementSyncResult> Items);
