using System.Diagnostics;
using CyberWatch.Service.Models;

namespace CyberWatch.Service.Response;

public class LiquidarProcesos : ILiquidadorProcesos
{
    public void Liquidar(ReporteAmenaza reporte)
    {
        try
        {
            foreach (var proceso in Process.GetProcessesByName(reporte.NombreProceso))
            {
                try
                {
                    proceso.Kill();
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Error al liquidar el proceso {reporte.NombreProceso}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Error al liquidar procesos: {ex.Message}");
        }
    }
}
