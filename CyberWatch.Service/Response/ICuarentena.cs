using CyberWatch.Service.Models;

namespace CyberWatch.Service.Response;

public interface ICuarentena
{
    ResultadoCuarentena Cuarentenar(ReporteAmenaza reporte);
}
