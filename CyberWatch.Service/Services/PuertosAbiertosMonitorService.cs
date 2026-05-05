using System.Runtime.Versioning;
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
/// Compara sockets TCP IPv4 (tabla del kernel) con whitelist/sospechosos; persiste en Firestore y alerta.
/// </summary>
[SupportedOSPlatform("windows")]
public class PuertosAbiertosMonitorService : BackgroundService
{
    private readonly ILogger<PuertosAbiertosMonitorService> _logger;
    private readonly FirebaseSettings _firebase;
    private readonly UmbralesSettings _umbrales;
    private readonly IConfigRedCache _configRed;
    private readonly HashSet<int> _puertosBase;
    private readonly HashSet<int> _puertosSospechosos;

    private FirestoreDb? _db;
    private string? _machineId;

    private HashSet<string> _clavesCicloAnterior = new(StringComparer.Ordinal);
    private bool _esPrimerCiclo = true;

    public PuertosAbiertosMonitorService(
        IOptions<FirebaseSettings> firebase,
        IOptions<UmbralesSettings> umbrales,
        IConfigRedCache configRed,
        ILogger<PuertosAbiertosMonitorService> logger)
    {
        _firebase = firebase.Value;
        _umbrales = umbrales.Value;
        _configRed = configRed;
        _logger = logger;
        _puertosBase = WhitelistPuertosBase.CargarPuertosBase();
        _puertosSospechosos = WhitelistPuertosBase.CargarPuertosSospechosos();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogInformation("PuertosAbiertos: solo Windows; servicio inactivo.");
            return;
        }

        _machineId = MachineIdHelper.Read();
        if (_machineId == null)
        {
            _logger.LogWarning("PuertosAbiertos: machineId no encontrado; monitor desactivado.");
            return;
        }

