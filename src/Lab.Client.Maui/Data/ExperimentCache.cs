using Lab.Client.Maui.Services;
using Lab.Contracts.Experiments;
using Lab.Contracts.Instruments;
using Lab.Contracts.Sites;
using Lab.Domain.Experiments;
using Lab.Domain.Instruments;
using Lab.Domain.Sites;
using Lab.Domain.Users;
using Lab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lab.Client.Maui.Data;

/// <summary>Локальний кеш на спільній Fluent-моделі. Замінюється лише після повного читання API.</summary>
public sealed class ExperimentCache(ClientAccount account, string root)
{
    public async Task<IReadOnlyList<ExperimentDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var services = account.OpenServices(root);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        var items = await db.Experiments.AsNoTracking().Include(item => item.Instrument)
            .Where(item => item.OwnerId == account.UserId).OrderBy(item => item.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return items.Select(ToDto).ToArray();
    }

    public async Task ValidateMeasurementAsync(int experimentId, double value, CancellationToken cancellationToken = default)
    {
        await using var services = account.OpenServices(root);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        var series = await db.Experiments.Include(item => item.Instrument)
            .SingleAsync(item => item.Id == experimentId && item.OwnerId == account.UserId, cancellationToken).ConfigureAwait(false);
        // Викликаємо саме доменні інваріанти; цей перевірочний контекст не зберігається.
        series.AddMeasurement(Guid.NewGuid(), value, series.Unit, DateTime.UtcNow);
    }

    public async Task ReplaceAsync(IReadOnlyList<SiteDto> sites, IReadOnlyList<InstrumentDto> instruments,
        IReadOnlyList<ExperimentDto> experiments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(instruments);
        ArgumentNullException.ThrowIfNull(experiments);
        await using var services = account.OpenServices(root);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        // У цій БД тільки кеш одного акаунта; журнал невідправлених операцій зберігається окремо.
        db.Experiments.RemoveRange(await db.Experiments.ToListAsync(cancellationToken).ConfigureAwait(false));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        db.Instruments.RemoveRange(await db.Instruments.ToListAsync(cancellationToken).ConfigureAwait(false));
        db.Sites.RemoveRange(await db.Sites.ToListAsync(cancellationToken).ConfigureAwait(false));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var users = await db.Users.ToListAsync(cancellationToken).ConfigureAwait(false);
        var userIds = instruments.Select(item => item.OwnerId).Append(account.UserId)
            .Concat(experiments.Where(item => item.SignedById.HasValue).Select(item => item.SignedById!.Value)).Distinct();
        foreach (var id in userIds)
        {
            if (users.Any(item => item.Id == id)) continue;
            // Технічний FK-профіль кешу, не реєстрація і не пароль користувача.
            var user = new User($"Користувач {id}", $"cache-{id}@example.invalid");
            db.Add(user);
            db.Entry(user).Property(item => item.Id).CurrentValue = id;
        }
        db.Sites.AddRange(sites.Select(item => new Site
        {
            Id = item.Id, Name = item.Name, Latitude = item.Latitude, Longitude = item.Longitude,
            OwnerId = account.UserId, CreatedAt = item.CreatedAt,
        }));
        var localInstruments = new Dictionary<int, Instrument>();
        foreach (var item in instruments)
        {
            var instrument = new Instrument(item.OwnerId, item.Name, item.SerialNumber, item.Unit, item.MinValue, item.MaxValue, item.AbsoluteError);
            db.Add(instrument);
            db.Entry(instrument).Property(entity => entity.Id).CurrentValue = item.Id;
            localInstruments.Add(item.Id, instrument);
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var item in experiments)
        {
            if (!localInstruments.TryGetValue(item.InstrumentId, out var instrument))
                throw new InvalidDataException("У відповіді API немає приладу серії; кеш не змінено.");
            var experiment = new Experiment(item.SiteId, account.UserId, item.Name, instrument);
            db.Add(experiment);
            var entry = db.Entry(experiment);
            entry.Property(entity => entity.Id).CurrentValue = item.Id;
            entry.Property(entity => entity.CreatedAt).CurrentValue = item.CreatedAt;
            entry.Property(entity => entity.Status).CurrentValue = Enum.Parse<ExperimentStatus>(item.Status);
            entry.Property(entity => entity.SignedAt).CurrentValue = item.SignedAt;
            entry.Property(entity => entity.SignedById).CurrentValue = item.SignedById;
            entry.Property(entity => entity.Version).CurrentValue = item.Version;
            // ReferenceValue не копіюємо без повного еталона; поле лишається у спільному API DTO.
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ExperimentDto ToDto(Experiment item) => new(item.Id, item.Name, item.SiteId, item.InstrumentId,
        item.ReferenceValueId, item.Status.ToString(), item.CreatedAt, item.SignedAt, item.SignedById, item.Version, item.Unit);
}
