using System.Collections.Concurrent;
using CyberWatch.Service.Detection;
using CyberWatch.Service.Models;
using Microsoft.Extensions.Logging;

namespace CyberWatch.Service.Monitoring;

public class MonitorActividadArchivos
{
    private readonly RastreadorProcesos _rastreador;
    private readonly IEvaluadorAmenazas _evaluador;
    private readonly ILogger<MonitorActividadArchivos> _logger;
    private List<FileSystemWatcher> _watchers = new();

    public ConcurrentBag<EventoArchivo> Eventos { get; private set; } = new();

    /// <summary>
    /// Toma un snapshot de los eventos actuales y limpia el bag para el próximo ciclo.
    /// </summary>
    public List<EventoArchivo> TomarSnapshotYLimpiar()
    {
        var actual = Eventos;
        Eventos = new ConcurrentBag<EventoArchivo>();
        return actual.ToList();
    }

    public MonitorActividadArchivos(RastreadorProcesos rastreador, IEvaluadorAmenazas evaluador, ILogger<MonitorActividadArchivos> logger)
    {
        _rastreador = rastreador;
        _evaluador  = evaluador;
        _logger     = logger;
    }

    public void IniciarMonitorizacion()
    {
        var unidades = DriveInfo.GetDrives()
            .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Network))
            .ToList();

        _logger.LogInformation("[Monitor] Unidades detectadas: {Unidades}", string.Join(", ", unidades.Select(u => u.Name)));

        foreach (var unidad in unidades)
        {
            try
            {
                var watcher = new FileSystemWatcher(unidad.Name)
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents   = true,
                    NotifyFilter          = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    InternalBufferSize    = 262144 // 256KB para soportar actividad de fondo de Windows
                };
                _watchers.Add(watcher);

                watcher.Created += OnCreado;
                watcher.Deleted += OnEliminado;
                watcher.Renamed += OnRenombrado;
                watcher.Changed += OnModificado;
                watcher.Error   += (s, e) => _logger.LogError(e.GetException(), "[Monitor] Error en watcher de {Unidad}", unidad.Name);

                _logger.LogInformation("[Monitor] Watcher iniciado en {Unidad}", unidad.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Monitor] No se pudo crear watcher en {Unidad}", unidad.Name);
            }
        }
    }

    public void DetenerMonitorizacion()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
    }

    private void OnCreado(object sender, FileSystemEventArgs e)
    {
        var proceso = _rastreador.ObtenerProcesoPorArchivo(e.FullPath);
        _logger.LogInformation("[Monitor] CREADO: {Ruta} | Proceso: {Proceso}", e.FullPath, proceso);
        Eventos.Add(new EventoArchivo
        {
            NombreProceso = proceso,
            RutaArchivo   = e.FullPath,
            FechaHora     = DateTime.Now,
            TipoEvento    = TipoEvento.Creacion
        });
    }

    private void OnModificado(object sender, FileSystemEventArgs e)
    {
        Eventos.Add(new EventoArchivo
        {
            NombreProceso = _rastreador.ObtenerProcesoPorArchivo(e.FullPath),
            RutaArchivo   = e.FullPath,
            FechaHora     = DateTime.Now,
            TipoEvento    = TipoEvento.Escritura
        });
    }

    private void OnEliminado(object sender, FileSystemEventArgs e)
    {
        Eventos.Add(new EventoArchivo
        {
            NombreProceso = _rastreador.ObtenerProcesoPorArchivo(e.FullPath),
            RutaArchivo   = e.FullPath,
            FechaHora     = DateTime.Now,
            TipoEvento    = TipoEvento.Eliminacion
        });
    }

    public void OnRenombrado(object sender, RenamedEventArgs e)
    {
        var proceso = _rastreador.ObtenerProcesoPorArchivo(e.FullPath);
        _logger.LogInformation("[Monitor] RENOMBRADO: {RutaVieja} -> {RutaNueva} | Proceso: {Proceso}", e.OldFullPath, e.FullPath, proceso);

        var evento = new EventoArchivo
        {
            NombreProceso = proceso,
            RutaArchivo   = e.FullPath,
            FechaHora     = DateTime.Now,
            TipoEvento    = TipoEvento.Renombrado
        };

        if (_evaluador.TieneExtensionSospechosa(evento))
        {
            evento.TipoEvento = TipoEvento.ExtensionSospechosa;
            _logger.LogWarning("[Monitor] EXTENSION SOSPECHOSA detectada: {Ruta}", e.FullPath);
        }

        Eventos.Add(evento);
    }
}
