namespace CyberWatch.Service.Config;

/// <summary>
/// Configuración de Firebase (SDK cliente / web).
/// Para el Admin SDK en .NET también se necesita un archivo de cuenta de servicio (CredentialPath).
/// </summary>
public class FirebaseSettings
{
    public const string SectionName = "Firebase";

    public string ApiKey { get; set; } = string.Empty;
    public string AuthDomain { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string StorageBucket { get; set; } = string.Empty;
    public string MessagingSenderId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string MeasurementId { get; set; } = string.Empty;

    /// <summary>
    /// Ruta al JSON de cuenta de servicio (Firebase Console → Configuración del proyecto → Cuentas de servicio).
    /// Si está configurado, el servicio puede enviar alertas a Firestore.
    /// </summary>
    public string? CredentialPath { get; set; }

    /// <summary>
    /// Contenido JSON de la cuenta de servicio embebido directamente en la config (alternativa a CredentialPath).
    /// Útil para distribuciones donde no se puede incluir el archivo físico.
    /// Se escribe en un archivo temporal en tiempo de ejecución.
    /// </summary>
    public string? CredentialJson { get; set; }

/// <summary>
    /// Nombre de la colección de Firestore donde se guardan las alertas. Por defecto: "alertas".
    /// </summary>
    public string FirestoreCollectionAlertas { get; set; } = "alertas";

    /// <summary>
    /// Colección donde cada instancia de CyberWatch se registra (documento por máquina, para saber dónde está instalado).
    /// Ej: "cyberwatch_instancias"
    /// </summary>
    public string FirestoreColeccionInstancias { get; set; } = "cyberwatch_instancias";

    /// <summary>
    /// Cada cuántos minutos se actualiza el registro de esta máquina en Firestore. 0 = desactivado.
    /// </summary>
    public int IntervaloRegistroInstanciaMinutos { get; set; } = 5;

    public bool IsAdminConfigured =>
        (!string.IsNullOrWhiteSpace(CredentialPath) && File.Exists(CredentialPath)) ||
        !string.IsNullOrWhiteSpace(CredentialJson);

    /// <summary>
    /// Devuelve la ruta efectiva al archivo de credencial.
    /// Si se configuró CredentialJson, lo escribe en un archivo temporal y retorna esa ruta.
    /// </summary>
    public string? GetEffectiveCredentialPath()
    {
        // 1. Ruta absoluta o relativa al directorio del ejecutable
        if (!string.IsNullOrWhiteSpace(CredentialPath))
        {
            var resolved = Path.IsPathRooted(CredentialPath)
                ? CredentialPath
                : Path.Combine(AppContext.BaseDirectory, CredentialPath);
            if (File.Exists(resolved)) return resolved;
        }

        // 2. Contenido JSON embebido → archivo temporal
        if (!string.IsNullOrWhiteSpace(CredentialJson))
        {
            var tmp = Path.Combine(Path.GetTempPath(), "cyberwatch_cred.json");
            File.WriteAllText(tmp, CredentialJson);
            return tmp;
        }

        return null;
    }
}
