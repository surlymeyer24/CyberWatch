using CyberWatch.Service;
using CyberWatch.Service.Config;
using CyberWatch.Shared.Config;
using CyberWatch.Service.Detection;
using CyberWatch.Shared.Logging;
using CyberWatch.Service.Monitoring;
using CyberWatch.Service.Response;
using CyberWatch.Service.Services;
using Microsoft.Extensions.Options;

IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureLogging((context, logging) =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
        logging.AddProvider(new FileLoggerProvider("cyberwatch_service.log", null, LogLevel.Information));
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
