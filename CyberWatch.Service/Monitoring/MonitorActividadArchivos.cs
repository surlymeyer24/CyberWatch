using System.Collections.Concurrent;
using System.Diagnostics;
using CyberWatch.Service.Config;
using CyberWatch.Service.Detection;
using CyberWatch.Service.Models;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberWatch.Service.Monitoring;

public class MonitorActividadArchivos
{
    private readonly IEvaluadorAmenazas _evaluador;
    private readonly UmbralesSettings _umbrales;
    private readonly ILogger<MonitorActividadArchivos> _logger;
    private List<string> _pathsMonitoreados = new();
    private TraceEventSession? _session;

    // Deduplicación de escrituras: solo un evento por (proceso+archivo) por ciclo
    private readonly ConcurrentDictionary<string, byte> _escriturasVistas = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentBag<EventoArchivo> Eventos { get; private set; } = new();

    public List<EventoArchivo> TomarSnapshotYLimpiar()
    {
        var actual = Eventos;
        Eventos = new ConcurrentBag<EventoArchivo>();
        _escriturasVistas.Clear();
        return actual.ToList();
    }

    public MonitorActividadArchivos(
        IEvaluadorAmenazas evaluador,
        IOptions<UmbralesSettings> umbrales,
        ILogger<MonitorActividadArchivos> logger)
    {
        _evaluador = evaluador;
        _umbrales  = umbrales.Value;
        _logger    = logger;
    }

    public void IniciarMonitorizacion()
    {
        var driveSistema = Path.GetPathRoot(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            if (string.Equals(drive.Name, driveSistema, StringComparison.OrdinalIgnoreCase))
                _pathsMonitoreados.Add(Path.Combine(drive.Name, "Users"));
            else
                _pathsMonitoreados.Add(drive.Name);
        }

        _logger.LogInformation("[Monitor] Paths a monitorear: {Paths}", string.Join(", ", _pathsMonitoreados));

        // Cerrar sesión previa si el proceso crasheó sin limpiarla
        if (TraceEventSession.GetActiveSessionNames().Contains("CyberWatchETW"))
        {
            _logger.LogWarning("[Monitor] Sesión ETW previa encontrada, cerrando...");
            using var vieja = new TraceEventSession("CyberWatchETW");
            vieja.Stop();
        }

        _session = new TraceEventSession("CyberWatchETW");
        _session.EnableKernelProvider(
            KernelTraceEventParser.Keywords.FileIOInit |
            KernelTraceEventParser.Keywords.FileIO);

        _session.Source.Kernel.FileIOCreate += OnFileCreado;
        _session.Source.Kernel.FileIOWrite  += OnFileEscrito;
        _session.Source.Kernel.FileIORename += OnFileRenombrado;

        // Process() es bloqueante — corre en hilo background
        var hilo = new Thread(() =>
        {
            try { _session.Source.Process(); }
            catch (Exception ex) { _logger.LogError(ex, "[Monitor] Error en sesión ETW"); }
        })
        { IsBackground = true, Name = "ETW-FileMonitor" };

        hilo.Start();
        _logger.LogInformation("[Monitor] ETW iniciado");
    }

    public void DetenerMonitorizacion()
    {
        _session?.Stop();
        _session?.Dispose();
        _session = null;
    }

    private bool DebeMonitorear(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return _pathsMonitoreados.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private bool EsProcesoExcluido(string processName)
    {
        var sinExt = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4] : processName;
        return _umbrales.ProcesosExcluidos.Any(p =>
            string.Equals(p.Trim(), processName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Trim(), sinExt,      StringComparison.OrdinalIgnoreCase));
    }

    private static string? ObtenerRutaEjecutable(int pid)
    {
        try { return Process.GetProcessById(pid).MainModule?.FileName; }
        catch { return null; }
    }

    private void OnFileCreado(FileIOCreateTraceData e)
    {
        if (!DebeMonitorear(e.FileName) || EsProcesoExcluido(e.ProcessName)) return;

        var ruta = ObtenerRutaEjecutable(e.ProcessID);
        _logger.LogInformation("[Monitor] CREADO: {Ruta} | Proceso: {Proceso}", e.FileName, e.ProcessName);

        var evento = new EventoArchivo
        {
            NombreProceso  = e.ProcessName,
            RutaEjecutable = ruta,
            RutaArchivo    = e.FileName,
            FechaHora      = DateTime.Now,
            TipoEvento     = TipoEvento.Creacion
        };

        if (_evaluador.TieneExtensionSospechosa(evento))
        {
            evento.TipoEvento = TipoEvento.ExtensionSospechosa;
            _logger.LogWarning("[Monitor] EXTENSION SOSPECHOSA detectada: {Ruta}", e.FileName);
        }

        Eventos.Add(evento);
    }

    private void OnFileEscrito(FileIOReadWriteTraceData e)
    {
        if (!DebeMonitorear(e.FileName) || EsProcesoExcluido(e.ProcessName)) return;

        // Un evento por (proceso+archivo) por ciclo para no inflar el conteo
        var clave = $"{e.ProcessName}|{e.FileName}";
        if (!_escriturasVistas.TryAdd(clave, 0)) return;

        var ruta = ObtenerRutaEjecutable(e.ProcessID);

        Eventos.Add(new EventoArchivo
        {
            NombreProceso  = e.ProcessName,
            RutaEjecutable = ruta,
            RutaArchivo    = e.FileName,
            FechaHora      = DateTime.Now,
            TipoEvento     = TipoEvento.Escritura
        });
    }

    private void OnFileRenombrado(FileIOInfoTraceData e)
    {
        if (!DebeMonitorear(e.FileName) || EsProcesoExcluido(e.ProcessName)) return;

        var ruta = ObtenerRutaEjecutable(e.ProcessID);
        _logger.LogInformation("[Monitor] RENOMBRADO: {Ruta} | Proceso: {Proceso}", e.FileName, e.ProcessName);

        var evento = new EventoArchivo
        {
            NombreProceso  = e.ProcessName,
            RutaEjecutable = ruta,
            RutaArchivo    = e.FileName,
            FechaHora      = DateTime.Now,
            TipoEvento     = TipoEvento.Renombrado
        };

        if (_evaluador.TieneExtensionSospechosa(evento))
        {
            evento.TipoEvento = TipoEvento.ExtensionSospechosa;
            _logger.LogWarning("[Monitor] EXTENSION SOSPECHOSA detectada: {Ruta}", e.FileName);
        }

        Eventos.Add(evento);
    }
}
