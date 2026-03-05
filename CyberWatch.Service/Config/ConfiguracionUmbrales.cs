using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


namespace CyberWatch.Service.Config
{
    public static class ConfiguracionUmbrales
    {
        public static readonly int MaxEscrituraPermitida = 50;
        public static readonly int IntervaloTiempoSeg = 5;
        public static readonly int MaxRenombradosPermitidos = 20;
        public static readonly List<String> ExtensionesSospechosas = new List<String> {
            ".encrypted", ".locked", ".crypto", ".crypt", ".enc", ".ransom", ".pays", 
            ".zepto", ".locky", ".cerber", ".zzzzz", ".thor", ".aesir", ".osiris", ".shit", 
            ".wallet", ".odin", ".lol", ".wnry", ".wncry", ".wcry", ".wcryt", ".petya", ".mamba", 
            ".sage", ".jaff", ".lukitus", ".diablo6", ".ykcol", ".asasin"
        };
        public static readonly int TiempoEsperaLiquidacion = 10;
    }
}