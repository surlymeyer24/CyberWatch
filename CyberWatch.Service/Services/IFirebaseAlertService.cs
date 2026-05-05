using CyberWatch.Service.Models;
using CyberWatch.Shared.Models;

namespace CyberWatch.Service.Services;

public interface IFirebaseAlertService
{
    /// <summary>
    /// Envía un reporte de amenaza a Firebase (Firestore). No hace nada si Firebase no está configurado.
    /// </summary>
    Task EnviarAlertaAsync(ReporteAmenaza reporte, CancellationToken ct = default);
    Task EnviarAlertaAsync(ReporteAmenaza reporte, ResultadoCuarentena? cuarentena, CancellationToken ct = default);

    /// <param name="eventosArchivoEnCiclo">Eventos de archivo del ciclo atribuidos a este proceso (-1 si no se conoce).</param>
    Task EnviarAlertaAsync(ReporteAmenaza reporte, ResultadoCuarentena? cuarentena, int eventosArchivoEnCiclo, CancellationToken ct = default);

    /// <summary>Añade un documento en la subcolección <c>alertas</c> de la instancia actual. No-op si Firebase no está configurado.</summary>
    Task AgregarAlertaInstanciaAsync(Alerta alerta, CancellationToken ct = default);
}
