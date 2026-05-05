using Google.Cloud.Firestore;

namespace CyberWatch.Shared.Models;

/// <summary>
/// Documento Firestore <c>config/red</c>: listas remotas para el monitor de puertos TCP.
/// </summary>
[FirestoreData]
public class ConfigRedDocument
{
    [FirestoreProperty("puertos_globales_permitidos")]
    public List<int> PuertosGlobalesPermitidos { get; set; } = new();

    [FirestoreProperty("procesos_red_excluidos")]
    public List<string> ProcesosRedExcluidos { get; set; } = new();

    [FirestoreProperty("bloqueo_estricto_activo")]
    public bool BloqueoEstrictoActivo { get; set; }

    /// <summary>ISO 8601 UTC (texto); auditoría humana.</summary>
    [FirestoreProperty("ultima_modificacion")]
    public string? UltimaModificacion { get; set; }
}
