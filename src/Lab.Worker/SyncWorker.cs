using Lab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lab.Worker;

/// <summary>
/// Перевіряє БД, перераховує змінені серії та публікує версійні CSV/JSON-знімки.
/// </summary>
/// <param name="logger">Журнал обробника.</param>
/// <param name="scopeFactory">Фабрика областей для отримання scoped-контексту.</param>
/// <param name="configuration">Конфігурація застосунку.</param>
public sealed partial class SyncWorker(
    ILogger<SyncWorker> logger,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration) : BackgroundService
{
    private const int DefaultIntervalSeconds = 300;
    private const int MinIntervalSeconds = 5;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(
            MinIntervalSeconds,
            configuration.GetValue("Worker:IntervalSeconds", DefaultIntervalSeconds)));

        LogWorkerStarted(logger, interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckDatabaseAsync(stoppingToken);
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Штатна зупинка застосунку - виходимо без помилки.
                break;
            }
            catch (Exception exception) when (exception is IOException or System.Data.Common.DbException
                or DbUpdateException or InvalidOperationException or ArithmeticException or System.Text.Json.JsonException)
            {
                LogProcessingFailed(logger, exception);
                try { await Task.Delay(interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        var canConnect = await db.Database.CanConnectAsync(cancellationToken);

        LogDatabaseChecked(logger, canConnect, db.Database.ProviderName ?? "невідомий провайдер");
        if (!canConnect) return;
        var output = configuration["Worker:OutputDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "exports");
        var processor = new SeriesProcessor(db, output);
        processor.CleanTemporaryFiles(DateTime.UtcNow.AddDays(-1));
        // Пагінація ключем: пам'ять не росте разом із кількістю серій.
        var after = 0;
        while (true)
        {
            var ids = await db.Experiments.AsNoTracking().Where(item => item.Id > after).OrderBy(item => item.Id)
                .Select(item => item.Id).Take(100).ToListAsync(cancellationToken);
            if (ids.Count == 0) break;
            foreach (var id in ids)
            {
                try
                {
                    if (await processor.ProcessAsync(id, cancellationToken)) LogSeriesProcessed(logger, id);
                }
                catch (Exception exception) when (exception is IOException or System.Data.Common.DbException
                    or DbUpdateException or InvalidOperationException or ArithmeticException or System.Text.Json.JsonException)
                {
                    // Помилка однієї серії не блокує перерахунок решти; повтор — у наступному циклі.
                    LogSeriesFailed(logger, id, exception);
                }
            }
            after = ids[^1];
        }
    }

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "Фоновий обробник запущено. Інтервал перевірки: {IntervalSeconds} с")]
    private static partial void LogWorkerStarted(ILogger logger, double intervalSeconds);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Перевірка бази даних: доступна = {CanConnect}, провайдер = {Provider}")]
    private static partial void LogDatabaseChecked(ILogger logger, bool canConnect, string provider);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Серію {ExperimentId} перераховано; CSV/JSON та маніфест опубліковано")]
    private static partial void LogSeriesProcessed(ILogger logger, int experimentId);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Error, Message = "Фонова обробка не завершилась; повтор після інтервалу")]
    private static partial void LogProcessingFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Warning, Message = "Серію {ExperimentId} не оброблено; буде повторено")]
    private static partial void LogSeriesFailed(ILogger logger, int experimentId, Exception exception);
}
