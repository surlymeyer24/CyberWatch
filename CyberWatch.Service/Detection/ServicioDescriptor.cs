namespace CyberWatch.Service.Detection;

/// <summary>
/// Datos de un servicio instalado listos para el <see cref="AnalizadorServicios"/> (sin dependencias de Windows).
/// </summary>
public sealed record ServicioDescriptor(
    string Nombre,
    string NombreDisplay,
    string Estado,
    string TipoInicio,
    string RutaEjecutable);
