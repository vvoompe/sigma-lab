using Lab.Infrastructure.Persistence;

namespace Lab.Worker;

/// <summary>
/// Фоновий обробник (зона Z4 розширює його перерахунком і експортом). Зараз він виконує
/// одну чесну роботу: періодично перевіряє доступність бази й пише результат у журнал -
/// це дає змогу побачити проблеми з підключенням до того, як вони стануть інцидентом.
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
            catch (OperationCanceledException)
            {
                // Штатна зупинка застосунку - виходимо без помилки.
                break;
            }
        }
    }

    private async Task CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        var canConnect = await db.Database.CanConnectAsync(cancellationToken);

        LogDatabaseChecked(logger, canConnect, db.Database.ProviderName ?? "невідомий провайдер");
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
}
