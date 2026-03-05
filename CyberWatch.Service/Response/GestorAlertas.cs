using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CyberWatch.Service.Models;
using CyberWatch.Service.Config;

namespace CyberWatch.Service.Response
{
    public static class GestorAlertas
    {
        public static void Alertar(ReporteAmenaza reporte)
        {
            var mensaje = $"[{reporte.FechaHora}] Proceso: {reporte.NombreProceso} | " +
            $"Escrituras: {reporte.EscriturasSospechosas} | " +
            $"Renombrados: {reporte.RenombradosSospechosas} | " +
            $"Extension: {reporte.ExtensionSospechosa}";

            File.AppendAllText("cyberwatch.log", mensaje + Environment.NewLine);
        }
    }
}