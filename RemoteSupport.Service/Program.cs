using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureServices(services =>
    {
        services.AddSingleton<ServiceLogger>();
        services.AddSingleton<SessionManager>();
        services.AddHostedService<ServiceHost>();
    });

await builder.Build().RunAsync();
