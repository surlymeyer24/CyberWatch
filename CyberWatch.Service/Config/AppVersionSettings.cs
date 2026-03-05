namespace CyberWatch.Service.Config;

/// <summary>
/// Versión actual de la aplicación y nombre del servicio Windows (para el actualizador).
/// </summary>
public class AppVersionSettings
{
    public const string SectionName = "CyberWatch";

    /// <summary>
    /// Versión actual (ej: "1.0.0"). Se compara con la versión en Firestore para decidir si actualizar.
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Nombre del servicio Windows a detener/reiniciar al aplicar la actualización.
    /// </summary>
    public string ServiceName { get; set; } = "CyberWatchService";

    /// <summary>
    /// Intervalo en minutos entre comprobaciones de actualización en Firebase. 0 = desactivado.
    /// </summary>
    public int IntervaloActualizacionMinutos { get; set; } = 60;
}
