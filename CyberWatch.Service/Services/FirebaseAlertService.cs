using CyberWatch.Service.Config;
using CyberWatch.Service.Models;
using CyberWatch.Shared.Config;
using CyberWatch.Shared.Helpers;
using CyberWatch.Shared.Models;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberWatch.Service.Services;

public class FirebaseAlertService : IFirebaseAlertService
{
    private readonly FirebaseSettings _settings;
    private readonly ILogger<FirebaseAlertService> _logger;
    private FirestoreDb? _db;
    private readonly string _machineId;

    public FirebaseAlertService(IOptions<FirebaseSettings> settings, ILogger<FirebaseAlertService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _machineId = MachineIdHelper.Read() ?? "";

        if (_settings.IsAdminConfigured)
        {
            try
            {
                // Equivalente a Node: admin.initializeApp({ credential: admin.credential.cert(serviceAccount) })
                var serviceAccount = GoogleCredential.FromFile(_settings.CredentialPath);
                FirebaseApp.Create(new AppOptions
                {
                    Credential = serviceAccount,
                    ProjectId = _settings.ProjectId
                });

                _db = FirestoreDbFactory.Create(_settings.ProjectId, _settings.GetEffectiveCredentialPath());
                _logger.LogInformation("Firebase Admin inicializado. Proyecto: {ProjectId}. Alertas y registro de instancia activos.", _settings.ProjectId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo inicializar Firebase. Las alertas no se enviarán a Firestore.");
            }
        }
        else
        {
            _logger.LogInformation("Firebase no configurado (CredentialPath vacío o archivo no encontrado). Alertas solo en archivo local; Firestore no se actualizará.");
        }
    }

    public async Task EnviarAlertaAsync(ReporteAmenaza reporte, CancellationToken ct = default)
    {
        if (_db == null || string.IsNullOrEmpty(_machineId)) return;

        try
        {
            var col = _db.Collection(_settings.FirestoreColeccionInstancias)
                         .Document(_machineId)
                         .Collection(_settings.FirestoreCollectionAlertas);

            // Dedup: no crear si ya existe alerta con mismo proceso en últimos 10 min
            var proceso = reporte.NombreProceso ?? "";
            var desde = Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(-10));
            var existente = await col
                .WhereEqualTo("nombreProceso", proceso)
                .WhereGreaterThanOrEqualTo("fechaHora", desde)
                .Limit(1)
                .GetSnapshotAsync(ct).ConfigureAwait(false);

            if (existente.Count > 0)
            {
                _logger.LogDebug("Alerta duplicada omitida para proceso: {Proceso}", proceso);
                return;
            }

            var alerta = new Alerta
            {
                NombreProceso          = proceso,
                FechaHora              = Timestamp.FromDateTime(reporte.FechaHora.ToUniversalTime()),
                EscriturasSospechosas  = reporte.EscriturasSospechosas,
                RenombradosSospechosas = reporte.RenombradosSospechosas,
                ExtensionSospechosa    = reporte.ExtensionSospechosa,
                Origen                 = "CyberWatch.Service",
                MachineId              = _machineId,
                Hostname               = Environment.MachineName
            };

            await col.AddAsync(alerta, ct).ConfigureAwait(false);
            _logger.LogDebug("Alerta enviada a Firestore: {Proceso}", proceso);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al enviar alerta a Firebase");
        }
    }

}
