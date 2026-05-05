using System.Runtime.Versioning;
using System.ServiceProcess;
using CyberWatch.Service.Config;
using CyberWatch.Service.Detection;
using CyberWatch.Shared.Config;
using CyberWatch.Shared.Helpers;
using CyberWatch.Shared.Models;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberWatch.Service.Services;

/// <summary>
/// Periódicamente verifica firma Authenticode en el binario de servicios en ejecución.
/// Complementa <see cref="ServiciosDesconocidosService"/> (lista estática) con integridad del archivo.
/// </summary>
[SupportedOSPlatform("windows")]
public class MonitorServiciosFirmaDigitalService : BackgroundService
{
    private readonly ILogger<MonitorServiciosFirmaDigitalService> _logger;
    private readonly FirebaseSettings _firebase;
    private readonly UmbralesSettings _umbrales;
    private readonly IValidadorFirmaEjecutable _validador;
    private readonly HashSet<string> _whitelistBase;

    private FirestoreDb? _db;
    private string? _machineId;

    public MonitorServiciosFirmaDigitalService(
        IOptions<FirebaseSettings> firebase,
        IOptions<UmbralesSettings> umbrales,
        IValidadorFirmaEjecutable validador,
        ILogger<MonitorServiciosFirmaDigitalService> logger)
    {
        _firebase     = firebase.Value;
        _umbrales     = umbrales.Value;
        _validador    = validador;
        _logger       = logger;
        _whitelistBase = WhitelistServiciosBaseWindows.Cargar();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_umbrales.FirmaServiciosHabilitado)
        {
            _logger.LogInformation("FirmaServicios: deshabilitado (Umbrales:FirmaServiciosHabilitado=false).");
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            _logger.LogInformation("FirmaServicios: solo Windows; monitor inactivo.");
            return;
        }

        _machineId = MachineIdHelper.Read();
        if (_machineId == null)
        {
            _logger.LogWarning("FirmaServicios: machineId no encontrado; monitor desactivado.");
            return;
        }

