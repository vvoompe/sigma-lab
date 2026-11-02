using Lab.Infrastructure.DependencyInjection;
using Lab.Worker;
using Lab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLabInfrastructure(builder.Configuration);
builder.Services.AddHostedService<SyncWorker>();

var host = builder.Build();
if (builder.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await using var scope = host.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<LabDbContext>().Database.MigrateAsync();
}
await host.RunAsync();
