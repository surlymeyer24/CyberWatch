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
    /// Nombre de la colección de Firestore donde se guardan las alertas. Por defecto: "alertas".
    /// </summary>
    public string FirestoreCollectionAlertas { get; set; } = "alertas";

    /// <summary>
    /// Ruta del documento en Firestore donde está la config de actualización (mismo doc que el agente).
    /// Ej: "config/actualizaciones" → colección "config", documento "actualizaciones".
    /// </summary>
    public string FirestoreDocumentoActualizacion { get; set; } = "config/actualizaciones";

    /// <summary>
    /// Campo dentro del documento que tiene la config de CyberWatch (version + url), igual que "agente" para el otro servicio.
    /// Ej: "cyberwatch" → se lee data.cyberwatch.version y data.cyberwatch.url
    /// </summary>
    public string FirestoreCampoActualizacion { get; set; } = "cyberwatch";

    /// <summary>
    /// Colección donde cada instancia de CyberWatch se registra (documento por máquina, para saber dónde está instalado).
    /// Ej: "cyberwatch_instancias"
    /// </summary>
    public string FirestoreColeccionInstancias { get; set; } = "cyberwatch_instancias";

    /// <summary>
    /// Cada cuántos minutos se actualiza el registro de esta máquina en Firestore. 0 = desactivado.
    /// </summary>
    public int IntervaloRegistroInstanciaMinutos { get; set; } = 5;

    public bool IsAdminConfigured => !string.IsNullOrWhiteSpace(CredentialPath) && File.Exists(CredentialPath);
}
