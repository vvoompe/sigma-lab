using Lab.Domain.Experiments;

namespace Lab.Application.Security;

public enum ExperimentAccessOperation
{
    Sign,
    Mutate
}

public interface IExperimentAccessService
{
    IQueryable<Experiment> ApplyReadFilter(IQueryable<Experiment> query);
    Task EnsureCanAsync(Experiment experiment, ExperimentAccessOperation operation, CancellationToken cancellationToken = default);
}
