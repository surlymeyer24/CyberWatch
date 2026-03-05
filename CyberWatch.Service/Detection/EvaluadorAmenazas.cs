using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CyberWatch.Service.Models;
using CyberWatch.Service.Config;

namespace CyberWatch.Service.Detection
{
    public static class EvaluadorAmenazas
    {
        public static List<EventoArchivo> EvaluarAmenazas(List<EventoArchivo> eventos) 
        {
            List<EventoArchivo> amenazasDetected = new List<EventoArchivo>();
            return amenazasDetected;
        }

        // Método para evaluar si un proceso supera el umbral de escritura
        // Define el limite de tiempo para la evaluación
        // Verifica 
        private static bool SuperaUmbralEscritura(List<EventoArchivo> eventos, string nombreProceso)
        {
            var limite = DateTime.Now.AddSeconds(-ConfiguracionUmbrales.IntervaloTiempoSeg);

            var escriturasProceso = eventos
                .Where(e => e.NombreProceso == nombreProceso && e.FechaHora >= limite)
                .Count();
            

            return escriturasProceso >= ConfiguracionUmbrales.MaxEscrituraPermitida;
        }

        private static bool SuperaUmbralRenombrados(List<EventoArchivo> eventos, string nombreProceso)
        {
            var limite = DateTime.Now.AddSeconds(-ConfiguracionUmbrales.IntervaloTiempoSeg);

            var renombradosProceso = eventos
                .Where(e => e.NombreProceso == nombreProceso && e.FechaHora >= limite && e.TipoEvento == TipoEvento.Renombrado)
                .Count();

            return renombradosProceso >= ConfiguracionUmbrales.MaxRenombradosPermitidos;
        }

        public static bool TieneExtensionSospechosa(EventoArchivo evento) 
        {
            var extension = Path.GetExtension(evento.RutaArchivo).ToLower();

            return ConfiguracionUmbrales.ExtensionesSospechosas.Contains(extension);
        }

        public static ReporteAmenaza? Evaluar(List<EventoArchivo> eventos, string nombreProceso)
        {
            bool escriturasSospechosas = SuperaUmbralEscritura(eventos, nombreProceso);
            bool renombradosSospechosas = SuperaUmbralRenombrados(eventos, nombreProceso);
            bool extensionSospechosa = eventos.Any(e => TieneExtensionSospechosa(e));

            
            if (escriturasSospechosas || renombradosSospechosas || extensionSospechosa)
            {
                return new ReporteAmenaza(nombreProceso, escriturasSospechosas, renombradosSospechosas, extensionSospechosa);
            }
            return null;
        }
    }
}