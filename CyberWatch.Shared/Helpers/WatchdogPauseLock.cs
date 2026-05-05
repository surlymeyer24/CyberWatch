namespace CyberWatch.Shared.Helpers;

/// <summary>
/// Lock compartido entre Service y UserAgent para pausar el watchdog durante
/// <c>actualizar_agente</c>, <c>reiniciar_servicio</c> u otros apagados intencionales.
/// </summary>
public static class WatchdogPauseLock
{
    /// <summary>Ruta fija visible desde SYSTEM y sesión de usuario.</summary>
    public const string LockPath = @"C:\ProgramData\CyberWatch\cyberwatch.updating";

    static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(15);

    /// <summary>
    /// true si existe el archivo y no es más viejo que <paramref name="ttl"/> (huérfanos tras crash).
    /// </summary>
    public static bool Activo(TimeSpan ttl = default)
    {
        if (ttl == default)
            ttl = DefaultTtl;
        try
        {
            if (!File.Exists(LockPath))
                return false;
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(LockPath);
            return age <= ttl;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Escribe el lock (best-effort).</summary>
    public static void Crear(string razon)
    {
        try
        {
            var dir = Path.GetDirectoryName(LockPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(LockPath, $"{razon}\n{DateTime.UtcNow:o}\n");
        }
        catch
        {
            /* ignorar */
        }
    }

    /// <summary>Elimina el lock si existe (best-effort).</summary>
    public static void Eliminar()
    {
        try
        {
            if (File.Exists(LockPath))
                File.Delete(LockPath);
        }
        catch
        {
            /* ignorar */
        }
    }
}
