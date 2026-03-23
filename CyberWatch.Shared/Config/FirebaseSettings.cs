namespace CyberWatch.Shared.Config;

/// <summary>
/// Configuración de Firebase compartida entre CyberWatch.Service y CyberWatch.UserAgent.
/// </summary>
public class FirebaseSettings
{
    public const string SectionName = "Firebase";

    public string ApiKey            { get; set; } = string.Empty;
    public string AuthDomain        { get; set; } = string.Empty;
    public string ProjectId         { get; set; } = string.Empty;
    public string StorageBucket     { get; set; } = string.Empty;
    public string MessagingSenderId { get; set; } = string.Empty;
    public string AppId             { get; set; } = string.Empty;
    public string MeasurementId     { get; set; } = string.Empty;

    /// <summary>
    /// Ruta al JSON de cuenta de servicio (Firebase Console → Cuentas de servicio).
    /// </summary>
    public string? CredentialPath { get; set; }

    /// <summary>
    /// Contenido JSON de la cuenta de servicio embebido directamente en la config.
    /// Se escribe en un archivo temporal en tiempo de ejecución.
    /// </summary>
    public string? CredentialJson { get; set; }

    public string FirestoreCollectionAlertas    { get; set; } = "alertas";
    /// <summary>Subcolección por máquina: historial completo de detecciones ransomware (incluye repeticiones).</summary>
    public string FirestoreCollectionLogsAmenazas { get; set; } = "logs_amenazas";
    public string FirestoreColeccionInstancias  { get; set; } = "cyberwatch_instancias";
    public int    IntervaloRegistroInstanciaMinutos { get; set; } = 5;

    /// <summary>
    /// Dominio de empresa para filtrar perfiles de Chrome/Edge (ej: bacarsa.com.ar).
    /// Si está vacío, se incluyen todos los perfiles.
    /// </summary>
    public string? DominioEmpresa { get; set; }

    public bool IsAdminConfigured =>
        (!string.IsNullOrWhiteSpace(CredentialPath) && File.Exists(CredentialPath)) ||
        !string.IsNullOrWhiteSpace(CredentialJson);

    /// <summary>
    /// Devuelve la ruta efectiva al archivo de credencial.
    /// Si se configuró CredentialJson, lo escribe en un archivo temporal y retorna esa ruta.
    /// </summary>
    public string? GetEffectiveCredentialPath()
    {
        if (!string.IsNullOrWhiteSpace(CredentialPath))
        {
            var resolved = Path.IsPathRooted(CredentialPath)
                ? CredentialPath
                : Path.Combine(AppContext.BaseDirectory, CredentialPath);
            if (File.Exists(resolved)) return resolved;
        }

        if (!string.IsNullOrWhiteSpace(CredentialJson))
        {
            var tmp = Path.Combine(Path.GetTempPath(), "cyberwatch_cred.json");
            File.WriteAllText(tmp, CredentialJson);
            return tmp;
        }

        return null;
    }
}
