using Lab.Contracts.Sync;

namespace Lab.Application.Abstractions;

public interface IMeasurementSyncService
{
    Task<MeasurementSyncResult> SynchronizeAsync(MeasurementSyncRequest? request, CancellationToken cancellationToken = default);
    Task<MeasurementSyncBatchResult> SynchronizeBatchAsync(MeasurementSyncBatchRequest request, CancellationToken cancellationToken = default);
}
