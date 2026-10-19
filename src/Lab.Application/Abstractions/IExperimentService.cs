using Lab.Contracts;
using Lab.Contracts.Experiments;

namespace Lab.Application.Abstractions;

public interface IExperimentService
{
    Task<PagedResult<ExperimentDto>> ListAsync(ExperimentQuery query, CancellationToken cancellationToken = default);
    Task<ExperimentDto> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<ExperimentDto> CreateAsync(CreateExperimentRequest request, CancellationToken cancellationToken = default);
    Task<ExperimentDto> RenameAsync(int id, RenameExperimentRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, long expectedVersion, CancellationToken cancellationToken = default);
    Task<MeasurementMutationDto> AddMeasurementAsync(int id, AddMeasurementRequest request, CancellationToken cancellationToken = default);
    Task<MeasurementDto> GetMeasurementAsync(int id, int measurementId, CancellationToken cancellationToken = default);
    Task<PagedResult<MeasurementDto>> ListMeasurementsAsync(int id, ResourcePageQuery query, CancellationToken cancellationToken = default);
    Task<ExperimentStatisticsDto> StatisticsAsync(int id, StatisticsQuery query, CancellationToken cancellationToken = default);
}
