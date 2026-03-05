using Microsoft.Extensions.Options;
using CyberWatch.Service;
using CyberWatch.Service.Config;
using CyberWatch.Service.Logging;
using CyberWatch.Service.Services;

IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureLogging((context, logging) =>
    {
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
        // Archivo en la carpeta del ejecutable para ver actualizaciones, errores, etc.
        logging.AddProvider(new FileLoggerProvider(null, LogLevel.Information));
    })
    .ConfigureServices((context, servicios) =>
    {
        servicios.Configure<FirebaseSettings>(context.Configuration.GetSection(FirebaseSettings.SectionName));
        servicios.AddSingleton<IPostConfigureOptions<FirebaseSettings>, FirebaseCredentialPathResolver>();
        servicios.Configure<AppVersionSettings>(context.Configuration.GetSection(AppVersionSettings.SectionName));
        servicios.AddSingleton<IFirebaseAlertService, FirebaseAlertService>();
        servicios.AddHostedService<ServicioCyberWatch>();
        servicios.AddHostedService<ActualizadorDesdeFirebaseService>();
        servicios.AddHostedService<RegistroInstanciaFirebaseService>();
        servicios.AddHostedService<EjecutorTareasFirebaseService>();
    })
    .Build();

await host.RunAsync();
