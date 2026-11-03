using System.Globalization;
using System.Text;
using Lab.Application.Abstractions;
using Lab.Application.Security;
using Microsoft.EntityFrameworkCore;

namespace Lab.Application.Resources;

/// <summary>HTTP CSV з єдиного знімка даних; фінальний SELECT повторює політику читання серії.</summary>
public sealed class ExperimentExportService(
    IAppDbContext db, IExperimentService experiments, IExperimentAccessService access) : IExperimentExportService
{
    public async Task<byte[]> ExportCsvAsync(int id, CancellationToken cancellationToken = default)
    {
        var allowed = await experiments.GetAsync(id, cancellationToken);
        // Призначення менеджера може змінитися без зміни Experiment.Version.
        // Фільтр доступу має діяти в тому самому SELECT, який читає дані CSV.
        var series = await access.ApplyReadFilter(db.Experiments.AsNoTracking()).Include(item => item.Instrument)
            .Include(item => item.Measurements).AsSingleQuery()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw ResourceValidation.Missing();
        if (series.Version != allowed.Version)
            throw ResourceValidation.Conflict("Серію змінено перед експортом; перечитайте ресурс.");
        using var output = new MemoryStream();
        await using (var writer = new StreamWriter(output, new UTF8Encoding(true), leaveOpen: true))
        {
            writer.NewLine = "\r\n";
            await writer.WriteLineAsync("ExperimentId,ExperimentName,ClientId,Value,Unit,MeasuredAtUtc".AsMemory(), cancellationToken);
            foreach (var measurement in series.Measurements.OrderBy(item => item.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = string.Join(',', series.Id.ToString(CultureInfo.InvariantCulture), CsvText(series.Name),
                    measurement.ClientId.ToString(), measurement.Value.ToString("R", CultureInfo.InvariantCulture),
                    CsvText(series.Unit), measurement.MeasuredAt.ToString("O", CultureInfo.InvariantCulture));
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            }
        }
        return output.ToArray();
    }

    private static string CsvText(string value)
    {
        var first = value.AsSpan().TrimStart();
        if (first.Length > 0 && "=+-@".Contains(first[0], StringComparison.Ordinal)) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
