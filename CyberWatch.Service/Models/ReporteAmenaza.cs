using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CyberWatch.Service.Models;
using CyberWatch.Service.Config;

namespace CyberWatch.Service.Models
{
    public class ReporteAmenaza
    {
        public string NombreProceso { get; set; }
        public DateTime FechaHora { get; set; }
        public bool EscriturasSospechosas { get; set; }
        public bool RenombradosSospechosas { get; set; }
        public bool ExtensionSospechosa { get; set; }
        public string? ExtensionDetectada { get; set; }
        public string? RutaEjecutable { get; set; }

        public ReporteAmenaza(string nombreProceso, bool escriturasSospechosas, bool renombradosSospechosas, bool extensionSospechosa, string? extensionDetectada = null)
        {
            NombreProceso = nombreProceso;
            FechaHora = DateTime.Now;
            EscriturasSospechosas = escriturasSospechosas;
            RenombradosSospechosas = renombradosSospechosas;
            ExtensionSospechosa = extensionSospechosa;
            ExtensionDetectada = extensionDetectada;
        }
    }
}