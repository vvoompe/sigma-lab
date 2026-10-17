using Lab.Api.Errors;
using Lab.Api.Security;
using Lab.Api.Startup;
using Lab.Application.DependencyInjection;
using Lab.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Шари підключаються двома викликами: прикладний і інфраструктурний.
builder.Services.AddLabInfrastructure(builder.Configuration);
builder.Services.AddLabApplication();
builder.Services.AddLabSecurity();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();

var app = builder.Build();

// Єдиний обробник помилок: назовні йдуть лише ProblemDetails, без трасування стеку.
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

// Схема бази будується міграціями під час старту (вимикається ключем Database:MigrateOnStartup).
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await DatabaseStartup.ApplyMigrationsAsync(app.Services, app.Logger);
}

app.MapControllers();
app.MapOpenApi();

await app.RunAsync();

/// <summary>Точка входу застосунку, доступна тестам інтеграції.</summary>
public partial class Program;
