using System.Runtime.Versioning;
using System.ServiceProcess;
using CyberWatch.Service.Config;
using CyberWatch.Service.Detection;
using CyberWatch.Shared.Config;
using CyberWatch.Shared.Helpers;
using CyberWatch.Shared.Models;
using Google.Cloud.Firestore;
using Microsoft.Win32;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberWatch.Service.Services;

/// <summary>
/// Compara los servicios instalados con la whitelist base embebida; persiste no-base en Firestore y alerta si aparecen nuevos entre ciclos.
/// </summary>
[SupportedOSPlatform("windows")]
public class ServiciosDesconocidosService : BackgroundService
{
    private readonly ILogger<ServiciosDesconocidosService> _logger;
    private readonly FirebaseSettings _firebase;
    private readonly UmbralesSettings _umbrales;
    private readonly HashSet<string> _whitelistBase;

    private FirestoreDb? _db;
    private string? _machineId;

    /// <summary>Conjunto de nombres cortos no-base vistos en el ciclo anterior (sin distinguir mayúsculas).</summary>
    private HashSet<string> _desconocidosCicloAnterior = new(StringComparer.OrdinalIgnoreCase);

    private bool _esPrimerCiclo = true;

    public ServiciosDesconocidosService(
        IOptions<FirebaseSettings> firebase,
        IOptions<UmbralesSettings> umbrales,
        ILogger<ServiciosDesconocidosService> logger)
    {
        _firebase     = firebase.Value;
        _umbrales     = umbrales.Value;
        _logger       = logger;
        _whitelistBase = WhitelistServiciosBaseWindows.Cargar();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogInformation("ServiciosDesconocidos: solo Windows; servicio inactivo.");
            return;
        }

        _machineId = MachineIdHelper.Read();
        if (_machineId == null)
        {
            _logger.LogWarning("ServiciosDesconocidos: machineId no encontrado; monitor desactivado.");
            return;
        }

