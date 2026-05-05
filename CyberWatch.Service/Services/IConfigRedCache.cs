namespace CyberWatch.Service.Services;

/// <summary>
/// Caché en memoria del documento <c>config/red</c> (actualizada vía listener Firestore).
/// </summary>
public interface IConfigRedCache
{
    /// <summary>Puertos admitidos remotamente (sin incluir la whitelist embebida).</summary>
    IReadOnlyCollection<int> PuertosGlobalesRemotos { get; }

    /// <summary>true si el último snapshot indicaba documento existente.</summary>
    bool UltimoSnapshotExistia { get; }

    bool BloqueoEstrictoActivo { get; }

    string? UltimaModificacionRaw { get; }

    /// <summary>Mismo criterio que <see cref="Config.UmbralesSettings.ProcesosExcluidos"/> (exe opcional).</summary>
    bool EsProcesoRedExcluido(string? nombreProceso);
}
