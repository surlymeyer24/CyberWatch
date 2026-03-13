using CyberWatch.Service.Models;

namespace CyberWatch.Service.Response;

public class GestorAlertas : IGestorAlertas
{
    public void Alertar(ReporteAmenaza reporte)
    {
        var mensaje = $"[{reporte.FechaHora}] Proceso: {reporte.NombreProceso} | " +
                      $"Escrituras: {reporte.EscriturasSospechosas} | " +
                      $"Renombrados: {reporte.RenombradosSospechosas} | " +
                      $"Extension: {reporte.ExtensionSospechosa}";

        File.AppendAllText("cyberwatch.log", mensaje + Environment.NewLine);
    }
}