        if (!_firebase.IsAdminConfigured)
            _logger.LogInformation("FirmaServicios: Firebase no configurado; solo logs locales.");

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
                _logger.LogError(ex, "FirmaServicios: error al conectar con Firestore.");
            }
        }

        var horas = Math.Max(1, _umbrales.IntervaloFirmaServiciosHoras);
        var intervalo = TimeSpan.FromHours(horas);
        _logger.LogInformation("FirmaServicios iniciado; intervalo {Horas} h; soloNoBase={Solo}.",
            horas, _umbrales.FirmaServiciosSoloNoBase);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluarFirmasAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FirmaServicios: error en ciclo.");
            }

            try { await Task.Delay(intervalo, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task EvaluarFirmasAsync(CancellationToken ct)
    {
        var exclusiones = new HashSet<string>(_umbrales.ServiciosFirmaExcluidos, StringComparer.OrdinalIgnoreCase);
        ServiceController[]? todos = null;
        try
        {
            todos = ServiceController.GetServices();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FirmaServicios: no se pudo enumerar servicios.");
            return;
        }

        foreach (var sc in todos)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (sc.Status != ServiceControllerStatus.Running)
                    continue;

                var nombre = sc.ServiceName;
                if (exclusiones.Contains(nombre))
                    continue;

                if (_umbrales.FirmaServiciosSoloNoBase && _whitelistBase.Contains(nombre))
                    continue;

                var imageRaw = ServiciosDesconocidosService.LeerImagePathRegistro(nombre);
                var expanded = ServicioWindowsPaths.NormalizarImagePath(imageRaw);
                var binPath = ServicioWindowsPaths.ExtraerRutaBinarioFirma(expanded);
                var pathFs = ServicioWindowsPaths.NormalizarPathParaFileSystem(binPath);

                if (string.IsNullOrWhiteSpace(pathFs))
                {
                    _logger.LogDebug("FirmaServicios: sin ruta ejecutable para {Nombre}", nombre);
                    continue;
                }

                var resultado = _validador.Validar(pathFs);

                if (resultado.Estado == EstadoFirmaEjecutable.Confiable)
                    continue;

                if (resultado.Estado == EstadoFirmaEjecutable.ArchivoInexistente)
                {
                    _logger.LogDebug("FirmaServicios: binario no encontrado {Nombre} → {Ruta}", nombre, pathFs);
                    continue;
                }

                if (resultado.Estado == EstadoFirmaEjecutable.Error)
                {
                    _logger.LogWarning("FirmaServicios: error validando {Nombre} ({Ruta}): {Msg}",
                        nombre, pathFs, resultado.Mensaje);
                    continue;
                }

                await PersistirAlertaAsync(nombre, sc.DisplayName ?? nombre, expanded, pathFs, resultado, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "FirmaServicios: error procesando un servicio.");
            }
            finally
            {
                sc.Dispose();
            }
        }
    }

    private async Task PersistirAlertaAsync(
        string nombreScm,
        string nombreDisplay,
        string imagePathExpandido,
        string rutaBinarioVerificado,
        ResultadoValidacionFirma resultado,
        CancellationToken ct)
    {
        if (_db == null || _machineId == null)
        {
            _logger.LogWarning(
                "FirmaServicios: alerta local — {Nombre}: {Estado} ({Ruta})",
                nombreScm, resultado.Estado, rutaBinarioVerificado);
            return;
        }

        var colAlertas = _db.Collection(_firebase.FirestoreColeccionInstancias)
            .Document(_machineId)
            .Collection(_firebase.FirestoreCollectionAlertas);

        var razon = resultado.Estado switch
        {
            EstadoFirmaEjecutable.SinFirma => "sin_firma",
            EstadoFirmaEjecutable.CadenaNoConfiable => "cadena_invalida",
            _ => "desconocido"
        };

        var alerta = new Alerta
        {
            Tipo = ServicioFirmaDigitalTipoAlerta.SinFirmaValida,
            EventoId = 0,
            Descripcion =
                $"Servicio en ejecución sin firma válida: {nombreDisplay} ({nombreScm})",
            Detalle =
                $"ImagePath normalizado: {imagePathExpandido}; binario: {rutaBinarioVerificado}; estado={resultado.Estado}; {resultado.Mensaje}",
            NombreServicio = nombreScm,
            FechaHora = Timestamp.FromDateTime(DateTime.UtcNow),
            Origen = nameof(MonitorServiciosFirmaDigitalService),
            MachineId = _machineId,
            Hostname = Environment.MachineName,
            RutaEjecutableOriginal = rutaBinarioVerificado,
            SubjectFirma = resultado.SubjectCertificado,
            RazonFirma = razon
        };

        var ventanaHoras = Math.Max(1, _umbrales.DedupFirmaServiciosHoras);
        var desde = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(-ventanaHoras));

        try
        {
            var existente = await colAlertas
                .WhereEqualTo("tipo", ServicioFirmaDigitalTipoAlerta.SinFirmaValida)
                .WhereEqualTo("nombreServicio", nombreScm)
                .WhereGreaterThanOrEqualTo("fechaHora", desde)
                .Limit(1)
                .GetSnapshotAsync(ct)
                .ConfigureAwait(false);

            if (existente.Count > 0)
            {
                _logger.LogDebug("FirmaServicios: dedup omitido para {Nombre}", nombreScm);
                return;
            }

            await colAlertas.AddAsync(alerta, ct).ConfigureAwait(false);
            _logger.LogWarning(
                "FirmaServicios: alerta enviada — {Nombre} ({Razon}) → {Ruta}",
                nombreScm, razon, rutaBinarioVerificado);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FirmaServicios: error al guardar alerta para {Nombre}", nombreScm);
        }
    }
}
