using System.Diagnostics;
using CyberWatch.Service.Config;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberWatch.Service.Services;

/// <summary>
/// Lee el Event Log de Windows cada 2 minutos y detecta eventos de seguridad críticos:
/// - 1116: Malware detectado por Windows Defender
/// - 7036: Servicio detenido (detectar Defender desactivado)
/// - 4732: Usuario agregado al grupo Administradores
/// - 4625: Login fallido (>5 en 5 min = posible brute force)
/// Escribe alertas a Firestore colección "alertas" y actualiza "alertas_sistema" en la instancia.
/// </summary>
public class SecurityEventMonitorService : BackgroundService
{
    private readonly ILogger<SecurityEventMonitorService> _logger;
    private readonly FirebaseSettings _firebase;
    private FirestoreDb? _db;
    private string? _machineId;
    private DateTime _ultimaVerificacion = DateTime.UtcNow;

    // Contadores en memoria para detección de brute force
    private readonly List<DateTime> _loginsFallidos = new();

    public SecurityEventMonitorService(
        IOptions<FirebaseSettings> firebase,
        ILogger<SecurityEventMonitorService> logger)
    {
        _firebase = firebase.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _machineId = LeerMachineId();
        if (_machineId == null)
        {
            _logger.LogWarning("SecurityEventMonitor: machineId no encontrado, servicio desactivado.");
            return;
        }

        if (!_firebase.IsAdminConfigured)
        {
            _logger.LogInformation("SecurityEventMonitor: Firebase no configurado, servicio desactivado.");
            return;
        }

        try
        {
            var credPath = _firebase.GetEffectiveCredentialPath();
            if (!string.IsNullOrEmpty(credPath))
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credPath);
            _db = FirestoreDb.Create(_firebase.ProjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SecurityEventMonitor: error al conectar con Firestore.");
            return;
        }

        _logger.LogInformation("SecurityEventMonitor iniciado, verificando cada 2 minutos.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await VerificarEventosAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SecurityEventMonitor: error durante verificación.");
            }

            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
    }

    private async Task VerificarEventosAsync(CancellationToken ct)
    {
        var desde = _ultimaVerificacion;
        _ultimaVerificacion = DateTime.UtcNow;

        var alertas = new List<Dictionary<string, object>>();

        // --- Event ID 1116: Malware detectado por Defender ---
        alertas.AddRange(LeerEventos(
            "Microsoft-Windows-Windows Defender/Operational",
            1116, desde,
            msg => new Dictionary<string, object>
            {
                ["tipo"] = "malware_detectado",
                ["eventoId"] = 1116,
                ["descripcion"] = "Windows Defender detectó malware",
                ["detalle"] = msg[..Math.Min(500, msg.Length)]
            }));

        // --- Event ID 7036: Servicio detenido (filtrar Defender) ---
        alertas.AddRange(LeerEventos(
            "System", 7036, desde,
            msg =>
            {
                if (!msg.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase) &&
                    !msg.Contains("WinDefend", StringComparison.OrdinalIgnoreCase))
                    return null;
                if (!msg.Contains("detenido", StringComparison.OrdinalIgnoreCase) &&
                    !msg.Contains("stopped", StringComparison.OrdinalIgnoreCase))
                    return null;
                return new Dictionary<string, object>
                {
                    ["tipo"] = "defender_detenido",
                    ["eventoId"] = 7036,
                    ["descripcion"] = "Windows Defender fue detenido",
                    ["detalle"] = msg[..Math.Min(300, msg.Length)]
                };
            }));

        // --- Event ID 4732: Usuario agregado a grupo Administradores ---
        alertas.AddRange(LeerEventos(
            "Security", 4732, desde,
            msg => new Dictionary<string, object>
            {
                ["tipo"] = "admin_agregado",
                ["eventoId"] = 4732,
                ["descripcion"] = "Usuario agregado al grupo Administradores",
                ["detalle"] = msg[..Math.Min(500, msg.Length)]
            }));

        // --- Event ID 4625: Login fallido (brute force si >5 en 5 min) ---
        var loginsFallidosNuevos = LeerEventos("Security", 4625, desde, msg =>
            new Dictionary<string, object>
            {
                ["tipo"] = "brute_force",
                ["eventoId"] = 4625,
                ["descripcion"] = "Múltiples intentos de login fallidos (posible brute force)",
                ["detalle"] = msg[..Math.Min(300, msg.Length)]
            });

        // Contar en ventana de 5 minutos
        var ahora = DateTime.UtcNow;
        _loginsFallidos.AddRange(Enumerable.Repeat(ahora, loginsFallidosNuevos.Count));
        _loginsFallidos.RemoveAll(t => (ahora - t).TotalMinutes > 5);
        if (_loginsFallidos.Count >= 5 && loginsFallidosNuevos.Count > 0)
            alertas.Add(loginsFallidosNuevos[0]); // enviar una sola alerta por ráfaga

        if (alertas.Count == 0) return;

        _logger.LogWarning("SecurityEventMonitor: {Count} evento(s) de seguridad detectado(s).", alertas.Count);
        await EnviarAlertasAsync(alertas, ct);
    }

    private List<Dictionary<string, object>> LeerEventos(
        string logName, int eventId, DateTime desde,
        Func<string, Dictionary<string, object>?> mapear)
    {
        var resultado = new List<Dictionary<string, object>>();
        try
        {
            using var log = new EventLog(logName);
            foreach (EventLogEntry entry in log.Entries)
            {
                if (entry.InstanceId != eventId) continue;
                if (entry.TimeGenerated.ToUniversalTime() <= desde) continue;

                var mapped = mapear(entry.Message ?? "");
                if (mapped != null)
                    resultado.Add(mapped);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("SecurityEventMonitor: no se pudo leer log '{Log}': {Msg}", logName, ex.Message);
        }
        return resultado;
    }

    private async Task EnviarAlertasAsync(List<Dictionary<string, object>> alertas, CancellationToken ct)
    {
        if (_db == null || _machineId == null) return;

        var colAlertas   = _db.Collection(_firebase.FirestoreCollectionAlertas);
        var docInstancia = _db.Collection(_firebase.FirestoreColeccionInstancias).Document(_machineId);

        var resumenAlertas = new List<object>();

        foreach (var alerta in alertas)
        {
            alerta["fechaHora"] = Timestamp.FromDateTime(DateTime.UtcNow);
            alerta["origen"]    = "SecurityEventMonitor";
            alerta["machineId"] = _machineId;
            alerta["hostname"]  = Environment.MachineName;

            try { await colAlertas.AddAsync(alerta, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error al guardar alerta de sistema."); }

            resumenAlertas.Add(new
            {
                tipo      = alerta["tipo"],
                fechaHora = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                descripcion = alerta["descripcion"]
            });
        }

        // Actualizar campo alertas_sistema en la instancia (últimas 10)
        try
        {
            await docInstancia.SetAsync(
                new Dictionary<string, object> { ["alertas_sistema"] = resumenAlertas.TakeLast(10).ToList() },
                SetOptions.MergeAll, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al actualizar alertas_sistema en instancia.");
        }
    }

    private static string? LeerMachineId()
    {
        try
        {
            var idFile = Path.Combine(AppContext.BaseDirectory, "cyberwatch_machine_id.txt");
            if (File.Exists(idFile))
            {
                var id = File.ReadAllText(idFile).Trim();
                if (!string.IsNullOrEmpty(id) && id.Length >= 8) return id;
            }
        }
        catch { /* ignore */ }
        return null;
    }
}
