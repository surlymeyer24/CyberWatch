using CyberWatch.Service.Models;

namespace CyberWatch.Service.Services;

public interface IFirebaseAlertService
{
    /// <summary>
    /// Envía un reporte de amenaza a Firebase (Firestore). No hace nada si Firebase no está configurado.
    /// </summary>
    Task EnviarAlertaAsync(ReporteAmenaza reporte, CancellationToken ct = default);
    Task EnviarAlertaAsync(ReporteAmenaza reporte, ResultadoCuarentena? cuarentena, CancellationToken ct = default);
}
