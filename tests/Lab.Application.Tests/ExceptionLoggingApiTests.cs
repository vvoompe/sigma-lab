using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using Lab.Application.Resources;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lab.Application.Tests;

public sealed class ExceptionLoggingApiTests
{
    [Theory]
    [InlineData("argument", 400)]
    [InlineData("range", 400)]
    [InlineData("validation", 400)]
    [InlineData("missing", 404)]
    [InlineData("conflict", 409)]
    [InlineData("concurrency", 409)]
    public async Task Expected_failure_has_no_error_log_or_exception(string kind, int status)
    {
        var logs = new CaptureLogs();
        await using var factory = new ErrorsFactory(logs);
        using var client = factory.CreateClient();
        ApiTestJwt.Authenticate(client);
        using var response = await client.GetAsync($"/tests/errors/{kind}");
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(status, problem?.Status);
        var entries = logs.Entries.Where(item => item.Category == "Lab.Api.Errors.UnhandledExceptionHandler").ToArray();
        var entry = Assert.Single(entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain("sensitive-input", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Entries, item => item.Level >= LogLevel.Error);
        if (kind is "argument" or "range")
        {
            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("kind", text, StringComparison.Ordinal);
            Assert.DoesNotContain("sensitive-input", text, StringComparison.Ordinal);
            Assert.Equal("Перевірте значення полів запиту.", problem?.Detail);
        }
    }

    [Fact]
    public async Task Unexpected_failure_is_logged_with_exception_but_response_is_safe()
    {
        var logs = new CaptureLogs();
        await using var factory = new ErrorsFactory(logs);
        using var client = factory.CreateClient();
        ApiTestJwt.Authenticate(client);
        using var response = await client.GetAsync("/tests/errors/server");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Внутрішня помилка сервера.", problem?.Title);
        Assert.Null(problem?.Detail);
        Assert.DoesNotContain("sensitive-input", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var entry = Assert.Single(logs.Entries, item => item.Category == "Lab.Api.Errors.UnhandledExceptionHandler");
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.NotNull(entry.Exception.StackTrace);
    }

    private sealed class ErrorsFactory(CaptureLogs logs) : WebApplicationFactory<Program>
    {
        static ErrorsFactory() => ApiTestJwt.EnsureConfigured();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureServices(services => services.AddControllers().AddApplicationPart(typeof(ErrorsProbeController).Assembly));
        }
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class CaptureLogs : ILoggerProvider
    {
        internal ConcurrentQueue<LogEntry> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, Entries);
        public void Dispose() { }

        private sealed class CaptureLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception), exception));
        }
    }
}

/// <summary>Тільки тестовий контролер: додається до тестового хоста, не до API.</summary>
[ApiController]
[Route("tests/errors")]
public sealed class ErrorsProbeController : ControllerBase
{
    [HttpGet("{kind}")]
    public IActionResult Get(string kind)
    {
        ArgumentNullException.ThrowIfNull(HttpContext);
        throw (kind switch
        {
            "argument" => new ArgumentException("sensitive-input", nameof(kind)),
            "range" => new ArgumentOutOfRangeException(nameof(kind), "sensitive-input", "Поза діапазоном."),
            "validation" => new ValidationException("Перевірте дані."),
            "missing" => new KeyNotFoundException("Ресурс не знайдено."),
            "conflict" => new ResourceException(ResourceErrorKind.Conflict, "ClientId вже використовується."),
            "concurrency" => new DbUpdateConcurrencyException("sensitive-input"),
            _ => new InvalidOperationException("sensitive-input"),
        });
    }
}
