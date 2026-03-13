namespace CyberWatch.Service.Config;

public class UmbralesSettings
{
    public const string SectionName = "Umbrales";

    public int MaxEscrituraPermitida    { get; set; } = 50;
    public int IntervaloTiempoSeg       { get; set; } = 5;
    public int MaxRenombradosPermitidos { get; set; } = 20;
    public int TiempoEsperaLiquidacion  { get; set; } = 10;

    public List<string> ExtensionesSospechosas { get; set; } = new()
    {
        ".encrypted", ".locked", ".crypto", ".crypt", ".enc", ".ransom", ".pays",
        ".zepto", ".locky", ".cerber", ".zzzzz", ".thor", ".aesir", ".osiris", ".shit",
        ".wallet", ".odin", ".lol", ".wnry", ".wncry", ".wcry", ".wcryt", ".petya", ".mamba",
        ".sage", ".jaff", ".lukitus", ".diablo6", ".ykcol", ".asasin"
    };
}
