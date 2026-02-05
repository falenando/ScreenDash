using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrivilegedService;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddEventLog();
builder.Logging.AddSimpleConsole();
builder.Services.AddHostedService<PrivilegedWorker>();

var app = builder.Build();

await app.RunAsync();
