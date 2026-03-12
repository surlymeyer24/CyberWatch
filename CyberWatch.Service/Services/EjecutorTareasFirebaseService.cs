using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
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
///   cyberwatch_instancias/{machineId} → { "comando": "<comando>" }
/// </summary>
public class EjecutorTareasFirebaseService : BackgroundService
{
    private readonly FirebaseSettings _firebase;
    private readonly AppVersionSettings _app;
    private readonly ILogger<EjecutorTareasFirebaseService> _logger;

    // Evita ejecuciones concurrentes si Firestore dispara el listener varias veces
    private int _ejecutando;

    public EjecutorTareasFirebaseService(
        IOptions<FirebaseSettings> firebase,
        IOptions<AppVersionSettings> app,
        ILogger<EjecutorTareasFirebaseService> logger)
    {
        _firebase = firebase.Value;
        _app = app.Value;
        _logger = logger;
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
            db = new FirestoreDbBuilder
            {
                ProjectId = _firebase.ProjectId,
                CredentialsPath = _firebase.GetEffectiveCredentialPath()
            }.Build();
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
                    case "actualizar_agente":
                        await EjecutarActualizacionAgenteAsync(docRef, db, ct);
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

    // ── Actualización remota ──────────────────────────────────────────────────

    private async Task EjecutarActualizacionAgenteAsync(DocumentReference docRef, FirestoreDb db, CancellationToken ct)
    {
        // 1. Leer url_descarga desde config/ciberseguridad
        string url;
        try
        {
            var configSnap = await db.Collection("config").Document("ciberseguridad").GetSnapshotAsync(ct);
            if (!configSnap.Exists || !configSnap.TryGetValue<string>("url_descarga", out var u) || string.IsNullOrWhiteSpace(u))
            {
                await ActualizarDocAsync(docRef, new Dictionary<string, object>
                {
                    ["comando_estado"]    = "error",
                    ["comando_resultado"] = "No se encontró url_descarga en config/ciberseguridad."
                }, ct);
                return;
            }
            url = u;
        }
        catch (Exception ex)
        {
            await ActualizarDocAsync(docRef, new Dictionary<string, object>
            {
                ["comando_estado"]    = "error",
                ["comando_resultado"] = $"Error leyendo config: {ex.Message}"
            }, ct);
            return;
        }

        // 2. Descargar ZIP
        var zipPath     = Path.Combine(Path.GetTempPath(), "cyberwatch_update.zip");
        var extractPath = Path.Combine(Path.GetTempPath(), "cyberwatch_update_tmp");

        _logger.LogInformation("Descargando actualización desde {Url}", url);
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "CyberWatch-Updater/1.0");
            var bytes = await http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(zipPath, bytes, ct);
        }
        catch (Exception ex)
        {
            await ActualizarDocAsync(docRef, new Dictionary<string, object>
            {
                ["comando_estado"]    = "error",
                ["comando_resultado"] = $"Error al descargar: {ex.Message}"
            }, ct);
            return;
        }

        // 3. Extraer ZIP
        try
        {
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            ZipFile.ExtractToDirectory(zipPath, extractPath);
        }
        catch (Exception ex)
        {
            await ActualizarDocAsync(docRef, new Dictionary<string, object>
            {
                ["comando_estado"]    = "error",
                ["comando_resultado"] = $"Error al extraer ZIP: {ex.Message}"
            }, ct);
            return;
        }

        // 4. Escribir script de actualización que corre desacoplado del proceso actual
        var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var scriptPath = Path.Combine(Path.GetTempPath(), "cw_update.bat");
        File.WriteAllText(scriptPath,
            $"@echo off\r\n" +
            $"timeout /t 3 /nobreak >nul\r\n" +
            $"net stop CyberWatch\r\n" +
            $"timeout /t 2 /nobreak >nul\r\n" +
            $"xcopy /E /I /Y \"{extractPath}\\*\" \"{installDir}\\\"\r\n" +
            $"net start CyberWatch\r\n" +
            $"rd /s /q \"{extractPath}\"\r\n" +
            $"del \"{zipPath}\"\r\n" +
            $"del \"%~f0\"\r\n");

        // 5. Lanzar script desacoplado (sobrevive al stop del servicio)
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
        {
            CreateNoWindow  = true,
            UseShellExecute = false
        });

        _logger.LogInformation("Script de actualización lanzado. El servicio se reiniciará.");

        // 6. Marcar estado antes de que el servicio se detenga
        await ActualizarDocAsync(docRef, new Dictionary<string, object>
        {
            ["comando_estado"]    = "reiniciando",
            ["comando_resultado"] = $"Descargado desde {url}. El servicio se reiniciará en segundos."
        }, ct);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task ActualizarDocAsync(DocumentReference docRef,
        Dictionary<string, object> campos, CancellationToken ct)
    {
        try { await docRef.UpdateAsync(campos); }
        catch (Exception ex) { _logger.LogWarning(ex, "No se pudo actualizar el documento."); }
    }

    private static string? GetStr(Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out var v) ? v?.ToString()?.Trim() : null;

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
}
