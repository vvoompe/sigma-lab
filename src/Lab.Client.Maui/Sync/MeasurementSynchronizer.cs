using System.Globalization;
using System.Net;
using System.Text.Json;
using Lab.Client.Maui.Services;
using Lab.Contracts.Sync;

namespace Lab.Client.Maui.Sync;

/// <summary>Команда черги, не копія доменної моделі виміру.</summary>
public sealed record PendingMeasurement(int ExperimentId, Guid ClientId, double Value, DateTime MeasuredAt, long ExpectedVersion);
public sealed record SynchronizationReport(int Sent, int Failed, int Pending);

/// <summary>Пакети до 50 команд; підтвердження перевіряється для кожного елемента.</summary>
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
        item = item with { MeasuredAt = new DateTime(item.MeasuredAt.Ticks - item.MeasuredAt.Ticks % 10, DateTimeKind.Utc) };
        return queue.EnqueueAsync("Measurement", item.ExperimentId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(item, JsonOptions), item.ClientId, cancellationToken);
    }

    public async Task<SynchronizationReport> SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await queue.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var commands = items.Where(item => item.Status != SyncQueueStatus.Sent).Select(item =>
                (Operation: item, Command: JsonSerializer.Deserialize<PendingMeasurement>(item.Payload, JsonOptions)
                    ?? throw new InvalidDataException("Пошкоджено команду виміру."))).ToArray();
            var existingFailures = new HashSet<int>();
            var pending = new List<(SyncQueueItem Operation, PendingMeasurement Command)>();
            foreach (var item in commands)
            {
                if (item.Operation.Status == SyncQueueStatus.Failed) existingFailures.Add(item.Command.ExperimentId);
                else if (!existingFailures.Contains(item.Command.ExperimentId)) pending.Add(item);
            }
            // Пізніший Failed блокує наступні команди, але не вже явно повторену попередню.
            var blocked = new HashSet<int>();
            var checkedSeries = new HashSet<int>();
            var stop = false;
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Відомий конфлікт першої команди блокує залежні команди до відправлення пакета.
                foreach (var first in pending.Where(item => !blocked.Contains(item.Command.ExperimentId))
                    .GroupBy(item => item.Command.ExperimentId).Select(group => group.First()).ToArray())
                {
                    if (!checkedSeries.Add(first.Command.ExperimentId)) continue;
                    try
                    {
                        var series = await api.ExperimentAsync(first.Command.ExperimentId, cancellationToken).ConfigureAwait(false);
                        if (series.Status == "Draft" && series.Version == first.Command.ExpectedVersion) continue;
                        // Single перевіряє UUID до старої версії, зокрема після втрати відповіді.
                        var result = await api.SynchronizeSingleAsync(ToRequest(first.Command), cancellationToken).ConfigureAwait(false);
                        ValidateResult(first.Command, result);
                        if (result.Status is not (200 or 201))
                            throw new ClientApiException((HttpStatusCode)result.Status, result.Error!.Title);
                        await queue.MarkSentAsync(first.Operation.OperationId, cancellationToken).ConfigureAwait(false);
                        pending.Remove(first);
                    }
                    catch (Exception exception) when (exception is ClientApiException or HttpRequestException or IOException
                        or InvalidDataException or JsonException
                        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
                    {
                        await queue.MarkFailedAsync(first.Operation.OperationId, exception.Message, cancellationToken).ConfigureAwait(false);
                        blocked.Add(first.Command.ExperimentId);
                        if (exception is not ClientApiException || exception is ClientApiException { Status: HttpStatusCode.Unauthorized })
                        {
                            stop = true;
                            break;
                        }
                    }
                }
                if (stop) break;
                var batch = pending.Where(item => !blocked.Contains(item.Command.ExperimentId)).Take(50).ToArray();
                if (batch.Length == 0) break;
                foreach (var item in batch) pending.Remove(item);
                try
                {
                    var requests = batch.Select(item => (MeasurementSyncRequest?)ToRequest(item.Command)).ToArray();
                    var results = batch.Length == 1
                        ? new[] { await api.SynchronizeSingleAsync(requests[0]!, cancellationToken).ConfigureAwait(false) }
                        : (await api.SynchronizeBatchAsync(requests, cancellationToken).ConfigureAwait(false)).Items.ToArray();
                    if (results.Length != batch.Length)
                        throw new InvalidDataException("API повернув неповний звіт синхронізації.");
                    // Спершу перевіряємо весь контракт, щоб пошкоджений звіт не дав хибний Sent.
                    for (var index = 0; index < batch.Length; index++) ValidateResult(batch[index].Command, results[index]);
                    for (var index = 0; index < batch.Length; index++)
                    {
                        var result = results[index];
                        var item = batch[index];
                        if (result.Status is 200 or 201)
                            await queue.MarkSentAsync(item.Operation.OperationId, cancellationToken).ConfigureAwait(false);
                        else
                        {
                            await queue.MarkFailedAsync(item.Operation.OperationId,
                                $"HTTP {result.Status}: {result.Error?.Title ?? "Помилка синхронізації."}", cancellationToken).ConfigureAwait(false);
                            blocked.Add(item.Command.ExperimentId);
                        }
                    }
                    if (results.Any(item => item.Status == 401)) break;
                }
                catch (Exception exception) when (exception is ClientApiException or HttpRequestException or IOException or InvalidDataException or JsonException
                    || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    // Вже прийняті сервером команди повторяться з тим самим UUID.
                    foreach (var item in batch)
                    {
                        await queue.MarkFailedAsync(item.Operation.OperationId,
                            exception is ClientApiException ? exception.Message : "Немає достовірного підтвердження; повторіть із тим самим UUID.",
                            cancellationToken).ConfigureAwait(false);
                        blocked.Add(item.Command.ExperimentId);
                    }
                    if (exception is not ClientApiException || exception is ClientApiException { Status: HttpStatusCode.Unauthorized }) break;
                }
            }
            var final = await queue.GetAllAsync(cancellationToken).ConfigureAwait(false);
            return new(final.Count(item => item.Status == SyncQueueStatus.Sent),
                final.Count(item => item.Status == SyncQueueStatus.Failed), final.Count(item => item.Status == SyncQueueStatus.Pending));
        }
        finally { Gate.Release(); }
    }

    private static MeasurementSyncRequest ToRequest(PendingMeasurement item) => new()
    {
        ExperimentId = item.ExperimentId, ClientId = item.ClientId, Value = item.Value,
        MeasuredAt = item.MeasuredAt, ExpectedVersion = item.ExpectedVersion,
    };

    private static void ValidateResult(PendingMeasurement command, MeasurementSyncResult result)
    {
        if (result is null || result.ClientId != command.ClientId)
            throw new InvalidDataException("API змінив порядок або UUID результатів.");
        if (result.Status is 200 or 201)
        {
            var item = result.Measurement;
            if (item is null || item.ClientId != command.ClientId || item.ExperimentId != command.ExperimentId
                || item.Value != command.Value || item.MeasuredAt != command.MeasuredAt
                || result.ExperimentVersion is not > 0 || result.Error is not null
                || result.Duplicate != (result.Status == 200))
                throw new InvalidDataException("API не підтвердив payload виміру.");
        }
        else if (result.Status is < 400 or > 599 || result.Measurement is not null || result.Duplicate || result.Error is null)
            throw new InvalidDataException("Некоректний результат помилки синхронізації.");
    }
}