        if (!_firebase.IsAdminConfigured)
        {
            _logger.LogInformation("ServiciosDesconocidos: Firebase no configurado; solo registro local.");
        }
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
                _logger.LogError(ex, "ServiciosDesconocidos: error al conectar con Firestore.");
            }
        }

        var intervalo = TimeSpan.FromMinutes(Math.Max(1, _umbrales.IntervaloServiciosMinutos));
        _logger.LogInformation("ServiciosDesconocidos iniciado; intervalo {Min} min.", intervalo.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluarServiciosAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ServiciosDesconocidos: error en ciclo de evaluación.");
            }

            try { await Task.Delay(intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task EvaluarServiciosAsync(CancellationToken ct)
    {
        var instalados = EnumerarServiciosEnEstaMaquina();
        var exclusiones = new HashSet<string>(_umbrales.ServiciosExcluidos, StringComparer.OrdinalIgnoreCase);

        var desconocidos = AnalizadorServicios.Evaluar(
            instalados,
            _whitelistBase,
            exclusiones,
            _desconocidosCicloAnterior);

        var actuales = new HashSet<string>(
            desconocidos.Select(d => d.Descriptor.Nombre),
            StringComparer.OrdinalIgnoreCase);

        var emitirAlertas = !(_umbrales.SuprimirAlertasPrimerCicloServicios && _esPrimerCiclo);

        _logger.LogInformation(
            "ServiciosDesconocidos: instalados={Total}, no-base={NoBase}, ciclo_anterior={Prev}, emitir_alertas={Alertas}",
            instalados.Count, desconocidos.Count, _desconocidosCicloAnterior.Count, emitirAlertas);

        if (_db != null && _machineId != null)
        {
            foreach (var item in desconocidos)
            {
                ct.ThrowIfCancellationRequested();
                await PersistirServicioAsync(item, ct);
                if (item.EsNuevo && emitirAlertas)
                    await PersistirAlertaSiCorrespondeAsync(item.Descriptor, ct);
            }
        }

        _desconocidosCicloAnterior = actuales;
        _esPrimerCiclo = false;
    }

    private async Task PersistirServicioAsync(ServicioDesconocidoEvaluado item, CancellationToken ct)
    {
        if (_db == null || _machineId == null) return;

        var d = item.Descriptor;
        var docId = IdDocumentoFirestore(d.Nombre);
        var col = _db.Collection(_firebase.FirestoreColeccionInstancias)
            .Document(_machineId)
            .Collection(_firebase.FirestoreCollectionServiciosDesconocidos);

        var modelo = new ServicioDesconocido
        {
            Nombre           = d.Nombre,
            NombreDisplay    = d.NombreDisplay,
            Estado           = d.Estado,
            RutaEjecutable   = d.RutaEjecutable,
            TipoInicio       = d.TipoInicio,
            MachineId        = _machineId,
            Hostname         = Environment.MachineName,
            FechaDeteccion   = Timestamp.FromDateTime(DateTime.UtcNow),
            EsNuevo          = item.EsNuevo
        };

        try
        {
            await col.Document(docId).SetAsync(modelo, SetOptions.MergeAll, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ServiciosDesconocidos: error al persistir servicio {Nombre}", d.Nombre);
        }
    }

    private async Task PersistirAlertaSiCorrespondeAsync(ServicioDescriptor d, CancellationToken ct)
    {
        if (_db == null || _machineId == null) return;

        var colAlertas = _db.Collection(_firebase.FirestoreColeccionInstancias)
            .Document(_machineId)
            .Collection(_firebase.FirestoreCollectionAlertas);

        var alerta = new Alerta
        {
            Tipo            = ServicioDesconocidoTipoAlerta.NuevoEntreCiclos,
            EventoId        = 0,
            Descripcion     = $"Servicio no base nuevo: {d.NombreDisplay} ({d.Nombre})",
            Detalle         = $"ImagePath: {d.RutaEjecutable}",
            NombreServicio  = d.Nombre,
            FechaHora       = Timestamp.FromDateTime(DateTime.UtcNow),
            Origen          = nameof(ServiciosDesconocidosService),
            MachineId       = _machineId,
            Hostname        = Environment.MachineName
        };

        var desde = Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(-10));

        try
        {
            var existente = await colAlertas
                .WhereEqualTo("tipo", alerta.Tipo ?? "")
                .WhereEqualTo("nombreServicio", d.Nombre)
                .WhereGreaterThanOrEqualTo("fechaHora", desde)
                .Limit(1)
                .GetSnapshotAsync(ct);

            if (existente.Count > 0)
            {
                _logger.LogDebug("ServiciosDesconocidos: alerta duplicada omitida para {Nombre}", d.Nombre);
                return;
            }

            await colAlertas.AddAsync(alerta, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ServiciosDesconocidos: error al guardar alerta para {Nombre}", d.Nombre);
        }
    }

    private List<ServicioDescriptor> EnumerarServiciosEnEstaMaquina()
    {
        var lista = new List<ServicioDescriptor>();
        ServiceController[]? servicios = null;
        try { servicios = ServiceController.GetServices(); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ServiciosDesconocidos: no se pudo enumerar servicios.");
            return lista;
        }

        foreach (var sc in servicios)
        {
            try
            {
                var nombre = sc.ServiceName;
                var imagePath = LeerImagePathRegistro(nombre);
                lista.Add(new ServicioDescriptor(
                    nombre,
                    sc.DisplayName ?? nombre,
                    sc.Status.ToString(),
                    sc.StartType.ToString(),
                    ServicioWindowsPaths.NormalizarImagePath(imagePath)));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ServiciosDesconocidos: error leyendo servicio.");
            }
            finally
            {
                sc.Dispose();
            }
        }

        return lista;
    }

    internal static string? LeerImagePathRegistro(string nombreServicio)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{nombreServicio}");
            return key?.GetValue("ImagePath") as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Expande variables de entorno y normaliza comillas típicas de ImagePath.</summary>
    internal static string NormalizarImagePath(string? raw)
        => ServicioWindowsPaths.NormalizarImagePath(raw);

    /// <summary>IDs de documento válidos en Firestore (evita / y segmentos reservados).</summary>
    internal static string IdDocumentoFirestore(string nombreServicio)
    {
        if (string.IsNullOrEmpty(nombreServicio))
            return "_vacío";

        var id = nombreServicio.Replace("/", "_", StringComparison.Ordinal)
            .Replace("\\", "_", StringComparison.Ordinal);

        return id is "." or ".." ? "_" + id : id;
    }
}
