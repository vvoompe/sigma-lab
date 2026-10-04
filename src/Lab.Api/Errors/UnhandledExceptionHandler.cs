using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Lab.Api.Errors;

/// <summary>
/// Перетворює винятки на відповіді <see cref="ProblemDetails"/>.
/// Подробиці внутрішніх помилок (500) назовні не віддаються - вони лише в журналі,
/// щоб не розкривати будову системи.
/// </summary>
/// <param name="logger">Журнал застосунку.</param>
internal sealed partial class UnhandledExceptionHandler(ILogger<UnhandledExceptionHandler> logger)
    : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var (status, title, detail) = exception switch
        {
            ArgumentException argumentException => (
                StatusCodes.Status400BadRequest,
                "Некоректні дані запиту.",
                argumentException.Message),
            KeyNotFoundException keyNotFoundException => (
                StatusCodes.Status404NotFound,
                "Ресурс не знайдено.",
                keyNotFoundException.Message),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Внутрішня помилка сервера.",
                (string?)null),
        };

        LogUnhandled(logger, status, exception);

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
            },
            cancellationToken);

        return true;
    }

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Error,
        Message = "Необроблений виняток, код відповіді {Status}")]
    private static partial void LogUnhandled(ILogger logger, int status, Exception exception);
}