        if (!_firebase.IsAdminConfigured)
            _logger.LogInformation("PuertosAbiertos: Firebase no configurado; solo registro local.");

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
                _logger.LogError(ex, "PuertosAbiertos: error al conectar con Firestore.");
            }
        }

        var intervalo = TimeSpan.FromMinutes(Math.Max(1, _umbrales.IntervaloPuertosMinutos));
        _logger.LogInformation("PuertosAbiertos iniciado; intervalo {Min} min; soloListen={SoloL}.",
            intervalo.TotalMinutes, _umbrales.MonitorearSoloListen);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluarPuertosAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PuertosAbiertos: error en ciclo de evaluación.");
            }

            try { await Task.Delay(intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task EvaluarPuertosAsync(CancellationToken ct)
    {
        var filas = AnalizadorPuertosTcp.Enumerar(_umbrales.MonitorearSoloListen);
        var exclusiones = new HashSet<int>(_umbrales.PuertosExcluidos);
        var puertosPermitidos = new HashSet<int>(_puertosBase);
        foreach (var p in _configRed.PuertosGlobalesRemotos)
            puertosPermitidos.Add(p);

        var clavesActuales = new HashSet<string>(StringComparer.Ordinal);
        var evaluados = new List<PuertoEvaluado>();

        foreach (var d in filas)
        {
            var clave = ClaveUnica(d);
            clavesActuales.Add(clave);

            var sospechoso = _puertosSospechosos.Contains(d.PuertoLocal);
            var nuevo = !_clavesCicloAnterior.Contains(clave)
                        && !puertosPermitidos.Contains(d.PuertoLocal)
                        && !exclusiones.Contains(d.PuertoLocal);

            evaluados.Add(new PuertoEvaluado
            {
                Descriptor = d,
                EsSospechoso = sospechoso,
                EsNuevoEntreCiclos = nuevo
            });
        }

        var emitirAlertas = !(_umbrales.SuprimirAlertasPrimerCicloPuertos && _esPrimerCiclo);

        _logger.LogInformation(
            "PuertosAbiertos: filas={Total}, ciclo_anterior={Prev}, emitir_alertas={Alertas}",
            filas.Count, _clavesCicloAnterior.Count, emitirAlertas);

        if (_db != null && _machineId != null)
        {
            foreach (var item in evaluados)
            {
                ct.ThrowIfCancellationRequested();
                await PersistirPuertoAsync(item, ct);

                if (!emitirAlertas)
                    continue;

                var d = item.Descriptor;

                if (_configRed.EsProcesoRedExcluido(d.NombreProceso))
                    continue;

                if (item.EsSospechoso)
                    await PersistirAlertaAsync(PuertoTipoAlerta.Sospechoso, d, ct);
                else if (item.EsNuevoEntreCiclos)
                    await PersistirAlertaAsync(PuertoTipoAlerta.NuevoEntreCiclos, d, ct);
            }
        }

        _clavesCicloAnterior = clavesActuales;
        _esPrimerCiclo = false;
    }

    private static string ClaveUnica(PuertoTcpDescriptor d)
        => $"{d.EstadoTexto}|{d.IpLocal}|{d.PuertoLocal}|{d.Pid}";

    internal static string IdDocumentoFirestore(PuertoTcpDescriptor d)
    {
        var raw = $"tcp_{d.EstadoTexto}_{d.IpLocal}_{d.PuertoLocal}_{d.Pid}";
        foreach (var c in Path.GetInvalidFileNameChars())
            raw = raw.Replace(c, '_');
        return raw.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
    }

    private async Task PersistirPuertoAsync(PuertoEvaluado item, CancellationToken ct)
    {
        if (_db == null || _machineId == null) return;

        var d = item.Descriptor;
        var docId = IdDocumentoFirestore(d);

        var col = _db.Collection(_firebase.FirestoreColeccionInstancias)
            .Document(_machineId)
            .Collection(_firebase.FirestoreCollectionPuertosAbiertos);

        var modelo = new PuertoAbierto
        {
            Protocolo = "TCP",
            EstadoTcp = d.EstadoTexto,
            PuertoLocal = d.PuertoLocal,
            IpLocal = d.IpLocal,
            IpRemota = d.IpRemota,
            PuertoRemoto = d.PuertoRemoto,
            Pid = d.Pid,
            NombreProceso = d.NombreProceso,
            RutaProceso = d.RutaProceso,
            MachineId = _machineId,
            Hostname = Environment.MachineName,
            FechaDeteccion = Timestamp.FromDateTime(DateTime.UtcNow),
            EsSospechoso = item.EsSospechoso,
            EsNuevo = item.EsNuevoEntreCiclos
        };

        try
        {
            await col.Document(docId).SetAsync(modelo, SetOptions.MergeAll, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PuertosAbiertos: error al persistir puerto {Puerto}", d.PuertoLocal);
        }
    }

    private async Task PersistirAlertaAsync(string tipoAlerta, PuertoTcpDescriptor d, CancellationToken ct)
    {
        if (_db == null || _machineId == null) return;

        var colAlertas = _db.Collection(_firebase.FirestoreColeccionInstancias)
            .Document(_machineId)
            .Collection(_firebase.FirestoreCollectionAlertas);

        var alerta = new Alerta
        {
            Tipo = tipoAlerta,
            EventoId = 0,
            Descripcion = tipoAlerta == PuertoTipoAlerta.Sospechoso
                ? $"Puerto TCP sospechoso en escucha/uso: {d.PuertoLocal} ({d.NombreProceso})"
                : $"Puerto TCP nuevo entre ciclos: {d.PuertoLocal} ({d.NombreProceso})",
            Detalle = $"PID={d.Pid}; local={d.IpLocal}:{d.PuertoLocal}; remoto={d.IpRemota}:{d.PuertoRemoto}; ruta={d.RutaProceso}",
            FechaHora = Timestamp.FromDateTime(DateTime.UtcNow),
            Origen = nameof(PuertosAbiertosMonitorService),
            MachineId = _machineId,
            Hostname = Environment.MachineName,
            PuertoLocal = d.PuertoLocal,
            PidProceso = d.Pid
        };

        var desde = Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(-10));

        try
        {
            var existente = await colAlertas
                .WhereEqualTo("tipo", tipoAlerta)
                .WhereEqualTo("puertoLocal", d.PuertoLocal)
                .WhereEqualTo("pidProceso", d.Pid)
                .WhereGreaterThanOrEqualTo("fechaHora", desde)
                .Limit(1)
                .GetSnapshotAsync(ct);

            if (existente.Count > 0)
            {
                _logger.LogDebug("PuertosAbiertos: alerta duplicada omitida ({Tipo}) para {Puerto}/{Pid}",
                    tipoAlerta, d.PuertoLocal, d.Pid);
                return;
            }

            await colAlertas.AddAsync(alerta, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PuertosAbiertos: error al guardar alerta para puerto {Puerto}", d.PuertoLocal);
        }
    }
}
