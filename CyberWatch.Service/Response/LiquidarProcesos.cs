using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using CyberWatch.Service.Models;
using CyberWatch.Service.Config;

namespace CyberWatch.Service.Response
{
    public static class LiquidarProcesos
    {
        public static void Liquidar(ReporteAmenaza reporte)
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
                        Console.WriteLine($"Error al liquidar el proceso {reporte.NombreProceso}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al liquidar procesos: {ex.Message}");
            }
        }
    }
}