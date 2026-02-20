using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CyberWatch.Service.Models
{
    public class ReporteAmenaza
    {
        public string NombreProceso { get; set; }
        public DateTime FechaHora { get; set; }
        public bool EscriturasSospechosas { get; set; }
        public bool RenombradosSospechosas { get; set; }
        public bool ExtensionSospechosa { get; set; }

        public ReporteAmenaza(string nombreProceso, bool escriturasSospechosas, bool renombradosSospechosas, bool extensionSospechosa)
        {
            NombreProceso = nombreProceso;
        }
    }
}