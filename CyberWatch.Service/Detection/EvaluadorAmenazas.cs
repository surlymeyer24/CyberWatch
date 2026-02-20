using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CyberWatch.Service.Detection
{
    public static class EvaluadorAmenazas
    {
        public List<EventoArchivos> EvaluarAmenazas(List<EventoArchivos> eventos) 
        {
            List<EventoArchivos> amenazasDetected = new List<EventoArchivos>();
        }

        // Método para evaluar si un proceso supera el umbral de escritura
        // Define el limite de tiempo para la evaluación
        // Verifica 
        private static bool SuperaUmbralEscritura(List<EventoArchivos> eventos, string nombreProceso)
        {
            var limite = DateTime.Now.AddSeconds(-ConfiguracionUmbrales.IntervaloTiempoSeg);

            var escriturasProceso = eventos
                .Where(e => e.NombreProceso == nombreProceso && e.FechaHora >= limite)
                .Count();
            

            return escriturasProceso >= ConfiguracionUmbrales.MaxEscrituraPermitida;
        }

        private static bool SuperaUmbralRenombrados(List<EventoArchivos> eventos, string nombreProceso)
        {
            var limite = DateTime.Now.AddSeconds(-ConfiguracionUmbrales.IntervaloTiempoSeg);

            var renombradosProceso = eventos
                .Where(e => e.NombreProceso == nombreProceso && e.FechaHora >= limite && e.TipoEvento == TipoEvento.Renombrado)
                .Count();

            return renombradosProceso >= ConfiguracionUmbrales.MaxRenombradosPermitidos;
        }

        private static bool TieneExtensionSospechosa(EventoArchivo evento) 
        {
            var extension = Path.GetExtension(evento.RutaArchivo).ToLower();

            return ConfiguracionUmbrales.ExtensionesSospechosas.Contains(extension);
        }

        public static ReporteAmenaza? Evaluar(List<EventoArchivos> eventos, string nombreProceso)
        {
            bool escriturasSopechosas = SuperaUmbralEscritura(eventos, nombreProceso);
            bool renombradosSopechosas = SuperaUmbralRenombrados(eventos, nombreProceso);
            bool extensionSospechosa = eventos.Any(e => TieneExtensionSospechosa(e));

            
            if (escriturasSopechosas || renombradosSospechosas || extensionSospechosa)
            {
                return new ReporteAmenaza(nombreProceso, escriturasSopechosas, renombradosSospechosas, extensionSospechosa);
            }
            return null;
        }
    }
}