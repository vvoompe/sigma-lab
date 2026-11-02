namespace Lab.Application.Abstractions;

public interface IExperimentExportService
{
    Task<byte[]> ExportCsvAsync(int id, CancellationToken cancellationToken = default);
}
