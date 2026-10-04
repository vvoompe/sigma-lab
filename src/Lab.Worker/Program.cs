using Lab.Infrastructure.DependencyInjection;
using Lab.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLabInfrastructure(builder.Configuration);
builder.Services.AddHostedService<SyncWorker>();

var host = builder.Build();
await host.RunAsync();
