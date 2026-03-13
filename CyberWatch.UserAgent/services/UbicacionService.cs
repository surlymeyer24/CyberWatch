using CyberWatch.Shared.Config;
using CyberWatch.Shared.Helpers;
using CyberWatch.Shared.Models;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Windows.Devices.Geolocation;

namespace CyberWatch.UserAgent.services;

public class UbicacionService : BackgroundService
{
    private readonly FirebaseSettings _firebase;
    private readonly ILogger<UbicacionService> _logger;

    public UbicacionService(IOptions<FirebaseSettings> firebase, ILogger<UbicacionService> logger)
    {
        _firebase = firebase.Value;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var machineId = MachineIdHelper.Read();
        if (machineId == null)
        {
            _logger.LogWarning("No se encontró cyberwatch_machine_id.txt. UbicacionService desactivado.");
            return;
        }

        if (string.IsNullOrEmpty(_firebase.ProjectId))
        {
            _logger.LogWarning("Firebase:ProjectId no configurado. UbicacionService desactivado.");
            return;
        }

        FirestoreDb db;
        try
        {
            db = FirestoreDbFactory.Create(_firebase.ProjectId, _firebase.GetEffectiveCredentialPath());
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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var posicion = await geolocator.GetGeopositionAsync().AsTask(stoppingToken);
                var coord    = posicion.Coordinate;

                var instancia = new InstanciaMaquina
                {
                    LatGps             = coord.Point.Position.Latitude,
                    LonGps             = coord.Point.Position.Longitude,
                    PrecisionGps       = coord.Accuracy,
                    UltimaUbicacionGps = Timestamp.FromDateTime(DateTime.UtcNow)
                };

                await db.Collection(_firebase.FirestoreColeccionInstancias).Document(machineId)
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
}
