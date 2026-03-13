using CyberWatch.Service.Models;

namespace CyberWatch.Service.Response;

public interface IGestorAlertas
{
    void Alertar(ReporteAmenaza reporte);
}
