namespace CyberWatch.Service.Config;

/// <summary>
/// Versión actual de la aplicación y nombre del servicio Windows.
/// </summary>
public class AppVersionSettings
{
    public const string SectionName = "CyberWatch";

    /// <summary>
    /// Versión actual (ej: "1.0.0"). Se escribe en el registro de instancia en Firestore.
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Nombre del servicio Windows (para logs y tareas).
    /// </summary>
    /// <remarks>Debe coincidir con el nombre en <c>sc create</c> / <c>install.bat</c> (por defecto <c>CyberWatch</c>).</remarks>
    public string ServiceName { get; set; } = "CyberWatch";
}
