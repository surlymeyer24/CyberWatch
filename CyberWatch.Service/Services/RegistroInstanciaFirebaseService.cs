using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using CyberWatch.Service.Config;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberWatch.Service.Services;

/// <summary>
/// Registra esta máquina en Firestore periódicamente para saber dónde está instalado CyberWatch
/// (igual que el otro servicio con "computadoras").
/// </summary>
public class RegistroInstanciaFirebaseService : BackgroundService
{
    private readonly FirebaseSettings _firebase;
    private readonly AppVersionSettings _app;
    private readonly ILogger<RegistroInstanciaFirebaseService> _logger;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private FirestoreDb? _db;
    private string? _machineId;
    private GeolocalizacionResultado? _geoCache;
    private DateTime _ultimaGeolocalizacion = DateTime.MinValue;

    public RegistroInstanciaFirebaseService(
        IOptions<FirebaseSettings> firebase,
        IOptions<AppVersionSettings> app,
        ILogger<RegistroInstanciaFirebaseService> logger)
    {
        _firebase = firebase.Value;
        _app = app.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_firebase.IntervaloRegistroInstanciaMinutos <= 0)
        {
            _logger.LogDebug("Registro de instancia en Firestore desactivado.");
            return;
        }

        if (!_firebase.IsAdminConfigured)
        {
            _logger.LogInformation("Firebase no configurado; esta instancia no se registrará en Firestore.");
            return;
        }

        _machineId = ObtenerOCrearMachineId();
        if (string.IsNullOrEmpty(_machineId))
        {
            _logger.LogWarning("No se pudo obtener/crear ID de máquina; no se registrará en Firestore.");
            return;
        }

        try
        {
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", _firebase.CredentialPath);
            _db = FirestoreDb.Create(_firebase.ProjectId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo conectar a Firestore para registrar la instancia.");
            return;
        }

        var interval = TimeSpan.FromMinutes(_firebase.IntervaloRegistroInstanciaMinutos);
        _logger.LogInformation("Registro de instancia cada {Minutos} min (colección: {Coleccion}, id: {Id}).",
            _firebase.IntervaloRegistroInstanciaMinutos, _firebase.FirestoreColeccionInstancias, _machineId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RegistrarAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar instancia en Firestore");
            }

            await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RegistrarAsync(CancellationToken ct)
    {
        if (_db == null || string.IsNullOrEmpty(_machineId)) return;

        var hostname = ObtenerNombrePC(_machineId);
        var ipLocal = ObtenerIpLocal();

        var doc = new Dictionary<string, object>
        {
            ["hostname"] = hostname,
            ["version"] = _app.Version,
            ["ultima_conexion"] = Timestamp.FromDateTime(DateTime.UtcNow),
            ["servicio"] = _app.ServiceName
        };
        if (!string.IsNullOrEmpty(ipLocal))
            doc["ip_local"] = ipLocal;

        // Geolocalizar solo la primera vez o cada 30 minutos
        if (_geoCache == null || (DateTime.UtcNow - _ultimaGeolocalizacion).TotalMinutes >= 30)
        {
            var ipPublica = await ObtenerIpPublicaAsync().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(ipPublica))
            {
                _geoCache = await GeolocalizarIpAsync(ipPublica).ConfigureAwait(false);
                _ultimaGeolocalizacion = DateTime.UtcNow;
                if (_geoCache != null)
                    doc["ip_publica"] = ipPublica;
            }
        }

        if (_geoCache != null)
        {
            doc["lat"] = _geoCache.Lat;
            doc["lon"] = _geoCache.Lon;
            doc["ciudad"] = _geoCache.Ciudad;
            doc["pais"] = _geoCache.Pais;
            doc["isp"] = _geoCache.Isp;
            doc["ultima_geolocalizacion"] = Timestamp.FromDateTime(_ultimaGeolocalizacion);
        }

        var refDoc = _db.Collection(_firebase.FirestoreColeccionInstancias).Document(_machineId);
        await refDoc.SetAsync(doc, SetOptions.MergeAll, ct).ConfigureAwait(false);
        _logger.LogDebug("Instancia registrada: {Hostname} ({Version})", hostname, _app.Version);
    }

    private static string ObtenerOCrearMachineId()
    {
        try
        {
            var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var idFile = Path.Combine(appDir, "cyberwatch_machine_id.txt");
            if (File.Exists(idFile))
            {
                var id = File.ReadAllText(idFile).Trim();
                if (!string.IsNullOrEmpty(id) && id.Length >= 8)
                    return id;
            }
            var newId = Guid.NewGuid().ToString("D");
            File.WriteAllText(idFile, newId);
            return newId;
        }
        catch
        {
            return Environment.MachineName + "_" + Guid.NewGuid().ToString("N")[..8];
        }
    }

    /// <summary>
    /// Obtiene el nombre de la PC. Environment.MachineName a veces viene vacío cuando el servicio corre como LocalSystem.
    /// </summary>
    private static string ObtenerNombrePC(string? machineId)
    {
        var name = Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        try
        {
            name = Dns.GetHostName();
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        }
        catch { /* ignore */ }
        return "PC-" + (machineId != null && machineId.Length >= 8 ? machineId[..8] : "?");
    }

    private static string? ObtenerIpLocal()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint ep)
                return ep.Address.ToString();
        }
        catch { /* ignore */ }
        return null;
    }

    private static async Task<string?> ObtenerIpPublicaAsync()
    {
        try
        {
            var json = await _http.GetStringAsync("https://api.ipify.org?format=json").ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("ip").GetString();
        }
        catch { /* ignore */ }
        return null;
    }

    private record GeolocalizacionResultado(double Lat, double Lon, string Ciudad, string Pais, string Isp);

    private static async Task<GeolocalizacionResultado?> GeolocalizarIpAsync(string ip)
    {
        try
        {
            var json = await _http.GetStringAsync($"http://ip-api.com/json/{ip}").ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.GetProperty("status").GetString() != "success") return null;
            return new GeolocalizacionResultado(
                root.GetProperty("lat").GetDouble(),
                root.GetProperty("lon").GetDouble(),
                root.GetProperty("city").GetString() ?? "",
                root.GetProperty("country").GetString() ?? "",
                root.GetProperty("isp").GetString() ?? ""
            );
        }
        catch { /* ignore */ }
        return null;
    }
}
