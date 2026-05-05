using Google.Cloud.Firestore;

namespace CyberWatch.Shared.Models;

/// <summary>
/// Documento Firestore <c>config/servicios</c>: exclusiones por nombre SCM y lista blanca por hash SHA-256 del binario.
/// </summary>
[FirestoreData]
public class ConfigServiciosDocument
{
    [FirestoreProperty("nombres_excluidos")]
    public List<string> NombresExcluidos { get; set; } = new();

    /// <summary>Hashes SHA-256 en hex minúsculas (64 caracteres) de ejecutables considerados legítimos sin depender de la firma.</summary>
    [FirestoreProperty("hashes_permitidos")]
    public List<string> HashesPermitidos { get; set; } = new();

    [FirestoreProperty("ultima_modificacion")]
    public string? UltimaModificacion { get; set; }
}

/// <summary>Valor de <see cref="Alerta.Tipo"/> para el monitor de servicios anómalos (iteración 7).</summary>
public static class ServicioAnomaloTipoAlerta
{
    public const string NoFirmado = "servicio_no_firmado";
}
