namespace CyberWatch.Service.Config;

public class UmbralesSettings
{
    public const string SectionName = "Umbrales";

    public int MaxEscrituraPermitida    { get; set; } = 500;
    public int IntervaloTiempoSeg       { get; set; } = 10;
    public int MaxRenombradosPermitidos { get; set; } = 100;
    public int TiempoEsperaLiquidacion  { get; set; } = 10;
    public int UmbralPuntuacionAmenaza  { get; set; } = 3;

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
        @"C:\Program Files",
        @"C:\Program Files (x86)",
        @"C:\ProgramData\CyberWatch"
    };

    /// <summary>
    /// Nombres de proceso que no se consideran amenaza aunque superen umbrales
    /// (servicios Windows, indexador, sincronización en nube, etc.).
    /// Comparación sin distinguir mayúsculas; se acepta con o sin ".exe".
    /// </summary>
    /// <summary>
    /// Máximo de amenazas distintas permitidas en un ciclo de detección.
    /// Si se superan, se asume falso positivo masivo y se suspende la respuesta activa (kill/cuarentena).
    /// </summary>
    public int MaxAmenazasPorCiclo { get; set; } = 3;

    /// <summary>
    /// Minutos de cooldown tras liquidar un proceso. No se vuelve a matar el mismo proceso
    /// dentro de este período (evita loops de kill → respawn → kill).
    /// </summary>
    public int CooldownLiquidacionMinutos { get; set; } = 10;

    public List<string> ProcesosExcluidos { get; set; } = new()
    {
        "SearchIndexer", "SearchProtocolHost", "SearchHost", "svchost", "csrss", "wininit",
        "OneDrive", "OneDriveStandaloneUpdater", "FileCoAuth", "Microsoft.OneDrive",
        "GoogleDriveFS", "Dropbox", "dbfsvc", "Backup", "VSSVC", "MsMpEng", "NisSrv",
        "SQLWriter", "wbengine", "TiWorker", "TrustedInstaller", "setup",
        "msiexec", "conhost", "RuntimeBroker", "ApplicationFrameHost", "SystemSettings",
        "Microsoft.Photos", "PhotosApp", "ShellExperienceHost", "StartMenuExperienceHost",
        "SearchApp", "Widgets", "WidgetService", "SecurityHealthService", "SecurityHealthSystray",
        "chrome", "msedge", "msedgewebview2", "firefox", "brave", "opera",
        "Code", "devenv", "notepad++", "sublime_text", "notepad",
        "AnyDesk", "TeamViewer", "mstsc",
        "explorer", "taskmgr", "dwm", "lsass", "services", "smss", "winlogon",
        "spoolsv", "dllhost", "WmiPrvSE", "sihost", "ctfmon",
        "OUTLOOK", "EXCEL", "WINWORD", "POWERPNT", "Teams", "ms-teams", "Slack", "Discord",
        "spotify", "node", "python", "java", "javaw", "git",
        "powershell", "pwsh", "WindowsTerminal", "cmd",
        "7zFM", "WinRAR", "Steam"
    };

    /// <summary>Intervalo entre evaluaciones del monitor de servicios no-base (minutos).</summary>
    public int IntervaloServiciosMinutos { get; set; } = 10;

    /// <summary>
    /// Nombres cortos SCM adicionales que no deben tratarse como “no-base” (ej. agentes internos).
    /// Comparación sin distinguir mayúsculas.
    /// </summary>
    public List<string> ServiciosExcluidos { get; set; } = new();

    /// <summary>
    /// Si es true, el primer ciclo tras el arranque solo persiste estado en Firestore y no crea alertas en <c>alertas</c>.
    /// </summary>
    public bool SuprimirAlertasPrimerCicloServicios { get; set; } = true;

    /// <summary>Intervalo entre ejecuciones del monitor de puertos TCP (minutos).</summary>
    public int IntervaloPuertosMinutos { get; set; } = 5;

    /// <summary>Puertos que no deben generar alerta por “nuevo entre ciclos” (software interno).</summary>
    public List<int> PuertosExcluidos { get; set; } = new();

    /// <summary>Si es true, el primer ciclo del monitor de puertos no crea alertas en <c>alertas</c>.</summary>
    public bool SuprimirAlertasPrimerCicloPuertos { get; set; } = true;

    /// <summary>Si es true, solo se incluyen filas en estado Listen (menos ruido que Established).</summary>
    public bool MonitorearSoloListen { get; set; } = true;

    // ── Entropía (refuerzo de puntuación ransomware; desactivado por defecto) ──

    /// <summary>Si es false (por defecto), no se leen muestras ni se suma bonus por entropía.</summary>
    public bool EntropiaHabilitada { get; set; }

    /// <summary>Tamaño máximo de lectura por archivo para la muestra (KB).</summary>
    public int EntropiaTamanoMuestraKb { get; set; } = 256;

    /// <summary>Solo si la entropía de Shannon (bits/byte) supera este valor se suma <see cref="EntropiaBonusPuntos"/>.</summary>
    public double EntropiaUmbralAlto { get; set; } = 7.2;

    /// <summary>Puntos que se suman al score base si la muestra supera <see cref="EntropiaUmbralAlto"/>.</summary>
    public int EntropiaBonusPuntos { get; set; } = 2;

    /// <summary>
    /// Extensiones donde alta entropía es habitual (archivos comprimidos, multimedia); no se eligen como muestra preferida.
    /// </summary>
    public List<string> ExtensionesEntropiaAltaEsperada { get; set; } = new()
    {
        ".zip", ".7z", ".rar", ".cab", ".gz", ".bz2", ".xz",
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".mp4", ".mkv", ".avi", ".mov",
        ".mp3", ".flac", ".woff", ".woff2", ".ttf", ".eot"
    };

    /// <summary>
    /// Si es true (por defecto), el bonus de entropía solo se evalúa cuando ya hay patrón coherente
    /// (escrituras/renombrados por encima de umbral o extensión sospechosa).
    /// </summary>
    public bool EntropiaRequierePatronRansomware { get; set; } = true;

    /// <summary>
    /// Puntuación base mínima (sin entropía) para permitir sumar el bonus; evita disparar solo por entropía aislada.
    /// </summary>
    public int EntropiaMinimoPuntuacionBase { get; set; } = 1;

    // ── Firma digital (Authenticode) en servicios Windows (iteración 6) ──────

    /// <summary>Si es false (por defecto), el monitor de firmas no corre.</summary>
    public bool FirmaServiciosHabilitado { get; set; }

    /// <summary>Intervalo entre pasadas completas del escaneo de firmas.</summary>
    public int IntervaloFirmaServiciosHoras { get; set; } = 1;

    /// <summary>Si es true, solo se comprueba firma en servicios cuyo nombre SCM no está en la whitelist base embebida.</summary>
    public bool FirmaServiciosSoloNoBase { get; set; }

    /// <summary>Nombres cortos SCM que omiten la verificación de firma (software interno sin firma).</summary>
    public List<string> ServiciosFirmaExcluidos { get; set; } = new();

    /// <summary>Ventana de deduplicación de alertas <c>servicio_sin_firma_valida</c> (horas).</summary>
    public int DedupFirmaServiciosHoras { get; set; } = 24;

    // ── Servicios anómalos / política remota config/servicios (iteración 7) ───

    /// <summary>Si es false (por defecto), no se ejecuta el monitor WMI + Verify + hash.</summary>
    public bool MonitorServiciosAnomalosHabilitado { get; set; }

    /// <summary>Intervalo entre pasadas completas del escaneo de servicios anómalos (minutos).</summary>
    public int IntervaloServiciosAnomalosMinutos { get; set; } = 60;

    /// <summary>Ventana de deduplicación de alertas <c>servicio_no_firmado</c> (horas).</summary>
    public int DedupServiciosAnomalosHoras { get; set; } = 24;
}
