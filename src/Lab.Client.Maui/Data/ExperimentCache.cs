using Lab.Client.Maui.Services;
using Lab.Contracts.Experiments;
using Lab.Contracts.Instruments;
using Lab.Contracts.ReferenceValues;
using Lab.Contracts.Sites;
using Lab.Domain.Experiments;
using Lab.Domain.Instruments;
using Lab.Domain.ReferenceValues;
using Lab.Domain.Sites;
using Lab.Domain.Users;
using Lab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lab.Client.Maui.Data;

/// <summary>Кеш дозволених серій на спільній Fluent-моделі, ізольований за акаунтом.</summary>
public sealed class ExperimentCache(ClientAccount account, string root)
{
    public async Task<IReadOnlyList<ExperimentDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var services = account.OpenServices(root);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        var items = await db.Experiments.AsNoTracking().Include(item => item.Instrument)
            .OrderBy(item => item.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return items.Select(ToDto).ToArray();
    }

    public async Task ValidateMeasurementAsync(int experimentId, double value, CancellationToken cancellationToken = default)
    {
        if (!account.CanWrite) throw new InvalidOperationException("Цей профіль має режим читання. Для внесення вимірів потрібен Researcher.");
        await using var services = account.OpenServices(root);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        var series = await db.Experiments.Include(item => item.Instrument)
            .SingleAsync(item => item.Id == experimentId, cancellationToken).ConfigureAwait(false);
        if (series.OwnerId != account.UserId) throw new InvalidOperationException("Чужа серія доступна лише для читання.");
        series.AddMeasurement(Guid.NewGuid(), value, series.Unit, DateTime.UtcNow);
    }

