using Lab.Application.Abstractions;
using Lab.Application.Resources;
using Lab.Domain.Experiments;
using Lab.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Lab.Application.Security;

public sealed class ExperimentAccessService(IAppDbContext db, ICurrentUser currentUser) : IExperimentAccessService
{
    public IQueryable<Experiment> ApplyReadFilter(IQueryable<Experiment> query)
    {
        if (currentUser.Role == nameof(UserRole.Administrator))
            return query;

        var userId = ResourceValidation.UserId(currentUser);
        if (currentUser.Role == nameof(UserRole.Researcher))
            return query.Where(item => item.OwnerId == userId);

        if (currentUser.Role == nameof(UserRole.LabManager))
        {
            return query.Where(item => item.OwnerId == userId ||
                db.Sites.Any(site => site.Id == item.SiteId && site.ManagerId == userId));
        }

        return query.Where(_ => false);
    }

    public async Task EnsureCanAsync(
        Experiment experiment,
        ExperimentAccessOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        var userId = ResourceValidation.UserId(currentUser);
        if (experiment.OwnerId == userId)
            return;

        if (operation == ExperimentAccessOperation.Sign &&
            currentUser.Role == nameof(UserRole.LabManager) &&
            await db.Sites.AnyAsync(
                site => site.Id == experiment.SiteId && site.ManagerId == userId,
                cancellationToken))
        {
            return;
        }

        throw new ResourceException(ResourceErrorKind.Forbidden, "Немає прав на зміну серії.");
    }
}
