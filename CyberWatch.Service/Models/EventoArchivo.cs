using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CyberWatch.Service.Models
{
    public class EventoArchivos
    {
        public string NombreProceso { get; set; }
        public string RutaArchivo { get; set; }
        public DateTime FechaHora { get; set; }
        public TipoEvento TipoEvento { get; set; }
    }
}