using CyberWatch.Service.Config;
using CyberWatch.Service.Models;
using Microsoft.Extensions.Options;

namespace CyberWatch.Service.Detection;

public class EvaluadorAmenazas : IEvaluadorAmenazas
{
    private readonly UmbralesSettings _umbrales;

    public EvaluadorAmenazas(IOptions<UmbralesSettings> umbrales)
    {
        _umbrales = umbrales.Value;
    }

    public ReporteAmenaza? Evaluar(List<EventoArchivo> eventos, string nombreProceso)
    {
        if (string.IsNullOrWhiteSpace(nombreProceso))
            return null;

        if (EstaExcluido(nombreProceso))
            return null;

        bool escriturasSospechosas  = SuperaUmbralEscritura(eventos, nombreProceso);
        bool renombradosSospechosas = SuperaUmbralRenombrados(eventos, nombreProceso);
        bool extensionSospechosa    = eventos.Any(TieneExtensionSospechosa);

        if (escriturasSospechosas || renombradosSospechosas || extensionSospechosa)
        {
            var extDetectada = eventos
                .Where(e => e.TipoEvento == TipoEvento.ExtensionSospechosa)
                .Select(e => Path.GetExtension(e.RutaArchivo).ToLower())
                .FirstOrDefault();
            return new ReporteAmenaza(nombreProceso, escriturasSospechosas, renombradosSospechosas, extensionSospechosa, extDetectada);
        }

        return null;
    }

    private bool EstaExcluido(string nombreProceso)
    {
        var nombre = nombreProceso.Trim();
        if (nombre.Length == 0) return true;
        var sinExt = nombre.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? nombre[..^4]
            : nombre;
        return _umbrales.ProcesosExcluidos.Any(excluido =>
            string.Equals(excluido.Trim(), nombre, StringComparison.OrdinalIgnoreCase)
            || string.Equals(excluido.Trim(), sinExt, StringComparison.OrdinalIgnoreCase));
    }

    public bool TieneExtensionSospechosa(EventoArchivo evento)
    {
        var extension = Path.GetExtension(evento.RutaArchivo).ToLower();
        return _umbrales.ExtensionesSospechosas.Contains(extension);
    }

    private bool SuperaUmbralEscritura(List<EventoArchivo> eventos, string nombreProceso)
    {
        var limite = DateTime.Now.AddSeconds(-_umbrales.IntervaloTiempoSeg);
        var count  = eventos.Count(e => e.NombreProceso == nombreProceso && e.FechaHora >= limite);
        return count >= _umbrales.MaxEscrituraPermitida;
    }

    private bool SuperaUmbralRenombrados(List<EventoArchivo> eventos, string nombreProceso)
    {
        var limite = DateTime.Now.AddSeconds(-_umbrales.IntervaloTiempoSeg);
        var count  = eventos.Count(e => e.NombreProceso == nombreProceso
                                     && e.FechaHora >= limite
                                     && e.TipoEvento == TipoEvento.Renombrado);
        return count >= _umbrales.MaxRenombradosPermitidos;
    }
}
