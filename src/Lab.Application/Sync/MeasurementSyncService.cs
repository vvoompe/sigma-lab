using System.ComponentModel.DataAnnotations;
using Lab.Application.Abstractions;
using Lab.Application.Resources;
using Lab.Application.Security;
using Lab.Contracts.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lab.Application.Sync;

/// <summary>Кожен елемент і повторне читання після відкату мають свій scoped DbContext.</summary>
public sealed partial class MeasurementSyncService(IServiceScopeFactory scopes, ILogger<MeasurementSyncService> logger) : IMeasurementSyncService
{
    public async Task<MeasurementSyncBatchResult> SynchronizeBatchAsync(
        MeasurementSyncBatchRequest request, CancellationToken cancellationToken = default)
    {
        ResourceValidation.Validate(request);
        var results = new List<MeasurementSyncResult>();
        foreach (var item in request.Items!)
            results.Add(await SynchronizeAsync(item, cancellationToken));
        return new MeasurementSyncBatchResult(results);
    }

    public async Task<MeasurementSyncResult> SynchronizeAsync(
        MeasurementSyncRequest? request, CancellationToken cancellationToken = default)
    {
        try
        {
            try
            {
                return (await ExecuteAsync(request, true, cancellationToken))!;
            }
            catch (Exception exception) when (exception is DbUpdateException
                or ResourceException { Kind: ResourceErrorKind.Conflict })
            {
                // Після невдалого SaveChanges відкидаємо весь контекст. Зокрема,
                // PostgreSQL уже відкотив вставку при конфлікті Version агрегата.
                var duplicate = await ExecuteAsync(request, false, cancellationToken);
                if (duplicate is not null) return duplicate;
                if (exception is DbUpdateConcurrencyException)
                    return Failure(request, 409, "Серію змінено паралельним запитом; перечитайте її.",
                        await ReadVersionAsync(request, cancellationToken));
                throw;
            }
        }
        catch (ResourceException exception)
        {
            var status = exception.Kind switch
            {
                ResourceErrorKind.Unauthorized => 401,
                ResourceErrorKind.Forbidden => 403,
                ResourceErrorKind.NotFound => 404,
                ResourceErrorKind.Conflict => 409,
                _ => throw new InvalidOperationException("Невідомий вид прикладної помилки.", exception),
            };
            return Failure(request, status, exception.Message,
                status == 409 ? await ReadVersionAsync(request, cancellationToken) : null);
        }
        catch (Exception exception) when (exception is ArgumentException or ValidationException)
        {
            return Failure(request, 400, "Некоректні дані виміру; перевірте поля, UTC-час і діапазон приладу.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogItemFailure(logger, exception);
            return Failure(request, 500, "Внутрішня помилка сервера.");
        }
    }

    private async Task<MeasurementSyncResult?> ExecuteAsync(
        MeasurementSyncRequest? request, bool create, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MeasurementSyncAttempt>()
            .ExecuteAsync(request, create, cancellationToken);
    }

    internal static MeasurementSyncResult Failure(MeasurementSyncRequest? request, int status, string title, long? version = null) =>
        new(request?.ClientId, status, false, null, version, new MeasurementSyncError(title));

    private async Task<long?> ReadVersionAsync(MeasurementSyncRequest? request, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var owner = ResourceValidation.UserId(scope.ServiceProvider.GetRequiredService<ICurrentUser>());
        return await scope.ServiceProvider.GetRequiredService<IAppDbContext>().Experiments.AsNoTracking()
            .Where(item => item.Id == request!.ExperimentId && item.OwnerId == owner)
            .Select(item => (long?)item.Version).SingleOrDefaultAsync(cancellationToken);
    }

    [LoggerMessage(EventId = 2100, Level = LogLevel.Error, Message = "Серверна помилка елемента синхронізації.")]
    private static partial void LogItemFailure(ILogger logger, Exception exception);
}
