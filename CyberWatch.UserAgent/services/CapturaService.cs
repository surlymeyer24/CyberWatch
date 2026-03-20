using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
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

public class CapturaService : BackgroundService
{
    private readonly ILogger<CapturaService> _logger;
    private readonly FirebaseSettings _firebase;
    private static readonly string _directorioCapturas =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyberWatch", "capturas");

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public CapturaService(ILogger<CapturaService> logger, IOptions<FirebaseSettings> firebase)
    {
        _logger  = logger;
        _firebase = firebase.Value;
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
        string? ruta  = null;
        string? nombre = null;
        try
        {
            int w = GetSystemMetrics(0);
            int h = GetSystemMetrics(1);

            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var g   = Graphics.FromImage(bmp);
            g.CopyFromScreen(0, 0, 0, 0, new Size(w, h));

            nombre = $"captura_{DateTime.Now:yyyyMMdd_HHmmss}_{SanitizarNombre(motivo)}.png";
            ruta   = Path.Combine(_directorioCapturas, nombre);
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
            var machineId = MachineIdHelper.Read();
            var credPath  = _firebase.GetEffectiveCredentialPath();

            if (string.IsNullOrEmpty(credPath) || string.IsNullOrEmpty(_firebase.StorageBucket)
                || string.IsNullOrEmpty(_firebase.ProjectId) || machineId == null)
            {
                _logger.LogWarning("Storage no configurado o machineId no encontrado. Captura solo local.");
                return;
            }

            GoogleCredential credential;
            using (var stream = File.OpenRead(credPath))
#pragma warning disable CS0618 // FromStream obsoleto; CredentialFactory no ofrece API sencilla para path
                credential = GoogleCredential.FromStream(stream);
#pragma warning restore CS0618
            var objectName = $"capturas/{machineId}/{nombre}";

            using var storageClient = StorageClient.Create(credential);
            using var fileStream    = File.OpenRead(rutaLocal);
            await storageClient.UploadObjectAsync(_firebase.StorageBucket, objectName, "image/png", fileStream);

            var urlSigner = UrlSigner.FromCredential(credential);
            var url       = await urlSigner.SignAsync(_firebase.StorageBucket, objectName, TimeSpan.FromDays(7));
            _logger.LogInformation("Captura subida a Storage: {ObjectName}", objectName);

            var db     = FirestoreDbFactory.Create(_firebase.ProjectId, credPath);
            var docRef = db.Collection(_firebase.FirestoreColeccionInstancias).Document(machineId);
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

    private static string SanitizarNombre(string nombre)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        return string.Concat(nombre.Select(c => invalidos.Contains(c) ? '_' : c));
    }
}
