using CyberWatch.Service;
using CyberWatch.Service.Config;
using CyberWatch.Shared.Config;
using CyberWatch.Service.Detection;
using CyberWatch.Shared.Helpers;
using CyberWatch.Shared.Logging;
using CyberWatch.Service.Monitoring;
using CyberWatch.Service.Response;
using CyberWatch.Service.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

// Modo consola: versión desplegada (misma lectura que en runtime: appsettings.json junto al .exe)
if (args.Length > 0)
{
    var a = args[0].Trim();
    if (string.Equals(a, "--version", StringComparison.OrdinalIgnoreCase)
        || string.Equals(a, "-v", StringComparison.OrdinalIgnoreCase)
        || string.Equals(a, "/version", StringComparison.OrdinalIgnoreCase)
        || string.Equals(a, "-version", StringComparison.OrdinalIgnoreCase))
    {
        var cfg = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
        var sec = cfg.GetSection(AppVersionSettings.SectionName);
        var version = sec["Version"] ?? "?";
        var serviceName = sec["ServiceName"] ?? "CyberWatch";
        Console.WriteLine($"{serviceName} {version}");
        return;
    }
}

IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureLogging((context, logging) =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
        logging.AddProvider(new FileLoggerProvider("cyberwatch_service.log", null, LogLevel.Information));

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
                logging.AddProvider(new FirestoreLoggerProvider(firestoreDb, "CyberWatch.Service", machineId, Environment.MachineName));
            }
        }
    })
    .ConfigureServices((context, servicios) =>
    {
        // ── Configuración ──────────────────────────────────────────────────────
        servicios.Configure<FirebaseSettings>(context.Configuration.GetSection(FirebaseSettings.SectionName));
        servicios.AddSingleton<IPostConfigureOptions<FirebaseSettings>, FirebaseCredentialPathResolver>();
        servicios.Configure<AppVersionSettings>(context.Configuration.GetSection(AppVersionSettings.SectionName));
        servicios.Configure<UmbralesSettings>(context.Configuration.GetSection(UmbralesSettings.SectionName));

        // ── Detección y respuesta ──────────────────────────────────────────────
        servicios.AddSingleton<IEvaluadorAmenazas, EvaluadorAmenazas>();
        servicios.AddSingleton<IGestorAlertas, GestorAlertas>();
        servicios.AddSingleton<ILiquidadorProcesos, LiquidarProcesos>();
        servicios.AddSingleton<ICuarentena, ServicioCuarentena>();
        servicios.AddSingleton<RastreadorProcesos>();
        servicios.AddSingleton<MonitorActividadArchivos>();

        // ── Firebase ───────────────────────────────────────────────────────────
        servicios.AddSingleton<IFirebaseAlertService, FirebaseAlertService>();

        // ── Pipe ───────────────────────────────────────────────────────────────
        servicios.AddSingleton<AgentePipeServerService>();
        servicios.AddHostedService(sp => sp.GetRequiredService<AgentePipeServerService>());

        // ── Hosted services ────────────────────────────────────────────────────
        servicios.AddHostedService<ServicioCyberWatch>();
        servicios.AddHostedService<RegistroInstanciaFirebaseService>();
        servicios.AddHostedService<EjecutorTareasFirebaseService>();
        servicios.AddHostedService<RegistradorTareaUsuarioService>();
        servicios.AddHostedService<SecurityEventMonitorService>();
    })
    .Build();

await host.RunAsync();
