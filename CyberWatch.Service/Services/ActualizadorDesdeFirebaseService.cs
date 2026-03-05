using System.IO.Compression;
using System.Diagnostics;
using CyberWatch.Service.Config;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberWatch.Service.Services;

/// <summary>
/// Comprueba periódicamente en Firestore si hay una nueva versión (release de GitHub)
/// y actualiza el servicio descargando e instalando el release.
/// </summary>
public class ActualizadorDesdeFirebaseService : BackgroundService
{
    private readonly FirebaseSettings _firebase;
    private readonly AppVersionSettings _app;
    private readonly ILogger<ActualizadorDesdeFirebaseService> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private FirestoreDb? _db;

    public ActualizadorDesdeFirebaseService(
        IOptions<FirebaseSettings> firebase,
        IOptions<AppVersionSettings> app,
        ILogger<ActualizadorDesdeFirebaseService> logger,
        IHostApplicationLifetime lifetime)
    {
        _firebase = firebase.Value;
        _app = app.Value;
        _logger = logger;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_app.IntervaloActualizacionMinutos <= 0)
        {
            _logger.LogInformation("Comprobación de actualizaciones desactivada (IntervaloActualizacionMinutos = 0).");
            return;
        }

        if (!_firebase.IsAdminConfigured)
        {
            _logger.LogInformation("Firebase no configurado; no se comprobarán actualizaciones desde Firestore.");
            return;
        }

        try
        {
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", _firebase.CredentialPath);
            _db = FirestoreDb.Create(_firebase.ProjectId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo conectar a Firestore para comprobar actualizaciones.");
            return;
        }

        var interval = TimeSpan.FromMinutes(_app.IntervaloActualizacionMinutos);
        _logger.LogInformation("Comprobación de actualizaciones cada {Minutos} min (documento: {Ruta}).",
            _app.IntervaloActualizacionMinutos, _firebase.FirestoreDocumentoActualizacion);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ComprobarYActualizarAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al comprobar actualización");
            }
        }
    }

    private async Task ComprobarYActualizarAsync(CancellationToken ct)
    {
        if (_db == null) return;

        var (collection, documentId) = ParseDocumentPath(_firebase.FirestoreDocumentoActualizacion);
        var docRef = _db.Collection(collection).Document(documentId);
        var snapshot = await docRef.GetSnapshotAsync(ct).ConfigureAwait(false);

        if (!snapshot.Exists)
        {
            _logger.LogInformation("El documento de actualización no existe. Creando {Coleccion}/{Documento} con la versión actual.", collection, documentId);
            try
            {
                var datosIniciales = new Dictionary<string, object>
                {
                    ["version"] = _app.Version,
                    ["url"] = "https://github.com/surlymeyer24/CyberWatch/releases/download/v" + _app.Version + "/CyberWatch.Service.zip"
                };
                await docRef.SetAsync(datosIniciales, SetOptions.MergeAll, ct).ConfigureAwait(false);
                _logger.LogInformation("Documento creado. Edita en Firebase Console la URL cuando publiques un release.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo crear el documento en Firestore. Revisa permisos de escritura en la colección {Coleccion}.", collection);
            }
            return;
        }

        var data = snapshot.ToDictionary();
        // Si está configurado un campo (ej: "cyberwatch"), leer ese objeto del documento (mismo doc que "agente")
        var target = data;
        if (!string.IsNullOrWhiteSpace(_firebase.FirestoreCampoActualizacion))
        {
            target = GetNestedMap(data, _firebase.FirestoreCampoActualizacion);
            if (target == null)
            {
                _logger.LogDebug("No existe el campo '{Campo}' en el documento de actualización.", _firebase.FirestoreCampoActualizacion);
                return;
            }
        }
        // Dentro del objeto: version + url (o dentro de "config" anidado)
        string versionRemota;
        string downloadUrl;
        var config = GetNestedMap(target, "config");
        if (config != null)
        {
            versionRemota = GetString(config, "version");
            downloadUrl = GetString(config, "url") ?? GetString(config, "downloadUrl") ?? "";
        }
        else
        {
            versionRemota = GetString(target, "version");
            downloadUrl = GetString(target, "url") ?? GetString(target, "downloadUrl") ?? "";
        }

        if (string.IsNullOrWhiteSpace(versionRemota) || string.IsNullOrWhiteSpace(downloadUrl))
        {
            _logger.LogWarning("Faltan 'version' y 'url' (o 'downloadUrl') en el documento de Firestore.");
            return;
        }

        if (!Version.TryParse(versionRemota, out var remoteVersion))
        {
            _logger.LogWarning("Versión en Firestore no válida: {Version}", versionRemota);
            return;
        }

        if (!Version.TryParse(_app.Version, out var currentVersion))
            currentVersion = new Version(0, 0, 0);

        if (remoteVersion <= currentVersion)
        {
            _logger.LogDebug("Ya está en la última versión ({Version}).", _app.Version);
            return;
        }

        _logger.LogInformation("Nueva versión disponible: {Remote} (actual: {Current}). Descargando...", versionRemota, _app.Version);

        var tempDir = Path.Combine(Path.GetTempPath(), "CyberWatchUpdate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zipPath = Path.Combine(tempDir, "update.zip");

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "CyberWatch-Updater/1.0");
            await using var stream = await http.GetStreamAsync(downloadUrl, ct).ConfigureAwait(false);
            await using var file = File.Create(zipPath);
            await stream.CopyToAsync(file, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al descargar la actualización desde {Url}", downloadUrl);
            TryDeleteDir(tempDir);
            return;
        }

        var extractDir = Path.Combine(tempDir, "extracted");
        try
        {
            ZipFile.ExtractToDirectory(zipPath, extractDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al extraer el ZIP.");
            TryDeleteDir(tempDir);
            return;
        }

        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var ps1Path = Path.Combine(tempDir, "aplicar_actualizacion.ps1");
        var psContent = $@"
Start-Sleep -Seconds 5
Stop-Service -Name '{_app.ServiceName}' -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Copy-Item -Path '{extractDir}\*' -Destination '{appDir}' -Recurse -Force
Start-Service -Name '{_app.ServiceName}'
Start-Sleep -Seconds 2
Remove-Item -Path '{tempDir}' -Recurse -Force -ErrorAction SilentlyContinue
";
        await File.WriteAllTextAsync(ps1Path, psContent, ct).ConfigureAwait(false);

        _logger.LogInformation("Actualización descargada. Reiniciando servicio para aplicar...");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{ps1Path}\"",
            UseShellExecute = true,
            CreateNoWindow = true
        };
        Process.Start(psi);
        _lifetime.StopApplication();
    }

    private static string GetString(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var v) || v == null) return "";
        return v.ToString()?.Trim() ?? "";
    }

    private static Dictionary<string, object>? GetNestedMap(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var v)) return null;
        return v as Dictionary<string, object>;
    }

    private static (string collection, string documentId) ParseDocumentPath(string path)
    {
        var parts = path.Split(new[] { '/', '\\' }, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
            return (parts[0], parts[1]);
        return ("services", path.Length > 0 ? path : "CyberWatch");
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { /* ignore */ }
    }
}
