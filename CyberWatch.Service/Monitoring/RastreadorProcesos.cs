using System.Collections.Concurrent;
using System.Diagnostics;
using CyberWatch.Service.Config;
using CyberWatch.Service.Models;
using Microsoft.Extensions.Options;

namespace CyberWatch.Service.Monitoring;

public class RastreadorProcesos
{
    private readonly UmbralesSettings _umbrales;
    private readonly ConcurrentDictionary<string, (string NombreProceso, DateTime FechaAgregado)> _cache = new();
    private readonly ConcurrentDictionary<string, string> _cacheRutasEjecutable = new(StringComparer.OrdinalIgnoreCase);

    public RastreadorProcesos(IOptions<UmbralesSettings> umbrales)
    {
        _umbrales = umbrales.Value;
    }

    public string ObtenerProcesoPorArchivo(string rutaArchivo)
    {
        if (_cache.TryGetValue(rutaArchivo, out var cacheado))
            return cacheado.NombreProceso;

        foreach (var proceso in Process.GetProcesses())
        {
            try
            {
                foreach (ProcessModule modulo in proceso.Modules)
                {
                    if (string.Equals(modulo.FileName, rutaArchivo, StringComparison.OrdinalIgnoreCase))
                    {
                        _cache[rutaArchivo] = (proceso.ProcessName, DateTime.Now);
                        try
                        {
                            var rutaExe = proceso.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(rutaExe))
                                _cacheRutasEjecutable[proceso.ProcessName] = rutaExe;
                        }
                        catch { }
                        return proceso.ProcessName;
                    }
                }
            }
            catch
            {
                continue;
            }
        }

        _cache[rutaArchivo] = ("Desconocido", DateTime.Now);
        return "Desconocido";
    }

    public string? ObtenerRutaEjecutableCacheada(string nombreProceso)
    {
        return _cacheRutasEjecutable.TryGetValue(nombreProceso, out var ruta) ? ruta : null;
    }

    public void LimpiarCache()
    {
        var limite = DateTime.Now.AddSeconds(-_umbrales.TiempoEsperaLiquidacion);
        foreach (var key in _cache.Keys)
        {
            if (_cache.TryGetValue(key, out var valor) && valor.FechaAgregado < limite)
                _cache.TryRemove(key, out _);
        }
    }
}
