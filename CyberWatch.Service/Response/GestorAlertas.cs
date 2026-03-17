using CyberWatch.Service.Models;

namespace CyberWatch.Service.Response;

public class GestorAlertas : IGestorAlertas
{
    public void Alertar(ReporteAmenaza reporte)
    {
        var extInfo = reporte.ExtensionDetectada != null ? $" ({reporte.ExtensionDetectada})" : "";
        var mensaje = $"[{reporte.FechaHora}] Proceso: {reporte.NombreProceso} | " +
                      $"Escrituras: {reporte.EscriturasSospechosas} | " +
                      $"Renombrados: {reporte.RenombradosSospechosas} | " +
                      $"Extension: {reporte.ExtensionSospechosa}{extInfo}";

        File.AppendAllText("cyberwatch.log", mensaje + Environment.NewLine);
    }
}
