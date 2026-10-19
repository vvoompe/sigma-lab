using Lab.Application.Abstractions;
using Lab.Application.Security;
using Lab.Application.Statistics;
using Lab.Contracts;
using Lab.Contracts.Experiments;
using Lab.Domain.Audit;
using Lab.Domain.Experiments;
using Lab.Domain.Measurements;
using Lab.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Lab.Application.Resources;

public sealed class ExperimentService(IAppDbContext db, ICurrentUser currentUser) : IExperimentService
{
    public async Task<PagedResult<ExperimentDto>> ListAsync(ExperimentQuery query, CancellationToken cancellationToken = default)
    {
        ResourceValidation.Validate(query);
        var owner = ResourceValidation.UserId(currentUser);
        var items = db.Experiments.AsNoTracking().Where(item => item.OwnerId == owner);
        if (query.SiteId is { } site) items = items.Where(item => item.SiteId == site);
        if (query.Status is { Length: > 0 } status)
        {
            var state = Enum.Parse<ExperimentStatus>(status);
            items = items.Where(item => item.Status == state);
        }
        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            var name = query.Name.Trim();
            items = items.Where(item => item.Name.Contains(name));
        }
        var count = await items.CountAsync(cancellationToken);
        var size = Math.Min(query.PageSize, ResourcePageQuery.MaxPageSize);
        var offset = (query.Page - 1L) * size;
        var page = offset >= count ? [] : await items.Include(item => item.Instrument)
            .OrderBy(item => item.Id).Skip((int)offset).Take(size).ToListAsync(cancellationToken);
        return new PagedResult<ExperimentDto>(page.Select(ToDto).ToArray(), count, query.Page, size);
    }

    public async Task<ExperimentDto> GetAsync(int id, CancellationToken cancellationToken = default) =>
        ToDto(await ReadAsync(id, cancellationToken));

    public async Task<ExperimentDto> CreateAsync(CreateExperimentRequest request, CancellationToken cancellationToken = default)
    {
        ResourceValidation.RequireRole(currentUser, nameof(UserRole.Researcher));
        ResourceValidation.Validate(request);
        var owner = ResourceValidation.UserId(currentUser);
        if (!await db.Sites.AnyAsync(item => item.Id == request.SiteId && item.OwnerId == owner, cancellationToken))
            throw ResourceValidation.Missing();
        var instrument = await db.Instruments.SingleOrDefaultAsync(
            item => item.Id == request.InstrumentId && item.OwnerId == owner, cancellationToken)
            ?? throw ResourceValidation.Missing();
        var reference = request.ReferenceValueId is { } referenceId
            ? await db.ReferenceValues.SingleOrDefaultAsync(item => item.Id == referenceId, cancellationToken)
                ?? throw ResourceValidation.Missing() : null;
        var experiment = new Experiment(ResourceValidation.RequiredValue(request.SiteId), owner, request.Name, instrument, reference);
        db.Experiments.Add(experiment);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            if (!await db.Sites.AnyAsync(item => item.Id == request.SiteId && item.OwnerId == owner, cancellationToken)
                || !await db.Instruments.AnyAsync(item => item.Id == request.InstrumentId, cancellationToken)
                || (request.ReferenceValueId is { } referenceKey
                    && !await db.ReferenceValues.AnyAsync(item => item.Id == referenceKey, cancellationToken)))
                throw ResourceValidation.Conflict("Пов'язаний ресурс змінився; повторіть читання.");
            throw;
        }
        return ToDto(experiment);
    }

    public async Task<ExperimentDto> RenameAsync(int id, RenameExperimentRequest request, CancellationToken cancellationToken = default)
    {
        ResourceValidation.Validate(request);
        var experiment = await MutableAsync(id, ResourceValidation.RequiredValue(request.ExpectedVersion), cancellationToken);
        experiment.Rename(request.Name);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(experiment);
    }

    public async Task DeleteAsync(int id, long expectedVersion, CancellationToken cancellationToken = default)
    {
        var experiment = await MutableAsync(id, expectedVersion, cancellationToken);
        // Існуюча модель аудиту Z3, та сама транзакція SaveChanges, без окремого writer.
        db.AuditEntries.Add(new AuditEntry(ResourceValidation.UserId(currentUser),
            "experiment.deleted", "Experiment", experiment.Id, DateTime.UtcNow));
        db.Experiments.Remove(experiment);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<MeasurementMutationDto> AddMeasurementAsync(int id, AddMeasurementRequest request, CancellationToken cancellationToken = default)
    {
        ResourceValidation.Validate(request);
        var experiment = await MutableAsync(id, ResourceValidation.RequiredValue(request.ExpectedVersion), cancellationToken);
        // Колекційна операція домену потребує повної завантаженої колекції.
        await db.Measurements.Where(item => item.ExperimentId == id).LoadAsync(cancellationToken);
        if (await db.Measurements.AnyAsync(item => item.ClientId == request.ClientId, cancellationToken))
            throw ResourceValidation.Conflict("ClientId вже використовується.");
        var measurement = experiment.AddMeasurement(ResourceValidation.RequiredValue(request.ClientId), ResourceValidation.RequiredValue(request.Value),
            experiment.Unit, ResourceValidation.RequiredValue(request.MeasuredAt));
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw; }
        catch (DbUpdateException)
        {
            if (await db.Measurements.AnyAsync(item => item.ClientId == request.ClientId, cancellationToken))
                throw ResourceValidation.Conflict("ClientId вже використовується.");
            if (!await db.Experiments.AnyAsync(item => item.Id == id, cancellationToken))
                throw ResourceValidation.Conflict("Серію видалено паралельним запитом.");
            throw;
        }
        return new MeasurementMutationDto(ToDto(measurement, experiment.Unit), experiment.Version);
    }

    public async Task<MeasurementDto> GetMeasurementAsync(int id, int measurementId, CancellationToken cancellationToken = default)
    {
        var experiment = await ReadAsync(id, cancellationToken);
        var measurement = await db.Measurements.AsNoTracking().SingleOrDefaultAsync(
            item => item.ExperimentId == id && item.Id == measurementId, cancellationToken)
            ?? throw ResourceValidation.Missing();
        return ToDto(measurement, experiment.Unit);
    }

    public async Task<PagedResult<MeasurementDto>> ListMeasurementsAsync(int id, ResourcePageQuery query, CancellationToken cancellationToken = default)
    {
        ResourceValidation.Validate(query);
        if (query.Name is not null) throw new ArgumentException("Виміри не мають назви для фільтрації.", nameof(query));
        var experiment = await ReadAsync(id, cancellationToken);
        var items = db.Measurements.AsNoTracking().Where(item => item.ExperimentId == id);
        var count = await items.CountAsync(cancellationToken);
        var size = Math.Min(query.PageSize, ResourcePageQuery.MaxPageSize);
        var offset = (query.Page - 1L) * size;
        var page = offset >= count ? [] : await items.OrderBy(item => item.Id)
            .Skip((int)offset).Take(size).ToListAsync(cancellationToken);
        return new PagedResult<MeasurementDto>(page.Select(item => ToDto(item, experiment.Unit)).ToArray(), count, query.Page, size);
    }

    public async Task<ExperimentStatisticsDto> StatisticsAsync(int id, StatisticsQuery query, CancellationToken cancellationToken = default)
    {
        ResourceValidation.Validate(query);
        var owner = ResourceValidation.UserId(currentUser);
        // Один SQL-запит читає версію та виміри одного стану, без змішаного snapshot.
        var experiment = await db.Experiments.AsNoTracking().Include(item => item.Instrument)
            .Include(item => item.ReferenceValue).Include(item => item.Measurements).AsSingleQuery()
            .SingleOrDefaultAsync(item => item.Id == id && item.OwnerId == owner, cancellationToken)
            ?? throw ResourceValidation.Missing();
        var measurements = experiment.Measurements.OrderBy(item => item.Id).ToArray();
        var values = measurements.Select(item => item.Value).ToArray();
        try
        {
            var stats = SeriesStatistics.Calculate(values, experiment.Unit);
            var interval = SeriesStatistics.ConfidenceInterval(values, experiment.Unit, query.ConfidenceLevel);
            var outliers = MeasurementAnalysis.FindOutliers(values, experiment.Unit)
                .Select(item => measurements[item.Index].Id).ToArray();
            var comparison = experiment.ReferenceValue is { } reference
                ? MeasurementAnalysis.CompareToReference(stats.Mean, experiment.Unit, reference.Value, reference.Unit, reference.AbsoluteTolerance)
                : null;
            return new ExperimentStatisticsDto(id, experiment.Version, stats.Count, stats.Unit, stats.Mean,
                stats.SampleStandardDeviation, stats.StandardError, (experiment.Instrument ?? throw new InvalidOperationException("Прилад не завантажено.")).AbsoluteError,
                new ConfidenceIntervalDto(interval.Lower, interval.Upper, interval.ConfidenceLevel), outliers,
                comparison is null ? null : new ReferenceComparisonDto(comparison.AbsoluteDeviation, comparison.AbsoluteTolerance, comparison.WithinTolerance));
        }
        catch (ArithmeticException exception)
        {
            throw new ArgumentException("Результат не представляється скінченним double; змініть масштаб або рівень довіри.", nameof(query), exception);
        }
    }

    private async Task<Experiment> ReadAsync(int id, CancellationToken cancellationToken)
    {
        var owner = ResourceValidation.UserId(currentUser);
        return await db.Experiments.AsNoTracking().Include(item => item.Instrument)
            .SingleOrDefaultAsync(item => item.Id == id && item.OwnerId == owner, cancellationToken)
            ?? throw ResourceValidation.Missing();
    }

    private async Task<Experiment> MutableAsync(int id, long expectedVersion, CancellationToken cancellationToken)
    {
        ResourceValidation.RequireRole(currentUser, nameof(UserRole.Researcher));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedVersion);
        var experiment = await db.Experiments.Include(item => item.Instrument)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw ResourceValidation.Missing();
        if (experiment.OwnerId != ResourceValidation.UserId(currentUser))
            throw new ResourceException(ResourceErrorKind.Forbidden, "Немає прав на зміну серії.");
        if (experiment.Status != ExperimentStatus.Draft)
            throw ResourceValidation.Conflict("Підписану серію не можна змінювати.");
        if (experiment.Version != expectedVersion)
            throw ResourceValidation.Conflict("Версія серії застаріла; перечитайте ресурс.");
        return experiment;
    }

    private static ExperimentDto ToDto(Experiment item) => new(item.Id, item.Name, item.SiteId, item.InstrumentId,
        item.ReferenceValueId, item.Status.ToString(), item.CreatedAt, item.SignedAt, item.SignedById, item.Version, item.Unit);

    private static MeasurementDto ToDto(Measurement item, string unit) =>
        new(item.Id, item.ExperimentId, item.ClientId, item.Value, item.MeasuredAt, item.Version, unit);
}
