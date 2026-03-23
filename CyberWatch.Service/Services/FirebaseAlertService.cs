using System.Collections.Generic;
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
    private string? _machineId;

    public FirebaseAlertService(IOptions<FirebaseSettings> settings, ILogger<FirebaseAlertService> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        if (_settings.IsAdminConfigured)
        {
            try
            {
                var credPath = _settings.GetEffectiveCredentialPath();
                if (string.IsNullOrEmpty(credPath)) throw new InvalidOperationException("Credencial no configurada.");
                GoogleCredential serviceAccount;
                using (var stream = File.OpenRead(credPath))
#pragma warning disable CS0618 // FromStream obsoleto; CredentialFactory no ofrece API sencilla para path
                    serviceAccount = GoogleCredential.FromStream(stream);
#pragma warning restore CS0618
                // Equivalente a Node: admin.initializeApp({ credential: admin.credential.cert(serviceAccount) })
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

    private string GetMachineId()
    {
        if (string.IsNullOrEmpty(_machineId))
            _machineId = MachineIdHelper.Read() ?? "";
        return _machineId;
    }

    public Task EnviarAlertaAsync(ReporteAmenaza reporte, CancellationToken ct = default)
        => EnviarAlertaAsync(reporte, null, -1, ct);

    public Task EnviarAlertaAsync(ReporteAmenaza reporte, ResultadoCuarentena? cuarentena, CancellationToken ct = default)
        => EnviarAlertaAsync(reporte, cuarentena, -1, ct);

    public async Task EnviarAlertaAsync(ReporteAmenaza reporte, ResultadoCuarentena? cuarentena, int eventosArchivoEnCiclo, CancellationToken ct = default)
    {
        var machineId = GetMachineId();
        if (_db == null || string.IsNullOrEmpty(machineId)) return;

        try
        {
            var col = _db.Collection(_settings.FirestoreColeccionInstancias)
                         .Document(machineId)
                         .Collection(_settings.FirestoreCollectionAlertas);

            var colLogs = _db.Collection(_settings.FirestoreColeccionInstancias)
                             .Document(machineId)
                             .Collection(_settings.FirestoreCollectionLogsAmenazas);

            // Dedup: no crear si ya existe alerta con mismo proceso en últimos 10 min
            var proceso = reporte.NombreProceso ?? "";
            var desde = Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(-10));
            var existente = await col
                .WhereEqualTo("nombreProceso", proceso)
                .WhereGreaterThanOrEqualTo("fechaHora", desde)
                .Limit(1)
                .GetSnapshotAsync(ct).ConfigureAwait(false);

            var duplicada = existente.Count > 0;
            if (duplicada)
                _logger.LogDebug("Alerta duplicada omitida para proceso: {Proceso} (se registra en logs_amenazas)", proceso);

            var resumen = ConstruirResumen(reporte, cuarentena, eventosArchivoEnCiclo, duplicada);

            var log = new LogAmenaza
            {
                FechaHora = Timestamp.FromDateTime(DateTime.UtcNow),
                MachineId = machineId,
                Hostname = Environment.MachineName,
                AlertaFirestoreCreada = !duplicada,
                NombreProceso = proceso,
                EscriturasSospechosas = reporte.EscriturasSospechosas,
                RenombradosSospechosas = reporte.RenombradosSospechosas,
                ExtensionSospechosa = reporte.ExtensionSospechosa,
                ExtensionDetectada = reporte.ExtensionDetectada,
                RutaEjecutableOriginal = reporte.RutaEjecutable,
                CuarentenaExitosa = cuarentena?.Exitosa,
                RutaCuarentena = cuarentena?.RutaCuarentena,
                CuarentenaError = cuarentena?.Error,
                EventosArchivoEnCiclo = eventosArchivoEnCiclo < 0 ? 0 : eventosArchivoEnCiclo,
                Resumen = resumen
            };

            await colLogs.AddAsync(log, ct).ConfigureAwait(false);
            _logger.LogInformation("Log amenaza persistido: {Proceso} alertaNueva={Nueva} eventosCiclo={Ev}",
                proceso, !duplicada, eventosArchivoEnCiclo);

            if (duplicada)
                return;

            var alerta = new Alerta
            {
                NombreProceso          = proceso,
                FechaHora              = Timestamp.FromDateTime(reporte.FechaHora.ToUniversalTime()),
                EscriturasSospechosas  = reporte.EscriturasSospechosas,
                RenombradosSospechosas = reporte.RenombradosSospechosas,
                ExtensionSospechosa    = reporte.ExtensionSospechosa,
                ExtensionDetectada     = reporte.ExtensionDetectada,
                RutaEjecutableOriginal = reporte.RutaEjecutable,
                CuarentenaExitosa      = cuarentena?.Exitosa,
                RutaCuarentena         = cuarentena?.RutaCuarentena,
                CuarentenaError        = cuarentena?.Error,
                Origen                 = "CyberWatch.Service",
                MachineId              = machineId,
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

    private static string ConstruirResumen(ReporteAmenaza reporte, ResultadoCuarentena? cuarentena, int eventosArchivoEnCiclo, bool deduplicada)
    {
        var partes = new List<string>
        {
            $"proceso={reporte.NombreProceso}",
            $"escrituras={reporte.EscriturasSospechosas}",
            $"renombrados={reporte.RenombradosSospechosas}",
            $"extensionSospechosa={reporte.ExtensionSospechosa}",
        };
        if (!string.IsNullOrEmpty(reporte.ExtensionDetectada))
            partes.Add($"ext={reporte.ExtensionDetectada}");
        if (!string.IsNullOrEmpty(reporte.RutaEjecutable))
            partes.Add($"ruta={reporte.RutaEjecutable}");
        if (eventosArchivoEnCiclo >= 0)
            partes.Add($"eventosArchivoCiclo={eventosArchivoEnCiclo}");
        if (cuarentena != null)
            partes.Add(cuarentena.Exitosa ? "cuarentena=OK" : $"cuarentena=fallo:{cuarentena.Error}");
        partes.Add(deduplicada ? "alertaFirestore=omitida_dedup_10min" : "alertaFirestore=creada");
        return string.Join(" | ", partes);
    }

}
