using CyberWatch.Service.Models;

namespace CyberWatch.Service.Response;

public interface ILiquidadorProcesos
{
    void Liquidar(ReporteAmenaza reporte);
}
