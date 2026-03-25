using CyberWatch.Shared.Config;
using CyberWatch.Shared.Helpers;
using CyberWatch.Shared.Logging;
using CyberWatch.UserAgent.services;
using Microsoft.Extensions.Configuration;
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

            // Logs centralizados en Firestore
            var fbSettings = new FirebaseSettings();
            context.Configuration.GetSection(FirebaseSettings.SectionName).Bind(fbSettings);
            if (fbSettings.IsAdminConfigured)
            {
                var credPath = fbSettings.GetEffectiveCredentialPath();
                if (credPath != null)
                {
                    var firestoreDb = FirestoreDbFactory.Create(fbSettings.ProjectId, credPath);
                    var machineId = MachineIdHelper.Read() ?? "unknown";
                    logging.AddProvider(new FirestoreLoggerProvider(firestoreDb, "CyberWatch.UserAgent", machineId, Environment.MachineName));
                }
            }
        })
        .ConfigureServices((context, services) =>
        {
            services.Configure<FirebaseSettings>(context.Configuration.GetSection(FirebaseSettings.SectionName));

            services.AddSingleton<CapturaService>();
            services.AddHostedService(sp => sp.GetRequiredService<CapturaService>());
            services.AddSingleton<HistorialNavegacionService>();
            services.AddHostedService(sp => sp.GetRequiredService<HistorialNavegacionService>());
            services.AddHostedService<UbicacionService>();
            services.AddHostedService<PipClientService>();
            services.AddHostedService<ComandoService>();
        })
        .Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    var crash = Path.Combine(Path.GetTempPath(), "cyberwatch_ua_crash.txt");
    await File.WriteAllTextAsync(crash, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} CRASH:{Environment.NewLine}{ex}");
}
