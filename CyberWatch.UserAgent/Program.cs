using CyberWatch.Shared.Config;
using CyberWatch.Shared.Logging;
using CyberWatch.UserAgent.services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

try
{
    IHost host = Host.CreateDefaultBuilder(args)
        .UseContentRoot(AppContext.BaseDirectory)
        .ConfigureLogging((context, logging) =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyberWatch");
            logging.AddProvider(new FileLoggerProvider("cyberwatch_useragent.log", logDir, LogLevel.Information));
        })
        .ConfigureServices((context, services) =>
        {
            services.Configure<FirebaseSettings>(context.Configuration.GetSection(FirebaseSettings.SectionName));

            services.AddSingleton<CapturaService>();
            services.AddHostedService(sp => sp.GetRequiredService<CapturaService>());
            services.AddHostedService<UbicacionService>();
            services.AddHostedService<PipClientService>();
            services.AddHostedService<ComandoService>();
            services.AddHostedService<HistorialNavegacionService>();
        })
        .Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    var crash = Path.Combine(Path.GetTempPath(), "cyberwatch_ua_crash.txt");
    await File.WriteAllTextAsync(crash, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} CRASH:{Environment.NewLine}{ex}");
}
