using System.Diagnostics;
using System.IO.Compression;
using CyberWatch.Service.Config;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberWatch.Service.Services;

/// <summary>
/// Escucha en tiempo real el documento cyberwatch_instancias/{machineId}.
/// Cuando el campo "comando" cambia a un valor no vacío, lo ejecuta y limpia el campo.
///
/// Campos que escribe el servicio en el documento de la máquina:
///   comando          → limpiado a "" tras recibir el comando
///   comando_estado   → "ejecutando" | "completado" | "error"
///   comando_resultado→ descripción del resultado
///
/// Para disparar desde el Dashboard:
///   cyberwatch_instancias/{machineId} → { "comando": "actualizar" }
/// </summary>
public class EjecutorTareasFirebaseService : BackgroundService
{
    private readonly FirebaseSettings _firebase;
    private readonly AppVersionSettings _app;
    private readonly ILogger<EjecutorTareasFirebaseService> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    // Evita ejecuciones concurrentes si Firestore dispara el listener varias veces
    private int _ejecutando;

    public EjecutorTareasFirebaseService(
        IOptions<FirebaseSettings> firebase,
        IOptions<AppVersionSettings> app,
        ILogger<EjecutorTareasFirebaseService> logger,
        IHostApplicationLifetime lifetime)
    {
        _firebase = firebase.Value;
        _app = app.Value;
        _logger = logger;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_firebase.IsAdminConfigured)
        {
            _logger.LogInformation("Firebase no configurado; el ejecutor de comandos no arrancará.");
            return;
        }

        var machineId = await EsperarMachineIdAsync(stoppingToken);
        if (machineId is null) return;

