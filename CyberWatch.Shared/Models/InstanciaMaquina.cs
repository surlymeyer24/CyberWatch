using Google.Cloud.Firestore;

namespace CyberWatch.Shared.Models;

[FirestoreData]
public class InstanciaMaquina
{
    [FirestoreDocumentId]
    public string Id { get; set; } = "";

    // Identidad ----------------------------------------------
    [FirestoreProperty("hostname")]
    public string Hostname { get; set; } = "";

    [FirestoreProperty("version")]
    public string Version { get; set; } = "";

    [FirestoreProperty("servicio")]
    public string Servicio { get; set; } = "";

    [FirestoreProperty("ultima_conexion")]
    public Timestamp UltimaConexion { get; set; }

    // Red ---------------------------------------------------

    [FirestoreProperty("ip_local")]
    public string? IpLocal { get; set; }

    [FirestoreProperty("ip_publica")]
    public string? IpPublica { get; set; }

    // Geolocalización IP ------------------------------------

    [FirestoreProperty("lat")]
    public double? Lat { get; set; }

    [FirestoreProperty("lon")]
    public double? Lon { get; set; }

    [FirestoreProperty("ciudad")]
    public string? Ciudad { get; set; }

    [FirestoreProperty("pais")]
    public string? Pais { get; set; }

    [FirestoreProperty("isp")]
    public string? Isp { get; set; }

    [FirestoreProperty("ultima_geolocalizacion")]
    public Timestamp? UltimaGeolocalizacion { get; set; }

    // Seguridad del sistema ---------------------------------

    [FirestoreProperty("bitlocker_activo")]
    public bool BitlockerActivo { get; set; }

    [FirestoreProperty("firewall_activo")]
    public bool FirewallActivo { get; set; }

    [FirestoreProperty("admins_locales")]
    public List<string> AdminsLocales { get; set; } = [];

    // GPS (UserAgent) ---------------------------------------

    [FirestoreProperty("lat_gps")]
    public double? LatGps { get; set; }

    [FirestoreProperty("lon_gps")]
    public double? LonGps { get; set; }

    [FirestoreProperty("precision_gps")]
    public double? PrecisionGps { get; set; }

    [FirestoreProperty("ultima_ubicacion_gps")]
    public Timestamp? UltimaUbicacionGps { get; set; }

    // Capturas (UserAgent) ----------------------------------

    [FirestoreProperty("ultima_captura_url")]
    public string? UltimaCapturaUrl { get; set; }

    [FirestoreProperty("ultima_captura_ts")]
    public Timestamp? UltimaCapturaTz { get; set; }

    [FirestoreProperty("ultima_captura_motivo")]
    public string? UltimaCapturaMotivo { get; set; }

    // Alertas de sistema ------------------------------------

    [FirestoreProperty("alertas_sistema")]
    public List<Dictionary<string, object>> AlertasSistema { get; set; } = [];

    // Historial de navegación (UserAgent) ---------------------

    [FirestoreProperty("ultima_sync_historial")]
    public Timestamp? UltimaSyncHistorial { get; set; }

    // Comandos remotos --------------------------------------

    [FirestoreProperty("comando")]
    public string? Comando { get; set; }

    [FirestoreProperty("comando_estado")]
    public string? ComandoEstado { get; set; }

    [FirestoreProperty("comando_resultado")]
    public string? ComandoResultado { get; set; }
}
