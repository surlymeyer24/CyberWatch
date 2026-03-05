using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CyberWatch.Service.Models;
using CyberWatch.Service.Config;

namespace CyberWatch.Service.Models
{
    public class EventoArchivo
    {
        public string NombreProceso { get; set; } = string.Empty;
        public string RutaArchivo { get; set; } = string.Empty;
        public DateTime FechaHora { get; set; }
        public TipoEvento TipoEvento { get; set; }
    }
}