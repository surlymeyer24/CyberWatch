using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CyberWatch.Shared.Models;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CyberWatch.UserAgent.services;

public class CapturaService : BackgroundService
{
    private readonly ILogger<CapturaService> _logger;
    private readonly IConfiguration _config;
    private static readonly string _directorioCapturas =
        Path.Combine(AppContext.BaseDirectory, "capturas");

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public CapturaService(ILogger<CapturaService> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
        Directory.CreateDirectory(_directorioCapturas);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            if (!stoppingToken.IsCancellationRequested)
                await TomarCapturaAsync("periodica");
        }
    }

    public async Task TomarCapturaAsync(string motivo)
    {
        string? ruta = null;
        string? nombre = null;
        try
        {
            int w = GetSystemMetrics(0); // SM_CXSCREEN
            int h = GetSystemMetrics(1); // SM_CYSCREEN

            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(0, 0, 0, 0, new Size(w, h));

            nombre = $"captura_{DateTime.Now:yyyyMMdd_HHmmss}_{SanitizarNombre(motivo)}.png";
            ruta = Path.Combine(_directorioCapturas, nombre);
            bmp.Save(ruta, ImageFormat.Png);

            _logger.LogInformation("Captura guardada: {Ruta}", ruta);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al tomar captura de pantalla.");
            return;
        }

        await SubirAStorageAsync(ruta, nombre!, motivo);
    }

    private async Task SubirAStorageAsync(string rutaLocal, string nombre, string motivo)
    {
        try
        {
            var credPath  = _config["Firebase:CredentialPath"];
            var bucket    = _config["Firebase:StorageBucket"];
            var projectId = _config["Firebase:ProjectId"];
            var coleccion = _config["Firebase:FirestoreColeccionInstancias"] ?? "cyberwatch_instancias";
            var machineId = LeerMachineId();

            if (string.IsNullOrEmpty(credPath) || string.IsNullOrEmpty(bucket)
                || string.IsNullOrEmpty(projectId) || machineId == null)
            {
                _logger.LogWarning("Storage no configurado o machineId no encontrado. Captura solo local.");
                return;
            }

            var resolved = Path.IsPathRooted(credPath)
                ? credPath
                : Path.Combine(AppContext.BaseDirectory, credPath);

            var credential = GoogleCredential.FromFile(resolved);
            var objectName = $"capturas/{machineId}/{nombre}";

            using var storageClient = StorageClient.Create(credential);
            using var fileStream = File.OpenRead(rutaLocal);
            await storageClient.UploadObjectAsync(bucket, objectName, "image/png", fileStream);

            // URL firmada válida 7 días (límite máximo del firmador V4)
            var urlSigner = UrlSigner.FromCredential(credential);
            var url = await urlSigner.SignAsync(bucket, objectName, TimeSpan.FromDays(7));
            _logger.LogInformation("Captura subida a Storage: {ObjectName}", objectName);

            // Actualizar Firestore
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", resolved);
            var db = FirestoreDb.Create(projectId);
            var docRef = db.Collection(coleccion).Document(machineId);
            await docRef.SetAsync(new InstanciaMaquina
            {
                UltimaCapturaUrl    = url,
                UltimaCapturaTz     = Timestamp.FromDateTime(DateTime.UtcNow),
                UltimaCapturaMotivo = SanitizarNombre(motivo)
            }, SetOptions.MergeFields(
                "ultima_captura_url", "ultima_captura_ts", "ultima_captura_motivo"
            ));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al subir captura a Storage.");
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

    private static string SanitizarNombre(string nombre)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        return string.Concat(nombre.Select(c => invalidos.Contains(c) ? '_' : c));
    }
}
