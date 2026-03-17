using CyberWatch.Shared.Config;
using CyberWatch.Shared.Helpers;
using CyberWatch.Shared.Models;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberWatch.UserAgent.services;

public class HistorialNavegacionService : BackgroundService
{
    private readonly FirebaseSettings _firebase;
    private readonly ILogger<HistorialNavegacionService> _logger;
    private DateTime _ultimaSync;

    public HistorialNavegacionService(
        IOptions<FirebaseSettings> firebase,
        ILogger<HistorialNavegacionService> logger)
    {
        _firebase = firebase.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var machineId = MachineIdHelper.Read();
        if (machineId == null)
        {
            _logger.LogWarning("[Historial] No se encontró cyberwatch_machine_id.txt. Servicio desactivado.");
            return;
        }

        if (string.IsNullOrEmpty(_firebase.ProjectId))
        {
            _logger.LogWarning("[Historial] Firebase:ProjectId no configurado. Servicio desactivado.");
            return;
        }

        FirestoreDb db;
        try
        {
            db = FirestoreDbFactory.Create(_firebase.ProjectId, _firebase.GetEffectiveCredentialPath());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Historial] Error al conectar con Firestore.");
            return;
        }

        // Leer última sincronización de Firestore
        _ultimaSync = await ObtenerUltimaSyncAsync(db, machineId, stoppingToken);
        _logger.LogInformation("[Historial] Iniciado. Última sync: {UltimaSync:u}", _ultimaSync);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SincronizarHistorialAsync(db, machineId, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Historial] Error en ciclo de sincronización.");
            }

            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }

    private async Task SincronizarHistorialAsync(
        FirestoreDb db, string machineId, CancellationToken ct)
    {
        // Leer historial de los tres navegadores
        var entradas = new List<EntradaHistorial>();
        entradas.AddRange(LectorHistorialSqlite.LeerChrome(_ultimaSync, _logger));
        entradas.AddRange(LectorHistorialSqlite.LeerEdge(_ultimaSync, _logger));
        entradas.AddRange(LectorHistorialSqlite.LeerFirefox(_ultimaSync, _logger));

        if (entradas.Count == 0)
        {
            _logger.LogDebug("[Historial] Sin entradas nuevas.");
            return;
        }

        // Marcar timestamp de sincronización
        var ahora = Timestamp.FromDateTime(DateTime.UtcNow);
        foreach (var e in entradas)
            e.Sincronizado = ahora;

        // Batch write a subcollección
        var colRef = db.Collection(_firebase.FirestoreColeccionInstancias)
                       .Document(machineId)
                       .Collection("historial_navegacion");

        foreach (var chunk in entradas.Chunk(450))
        {
            var batch = db.StartBatch();
            foreach (var entrada in chunk)
            {
                var docRef = colRef.Document(); // auto-ID
                batch.Create(docRef, entrada);
            }
            await batch.CommitAsync(ct);
        }

        // Determinar la fecha de visita más reciente del batch
        var maxFecha = entradas.Max(e => e.FechaVisita.ToDateTime());

        // Actualizar última sync en Firestore
        var docInstancia = db.Collection(_firebase.FirestoreColeccionInstancias).Document(machineId);
        await docInstancia.SetAsync(new InstanciaMaquina
        {
            UltimaSyncHistorial = Timestamp.FromDateTime(maxFecha)
        }, SetOptions.MergeFields("ultima_sync_historial"), ct);

        _ultimaSync = maxFecha;

        _logger.LogInformation("[Historial] Sincronizadas {Count} entradas a Firestore.", entradas.Count);
    }

    private static async Task<DateTime> ObtenerUltimaSyncAsync(
        FirestoreDb db, string machineId, CancellationToken ct)
    {
        try
        {
            var snap = await db.Collection("cyberwatch_instancias")
                               .Document(machineId)
                               .GetSnapshotAsync(ct);

            if (snap.Exists && snap.TryGetValue<Timestamp>("ultima_sync_historial", out var ts))
                return ts.ToDateTime();
        }
        catch
        {
            // Si falla, usar default
        }

        // Primera vez: solo últimas 24 horas
        return DateTime.UtcNow.AddDays(-1);
    }
}
