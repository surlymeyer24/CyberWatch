using System.Text.Json;
using CyberWatch.Shared.Config;
using CyberWatch.Shared.Helpers;
using CyberWatch.Shared.Models;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberWatch.UserAgent.services;

public class HistorialNavegacionService : BackgroundService
{
    private readonly FirebaseSettings _firebase;
    private readonly ILogger<HistorialNavegacionService> _logger;
    private DateTime _ultimaSync;

    private static readonly string _directorioHistorial =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyberWatch", "historial");

    public HistorialNavegacionService(
        IOptions<FirebaseSettings> firebase,
        ILogger<HistorialNavegacionService> logger)
    {
        _firebase = firebase.Value;
        _logger = logger;
        Directory.CreateDirectory(_directorioHistorial);
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

    /// <summary>
    /// Comando remoto: lee TODO el historial de navegación, lo guarda como JSON
    /// y lo sube a Firebase Storage. Devuelve la URL firmada.
    /// </summary>
    public async Task ExportarHistorialCompletoAsync()
    {
        _logger.LogInformation("[Historial] Iniciando exportación de historial completo...");

        // Leer todo el historial (desde el inicio de los tiempos)
        var entradas = new List<EntradaHistorial>();
        entradas.AddRange(LectorHistorialSqlite.LeerChrome(DateTime.MinValue, _logger));
        entradas.AddRange(LectorHistorialSqlite.LeerEdge(DateTime.MinValue, _logger));
        entradas.AddRange(LectorHistorialSqlite.LeerFirefox(DateTime.MinValue, _logger));

        if (entradas.Count == 0)
        {
            _logger.LogWarning("[Historial] No se encontró historial en ningún navegador. No se sube nada a Storage.");
            await EscribirErrorHistorialEnFirestoreAsync("Sin entradas: no se encontró historial en Chrome, Edge ni Firefox.");
            return;
        }

        _logger.LogInformation("[Historial] Historial completo: {Count} entradas de todos los navegadores.", entradas.Count);

        // Serializar a JSON
        var nombre = $"historial_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        var rutaLocal = Path.Combine(_directorioHistorial, nombre);

        var datosJson = entradas.Select(e => new
        {
            url = e.Url,
            titulo = e.Titulo,
            fecha_visita = e.FechaVisita.ToDateTime().ToString("o"),
            navegador = e.Navegador,
            perfil = e.Perfil
        });

        var json = JsonSerializer.Serialize(datosJson, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(rutaLocal, json);

        _logger.LogInformation("[Historial] JSON guardado localmente: {Ruta} ({Size} KB)",
            rutaLocal, new FileInfo(rutaLocal).Length / 1024);

        // Subir a Firebase Storage
        await SubirAStorageAsync(rutaLocal, nombre);
    }

    private async Task SubirAStorageAsync(string rutaLocal, string nombre)
    {
        try
        {
            var machineId = MachineIdHelper.Read();
            var credPath = _firebase.GetEffectiveCredentialPath();

            if (string.IsNullOrEmpty(credPath) || string.IsNullOrEmpty(_firebase.StorageBucket)
                || string.IsNullOrEmpty(_firebase.ProjectId) || machineId == null)
            {
                var msg = "[Historial] Storage no configurado o machineId no encontrado. Historial solo local.";
                _logger.LogWarning(msg);
                await EscribirErrorHistorialEnFirestoreAsync("Storage no configurado o machineId no encontrado.");
                return;
            }

            GoogleCredential credential;
            using (var stream = File.OpenRead(credPath))
#pragma warning disable CS0618 // FromStream obsoleto; CredentialFactory no ofrece API sencilla para path
                credential = GoogleCredential.FromStream(stream);
#pragma warning restore CS0618
            var objectName = $"historial/{machineId}/{nombre}";

            using var storageClient = StorageClient.Create(credential);
            using var fileStream = File.OpenRead(rutaLocal);
            await storageClient.UploadObjectAsync(_firebase.StorageBucket, objectName, "application/json", fileStream);

            var urlSigner = UrlSigner.FromCredential(credential);
            var url = await urlSigner.SignAsync(_firebase.StorageBucket, objectName, TimeSpan.FromDays(7));
            _logger.LogInformation("[Historial] Historial completo subido a Storage: {ObjectName}", objectName);

            // Guardar URL firmada en Firestore
            var db = FirestoreDbFactory.Create(_firebase.ProjectId, credPath);
            var docRef = db.Collection(_firebase.FirestoreColeccionInstancias).Document(machineId);
            await docRef.SetAsync(new InstanciaMaquina
            {
                UltimaHistorialCompletoUrl = url,
                UltimaHistorialCompletoTs = Timestamp.FromDateTime(DateTime.UtcNow),
                UltimaHistorialCompletoError = null
            }, SetOptions.MergeFields(
                "ultima_historial_completo_url", "ultima_historial_completo_ts", "ultima_historial_completo_error"
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Historial] Error al subir historial completo a Storage: {Message}", ex.Message);
            await EscribirErrorHistorialEnFirestoreAsync(ex.Message);
        }
    }

    private async Task EscribirErrorHistorialEnFirestoreAsync(string mensaje)
    {
        try
        {
            var machineId = MachineIdHelper.Read();
            var credPath = _firebase.GetEffectiveCredentialPath();
            if (string.IsNullOrEmpty(credPath) || string.IsNullOrEmpty(_firebase.ProjectId) || machineId == null)
                return;
            var db = FirestoreDbFactory.Create(_firebase.ProjectId, credPath);
            var docRef = db.Collection(_firebase.FirestoreColeccionInstancias).Document(machineId);
            await docRef.SetAsync(new InstanciaMaquina
            {
                UltimaHistorialCompletoError = mensaje
            }, SetOptions.MergeFields("ultima_historial_completo_error"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Historial] No se pudo escribir error en Firestore.");
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

        // Primera vez: desde ahora (solo historial nuevo a partir del arranque)
        return DateTime.UtcNow;
    }
}
