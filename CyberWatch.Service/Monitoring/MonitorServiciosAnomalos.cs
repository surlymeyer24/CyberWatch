using System.Management;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CyberWatch.Service.Config;
using CyberWatch.Service.Detection;
using CyberWatch.Service.Services;
using CyberWatch.Shared.Config;
using CyberWatch.Shared.Helpers;
using CyberWatch.Shared.Models;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberWatch.Service.Monitoring;

/// <summary>
/// Servicios en ejecución (WMI): si <see cref="X509Certificate2.Verify"/> falla, SHA-256 del PE;
/// sin lista blanca remota (<see cref="IConfigServiciosCache"/>) → alerta <c>servicio_no_firmado</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MonitorServiciosAnomalos : BackgroundService
{
    private readonly ILogger<MonitorServiciosAnomalos> _logger;
    private readonly FirebaseSettings _firebase;
    private readonly UmbralesSettings _umbrales;
    private readonly IConfigServiciosCache _configServicios;
    private readonly IFirebaseAlertService _alertas;

    private FirestoreDb? _db;
    private string? _machineId;

    public MonitorServiciosAnomalos(
        IOptions<FirebaseSettings> firebase,
        IOptions<UmbralesSettings> umbrales,
        IConfigServiciosCache configServicios,
        IFirebaseAlertService alertas,
        ILogger<MonitorServiciosAnomalos> logger)
    {
        _firebase = firebase.Value;
        _umbrales = umbrales.Value;
        _configServicios = configServicios;
        _alertas = alertas;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_umbrales.MonitorServiciosAnomalosHabilitado)
        {
            _logger.LogInformation("ServiciosAnomalos: deshabilitado (Umbrales:MonitorServiciosAnomalosHabilitado=false).");
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            _logger.LogInformation("ServiciosAnomalos: solo Windows; monitor inactivo.");
            return;
        }

        _machineId = MachineIdHelper.Read();
        if (_machineId == null)
        {
            _logger.LogWarning("ServiciosAnomalos: machineId no encontrado; monitor desactivado.");
            return;
        }

        if (!_firebase.IsAdminConfigured)
            _logger.LogInformation("ServiciosAnomalos: Firebase no configurado; dedup y alertas Firestore inactivos.");

        else
        {
            try
            {
                var cred = _firebase.GetEffectiveCredentialPath();
                if (cred != null)
                    _db = FirestoreDbFactory.Create(_firebase.ProjectId, cred);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ServiciosAnomalos: error al conectar con Firestore.");
            }
        }

        var minutos = Math.Max(1, _umbrales.IntervaloServiciosAnomalosMinutos);
        var intervalo = TimeSpan.FromMinutes(minutos);
        _logger.LogInformation(
            "ServiciosAnomalos iniciado; intervalo {Minutos} min; dedup {Horas} h.",
            minutos,
            Math.Max(1, _umbrales.DedupServiciosAnomalosHoras));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluarServiciosAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ServiciosAnomalos: error en ciclo.");
            }

            try { await Task.Delay(intervalo, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task EvaluarServiciosAsync(CancellationToken ct)
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, PathName FROM Win32_Service WHERE State='Running'");

        ManagementObjectCollection? resultados = null;
        try
        {
            resultados = searcher.Get();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ServiciosAnomalos: WMI Win32_Service falló.");
            return;
        }

        try
        {
            foreach (ManagementObject mo in resultados)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var nombre = mo["Name"]?.ToString();
                    var pathRaw = mo["PathName"]?.ToString();
                    if (string.IsNullOrWhiteSpace(nombre))
                        continue;

                    var expanded = ServicioWindowsPaths.NormalizarImagePath(pathRaw ?? "");
                    var binPath = ServicioWindowsPaths.ExtraerRutaBinarioFirma(expanded);
                    var pathFs = ServicioWindowsPaths.NormalizarPathParaFileSystem(binPath);

                    if (string.IsNullOrWhiteSpace(pathFs))
                    {
                        _logger.LogDebug("ServiciosAnomalos: sin ruta ejecutable para {Nombre}", nombre);
                        continue;
                    }

                    if (!File.Exists(pathFs))
                    {
                        _logger.LogDebug("ServiciosAnomalos: archivo no existe {Nombre} → {Ruta}", nombre, pathFs);
                        continue;
                    }

                    if (CadenaFirmaVerifyOk(pathFs))
                        continue;

                    var hashHex = await CalcularSha256HexAsync(pathFs, ct).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(hashHex))
                    {
                        _logger.LogDebug("ServiciosAnomalos: no se pudo hashear {Nombre} ({Ruta})", nombre, pathFs);
                        continue;
                    }

                    if (_configServicios.EsHashPermitido(hashHex) || _configServicios.EsNombreScmExcluido(nombre))
                        continue;

                    await PersistirAlertaAsync(nombre, expanded, pathFs, hashHex, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "ServiciosAnomalos: error procesando un servicio WMI.");
                }
            }
        }
        finally
        {
            resultados?.Dispose();
        }
    }

    private static bool CadenaFirmaVerifyOk(string pathFs)
    {
        try
        {
            using var raw = X509Certificate.CreateFromSignedFile(pathFs);
            using var cert = new X509Certificate2(raw);
            return cert.Verify();
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> CalcularSha256HexAsync(string pathFs, CancellationToken ct)
    {
        try
        {
            await using var fs = new FileStream(
                pathFs,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1 << 16,
                useAsync: true);

            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(fs, ct).ConfigureAwait(false);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    private async Task PersistirAlertaAsync(
        string nombreScm,
        string imagePathExpandido,
        string rutaBinario,
        string hashSha256,
        CancellationToken ct)
    {
        var alerta = new Alerta
        {
            Tipo = ServicioAnomaloTipoAlerta.NoFirmado,
            EventoId = 0,
            Descripcion =
                $"Servicio en ejecución sin firma verificada y fuera de listas remotas (config/servicios): {nombreScm}",
            Detalle =
                $"ImagePath: {imagePathExpandido}; binario: {rutaBinario}; SHA256={hashSha256}; policy=Verify_failed_hash_not_whitelisted",
            NombreServicio = nombreScm,
            FechaHora = Timestamp.FromDateTime(DateTime.UtcNow),
            Origen = nameof(MonitorServiciosAnomalos),
            MachineId = _machineId ?? "",
            Hostname = Environment.MachineName,
            RutaEjecutableOriginal = rutaBinario,
            HashEjecutableSha256 = hashSha256
        };

        if (_db == null || string.IsNullOrEmpty(_machineId))
        {
            _logger.LogWarning(
                "ServiciosAnomalos: alerta local — {Nombre} hash={Hash} ({Ruta})",
                nombreScm, hashSha256, rutaBinario);
            return;
        }

        var colAlertas = _db.Collection(_firebase.FirestoreColeccionInstancias)
            .Document(_machineId)
            .Collection(_firebase.FirestoreCollectionAlertas);

        var ventanaHoras = Math.Max(1, _umbrales.DedupServiciosAnomalosHoras);
        var desde = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(-ventanaHoras));

        try
        {
            var existente = await colAlertas
                .WhereEqualTo("tipo", ServicioAnomaloTipoAlerta.NoFirmado)
                .WhereEqualTo("nombreServicio", nombreScm)
                .WhereGreaterThanOrEqualTo("fechaHora", desde)
                .Limit(1)
                .GetSnapshotAsync(ct)
                .ConfigureAwait(false);

            if (existente.Count > 0)
            {
                _logger.LogDebug("ServiciosAnomalos: dedup omitido para {Nombre}", nombreScm);
                return;
            }

            await _alertas.AgregarAlertaInstanciaAsync(alerta, ct).ConfigureAwait(false);
            _logger.LogWarning(
                "ServiciosAnomalos: alerta enviada — {Nombre} hash={Hash} → {Ruta}",
                nombreScm, hashSha256, rutaBinario);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ServiciosAnomalos: error al guardar alerta para {Nombre}", nombreScm);
        }
    }
}
