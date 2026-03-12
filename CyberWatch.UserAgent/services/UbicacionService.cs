using CyberWatch.Shared.Models;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Windows.Devices.Geolocation;

namespace CyberWatch.UserAgent.services;

public class UbicacionService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ILogger<UbicacionService> _logger;

    public UbicacionService(IConfiguration config, ILogger<UbicacionService> logger)
    {
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var machineId = LeerMachineId();
        if (machineId == null)
        {
            _logger.LogWarning("No se encontró cyberwatch_machine_id.txt. UbicacionService desactivado.");
            return;
        }

        var projectId = _config["Firebase:ProjectId"];
        if (string.IsNullOrEmpty(projectId))
        {
            _logger.LogWarning("Firebase:ProjectId no configurado. UbicacionService desactivado.");
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
            _logger.LogError(ex, "Error al conectar con Firestore.");
            return;
        }

        var acceso = await Geolocator.RequestAccessAsync();
        if (acceso != GeolocationAccessStatus.Allowed)
        {
            _logger.LogWarning("Acceso a ubicación denegado ({Status}). Activá 'Permitir que las apps de escritorio accedan a tu ubicación' en Configuración > Privacidad.", acceso);
            return;
        }

        var geolocator = new Geolocator { DesiredAccuracyInMeters = 100 };
        var coleccion = _config["Firebase:FirestoreColeccionInstancias"] ?? "cyberwatch_instancias";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var posicion = await geolocator.GetGeopositionAsync().AsTask(stoppingToken);
                var coord = posicion.Coordinate;

                var instancia = new InstanciaMaquina
                {
                    LatGps            = coord.Point.Position.Latitude,
                    LonGps            = coord.Point.Position.Longitude,
                    PrecisionGps      = coord.Accuracy,
                    UltimaUbicacionGps = Timestamp.FromDateTime(DateTime.UtcNow)
                };

                await db.Collection(coleccion).Document(machineId)
                    .SetAsync(instancia, SetOptions.MergeFields(
                        "lat_gps", "lon_gps", "precision_gps", "ultima_ubicacion_gps"
                    ), stoppingToken);

                _logger.LogDebug("Ubicación GPS actualizada: {Lat}, {Lon} (±{Acc}m)",
                    coord.Point.Position.Latitude, coord.Point.Position.Longitude, coord.Accuracy);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al obtener o guardar ubicación GPS.");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
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
