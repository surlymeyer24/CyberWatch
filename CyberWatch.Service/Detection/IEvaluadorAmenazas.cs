using CyberWatch.Service.Models;

namespace CyberWatch.Service.Detection;

public interface IEvaluadorAmenazas
{
    ReporteAmenaza? Evaluar(List<EventoArchivo> eventos, string nombreProceso);
    bool TieneExtensionSospechosa(EventoArchivo evento);
}
