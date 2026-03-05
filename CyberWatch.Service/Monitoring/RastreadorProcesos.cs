using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using CyberWatch.Service.Models;
using CyberWatch.Service.Config;

namespace CyberWatch.Service.Monitoring
{
    public class RastreadorProcesos
    {
        private static RastreadorProcesos? _instance;

        private Dictionary<string, (string NombreProceso, DateTime FechaAgregado)> _cache = new();

        public static RastreadorProcesos ObtenerInstancia()
        {
            if (_instance == null)
            {
                _instance = new RastreadorProcesos();
            }
            return _instance;
        }

        public string ObtenerProcesoPorArchivo(string rutaArchivo)
        {
            if (_cache.TryGetValue(rutaArchivo, out var cacheado))
            {
                return cacheado.NombreProceso;
            }

            foreach (var proceso in Process.GetProcesses())
            {
                try 
                {
                    foreach (ProcessModule modulo in proceso.Modules)
                    {
                        if (string.Equals(modulo.FileName, rutaArchivo, StringComparison.OrdinalIgnoreCase))
                        {
                            _cache[rutaArchivo] = (proceso.ProcessName, DateTime.Now);
                            return proceso.ProcessName;
                        }
                    }
                }
                catch
                {
                    // Procesos que no se pueden acceder, se ignoran
                    continue;
                }
            }
            _cache[rutaArchivo] = ("Desconocido", DateTime.Now);
            return "Desconocido";
        }

        private void LimpiarCache()
        {
            var entradasViejas = _cache
                .Where(e => e.Value.FechaAgregado < DateTime.Now.AddSeconds(-ConfiguracionUmbrales.TiempoEsperaLiquidacion))
                .Select(e => e.Key)
                .ToList();
            foreach (var entrada in entradasViejas)
            {
                _cache.Remove(entrada);
            }
        }
    }
}