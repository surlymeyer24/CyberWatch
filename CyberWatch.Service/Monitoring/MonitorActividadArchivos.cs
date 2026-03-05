using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CyberWatch.Service.Models;
using CyberWatch.Service.Config;
using CyberWatch.Service.Detection;

namespace CyberWatch.Service.Monitoring
{
    public class MonitorActividadArchivos
    {
        private List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        public List<EventoArchivo> Eventos { get; private set; } = new List<EventoArchivo>();

        
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
                    EnableRaisingEvents = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                };
                _watchers.Add(watcher);

                watcher.Created += OnCreado;
                watcher.Deleted += OnEliminado;
                watcher.Renamed += OnRenombrado;
                watcher.Changed += OnModificado;
            }
        }

        private void OnCreado(object sender, FileSystemEventArgs e)
        {
            var evento = new EventoArchivo {
                NombreProceso = RastreadorProcesos.ObtenerInstancia().ObtenerProcesoPorArchivo(e.FullPath),
                RutaArchivo = e.FullPath,
                FechaHora = DateTime.Now,
                TipoEvento = TipoEvento.Creacion
            };
            Eventos.Add(evento);
        }

        private void OnModificado(object sender, FileSystemEventArgs e)
        {
            var evento = new EventoArchivo {
                NombreProceso = RastreadorProcesos.ObtenerInstancia().ObtenerProcesoPorArchivo(e.FullPath),
                RutaArchivo = e.FullPath,
                FechaHora = DateTime.Now,
                TipoEvento = TipoEvento.Escritura
            };
            Eventos.Add(evento);
        }

        private void OnEliminado(object sender, FileSystemEventArgs e)
        {
            var evento = new EventoArchivo {
                NombreProceso = RastreadorProcesos.ObtenerInstancia().ObtenerProcesoPorArchivo(e.FullPath),
                RutaArchivo = e.FullPath,
                FechaHora = DateTime.Now,
                TipoEvento = TipoEvento.Eliminacion
            };
            Eventos.Add(evento);
        }

        public void OnRenombrado(object sender, RenamedEventArgs e)
        {
            var evento = new EventoArchivo {
                NombreProceso = RastreadorProcesos.ObtenerInstancia().ObtenerProcesoPorArchivo(e.FullPath),
                RutaArchivo = e.FullPath,
                FechaHora = DateTime.Now,
                TipoEvento = TipoEvento.ExtensionSospechosa
            };

            if (EvaluadorAmenazas.TieneExtensionSospechosa(evento))
            {
                evento.TipoEvento = TipoEvento.ExtensionSospechosa;
            }
            Eventos.Add(evento);
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
    }
}