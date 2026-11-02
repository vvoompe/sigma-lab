using Lab.Application.Abstractions;
using Lab.Application.Resources;
using Lab.Application.Security;
using Lab.Contracts.Experiments;
using Lab.Contracts.Sync;
using Lab.Domain.Experiments;
using Lab.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Lab.Application.Sync;

internal sealed class MeasurementSyncAttempt(IAppDbContext db, ICurrentUser currentUser, IExperimentService experiments)
{
    internal async Task<MeasurementSyncResult?> ExecuteAsync(
        MeasurementSyncRequest? request, bool create, CancellationToken cancellationToken)
    {
        ResourceValidation.RequireRole(currentUser, nameof(UserRole.Researcher));
        ArgumentNullException.ThrowIfNull(request);
        var id = ResourceValidation.RequiredValue(request.ExperimentId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        var add = new AddMeasurementRequest
        {
            ClientId = request.ClientId, Value = request.Value,
            MeasuredAt = request.MeasuredAt, ExpectedVersion = request.ExpectedVersion,
        };
        ResourceValidation.Validate(add);
        if (add.ClientId == Guid.Empty) throw new ArgumentException("Потрібен непорожній UUID.", nameof(request));

        var series = await db.Experiments.AsNoTracking().Include(item => item.Instrument)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw ResourceValidation.Missing();
        if (series.OwnerId != ResourceValidation.UserId(currentUser))
            throw new ResourceException(ResourceErrorKind.Forbidden, "Немає прав на зміну серії.");

        // Перевірка доступу перед глобальним UUID: жодних даних чужого виміру назовні.
        var existing = await db.Measurements.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ClientId == add.ClientId, cancellationToken);
        var measuredAt = Microseconds(add.MeasuredAt!.Value);
        if (existing is not null)
        {
            if (existing.ExperimentId != id || existing.Value != add.Value || Microseconds(existing.MeasuredAt) != measuredAt)
                return MeasurementSyncService.Failure(request, 409, "ClientId уже використано з іншими даними.", series.Version);
            var dto = new MeasurementDto(existing.Id, id, existing.ClientId, existing.Value,
                existing.MeasuredAt, existing.Version, series.Unit);
            return new MeasurementSyncResult(existing.ClientId, 200, true, dto, series.Version, null);
        }
        if (!create) return null;
        if (series.Status != ExperimentStatus.Draft || series.Version != add.ExpectedVersion)
            return MeasurementSyncService.Failure(request, 409, "Серію підписано або її версія застаріла; перечитайте ресурс.", series.Version);

        var result = await experiments.AddMeasurementAsync(id, add with { MeasuredAt = measuredAt }, cancellationToken);
        return new MeasurementSyncResult(result.Measurement.ClientId, 201, false, result.Measurement, result.ExperimentVersion, null);
    }

    private static DateTime Microseconds(DateTime value) => new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
}
