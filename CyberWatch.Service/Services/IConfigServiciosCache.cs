namespace CyberWatch.Service.Services;

/// <summary>
/// Caché del documento <c>config/servicios</c> (listener Firestore).
/// </summary>
public interface IConfigServiciosCache
{
    bool UltimoSnapshotExistia { get; }

    string? UltimaModificacionRaw { get; }

    /// <summary>Nombre corto SCM en <c>nombres_excluidos</c> (comparación flexible como exclusiones de proceso).</summary>
    bool EsNombreScmExcluido(string? nombreServicio);

    /// <summary>Hash SHA-256 en hex (cualquier casing) permitido remotamente.</summary>
    bool EsHashPermitido(string sha256Hex);
}
