using System.Diagnostics;
using CyberWatch.Service.Config;
using CyberWatch.Service.Models;
using Microsoft.Extensions.Options;

namespace CyberWatch.Service.Monitoring;

public class RastreadorProcesos
{
    private readonly UmbralesSettings _umbrales;
    private readonly Dictionary<string, (string NombreProceso, DateTime FechaAgregado)> _cache = new();

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

    public void LimpiarCache()
    {
        var limite = DateTime.Now.AddSeconds(-_umbrales.TiempoEsperaLiquidacion);
        var entradasViejas = _cache
            .Where(e => e.Value.FechaAgregado < limite)
            .Select(e => e.Key)
            .ToList();
        foreach (var entrada in entradasViejas)
            _cache.Remove(entrada);
    }
}