    /// <summary>Власні довідники без розширення прав; залежності серій беруться з контекстів API.</summary>
    public async Task ReplaceAsync(IReadOnlyList<ExperimentContextDto> contexts, IReadOnlyList<SiteDto> sites,
        IReadOnlyList<InstrumentDto> instruments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(instruments);
        foreach (var context in contexts)
        {
            var item = context.Experiment;
            if (item.SiteId != context.Site.Id || item.InstrumentId != context.Instrument.Id
                || context.OwnerId != context.Instrument.OwnerId || item.Unit != context.Instrument.Unit
                || item.ReferenceValueId != context.ReferenceValue?.Id)
                throw new InvalidDataException("API повернув неузгоджений контекст серії; кеш не змінено.");
        }
        var allSites = Unique(contexts.Select(item => item.Site).Concat(sites
            .Where(site => contexts.All(item => item.Site.Id != site.Id))
            .Select(item => new ExperimentSiteDto(item.Id, item.Name, item.Latitude, item.Longitude, item.CreatedAt, account.UserId, item.ManagerId))),
            item => item.Id);
        var allInstruments = Unique(contexts.Select(item => item.Instrument).Concat(instruments), item => item.Id);
        var references = Unique(contexts.Select(item => item.ReferenceValue).OfType<ReferenceValueDto>(), item => item.Id);
        await using var services = account.OpenServices(root);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        db.Experiments.RemoveRange(await db.Experiments.ToListAsync(cancellationToken).ConfigureAwait(false));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        db.Instruments.RemoveRange(await db.Instruments.ToListAsync(cancellationToken).ConfigureAwait(false));
        db.Sites.RemoveRange(await db.Sites.ToListAsync(cancellationToken).ConfigureAwait(false));
        db.ReferenceValues.RemoveRange(await db.ReferenceValues.ToListAsync(cancellationToken).ConfigureAwait(false));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var users = await db.Users.ToListAsync(cancellationToken).ConfigureAwait(false);
        var userIds = allInstruments.Select(item => item.OwnerId).Append(account.UserId)
            .Concat(contexts.Select(item => item.OwnerId))
            .Concat(allSites.SelectMany(item => new[] { item.OwnerId, item.ManagerId }).OfType<int>())
            .Concat(contexts.Select(item => item.Experiment.SignedById).OfType<int>()).Distinct();
        foreach (var id in userIds)
        {
            if (users.Any(item => item.Id == id)) continue;
            var user = new User($"Користувач {id}", $"cache-{id}@example.invalid");
            db.Add(user);
            db.Entry(user).Property(item => item.Id).CurrentValue = id;
        }
        foreach (var item in allSites)
        {
            var site = new Site
            {
                Id = item.Id, Name = item.Name, Latitude = item.Latitude, Longitude = item.Longitude,
                OwnerId = item.OwnerId, CreatedAt = item.CreatedAt,
            };
            if (item.ManagerId is { } manager) site.AssignManager(manager);
            db.Sites.Add(site);
        }
        var localInstruments = new Dictionary<int, Instrument>();
        foreach (var item in allInstruments)
        {
            var instrument = new Instrument(item.OwnerId, item.Name, item.SerialNumber, item.Unit, item.MinValue, item.MaxValue, item.AbsoluteError);
            db.Add(instrument);
            db.Entry(instrument).Property(entity => entity.Id).CurrentValue = item.Id;
            localInstruments.Add(item.Id, instrument);
        }
        var localReferences = new Dictionary<int, ReferenceValue>();
        foreach (var item in references)
        {
            var reference = new ReferenceValue(item.Name, item.Unit, item.Value, item.AbsoluteTolerance);
            db.Add(reference);
            db.Entry(reference).Property(entity => entity.Id).CurrentValue = item.Id;
            localReferences.Add(item.Id, reference);
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var context in contexts)
        {
            var item = context.Experiment;
            var reference = item.ReferenceValueId is { } referenceId ? localReferences[referenceId] : null;
            var experiment = new Experiment(item.SiteId, context.OwnerId, item.Name, localInstruments[item.InstrumentId], reference);
            db.Add(experiment);
            var entry = db.Entry(experiment);
            entry.Property(entity => entity.Id).CurrentValue = item.Id;
            entry.Property(entity => entity.CreatedAt).CurrentValue = item.CreatedAt;
            entry.Property(entity => entity.Status).CurrentValue = Enum.Parse<ExperimentStatus>(item.Status);
            entry.Property(entity => entity.SignedAt).CurrentValue = item.SignedAt;
            entry.Property(entity => entity.SignedById).CurrentValue = item.SignedById;
            entry.Property(entity => entity.Version).CurrentValue = item.Version;
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    // Сумісність з початковим Researcher-кешем без еталонів; у мережевому сценарії використовуються контексти.
    public Task ReplaceAsync(IReadOnlyList<SiteDto> sites, IReadOnlyList<InstrumentDto> instruments,
        IReadOnlyList<ExperimentDto> experiments, CancellationToken cancellationToken = default)
    {
        var contexts = experiments.Select(item =>
        {
            var instrument = instruments.SingleOrDefault(value => value.Id == item.InstrumentId)
                ?? throw new InvalidDataException("Немає приладу серії.");
            var site = sites.SingleOrDefault(value => value.Id == item.SiteId)
                ?? throw new InvalidDataException("Немає майданчика серії.");
            return new ExperimentContextDto(item, instrument.OwnerId,
                new ExperimentSiteDto(site.Id, site.Name, site.Latitude, site.Longitude, site.CreatedAt, account.UserId, site.ManagerId),
                instrument, null);
        }).ToArray();
        return ReplaceAsync(contexts, sites, instruments, cancellationToken);
    }

    private static T[] Unique<T>(IEnumerable<T> values, Func<T, int> key) where T : notnull =>
        values.GroupBy(key).Select(group => group.Distinct().Count() == 1 ? group.First()
            : throw new InvalidDataException("Пов’язані дані змінилися під час завантаження; повторіть його.")).ToArray();

    private static ExperimentDto ToDto(Experiment item) => new(item.Id, item.Name, item.SiteId, item.InstrumentId,
        item.ReferenceValueId, item.Status.ToString(), item.CreatedAt, item.SignedAt, item.SignedById, item.Version, item.Unit);
}
