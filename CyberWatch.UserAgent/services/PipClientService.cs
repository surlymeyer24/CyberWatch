using System.IO.Pipes;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text.Json;
using CyberWatch.Shared.Helpers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CyberWatch.UserAgent.services;

[SupportedOSPlatform("windows")]
public class PipClientService : BackgroundService
{
    private const string NombrePipe = "CyberWatch_AgentPipe";
    private const string NombreServicioWindows = "CyberWatch";

    private readonly CapturaService _captura;
    private readonly ILogger<PipClientService> _logger;

    /// <summary>Momento en que el pipe dejó de estar usable (conexión o lectura).</summary>
    private DateTime? _inicioFallaUtc;

    public PipClientService(CapturaService captura, ILogger<PipClientService> logger)
    {
        _captura = captura;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", NombrePipe, PipeDirection.In, PipeOptions.Asynchronous);
                _logger.LogDebug("Conectando al pipe del Service...");
                await pipe.ConnectAsync(stoppingToken).ConfigureAwait(false);
                _inicioFallaUtc = null;
                _logger.LogInformation("Conectado al pipe del Service.");

                using var reader = new StreamReader(pipe);
                while (!stoppingToken.IsCancellationRequested && pipe.IsConnected)
                {
                    var linea = await reader.ReadLineAsync(stoppingToken).ConfigureAwait(false);
                    if (linea == null) break;

                    try
                    {
                        var evt = JsonSerializer.Deserialize<EventoAgente>(linea,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (evt?.Tipo == "ping") continue;
                        if (evt?.Tipo == "amenaza")
                            await _captura.TomarCapturaAsync(evt.Proceso ?? "desconocido").ConfigureAwait(false);
                    }
                    catch (JsonException) { /* ignorar */ }
                }

                _logger.LogInformation("Pipe desconectado. Reconectando...");
                _inicioFallaUtc ??= DateTime.UtcNow;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error en pipe, reintentando en 5s...");
                _inicioFallaUtc ??= DateTime.UtcNow;
            }

            await IntentarArrancarServicioSiCorrespondeAsync().ConfigureAwait(false);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private Task IntentarArrancarServicioSiCorrespondeAsync()
    {
        if (_inicioFallaUtc == null || WatchdogPauseLock.Activo())
            return Task.CompletedTask;

        var transcurrido = DateTime.UtcNow - _inicioFallaUtc.Value;
        if (transcurrido <= TimeSpan.FromSeconds(30))
            return Task.CompletedTask;

        try
        {
            using var sc = new ServiceController(NombreServicioWindows);
            if (sc.Status == ServiceControllerStatus.Running)
            {
                _inicioFallaUtc = DateTime.UtcNow;
                return Task.CompletedTask;
            }

            sc.Start();
            _logger.LogInformation("[Watchdog] ServiceController.Start({Svc}) tras fallo prolongado del pipe.", NombreServicioWindows);
        }
        catch (Exception)
        {
            /* sin privilegios de admin: esperado en muchos equipos */
        }

        _inicioFallaUtc = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    private record EventoAgente(string? Tipo, string? Proceso);
}
