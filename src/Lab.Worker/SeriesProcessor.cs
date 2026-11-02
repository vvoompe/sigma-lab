using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Lab.Application.Statistics;
using Lab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lab.Worker;

/// <summary>Відновлюваний кеш; доменні дані та їхня версія залишаються джерелом істини.</summary>
public sealed record SeriesCache(int ExperimentId, long Version, int Count, string Unit,
    double? Mean, double? SampleStandardDeviation, double? StandardError);

/// <summary>Перерахунок змінених серій і потоковий експорт з атомарною публікацією маніфесту.</summary>
public sealed class SeriesProcessor(LabDbContext database, string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> ProcessAsync(int experimentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        // Єдиний узгоджений знімок для версії, статистики та обох форматів експорту.
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        var experiment = await database.Experiments.AsNoTracking().Include(item => item.Instrument)
            .SingleAsync(item => item.Id == experimentId, cancellationToken).ConfigureAwait(false);
        var prefix = Path.Combine(directory, $"series-{experiment.Id}-v{experiment.Version}");
        var manifest = prefix + ".manifest.json";
        if (File.Exists(manifest) && File.Exists(prefix + ".csv") && File.Exists(prefix + ".json"))
        {
            await using var cached = File.OpenRead(manifest);
            var result = await JsonSerializer.DeserializeAsync<SeriesCache>(cached, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (result?.ExperimentId == experiment.Id && result.Version == experiment.Version)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }
        }
        var values = await database.Measurements.AsNoTracking().Where(item => item.ExperimentId == experimentId)
            .OrderBy(item => item.Id).Select(item => item.Value).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var statistics = values.Length >= 2 ? SeriesStatistics.Calculate(values, experiment.Unit) : null;
        var cache = new SeriesCache(experiment.Id, experiment.Version, values.Length, experiment.Unit,
            statistics?.Mean ?? (values.Length == 1 ? values[0] : null), statistics?.SampleStandardDeviation, statistics?.StandardError);
        var temporaryCsv = prefix + $".{Guid.NewGuid():N}.csv.tmp";
        var temporaryJson = prefix + $".{Guid.NewGuid():N}.json.tmp";
        var temporaryManifest = prefix + $".{Guid.NewGuid():N}.manifest.tmp";
        try
        {
            await ExportCsvAsync(experimentId, experiment.Unit, temporaryCsv, cancellationToken).ConfigureAwait(false);
            await using (var stream = File.Create(temporaryJson))
            {
                // Серіалізатор споживає IAsyncEnumerable з БД поступово, без ToList/ToArray.
                var rows = database.Measurements.AsNoTracking().Where(item => item.ExperimentId == experimentId).OrderBy(item => item.Id)
                    .Select(item => new ExportMeasurement(item.ClientId, item.Value, experiment.Unit, item.MeasuredAt)).AsAsyncEnumerable();
                await JsonSerializer.SerializeAsync(stream, rows, JsonOptions, cancellationToken).ConfigureAwait(false);
            }
            await File.WriteAllTextAsync(temporaryManifest, JsonSerializer.Serialize(cache, JsonOptions), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            File.Move(temporaryCsv, prefix + ".csv", true);
            File.Move(temporaryJson, prefix + ".json", true);
            // Споживач використовує тільки версії з маніфестом. Він публікується останнім.
            File.Move(temporaryManifest, manifest, true);
            return true;
        }
        finally
        {
            DeleteTemporary(temporaryCsv);
            DeleteTemporary(temporaryJson);
            DeleteTemporary(temporaryManifest);
        }
    }

    public void CleanTemporaryFiles(DateTime olderThanUtc)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "series-*.tmp"))
        {
            if (File.GetLastWriteTimeUtc(file) < olderThanUtc) File.Delete(file);
        }
    }

    private async Task ExportCsvAsync(int experimentId, string unit, string path, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        await writer.WriteLineAsync("ClientId,Value,Unit,MeasuredAtUtc".AsMemory(), cancellationToken).ConfigureAwait(false);
        var rows = database.Measurements.AsNoTracking().Where(item => item.ExperimentId == experimentId)
            .OrderBy(item => item.Id).Select(item => new { item.ClientId, item.Value, item.MeasuredAt }).AsAsyncEnumerable();
        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var line = string.Join(',', row.ClientId, row.Value.ToString("R", CultureInfo.InvariantCulture),
                CsvText(unit), row.MeasuredAt.ToString("O", CultureInfo.InvariantCulture));
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string CsvText(string value)
    {
        // Захист текстової колонки від формул при відкритті CSV в електронній таблиці.
        if (value.Length > 0 && "=+-@\t\r\n".Contains(value[0], StringComparison.Ordinal)) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static void DeleteTemporary(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private sealed record ExportMeasurement(Guid ClientId, double Value, string Unit, DateTime MeasuredAtUtc);
}
