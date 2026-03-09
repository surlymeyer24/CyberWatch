using Google.Cloud.Firestore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CyberWatch.UserAgent.services;

/// <summary>
/// Polling a Firestore para leer comandos enviados desde el Dashboard (campo comando_ua).
/// </summary>
public class ComandoService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly CapturaService _captura;
    private readonly ILogger<ComandoService> _logger;

    public ComandoService(IConfiguration config, CapturaService captura, ILogger<ComandoService> logger)
    {
        _config = config;
        _captura = captura;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var machineId = LeerMachineId();
        if (machineId == null)
        {
            _logger.LogWarning("No se encontró cyberwatch_machine_id.txt. ComandoService desactivado.");
            return;
        }

        var projectId = _config["Firebase:ProjectId"];
        if (string.IsNullOrEmpty(projectId))
        {
            _logger.LogWarning("Firebase:ProjectId no configurado. ComandoService desactivado.");
            return;
        }

        FirestoreDb db;
        try
        {
            var credPath = _config["Firebase:CredentialPath"];
            if (!string.IsNullOrEmpty(credPath))
            {
                var resolved = Path.IsPathRooted(credPath)
                    ? credPath
                    : Path.Combine(AppContext.BaseDirectory, credPath);
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", resolved);
            }
            db = FirestoreDb.Create(projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al conectar con Firestore en ComandoService.");
            return;
        }

        var coleccion = _config["Firebase:FirestoreColeccionInstancias"] ?? "cyberwatch_instancias";
        var docRef = db.Collection(coleccion).Document(machineId);

        _logger.LogInformation("ComandoService iniciado, polling cada 15s.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snap = await docRef.GetSnapshotAsync(stoppingToken);
                if (snap.Exists && snap.TryGetValue<string>("comando_ua", out var cmd) && !string.IsNullOrEmpty(cmd))
                {
                    _logger.LogInformation("Comando UA recibido: {Cmd}", cmd);

                    if (cmd == "captura")
                        await _captura.TomarCapturaAsync("dashboard");

                    // Limpiar el campo para no re-ejecutar
                    await docRef.UpdateAsync(
                        new Dictionary<string, object> { ["comando_ua"] = FieldValue.Delete },
                        cancellationToken: stoppingToken);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al leer comando UA de Firestore.");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private static string? LeerMachineId()
    {
        try
        {
            var idFile = Path.Combine(AppContext.BaseDirectory, "cyberwatch_machine_id.txt");
            if (File.Exists(idFile))
            {
                var id = File.ReadAllText(idFile).Trim();
                if (!string.IsNullOrEmpty(id) && id.Length >= 8)
                    return id;
            }
        }
        catch { /* ignore */ }
        return null;
    }
}
