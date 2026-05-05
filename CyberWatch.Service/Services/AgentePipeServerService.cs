using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.Versioning;
using CyberWatch.Shared.Helpers;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CyberWatch.Service.Services;

/// <summary>
/// Named Pipe server que envía eventos al CyberWatch.UserAgent corriendo en la sesión de usuario.
/// </summary>
[SupportedOSPlatform("windows")]
public class AgentePipeServerService : BackgroundService
{
    public const string NombrePipe = "CyberWatch_AgentPipe";

    private readonly ConcurrentDictionary<Guid, StreamWriter> _clientes = new();
    private readonly ILogger<AgentePipeServerService> _logger;
    private readonly object _watchdogSync = new();
    private DateTime _ultimaActividadUtc = DateTime.UtcNow;

    private static readonly TimeSpan WatchdogSinCliente = TimeSpan.FromSeconds(60);

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

        _ = WatchdogUserAgentAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CrearPipeServer();
                await pipe.WaitForConnectionAsync(stoppingToken);

                MarcarActividadPipe();

                var id = Guid.NewGuid();
                var writer = new StreamWriter(pipe) { AutoFlush = false };
                _clientes[id] = writer;
                _logger.LogDebug("UserAgent conectado al pipe (id: {Id}).", id);

                _ = MonitorearDesconexionAsync(id, pipe, writer, stoppingToken);
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

    private static readonly TimeSpan KeepaliveInterval = TimeSpan.FromSeconds(20);

    private async Task MonitorearDesconexionAsync(Guid id, NamedPipeServerStream pipe, StreamWriter writer, CancellationToken ct)
    {
        var lastKeepalive = DateTime.UtcNow;
        try
        {
            while (pipe.IsConnected && !ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                // Keepalive: evita que el pipe inactivo se cierre por timeout del SO o firewall
                if (DateTime.UtcNow - lastKeepalive >= KeepaliveInterval && _clientes.ContainsKey(id))
                {
                    try
                    {
                        await writer.WriteLineAsync("{\"tipo\":\"ping\"}");
                        await writer.FlushAsync(ct);
                        lastKeepalive = DateTime.UtcNow;
                    }
                    catch { break; }
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _clientes.TryRemove(id, out _);
            try { writer.Dispose(); } catch { }
            pipe.Dispose();
            MarcarActividadPipe();
            _logger.LogDebug("UserAgent desconectado del pipe (id: {Id}).", id);
        }
    }

    private void MarcarActividadPipe()
    {
        lock (_watchdogSync)
            _ultimaActividadUtc = DateTime.UtcNow;
    }

    private async Task WatchdogUserAgentAsync(CancellationToken ct)
    {
        const string nombreExe = "CyberWatch.UserAgent.exe";
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);

                if (WatchdogPauseLock.Activo())
                    continue;

                bool vacio;
                TimeSpan sinActividad;
                lock (_watchdogSync)
                {
                    vacio = _clientes.IsEmpty;
                    sinActividad = DateTime.UtcNow - _ultimaActividadUtc;
                }

                if (!vacio || sinActividad <= WatchdogSinCliente)
                    continue;

                var exePath = Path.Combine(AppContext.BaseDirectory, nombreExe);
                _logger.LogWarning(
                    "[Watchdog] Sin cliente en el pipe durante {Seg}s; relanzando UserAgent.",
                    (int)sinActividad.TotalSeconds);

                LanzadorUserAgent.RelanzarDesdeTarea(exePath, _logger);

                lock (_watchdogSync)
                    _ultimaActividadUtc = DateTime.UtcNow;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Watchdog] Error en bucle de vigilancia del UserAgent.");
            }
        }
    }

    [SupportedOSPlatform("windows")]
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
