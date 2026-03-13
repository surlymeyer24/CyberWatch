using System.Collections.Concurrent;
using CyberWatch.Service.Detection;
using CyberWatch.Service.Models;

namespace CyberWatch.Service.Monitoring;

public class MonitorActividadArchivos
{
    private readonly RastreadorProcesos _rastreador;
    private readonly IEvaluadorAmenazas _evaluador;
    private List<FileSystemWatcher> _watchers = new();

    public ConcurrentBag<EventoArchivo> Eventos { get; } = new();

    public MonitorActividadArchivos(RastreadorProcesos rastreador, IEvaluadorAmenazas evaluador)
    {
        _rastreador = rastreador;
        _evaluador  = evaluador;
    }

    public void IniciarMonitorizacion()
    {
        var unidades = DriveInfo.GetDrives()
            .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Network))
            .ToList();

        foreach (var unidad in unidades)
        {
            var watcher = new FileSystemWatcher(unidad.Name)
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents   = true,
                NotifyFilter          = NotifyFilters.FileName | NotifyFilters.LastWrite
            };
            _watchers.Add(watcher);

            watcher.Created += OnCreado;
            watcher.Deleted += OnEliminado;
            watcher.Renamed += OnRenombrado;
            watcher.Changed += OnModificado;
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
        Eventos.Add(new EventoArchivo
        {
            NombreProceso = _rastreador.ObtenerProcesoPorArchivo(e.FullPath),
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
        var evento = new EventoArchivo
        {
            NombreProceso = _rastreador.ObtenerProcesoPorArchivo(e.FullPath),
            RutaArchivo   = e.FullPath,
            FechaHora     = DateTime.Now,
            TipoEvento    = TipoEvento.Renombrado
        };

        if (_evaluador.TieneExtensionSospechosa(evento))
            evento.TipoEvento = TipoEvento.ExtensionSospechosa;

        Eventos.Add(evento);
    }
}
