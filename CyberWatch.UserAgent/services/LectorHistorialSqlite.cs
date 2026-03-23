using System.Text.Json;
using CyberWatch.Shared.Models;
using Google.Cloud.Firestore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace CyberWatch.UserAgent.services;

public static class LectorHistorialSqlite
{
    // Chromium: microsegundos desde 1601-01-01 UTC
    private static readonly DateTime EpocaWebKit = new(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Firefox: microsegundos desde Unix epoch
    private static readonly DateTimeOffset EpocaUnix = DateTimeOffset.UnixEpoch;

    /// <summary>
    /// Descubre perfiles y lee historial de Chrome.
    /// Si dominioEmpresa no es null, solo incluye perfiles cuyo email termina en @dominioEmpresa.
    /// </summary>
    public static List<EntradaHistorial> LeerChrome(DateTime despuesDe, ILogger logger, string? dominioEmpresa = null)
    {
        var basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Google", "Chrome", "User Data");

        return LeerPerfilesChromium(basePath, "chrome", despuesDe, logger, dominioEmpresa);
    }

    /// <summary>
    /// Descubre perfiles y lee historial de Edge.
    /// Si dominioEmpresa no es null, solo incluye perfiles cuyo email termina en @dominioEmpresa.
    /// </summary>
    public static List<EntradaHistorial> LeerEdge(DateTime despuesDe, ILogger logger, string? dominioEmpresa = null)
    {
        var basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Edge", "User Data");

        return LeerPerfilesChromium(basePath, "edge", despuesDe, logger, dominioEmpresa);
    }

    /// <summary>
    /// Descubre perfiles y lee historial de Firefox.
    /// </summary>
    public static List<EntradaHistorial> LeerFirefox(DateTime despuesDe, ILogger logger)
    {
        var profilesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Mozilla", "Firefox", "Profiles");

        if (!Directory.Exists(profilesPath))
        {
            logger.LogDebug("Firefox no encontrado en {Path}", profilesPath);
            return [];
        }

        var entradas = new List<EntradaHistorial>();

        foreach (var perfilDir in Directory.GetDirectories(profilesPath))
        {
            var placesDb = Path.Combine(perfilDir, "places.sqlite");
            if (!File.Exists(placesDb)) continue;

            var perfil = Path.GetFileName(perfilDir);
            var items = LeerFirefoxDb(placesDb, perfil, despuesDe, logger);
            entradas.AddRange(items);
        }

        return entradas;
    }

    private static List<EntradaHistorial> LeerPerfilesChromium(
        string basePath, string navegador, DateTime despuesDe, ILogger logger, string? dominioEmpresa)
    {
        if (!Directory.Exists(basePath))
        {
            logger.LogDebug("{Navegador} no encontrado en {Path}", navegador, basePath);
            return [];
        }

        var infoPerfiles = LeerInfoPerfilesLocalState(basePath);
        var entradas = new List<EntradaHistorial>();

        foreach (var perfilDir in Directory.GetDirectories(basePath))
        {
            var nombreCarpeta = Path.GetFileName(perfilDir);
            if (nombreCarpeta != "Default" && !nombreCarpeta.StartsWith("Profile "))
                continue;

            var historyDb = Path.Combine(perfilDir, "History");
            if (!File.Exists(historyDb)) continue;

            // Obtener nombre de persona y email desde Local State
            var (nombrePerfil, email) = infoPerfiles.TryGetValue(nombreCarpeta, out var info)
                ? info
                : (nombreCarpeta, string.Empty);

            // Filtrar por dominio empresa si está configurado
            if (!string.IsNullOrEmpty(dominioEmpresa))
            {
                if (!email.EndsWith($"@{dominioEmpresa}", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogDebug("[Historial] Saltando perfil {Carpeta} ({Email}): no es cuenta empresa",
                        nombreCarpeta, string.IsNullOrEmpty(email) ? "sin email" : email);
                    continue;
                }
            }

            var items = LeerChromiumDb(historyDb, navegador, nombrePerfil, despuesDe, logger);
            entradas.AddRange(items);
        }

        return entradas;
    }

    /// <summary>
    /// Lee el archivo Local State de Chromium y devuelve un mapa de
    /// nombre de carpeta → (nombre de persona, email de Google).
    /// </summary>
    private static Dictionary<string, (string Nombre, string Email)> LeerInfoPerfilesLocalState(string basePath)
    {
        var resultado = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        var localStatePath = Path.Combine(basePath, "Local State");
        if (!File.Exists(localStatePath)) return resultado;

        try
        {
            using var stream = File.OpenRead(localStatePath);
            using var doc = JsonDocument.Parse(stream);

            if (!doc.RootElement.TryGetProperty("profile", out var profileEl)) return resultado;
            if (!profileEl.TryGetProperty("info_cache", out var infoCache)) return resultado;

            foreach (var entry in infoCache.EnumerateObject())
            {
                var carpeta = entry.Name;
                var nombre = entry.Value.TryGetProperty("name", out var nameProp)
                    ? nameProp.GetString() ?? carpeta
                    : carpeta;
                var email = entry.Value.TryGetProperty("user_name", out var emailProp)
                    ? emailProp.GetString() ?? string.Empty
                    : string.Empty;

                resultado[carpeta] = (nombre, email);
            }
        }
        catch (Exception ex)
        {
            // Local State puede estar bloqueado si el navegador está abierto; no es crítico
            _ = ex;
        }

        return resultado;
    }

    private static List<EntradaHistorial> LeerChromiumDb(
        string historyDbPath, string navegador, string perfil, DateTime despuesDe, ILogger logger)
    {
        var tempDir = ObtenerDirectorioTemp();
        var tempDb = Path.Combine(tempDir, $"{navegador}_{perfil}_History");

        try
        {
            File.Copy(historyDbPath, tempDb, overwrite: true);
            CopiarWalSiExiste(historyDbPath, tempDb);

            var desdeWebKit = DateTimeToWebKit(despuesDe);
            var entradas = new List<EntradaHistorial>();

            using var conn = new SqliteConnection($"Data Source={tempDb};Mode=ReadOnly");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT u.url, u.title, v.visit_time
                FROM urls u JOIN visits v ON u.id = v.url
                WHERE v.visit_time > @desde
                ORDER BY v.visit_time ASC";
            cmd.Parameters.AddWithValue("@desde", desdeWebKit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var visitTime = reader.GetInt64(2);
                entradas.Add(new EntradaHistorial
                {
                    Url = reader.GetString(0),
                    Titulo = reader.IsDBNull(1) ? null : reader.GetString(1),
                    FechaVisita = Timestamp.FromDateTime(WebKitToDateTime(visitTime)),
                    Navegador = navegador,
                    Perfil = perfil
                });
            }

            logger.LogDebug("[Historial] {Navegador}/{Perfil}: {Count} entradas nuevas",
                navegador, perfil, entradas.Count);
            return entradas;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Historial] Error leyendo {Navegador}/{Perfil}", navegador, perfil);
            return [];
        }
        finally
        {
            EliminarArchivoSeguro(tempDb);
            EliminarArchivoSeguro(tempDb + "-wal");
            EliminarArchivoSeguro(tempDb + "-shm");
        }
    }

    private static List<EntradaHistorial> LeerFirefoxDb(
        string placesDbPath, string perfil, DateTime despuesDe, ILogger logger)
    {
        var tempDir = ObtenerDirectorioTemp();
        var tempDb = Path.Combine(tempDir, $"firefox_{perfil}_places.sqlite");

        try
        {
            File.Copy(placesDbPath, tempDb, overwrite: true);
            CopiarWalSiExiste(placesDbPath, tempDb);

            var desdePRTime = DateTimeToPRTime(despuesDe);
            var entradas = new List<EntradaHistorial>();

            using var conn = new SqliteConnection($"Data Source={tempDb};Mode=ReadOnly");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT p.url, p.title, h.visit_date
                FROM moz_places p JOIN moz_historyvisits h ON p.id = h.place_id
                WHERE h.visit_date > @desde
                ORDER BY h.visit_date ASC";
            cmd.Parameters.AddWithValue("@desde", desdePRTime);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var visitDate = reader.GetInt64(2);
                entradas.Add(new EntradaHistorial
                {
                    Url = reader.GetString(0),
                    Titulo = reader.IsDBNull(1) ? null : reader.GetString(1),
                    FechaVisita = Timestamp.FromDateTime(PRTimeToDateTime(visitDate)),
                    Navegador = "firefox",
                    Perfil = perfil
                });
            }

            logger.LogDebug("[Historial] Firefox/{Perfil}: {Count} entradas nuevas", perfil, entradas.Count);
            return entradas;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Historial] Error leyendo Firefox/{Perfil}", perfil);
            return [];
        }
        finally
        {
            EliminarArchivoSeguro(tempDb);
            EliminarArchivoSeguro(tempDb + "-wal");
            EliminarArchivoSeguro(tempDb + "-shm");
        }
    }

    // ── Conversiones de timestamp ────────────────────────────

    private static DateTime WebKitToDateTime(long microseconds)
        => EpocaWebKit.AddTicks(microseconds * 10);

    private static long DateTimeToWebKit(DateTime dt)
        => (dt.ToUniversalTime() - EpocaWebKit).Ticks / 10;

    private static DateTime PRTimeToDateTime(long microseconds)
        => EpocaUnix.AddTicks(microseconds * 10).UtcDateTime;

    private static long DateTimeToPRTime(DateTime dt)
        => (dt.ToUniversalTime() - EpocaUnix).Ticks / 10;

    // ── Helpers ──────────────────────────────────────────────

    private static string ObtenerDirectorioTemp()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyberWatch", "temp");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CopiarWalSiExiste(string origenDb, string destinoDb)
    {
        var walOrigen = origenDb + "-wal";
        if (File.Exists(walOrigen))
            File.Copy(walOrigen, destinoDb + "-wal", overwrite: true);

        var shmOrigen = origenDb + "-shm";
        if (File.Exists(shmOrigen))
            File.Copy(shmOrigen, destinoDb + "-shm", overwrite: true);
    }

    private static void EliminarArchivoSeguro(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* ignorar errores de limpieza */ }
    }
}
