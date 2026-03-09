using CyberWatch.UserAgent.services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHost host = Host.CreateDefaultBuilder(args)
    .UseContentRoot(AppContext.BaseDirectory)
    .ConfigureServices((_, services) =>
    {
        services.AddSingleton<CapturaService>();
        services.AddHostedService(sp => sp.GetRequiredService<CapturaService>());
        services.AddHostedService<UbicacionService>();
        services.AddHostedService<PipClientService>();
        services.AddHostedService<ComandoService>();
    })
    .Build();

await host.RunAsync();
