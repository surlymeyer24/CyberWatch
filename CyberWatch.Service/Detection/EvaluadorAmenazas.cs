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
        bool escriturasSospechosas  = SuperaUmbralEscritura(eventos, nombreProceso);
        bool renombradosSospechosas = SuperaUmbralRenombrados(eventos, nombreProceso);
        bool extensionSospechosa    = eventos.Any(TieneExtensionSospechosa);

        if (escriturasSospechosas || renombradosSospechosas || extensionSospechosa)
            return new ReporteAmenaza(nombreProceso, escriturasSospechosas, renombradosSospechosas, extensionSospechosa);

        return null;
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
