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

    public string CarpetaCuarentena { get; set; } = @"C:\ProgramData\CyberWatch\Cuarentena";

    public List<string> DirectoriosProtegidos { get; set; } = new()
    {
        @"C:\Windows",
        @"C:\Program Files\CyberWatch"
    };

    /// <summary>
    /// Nombres de proceso que no se consideran amenaza aunque superen umbrales
    /// (servicios Windows, indexador, sincronización en nube, etc.).
    /// Comparación sin distinguir mayúsculas; se acepta con o sin ".exe".
    /// </summary>
    public List<string> ProcesosExcluidos { get; set; } = new()
    {
        "SearchIndexer", "SearchProtocolHost", "SearchHost", "svchost", "csrss", "wininit",
        "OneDrive", "OneDriveStandaloneUpdater", "FileCoAuth", "Microsoft.OneDrive",
        "GoogleDriveFS", "Dropbox", "dbfsvc", "Backup", "VSSVC", "MsMpEng", "NisSrv",
        "SQLWriter", "wbengine", "TiWorker", "TrustedInstaller", "setup",
        "msiexec", "conhost", "RuntimeBroker", "ApplicationFrameHost", "SystemSettings",
        "Microsoft.Photos", "PhotosApp", "ShellExperienceHost", "StartMenuExperienceHost",
        "SearchApp", "Widgets", "WidgetService", "SecurityHealthService", "SecurityHealthSystray"
    };
}