        FirestoreDb db;
        try
        {
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", _firebase.CredentialPath);
            db = FirestoreDb.Create(_firebase.ProjectId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo conectar a Firestore.");
            return;
        }

        var docRef = db.Collection(_firebase.FirestoreColeccionInstancias).Document(machineId);

        _logger.LogInformation("Escuchando comandos en tiempo real (máquina: {Id}).", machineId);

        var listener = docRef.Listen(async (snapshot, ct) =>
        {
            if (!snapshot.Exists) return;

            var data = snapshot.ToDictionary();
            var comando = GetStr(data, "comando") ?? "";
            if (string.IsNullOrWhiteSpace(comando)) return;

            // Garantizar una sola ejecución simultánea
            if (Interlocked.CompareExchange(ref _ejecutando, 1, 0) != 0)
            {
                _logger.LogWarning("Comando '{Cmd}' recibido pero ya hay una ejecución en curso.", comando);
                return;
            }

            try
            {
                _logger.LogInformation("Comando recibido: '{Comando}'", comando);

                // Limpiar el campo comando de inmediato para no re-ejecutar en reinicios
                await ActualizarDocAsync(docRef, new Dictionary<string, object>
                {
                    ["comando"] = "",
                    ["comando_estado"] = "ejecutando",
                    ["comando_resultado"] = $"Procesando '{comando}'..."
                }, ct);

                switch (comando.Trim().ToLowerInvariant())
                {
                    case "actualizar":
                        await EjecutarActualizarAsync(docRef, db, ct);
                        break;
                    default:
                        _logger.LogWarning("Comando desconocido: '{Cmd}'", comando);
                        await ActualizarDocAsync(docRef, new Dictionary<string, object>
                        {
                            ["comando_estado"] = "error",
                            ["comando_resultado"] = $"Comando desconocido: {comando}"
                        }, ct);
                        break;
                }
            }
            finally
            {
                Interlocked.Exchange(ref _ejecutando, 0);
            }
        });

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await listener.StopAsync();
        }
    }

    // ── Comando: actualizar ──────────────────────────────────────────────────

    private async Task EjecutarActualizarAsync(DocumentReference docRef, FirestoreDb db, CancellationToken ct)
    {
        // Leer URL desde config/ciberseguridad
        string downloadUrl;
        string versionRemota;
        try
        {
            var (col, docId) = ParsePath(_firebase.FirestoreDocumentoActualizacion);
            var snap = await db.Collection(col).Document(docId).GetSnapshotAsync(ct);
            if (!snap.Exists)
            {
                await ActualizarDocAsync(docRef, Estado("error",
                    $"No se encontró '{_firebase.FirestoreDocumentoActualizacion}' en Firestore."), ct);
                return;
            }
            var d = snap.ToDictionary();
            downloadUrl = GetStr(d, "url") ?? GetStr(d, "downloadUrl") ?? "";
            versionRemota = GetStr(d, "version") ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leyendo config de actualización.");
            await ActualizarDocAsync(docRef, Estado("error", $"Error leyendo config: {ex.Message}"), ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            await ActualizarDocAsync(docRef, Estado("error", "El campo 'url' está vacío en Firestore."), ct);
            return;
        }

        _logger.LogInformation("Descargando v{Ver} desde {Url}", versionRemota, downloadUrl);
        await ActualizarDocAsync(docRef, Estado("ejecutando", $"Descargando v{versionRemota}..."), ct);

        var tempDir = Path.Combine(Path.GetTempPath(), "CyberWatchUpdate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zipPath = Path.Combine(tempDir, "update.zip");

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "CyberWatch-Updater/1.0");
            await using var stream = await http.GetStreamAsync(downloadUrl, ct);
            await using var file = File.Create(zipPath);
            await stream.CopyToAsync(file, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al descargar desde {Url}", downloadUrl);
            await ActualizarDocAsync(docRef, Estado("error", $"Descarga fallida: {ex.Message}"), ct);
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
            await ActualizarDocAsync(docRef, Estado("error", $"Extracción fallida: {ex.Message}"), ct);
            TryDeleteDir(tempDir);
            return;
        }

        // Marcar completado ANTES de detener el servicio
        await ActualizarDocAsync(docRef, Estado("completado",
            $"v{versionRemota} descargada. Reiniciando servicio..."), ct);

        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var ps1Path = Path.Combine(tempDir, "aplicar_actualizacion.ps1");
        await File.WriteAllTextAsync(ps1Path, $@"
Start-Sleep -Seconds 5
Stop-Service -Name '{_app.ServiceName}' -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Copy-Item -Path '{extractDir}\*' -Destination '{appDir}' -Recurse -Force
Start-Service -Name '{_app.ServiceName}'
Start-Sleep -Seconds 2
Remove-Item -Path '{tempDir}' -Recurse -Force -ErrorAction SilentlyContinue
", ct);

        _logger.LogInformation("Aplicando actualización. El servicio se reiniciará.");
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{ps1Path}\"",
            UseShellExecute = true,
            CreateNoWindow = true
        });

        _lifetime.StopApplication();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task ActualizarDocAsync(DocumentReference docRef,
        Dictionary<string, object> campos, CancellationToken ct)
    {
        try { await docRef.UpdateAsync(campos); }
        catch (Exception ex) { _logger.LogWarning(ex, "No se pudo actualizar el documento."); }
    }

    private static Dictionary<string, object> Estado(string estado, string resultado) => new()
    {
        ["comando_estado"] = estado,
        ["comando_resultado"] = resultado
    };

    private static string? GetStr(Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out var v) ? v?.ToString()?.Trim() : null;

    private static (string col, string doc) ParsePath(string path)
    {
        var p = path.Split(new[] { '/', '\\' }, 2, StringSplitOptions.RemoveEmptyEntries);
        return p.Length == 2 ? (p[0], p[1]) : ("config", "ciberseguridad");
    }

    private async Task<string?> EsperarMachineIdAsync(CancellationToken ct)
    {
        for (int i = 0; i < 24; i++)
        {
            var id = LeerMachineId();
            if (id is not null) return id;
            if (i == 0)
                _logger.LogInformation("Esperando machine ID (lo genera RegistroInstanciaService)...");
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
        _logger.LogWarning("No se obtuvo machine ID en 2 min; el ejecutor de comandos no arrancará.");
        return null;
    }

    private static string? LeerMachineId()
    {
        try
        {
            var file = Path.Combine(
                AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
                "cyberwatch_machine_id.txt");
            if (!File.Exists(file)) return null;
            var id = File.ReadAllText(file).Trim();
            return id.Length >= 8 ? id : null;
        }
        catch { return null; }
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* ignore */ }
    }
}
