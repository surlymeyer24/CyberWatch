using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using CyberWatch.Service.Config;
using CyberWatch.Shared.Config;
using CyberWatch.Shared.Helpers;
using CyberWatch.Shared.Models;
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
/// Comandos: actualizar_agente, reiniciar_servicio, sc_query (consulta inmediata <c>sc query</c> y actualiza servicio_sc_*).
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
            db = FirestoreDbFactory.Create(_firebase.ProjectId, _firebase.GetEffectiveCredentialPath());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo conectar a Firestore.");
            return;
        }

        var docRef = db.Collection(_firebase.FirestoreColeccionInstancias).Document(machineId);

        // Al arrancar, si había una actualización en curso, marcarla como completada
        try
        {
            var snap = await docRef.GetSnapshotAsync(stoppingToken);
            if (snap.Exists && snap.TryGetValue<string>("comando_estado", out var estadoActual)
                && estadoActual == "reiniciando")
            {
                // Leer log del batch script para incluirlo en el resultado
                var updateLogPath = Path.Combine(Path.GetTempPath(), "cw_update.log");
                var logContent = "";
                if (File.Exists(updateLogPath))
                {
                    logContent = File.ReadAllText(updateLogPath);
                    _logger.LogInformation("Log de actualización:\n{Log}", logContent);
                }
                else
                {
                    _logger.LogWarning("No se encontró cw_update.log en {Path}", updateLogPath);
                }

                // Asegurar que la tarea del UserAgent exista y ejecutarla
                var exePath = Path.Combine(AppContext.BaseDirectory, "CyberWatch.UserAgent.exe");
                var uaInfo = "";
                if (File.Exists(exePath))
                {
                    uaInfo = AsegurarYEjecutarTareaUserAgent(exePath);
                }
                else
                {
                    uaInfo = "CyberWatch.UserAgent.exe no encontrado.";
                    _logger.LogWarning("CyberWatch.UserAgent.exe no encontrado en {Dir}", AppContext.BaseDirectory);
                }

                // Versión instalada = la que trae el appsettings.json del .exe recién desplegado
                var versionInstalada = _app.Version;
                _logger.LogInformation("[Comando] Versión instalada: {Version} (según CyberWatch:Version en appsettings.json del ejecutable)", versionInstalada);

                await ActualizarDocAsync(docRef, new Dictionary<string, object>
                {
                    ["comando_estado"]    = "completado",
                    ["comando_resultado"] = $"Versión instalada: {versionInstalada} (desde config del .exe). {uaInfo}\n\nLog batch:\n{logContent}"
                }, stoppingToken);
                _logger.LogInformation("[Comando] Actualización previa completada. Versión actual: {Version}", versionInstalada);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo verificar estado de actualización previa.");
        }

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
                _logger.LogInformation("[Comando] Recibido: '{Comando}' a las {Hora}", comando, DateTime.Now.ToString("HH:mm:ss"));

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
                        _logger.LogInformation("[Comando] Iniciando actualización de agente...");
                        await EjecutarActualizacionAgenteAsync(docRef, db, ct);
                        _logger.LogInformation("[Comando] Script lanzado. Esperando shutdown (bloqueando nuevos comandos).");
                        await Task.Delay(Timeout.Infinite, ct);
                        break;

                    case "reiniciar_servicio":
                        _logger.LogInformation("[Comando] Iniciando reinicio del servicio...");
                        await EjecutarReinicioServicioAsync(docRef, ct);
                        _logger.LogInformation("[Comando] Script de reinicio lanzado. Esperando shutdown.");
                        await Task.Delay(Timeout.Infinite, ct);
                        break;

                    case "sc_query":
                        _logger.LogInformation("[Comando] sc_query para servicio {Name}...", _app.ServiceName);
                        var scInst = new InstanciaMaquina();
                        ServicioScQueryHelper.AplicarEstadoServicioDesdeScQuery(_app.ServiceName, scInst);
                        var scPatch = new Dictionary<string, object>
                        {
                            ["servicio_sc_estado"] = scInst.ServicioScEstado,
                            ["servicio_sc_detalle"] = scInst.ServicioScDetalle ?? "",
                            ["servicio_sc_consultado"] = scInst.ServicioScConsultado!,
                            ["comando_estado"] = "completado",
                            ["comando_resultado"] =
                                $"sc query ({_app.ServiceName}): {scInst.ServicioScEstado} — {scInst.ServicioScDetalle ?? ""}"
                        };
                        scPatch["servicio_sc_salida"] = scInst.ServicioScSalida != null
                            ? scInst.ServicioScSalida
                            : FieldValue.Delete;
                        await ActualizarDocAsync(docRef, scPatch, ct);
                        break;

                    default:
                        _logger.LogWarning("[Comando] Comando desconocido: '{Cmd}'", comando);
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
        var serviceName = string.IsNullOrWhiteSpace(_app.ServiceName) ? "CyberWatch" : _app.ServiceName.Trim();

        // 1. Leer url_descarga (y version opcional) desde config/ciberseguridad
        string url;
        string? configVersion = null;
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
            if (configSnap.TryGetValue<string>("version", out var ver) && !string.IsNullOrWhiteSpace(ver))
                configVersion = ver.Trim();
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

        _logger.LogInformation(
            "[Comando][ActualizarAgente] Descargando — url={Url}, versionFirestore={Ver}, servicioWindows={Svc}",
            url, configVersion ?? "(sin campo)", serviceName);
        byte[] bytes;
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "CyberWatch-Updater/1.0");
            bytes = await http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(zipPath, bytes, ct);
            _logger.LogInformation(
                "[Comando][ActualizarAgente] Descarga OK — {Bytes} bytes guardados en {Zip}",
                bytes.Length, zipPath);
        }
        catch (Exception ex)
        {
            await ActualizarDocAsync(docRef, new Dictionary<string, object>
            {
                ["comando_estado"]    = "error",
                ["comando_resultado"] = $"Error al descargar desde {url}: {ex.Message}"
            }, ct);
            return;
        }

        // 3. Extraer ZIP
        int extractedFileCount;
        try
        {
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            ZipFile.ExtractToDirectory(zipPath, extractPath);
            extractedFileCount = Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories).Length;
            _logger.LogInformation(
                "[Comando][ActualizarAgente] Extracción OK — {Count} archivos bajo {Dir}",
                extractedFileCount, extractPath);
        }
        catch (Exception ex)
        {
            await ActualizarDocAsync(docRef, new Dictionary<string, object>
            {
                ["comando_estado"]    = "error",
                ["comando_resultado"] = $"Error al extraer ZIP ({bytes.Length} bytes descargados): {ex.Message}"
            }, ct);
            return;
        }

        // 4. Cabecera del log (evita problemas de caracteres en URL dentro del .bat)
        var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var scriptPath = Path.Combine(Path.GetTempPath(), "cw_update.bat");
        var logPath = Path.Combine(Path.GetTempPath(), "cw_update.log");
        var schtasksCleanupName = $"CyberWatch\\RemoteUpd_{Guid.NewGuid():N}";

        var logHeader = BuildActualizacionLogHeader(
            url, configVersion, bytes.Length, extractedFileCount, installDir, extractPath, serviceName, schtasksCleanupName);
        await File.WriteAllTextAsync(logPath, logHeader, new UTF8Encoding(false), ct);

        // 5. Script de actualización: debe ejecutarse FUERA del árbol de procesos del servicio.
        var svcBatch = BatchEscapeSetValue(serviceName);
        File.WriteAllText(scriptPath,
            $"@echo off\r\n" +
            $"setlocal EnableDelayedExpansion\r\n" +
            $"set \"CW_SVC={svcBatch}\"\r\n" +
            $"echo [%DATE% %TIME%] [FASE 1/8] Pausa 3s antes de detener el servicio... >> \"{logPath}\"\r\n" +
            $"timeout /t 3 /nobreak >nul\r\n" +
            $"echo [%DATE% %TIME%] [FASE 2/8] net stop \"%CW_SVC%\" ... >> \"{logPath}\"\r\n" +
            $"net stop \"%CW_SVC%\" >> \"{logPath}\" 2>&1\r\n" +
            $"echo [%DATE% %TIME%] [FASE 2/8] net stop codigo=%ERRORLEVEL% >> \"{logPath}\"\r\n" +
            $"echo [%DATE% %TIME%] [FASE 2/8] sc query \"%CW_SVC%\" (tras net stop) >> \"{logPath}\"\r\n" +
            $"sc query \"%CW_SVC%\" >> \"{logPath}\" 2>&1\r\n" +
            $"echo [%DATE% %TIME%] [FASE 3/8] taskkill UserAgent (si sigue en memoria)... >> \"{logPath}\"\r\n" +
            $"taskkill /F /IM CyberWatch.UserAgent.exe /T >> \"{logPath}\" 2>&1\r\n" +
            $"echo [%DATE% %TIME%] [FASE 3/8] taskkill codigo=%ERRORLEVEL% >> \"{logPath}\"\r\n" +
            $"timeout /t 3 /nobreak >nul\r\n" +
            BatchWaitCyberWatchStopped(logPath, serviceName, faseEtiqueta: "[FASE 4/8]") +
            $"echo [%DATE% %TIME%] [FASE 5/8] xcopy /E /I /Y extract -^> installDir >> \"{logPath}\"\r\n" +
            $"echo [%DATE% %TIME%] Origen: {extractPath} Destino: {installDir} >> \"{logPath}\"\r\n" +
            $"xcopy /E /I /Y \"{extractPath}\\*\" \"{installDir}\\\" >> \"{logPath}\" 2>&1\r\n" +
            $"echo [%DATE% %TIME%] [FASE 5/8] xcopy codigo=%ERRORLEVEL% >> \"{logPath}\"\r\n" +
            $"echo [%DATE% %TIME%] [FASE 6/8] Pausa 6s post-copia (liberar exe / antivirus)... >> \"{logPath}\"\r\n" +
            $"timeout /t 6 /nobreak >nul\r\n" +
            $"echo [%DATE% %TIME%] [FASE 7/8] Arranque del servicio (net start / sc / PowerShell, hasta 15 intentos)... >> \"{logPath}\"\r\n" +
            BatchStartCyberWatchRetryBlock(logPath, serviceName, "cw_try_start", "cw_start_ok", "cw_start_fail", maxTries: 15, delaySec: 5) +
            $":cw_start_fail\r\n" +
            $"echo [%DATE% %TIME%] [FASE 7/8] ERROR: no se pudo iniciar el servicio tras reintentos >> \"{logPath}\"\r\n" +
            $"goto cw_update_cleanup\r\n" +
            $":cw_start_ok\r\n" +
            $"echo [%DATE% %TIME%] [FASE 7/8] Servicio iniciado; verificacion sc query >> \"{logPath}\"\r\n" +
            $"sc query \"%CW_SVC%\" >> \"{logPath}\" 2>&1\r\n" +
            $":cw_update_cleanup\r\n" +
            $"echo [%DATE% %TIME%] [FASE 8/8] Limpieza (tarea schtasks, carpeta temp, zip, este bat) >> \"{logPath}\"\r\n" +
            $"echo [%DATE% %TIME%] === FIN ACTUALIZACION === >> \"{logPath}\"\r\n" +
            $"schtasks /Delete /TN \"{schtasksCleanupName}\" /F >> \"{logPath}\" 2>&1\r\n" +
            $"rd /s /q \"{extractPath}\"\r\n" +
            $"del \"{zipPath}\"\r\n" +
            $"del \"%~f0\"\r\n");

        // 6. Lanzar el .bat vía tarea programada (proceso bajo el Programador de tareas, no hijo del servicio).
        _logger.LogInformation("[Comando][ActualizarAgente] Batch generado: {Script}, log: {Log}, tarea: {Task}", scriptPath, logPath, schtasksCleanupName);
        if (!TryLaunchBatchViaScheduledTask(scriptPath, schtasksCleanupName, out var schMsg))
        {
            _logger.LogWarning("[Comando] schtasks no pudo programar la actualización ({Msg}). Usando fallback cmd start.", schMsg);
            TryLaunchBatchDetachedFallback(scriptPath);
        }

        _logger.LogInformation("[Comando][ActualizarAgente] Script programado; el servicio {Svc} se detendrá y volverá a iniciar.", serviceName);

        var resumenFirestore =
            $"Listo para aplicar actualización.\n" +
            $"- URL: {url}\n" +
            $"- Versión en config/ciberseguridad: {(configVersion ?? "(sin campo version)")}\n" +
            $"- ZIP descargado: {bytes.Length} bytes\n" +
            $"- Archivos en paquete extraído: {extractedFileCount}\n" +
            $"- Instalación: {installDir}\n" +
            $"- Servicio Windows: {serviceName}\n" +
            $"- Log local: {logPath}\n" +
            $"El servicio se reiniciará en segundos; el detalle del batch quedará en cw_update.log.";
        await ActualizarDocAsync(docRef, new Dictionary<string, object>
        {
            ["comando_estado"]    = "reiniciando",
            ["comando_resultado"] = resumenFirestore
        }, ct);
    }

    // ── Reinicio remoto ───────────────────────────────────────────────────────

    private async Task EjecutarReinicioServicioAsync(DocumentReference docRef, CancellationToken ct)
    {
        var serviceName = string.IsNullOrWhiteSpace(_app.ServiceName) ? "CyberWatch" : _app.ServiceName.Trim();
        var scriptPath = Path.Combine(Path.GetTempPath(), "cw_restart.bat");
        var logPath    = Path.Combine(Path.GetTempPath(), "cw_update.log");
        var schtasksCleanupName = $"CyberWatch\\RemoteRst_{Guid.NewGuid():N}";
        var svcBatch = BatchEscapeSetValue(serviceName);

        var reinicioHeader =
            $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] === Reinicio remoto CyberWatch (comando reiniciar_servicio) ===\r\n" +
            $"Servicio Windows: {serviceName}\r\n" +
            $"Tarea schtasks (cleanup): {schtasksCleanupName}\r\n" +
            "---\r\n";
        await File.AppendAllTextAsync(logPath, reinicioHeader, new UTF8Encoding(false), ct);

        File.WriteAllText(scriptPath,
            $"@echo off\r\n" +
            $"setlocal EnableDelayedExpansion\r\n" +
            $"set \"CW_SVC={svcBatch}\"\r\n" +
            $"echo [%DATE% %TIME%] [REINICIO 1/5] Pausa 3s >> \"{logPath}\"\r\n" +
            $"timeout /t 3 /nobreak >nul\r\n" +
            $"echo [%DATE% %TIME%] [REINICIO 2/5] net stop \"%CW_SVC%\" >> \"{logPath}\"\r\n" +
            $"net stop \"%CW_SVC%\" >> \"{logPath}\" 2>&1\r\n" +
            $"echo [%DATE% %TIME%] [REINICIO 2/5] codigo=%ERRORLEVEL% >> \"{logPath}\"\r\n" +
            $"timeout /t 3 /nobreak >nul\r\n" +
            BatchWaitCyberWatchStopped(logPath, serviceName, faseEtiqueta: "[REINICIO 3/5]") +
            $"echo [%DATE% %TIME%] [REINICIO 4/5] Arranque con reintentos >> \"{logPath}\"\r\n" +
            BatchStartCyberWatchRetryBlock(logPath, serviceName, "cw_rst_try", "cw_rst_ok", "cw_rst_fail", maxTries: 15, delaySec: 5) +
            $":cw_rst_fail\r\n" +
            $"echo [%DATE% %TIME%] [REINICIO 4/5] ERROR: sin arranque tras reintentos >> \"{logPath}\"\r\n" +
            $"goto cw_rst_end\r\n" +
            $":cw_rst_ok\r\n" +
            $"echo [%DATE% %TIME%] [REINICIO 4/5] sc query tras arranque >> \"{logPath}\"\r\n" +
            $"sc query \"%CW_SVC%\" >> \"{logPath}\" 2>&1\r\n" +
            $":cw_rst_end\r\n" +
            $"echo [%DATE% %TIME%] [REINICIO 5/5] Limpieza tarea + borrar bat >> \"{logPath}\"\r\n" +
            $"echo [%DATE% %TIME%] === FIN REINICIO === >> \"{logPath}\"\r\n" +
            $"schtasks /Delete /TN \"{schtasksCleanupName}\" /F >> \"{logPath}\" 2>&1\r\n" +
            $"del \"%~f0\"\r\n");

        _logger.LogInformation("[Comando][ReiniciarServicio] Batch {Path}, servicio={Svc}", scriptPath, serviceName);
        if (!TryLaunchBatchViaScheduledTask(scriptPath, schtasksCleanupName, out var schMsg))
        {
            _logger.LogWarning("[Comando] schtasks no pudo programar el reinicio ({Msg}). Usando fallback cmd start.", schMsg);
            TryLaunchBatchDetachedFallback(scriptPath);
        }

        _logger.LogInformation("[Comando] Script de reinicio programado (o lanzado).");

        await ActualizarDocAsync(docRef, new Dictionary<string, object>
        {
            ["comando_estado"]    = "reiniciando",
            ["comando_resultado"] = "El servicio se reiniciará en segundos."
        }, ct);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task ActualizarDocAsync(DocumentReference docRef,
        Dictionary<string, object> campos, CancellationToken ct)
    {
        try { await docRef.UpdateAsync(campos); }
        catch (Exception ex) { _logger.LogWarning(ex, "No se pudo actualizar el documento."); }
    }

    private string AsegurarYEjecutarTareaUserAgent(string exePath)
    {
        var result = "";
        try
        {
            // Generar XML y crear la tarea con /F (fuerza creación aunque exista)
            var xml = RegistradorTareaUsuarioService.GenerarXmlTarea(exePath);
            var xmlPath = Path.Combine(Path.GetTempPath(), "cyberwatch_useragent_task.xml");
            File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode);

            try
            {
                _logger.LogInformation("Registrando tarea UserAgent (forzado)...");
                var (creado, msgCreate) = EjecutarProceso("schtasks",
                    $"/Create /TN \"CyberWatch\\UserAgent\" /XML \"{xmlPath}\" /F");
                _logger.LogInformation("schtasks /Create: ok={Ok}, output={Msg}", creado, msgCreate);
                result += creado ? "Tarea registrada OK. " : $"Tarea no registrada: {msgCreate}. ";
            }
            finally
            {
                File.Delete(xmlPath);
            }

            _logger.LogInformation("Ejecutando tarea UserAgent...");
            var (ejecutado, msgRun) = EjecutarProceso("schtasks",
                "/Run /TN \"CyberWatch\\UserAgent\"");
            _logger.LogInformation("schtasks /Run: ok={Ok}, output={Msg}", ejecutado, msgRun);
            result += ejecutado ? "UserAgent iniciado OK." : $"UserAgent no iniciado: {msgRun}.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al asegurar/ejecutar tarea UserAgent.");
            result += $"Error: {ex.Message}";
        }
        return result;
    }

    /// <summary>
    /// Ejecuta un .bat bajo el Programador de tareas (no como hijo del proceso del servicio),
    /// para que sobreviva a <c>net stop CyberWatch</c> y pueda volver a arrancar el servicio.
    /// </summary>
    private static bool TryLaunchBatchViaScheduledTask(string scriptPath, string taskName, out string message)
    {
        message = "";
        try
        {
            // /SD debe coincidir con el formato regional del SO (servicio = misma cultura que la máquina).
            var runAt = DateTime.Now.AddMinutes(1);
            var sd = runAt.ToString("d", CultureInfo.CurrentCulture);
            var st = runAt.ToString("HH:mm", CultureInfo.InvariantCulture);

            var createPsi = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            createPsi.ArgumentList.Add("/Create");
            createPsi.ArgumentList.Add("/F");
            createPsi.ArgumentList.Add("/TN");
            createPsi.ArgumentList.Add(taskName);
            createPsi.ArgumentList.Add("/TR");
            createPsi.ArgumentList.Add(scriptPath);
            createPsi.ArgumentList.Add("/SC");
            createPsi.ArgumentList.Add("ONCE");
            createPsi.ArgumentList.Add("/SD");
            createPsi.ArgumentList.Add(sd);
            createPsi.ArgumentList.Add("/ST");
            createPsi.ArgumentList.Add(st);
            createPsi.ArgumentList.Add("/RL");
            createPsi.ArgumentList.Add("HIGHEST");
            // Misma cuenta que el servicio (LocalSystem): evita tareas creadas sin principal claro.
            createPsi.ArgumentList.Add("/RU");
            createPsi.ArgumentList.Add("SYSTEM");

            using var createProc = Process.Start(createPsi);
            var cout = createProc?.StandardOutput.ReadToEnd() ?? "";
            var cerr = createProc?.StandardError.ReadToEnd() ?? "";
            createProc?.WaitForExit();
            if (createProc?.ExitCode != 0)
            {
                message = $"{cout} {cerr}".Trim();
                if (string.IsNullOrEmpty(message))
                    message = $"schtasks /Create salió con código {createProc?.ExitCode}";
                return false;
            }

            var runPsi = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            runPsi.ArgumentList.Add("/Run");
            runPsi.ArgumentList.Add("/TN");
            runPsi.ArgumentList.Add(taskName);

            using var runProc = Process.Start(runPsi);
            var rout = runProc?.StandardOutput.ReadToEnd() ?? "";
            var rerr = runProc?.StandardError.ReadToEnd() ?? "";
            runProc?.WaitForExit();
            if (runProc?.ExitCode != 0)
            {
                message = $"{rout} {rerr}".Trim();
                if (string.IsNullOrEmpty(message))
                    message = $"schtasks /Run salió con código {runProc?.ExitCode}";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Fragmento .bat: espera hasta ~90 s a que el servicio quede en Stopped (no depende del idioma de <c>sc query</c>).
    /// </summary>
    private static string BatchWaitCyberWatchStopped(string logPath, string serviceName, string faseEtiqueta)
    {
        var psName = EscapeForPowerShellSingleQuoted(serviceName);
        return
            $"echo [%DATE% %TIME%] {faseEtiqueta} Espera SCM: servicio \"%CW_SVC%\" debe quedar Stopped (hasta ~90s, sondeo cada 2s; codigo 0=OK)... >> \"{logPath}\"\r\n" +
            $"powershell -NoProfile -ExecutionPolicy Bypass -Command \"" +
            $"for ($i=0; $i -lt 45; $i++) {{ " +
            $"$s = (Get-Service -Name '{psName}' -EA SilentlyContinue).Status; " +
            $"if ($i %% 10 -eq 0) {{ Write-Output ('t=' + $i + ' status=' + $s) }}; " +
            $"if ($s -eq 'Stopped') {{ exit 0 }}; Start-Sleep -s 2 }}; " +
            $"Write-Output ('timeout: ultimo status=' + $s); exit 1\" " +
            $">> \"{logPath}\" 2>&1\r\n" +
            $"echo [%DATE% %TIME%] {faseEtiqueta} espera_stopped codigo=%ERRORLEVEL% >> \"{logPath}\"\r\n";
    }

    /// <summary>
    /// Reintentos de arranque: <c>net start</c>, <c>sc start</c>, <c>Start-Service</c> (captura ERRORLEVEL fiable con delayed expansion).
    /// </summary>
    private static string BatchStartCyberWatchRetryBlock(
        string logPath, string serviceName, string tryLabel, string okLabel, string failLabel, int maxTries, int delaySec)
    {
        var psName = EscapeForPowerShellSingleQuoted(serviceName);
        return
            $"set CW_START_TRIES=0\r\n" +
            $":{tryLabel}\r\n" +
            $"set /a CW_START_TRIES+=1\r\n" +
            $"echo [%DATE% %TIME%] intento !CW_START_TRIES!/{maxTries}: net start \"%CW_SVC%\" >> \"{logPath}\"\r\n" +
            $"net start \"%CW_SVC%\" >> \"{logPath}\" 2>&1\r\n" +
            $"set CW_NS=!ERRORLEVEL!\r\n" +
            $"echo [%DATE% %TIME%] intento !CW_START_TRIES! net start codigo=!CW_NS! >> \"{logPath}\"\r\n" +
            $"if !CW_NS! equ 0 goto {okLabel}\r\n" +
            $"echo [%DATE% %TIME%] intento !CW_START_TRIES!/{maxTries}: sc start \"%CW_SVC%\" >> \"{logPath}\"\r\n" +
            $"sc start \"%CW_SVC%\" >> \"{logPath}\" 2>&1\r\n" +
            $"set CW_NS=!ERRORLEVEL!\r\n" +
            $"if !CW_NS! equ 0 goto {okLabel}\r\n" +
            $"echo [%DATE% %TIME%] intento !CW_START_TRIES!/{maxTries}: Start-Service PowerShell >> \"{logPath}\"\r\n" +
            $"powershell -NoProfile -ExecutionPolicy Bypass -Command \"try {{ Start-Service -Name '{psName}'; exit 0 }} catch {{ Write-Output $_.Exception.Message; exit 1 }}\" >> \"{logPath}\" 2>&1\r\n" +
            $"set CW_NS=!ERRORLEVEL!\r\n" +
            $"if !CW_NS! equ 0 goto {okLabel}\r\n" +
            $"if !CW_START_TRIES! geq {maxTries} goto {failLabel}\r\n" +
            $"echo [%DATE% %TIME%] Sin exito en intento !CW_START_TRIES!; pausa {delaySec}s y reintento... >> \"{logPath}\"\r\n" +
            $"timeout /t {delaySec} /nobreak >nul\r\n" +
            $"goto {tryLabel}\r\n";
    }

    private static string EscapeForPowerShellSingleQuoted(string s) => s.Replace("'", "''", StringComparison.Ordinal);

    /// <summary>Valor seguro en <c>set "CW_SVC=..."</c> en batch (sin comillas dobles).</summary>
    private static string BatchEscapeSetValue(string serviceName) =>
        serviceName.Replace("\"", "", StringComparison.Ordinal);

    private static string BuildActualizacionLogHeader(
        string url,
        string? configVersion,
        long zipBytes,
        int extractedFileCount,
        string installDir,
        string extractPath,
        string serviceName,
        string schtasksTaskName) =>
        $"=== CyberWatch — actualización remota (trazas) ===\r\n" +
        $"UTC inicio cabecera: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z\r\n" +
        $"Origen del paquete (config/ciberseguridad.url_descarga):\r\n{url}\r\n" +
        $"Versión publicada (config/ciberseguridad.version): {(configVersion ?? "(no definida en Firestore)")}\r\n" +
        $"Tamaño del ZIP descargado: {zipBytes} bytes\r\n" +
        $"Archivos en carpeta extraída: {extractedFileCount}\r\n" +
        $"Carpeta temporal de extracción: {extractPath}\r\n" +
        $"Directorio de instalación (destino xcopy): {installDir}\r\n" +
        $"Nombre del servicio Windows: {serviceName}\r\n" +
        $"Tarea Programador (cleanup al final): {schtasksTaskName}\r\n" +
        $"---\r\n" +
        $"A continuación: salida del script batch (fases numeradas).\r\n\r\n";

    /// <summary>
    /// Segundo intento si schtasks falla (políticas, etc.): <c>start</c> deja un cmd independiente.
    /// </summary>
    private static void TryLaunchBatchDetachedFallback(string scriptPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"\" /MIN \"{scriptPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath()
            });
        }
        catch
        {
            // último recurso
            Process.Start(new ProcessStartInfo(scriptPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath()
            });
        }
    }

    private static (bool success, string output) EjecutarProceso(string fileName, string args)
    {
        var info = new ProcessStartInfo(fileName, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute = false,
            CreateNoWindow  = true
        };
        using var proc = Process.Start(info);
        var stdout = proc?.StandardOutput.ReadToEnd() ?? "";
        var stderr = proc?.StandardError.ReadToEnd() ?? "";
        proc?.WaitForExit();
        return (proc?.ExitCode == 0, $"{stdout}{stderr}".Trim());
    }

    private static string? GetStr(Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out var v) ? v?.ToString()?.Trim() : null;

    private async Task<string?> EsperarMachineIdAsync(CancellationToken ct)
    {
        for (int i = 0; i < 24; i++)
        {
            var id = MachineIdHelper.Read();
            if (id is not null) return id;
            if (i == 0)
                _logger.LogInformation("Esperando machine ID (lo genera RegistroInstanciaService)...");
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
        _logger.LogWarning("No se obtuvo machine ID en 2 min; el ejecutor de comandos no arrancará.");
        return null;
    }

}
