using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CyberWatch.UserAgent.services;

public class PipClientService : BackgroundService
{
    private const string NombrePipe = "CyberWatch_AgentPipe";
    private readonly CapturaService _captura;
    private readonly ILogger<PipClientService> _logger;

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
                await pipe.ConnectAsync(stoppingToken);
                _logger.LogInformation("Conectado al pipe del Service.");

                using var reader = new StreamReader(pipe);
                while (!stoppingToken.IsCancellationRequested && pipe.IsConnected)
                {
                    var linea = await reader.ReadLineAsync(stoppingToken);
                    if (linea == null) break;

                    try
                    {
                        var evt = JsonSerializer.Deserialize<EventoAgente>(linea,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (evt?.Tipo == "amenaza")
                            await _captura.TomarCapturaAsync(evt.Proceso ?? "desconocido");
                    }
                    catch (JsonException) { /* mensaje malformado, ignorar */ }
                }

                _logger.LogInformation("Pipe desconectado. Reconectando...");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error en pipe, reintentando en 5s...");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private record EventoAgente(string? Tipo, string? Proceso);
}
