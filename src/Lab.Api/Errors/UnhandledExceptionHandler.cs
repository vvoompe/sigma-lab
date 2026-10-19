using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using Lab.Application.Resources;
using Microsoft.EntityFrameworkCore;

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
            ResourceException resourceException => (
                resourceException.Kind switch
                {
                    ResourceErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
                    ResourceErrorKind.Forbidden => StatusCodes.Status403Forbidden,
                    ResourceErrorKind.NotFound => StatusCodes.Status404NotFound,
                    ResourceErrorKind.Conflict => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status500InternalServerError,
                },
                resourceException.Message,
                (string?)null),
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "Ресурс змінено іншим запитом; перечитайте його перед повтором.",
                (string?)null),
            ValidationException validationException => (
                StatusCodes.Status400BadRequest,
                "Некоректні дані запиту.",
                validationException.Message),
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
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);

        return true;
    }

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Error,
        Message = "Необроблений виняток, код відповіді {Status}")]
    private static partial void LogUnhandled(ILogger logger, int status, Exception exception);
}
