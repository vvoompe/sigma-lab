using Lab.Contracts.Experiments;

namespace Lab.Application.Abstractions;

public interface IExperimentContextService
{
    Task<ExperimentContextDto> GetAsync(int id, CancellationToken cancellationToken = default);
}
