using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CyberWatch.Service.Services;

/// <summary>
/// Named Pipe server que envía eventos al CyberWatch.UserAgent corriendo en la sesión de usuario.
/// </summary>
public class AgentePipeServerService : BackgroundService
{
    public const string NombrePipe = "CyberWatch_AgentPipe";

    private readonly ConcurrentDictionary<Guid, StreamWriter> _clientes = new();
    private readonly ILogger<AgentePipeServerService> _logger;

    public AgentePipeServerService(ILogger<AgentePipeServerService> logger)
    {
        _logger = logger;
    }

    public async Task NotificarAmenazaAsync(string proceso, CancellationToken ct = default)
    {
        if (_clientes.IsEmpty) return;

        var json = JsonSerializer.Serialize(new { tipo = "amenaza", proceso });
        foreach (var (id, writer) in _clientes)
        {
            try
            {
                await writer.WriteLineAsync(json.AsMemory(), ct);
                await writer.FlushAsync(ct);
            }
            catch
            {
                _clientes.TryRemove(id, out _);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Named Pipe server iniciado: {Pipe}", NombrePipe);

        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CrearPipeServer();
                await pipe.WaitForConnectionAsync(stoppingToken);

                var id = Guid.NewGuid();
                var writer = new StreamWriter(pipe) { AutoFlush = false };
                _clientes[id] = writer;
                _logger.LogDebug("UserAgent conectado al pipe (id: {Id}).", id);

                _ = MonitorearDesconexionAsync(id, pipe, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                pipe?.Dispose();
                _logger.LogWarning(ex, "Error en pipe server, reintentando en 2s...");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task MonitorearDesconexionAsync(Guid id, NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            while (pipe.IsConnected && !ct.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _clientes.TryRemove(id, out _);
            pipe.Dispose();
            _logger.LogDebug("UserAgent desconectado del pipe (id: {Id}).", id);
        }
    }

    private static NamedPipeServerStream CrearPipeServer()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            NombrePipe,
            PipeDirection.Out,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0, 0, security);
    }
}
