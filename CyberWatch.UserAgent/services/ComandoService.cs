using CyberWatch.Shared.Config;
using CyberWatch.Shared.Helpers;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberWatch.UserAgent.services;

/// <summary>
/// Polling a Firestore para leer comandos enviados desde el Dashboard (campo comando_ua).
/// </summary>
public class ComandoService : BackgroundService
{
    private readonly FirebaseSettings _firebase;
    private readonly CapturaService   _captura;
    private readonly ILogger<ComandoService> _logger;

    public ComandoService(
        IOptions<FirebaseSettings> firebase,
        CapturaService             captura,
        ILogger<ComandoService>    logger)
    {
        _firebase = firebase.Value;
        _captura  = captura;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var machineId = MachineIdHelper.Read();
        if (machineId == null)
        {
            _logger.LogWarning("No se encontró cyberwatch_machine_id.txt. ComandoService desactivado.");
            return;
        }

        if (string.IsNullOrEmpty(_firebase.ProjectId))
        {
            _logger.LogWarning("Firebase:ProjectId no configurado. ComandoService desactivado.");
            return;
        }

        FirestoreDb db;
        try
        {
            db = FirestoreDbFactory.Create(_firebase.ProjectId, _firebase.GetEffectiveCredentialPath());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al conectar con Firestore en ComandoService.");
            return;
        }

        var docRef = db.Collection(_firebase.FirestoreColeccionInstancias).Document(machineId);
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
}
