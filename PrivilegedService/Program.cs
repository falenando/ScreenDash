using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddEventLog();
builder.Logging.AddSimpleConsole();
builder.Services.AddHostedService<PrivilegedWorker>();
builder.Services.Configure<HostOptions>(options =>
{
    options.ServicesStopTimeout = TimeSpan.FromSeconds(5);
});

var app = builder.Build();

await app.RunAsync();
