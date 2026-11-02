using Lab.Application.Abstractions;
using Lab.Application.Security;
using Lab.Contracts.Experiments;
using Lab.Contracts.Instruments;
using Lab.Contracts.ReferenceValues;
using Microsoft.EntityFrameworkCore;

namespace Lab.Application.Resources;

/// <summary>Один SQL-запит повертає тільки залежності доступної користувачу серії.</summary>
public sealed class ExperimentContextService(IAppDbContext db, IExperimentAccessService access) : IExperimentContextService
{
    public async Task<ExperimentContextDto> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        var row = await (
            from experiment in access.ApplyReadFilter(db.Experiments.AsNoTracking())
            join site in db.Sites on experiment.SiteId equals site.Id
            join instrument in db.Instruments on experiment.InstrumentId equals instrument.Id
            join reference in db.ReferenceValues on experiment.ReferenceValueId equals (int?)reference.Id into references
            from reference in references.DefaultIfEmpty()
            where experiment.Id == id
            select new { Experiment = experiment, Site = site, Instrument = instrument, Reference = reference })
            .SingleOrDefaultAsync(cancellationToken) ?? throw ResourceValidation.Missing();
        var item = row.Experiment;
        var instrumentDto = new InstrumentDto(row.Instrument.Id, row.Instrument.OwnerId, row.Instrument.Name,
            row.Instrument.SerialNumber, row.Instrument.Unit, row.Instrument.MinValue, row.Instrument.MaxValue, row.Instrument.AbsoluteError);
        return new ExperimentContextDto(
            new ExperimentDto(item.Id, item.Name, item.SiteId, item.InstrumentId, item.ReferenceValueId,
                item.Status.ToString(), item.CreatedAt, item.SignedAt, item.SignedById, item.Version, row.Instrument.Unit),
            item.OwnerId,
            new ExperimentSiteDto(row.Site.Id, row.Site.Name, row.Site.Latitude, row.Site.Longitude,
                row.Site.CreatedAt, row.Site.OwnerId, row.Site.ManagerId),
            instrumentDto,
            row.Reference is { } value
                ? new ReferenceValueDto(value.Id, value.Name, value.Unit, value.Value, value.AbsoluteTolerance) : null);
    }
}
