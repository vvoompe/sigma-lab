using System.Globalization;
using System.Net;
using System.Text.Json;
using Lab.Client.Maui.Services;
using Lab.Contracts.Experiments;

namespace Lab.Client.Maui.Sync;

/// <summary>Дані команди; це не копія доменної моделі виміру.</summary>
public sealed record PendingMeasurement(int ExperimentId, Guid ClientId, double Value, DateTime MeasuredAt, long ExpectedVersion);

/// <summary>Звіт клієнта. HTTP 207 належить серверному пакетному endpoint Z2.</summary>
public sealed record SynchronizationReport(int Sent, int Failed, int Pending);

/// <summary>Послідовне відправлення через наявний API з перевіркою повторів і версій.</summary>
public sealed class MeasurementSynchronizer(SyncQueueStore queue, LabApiClient api)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task<SyncQueueItem> EnqueueAsync(SyncQueueStore queue, PendingMeasurement item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(item);
        if (item.ClientId == Guid.Empty || item.ExperimentId <= 0 || item.ExpectedVersion <= 0
            || !double.IsFinite(item.Value) || item.MeasuredAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Некоректна офлайн-операція.", nameof(item));
        // PostgreSQL зберігає мікросекунди, DateTime — 100 нс. Канонізуємо до першої відправки.
        item = item with { MeasuredAt = new DateTime(item.MeasuredAt.Ticks - item.MeasuredAt.Ticks % 10, DateTimeKind.Utc) };
        return queue.EnqueueAsync("Measurement", item.ExperimentId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(item, JsonOptions), item.ClientId, cancellationToken);
    }

    public async Task<SynchronizationReport> SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var blocked = new HashSet<int>();
            var items = await queue.GetAllAsync(cancellationToken).ConfigureAwait(false);
            foreach (var operation in items.Where(item => item.Status != SyncQueueStatus.Sent))
            {
                var item = JsonSerializer.Deserialize<PendingMeasurement>(operation.Payload, JsonOptions)
                    ?? throw new InvalidDataException("Пошкоджено команду виміру.");
                if (operation.Status == SyncQueueStatus.Failed || blocked.Contains(item.ExperimentId))
                {
                    blocked.Add(item.ExperimentId);
                    continue;
                }
                try
                {
                    await SendAsync(item, cancellationToken).ConfigureAwait(false);
                    await queue.MarkSentAsync(operation.OperationId, cancellationToken).ConfigureAwait(false);
                }
                catch (ClientApiException exception)
                {
                    await queue.MarkFailedAsync(operation.OperationId, exception.Message, cancellationToken).ConfigureAwait(false);
                    blocked.Add(item.ExperimentId);
                    if (exception.Status == HttpStatusCode.Unauthorized) break;
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException
                    || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    // Невідомий результат запиту: на повторі спочатку перевіряємо ClientId на сервері.
                    await queue.MarkFailedAsync(operation.OperationId, "Зв’язок перервано; можна повторити відправлення.", cancellationToken).ConfigureAwait(false);
                    break;
                }
            }
            var result = await queue.GetAllAsync(cancellationToken).ConfigureAwait(false);
            return new(result.Count(item => item.Status == SyncQueueStatus.Sent),
                result.Count(item => item.Status == SyncQueueStatus.Failed), result.Count(item => item.Status == SyncQueueStatus.Pending));
        }
        finally { Gate.Release(); }
    }

    private async Task SendAsync(PendingMeasurement item, CancellationToken cancellationToken)
    {
        if (await IsDuplicateAsync(item, cancellationToken).ConfigureAwait(false)) return;
        var experiment = await api.ExperimentAsync(item.ExperimentId, cancellationToken).ConfigureAwait(false);
        if (experiment.Status != "Draft" || experiment.Version != item.ExpectedVersion)
            throw new ClientApiException(HttpStatusCode.Conflict,
                $"Конфлікт версій серії {item.ExperimentId}: локально {item.ExpectedVersion}, на сервері {experiment.Version}. Дані залишені в черзі; потрібне рішення користувача.");
        try
        {
            var result = await api.AddMeasurementAsync(item.ExperimentId, new AddMeasurementRequest
            {
                ClientId = item.ClientId, Value = item.Value, MeasuredAt = item.MeasuredAt, ExpectedVersion = item.ExpectedVersion,
            }, cancellationToken).ConfigureAwait(false);
            if (result.Measurement.ClientId != item.ClientId || result.Measurement.ExperimentId != item.ExperimentId
                || result.Measurement.Value != item.Value || result.Measurement.MeasuredAt != item.MeasuredAt)
                throw new InvalidDataException("API не підтвердив відправлений вимір; операція залишена для перевірки повтору.");
        }
        catch (ClientApiException exception) when (exception.Status == HttpStatusCode.Conflict)
        {
            if (!await IsDuplicateAsync(item, cancellationToken).ConfigureAwait(false)) throw;
        }
    }

    private async Task<bool> IsDuplicateAsync(PendingMeasurement item, CancellationToken cancellationToken)
    {
        var measurements = await api.MeasurementsAsync(item.ExperimentId, cancellationToken).ConfigureAwait(false);
        var duplicate = measurements.SingleOrDefault(existing => existing.ClientId == item.ClientId);
        if (duplicate is null) return false;
        if (duplicate.Value != item.Value || duplicate.MeasuredAt != item.MeasuredAt)
            throw new ClientApiException(HttpStatusCode.Conflict, "ClientId вже використаний для інших даних. Автоматична заміна заборонена.");
        return true;
    }
}
