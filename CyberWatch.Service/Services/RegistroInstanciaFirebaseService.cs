using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using CyberWatch.Service.Config;
using CyberWatch.Shared.Config;
using CyberWatch.Shared.Helpers;
using CyberWatch.Shared.Models;
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
            _db = FirestoreDbFactory.Create(_firebase.ProjectId, _firebase.GetEffectiveCredentialPath());
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

            try { await Task.Delay(interval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RegistrarAsync(CancellationToken ct)
    {
        if (_db == null || string.IsNullOrEmpty(_machineId)) return;

        var hostname = ObtenerNombrePC(_machineId);
        var ipLocal  = ObtenerIpLocal();

        var instancia = new InstanciaMaquina
        {
            Hostname        = hostname,
            Version         = _app.Version,
            UltimaConexion  = Timestamp.FromDateTime(DateTime.UtcNow),
            Servicio        = _app.ServiceName,
            BitlockerActivo = ObtenerBitLockerActivo(),
            FirewallActivo  = ObtenerFirewallActivo(),
            AdminsLocales   = ObtenerAdminsLocales()
        };

        AplicarEstadoServicioDesdeScQuery(_app.ServiceName, instancia);

        var campos = new List<string>
        {
            "hostname", "version", "servicio", "ultima_conexion",
            "bitlocker_activo", "firewall_activo", "admins_locales",
            "servicio_sc_estado", "servicio_sc_detalle", "servicio_sc_salida", "servicio_sc_consultado"
        };

        if (!string.IsNullOrEmpty(ipLocal))
        {
            instancia.IpLocal = ipLocal;
            campos.Add("ip_local");
        }

        // Geolocalizar solo la primera vez o cada 30 minutos
        if (_geoCache == null || (DateTime.UtcNow - _ultimaGeolocalizacion).TotalMinutes >= 30)
        {
            var ipPublica = await ObtenerIpPublicaAsync().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(ipPublica))
            {
                _geoCache = await GeolocalizarIpAsync(ipPublica).ConfigureAwait(false);
                _ultimaGeolocalizacion = DateTime.UtcNow;
                if (_geoCache != null)
                    instancia.IpPublica = ipPublica;
            }
        }

        if (_geoCache != null)
        {
            instancia.Lat                  = _geoCache.Lat;
            instancia.Lon                  = _geoCache.Lon;
            instancia.Ciudad               = _geoCache.Ciudad;
            instancia.Pais                 = _geoCache.Pais;
            instancia.Isp                  = _geoCache.Isp;
            instancia.UltimaGeolocalizacion = Timestamp.FromDateTime(_ultimaGeolocalizacion);
            campos.AddRange(new[] { "ip_publica", "lat", "lon", "ciudad", "pais", "isp", "ultima_geolocalizacion" });
        }

        var refDoc = _db.Collection(_firebase.FirestoreColeccionInstancias).Document(_machineId);
        await refDoc.SetAsync(instancia, SetOptions.MergeFields(campos.ToArray()), ct).ConfigureAwait(false);
        _logger.LogInformation("[RegistroInstancia] Datos actualizados: {Hostname} v{Version} | IP: {Ip} | Geo: {Ciudad},{Pais} | sc: {ScEstado} | BitLocker: {BL} | Firewall: {FW} | Admins: [{Admins}]",
            hostname, _app.Version, ipLocal ?? "N/A",
            _geoCache?.Ciudad ?? "N/A", _geoCache?.Pais ?? "N/A",
            instancia.ServicioScEstado,
            instancia.BitlockerActivo, instancia.FirewallActivo,
            string.Join(", ", instancia.AdminsLocales ?? new List<string>()));
    }

    private const int MaxServicioScSalidaChars = 2000;

    /// <summary>Ejecuta <c>sc query "NombreServicio"</c> y rellena campos para el dashboard.</summary>
    private static void AplicarEstadoServicioDesdeScQuery(string serviceName, InstanciaMaquina instancia)
    {
        instancia.ServicioScConsultado = Timestamp.FromDateTime(DateTime.UtcNow);
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            instancia.ServicioScEstado = "SIN_NOMBRE";
            instancia.ServicioScDetalle = "App:ServiceName vacío en appsettings.";
            instancia.ServicioScSalida = null;
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("query");
            psi.ArgumentList.Add(serviceName);
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                instancia.ServicioScEstado = "ERROR_SC";
                instancia.ServicioScDetalle = "No se pudo iniciar sc.exe";
                instancia.ServicioScSalida = null;
                return;
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(TimeSpan.FromSeconds(15));
            var combined = (stdout + "\n" + stderr).Trim();
            var exit = proc.ExitCode;

            if (exit != 0)
            {
                var lower = combined.ToLowerInvariant();
                if (combined.Contains("1060", StringComparison.Ordinal)
                    || lower.Contains("does not exist")
                    || lower.Contains("no existe el servicio")
                    || lower.Contains("specified service does not exist"))
                {
                    instancia.ServicioScEstado = "NO_EXISTE";
                    instancia.ServicioScDetalle = $"No hay servicio Windows llamado \"{serviceName}\" (revisá App:ServiceName vs sc create).";
                }
                else
                {
                    instancia.ServicioScEstado = "ERROR_SC";
                    instancia.ServicioScDetalle = $"sc.exe salió con código {exit}";
                }

                instancia.ServicioScSalida = TruncarServicioScSalida(combined);
                return;
            }

            // Inglés: STATE | Español: ESTADO (el texto puede ser varias palabras, ej. EN EJECUCIÓN)
            var match = Regex.Match(
                combined,
                @"(?m)^\s*(STATE|ESTADO)\s*:\s*\d+\s+(.+)$",
                RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var linea = match.Value.Trim();
                instancia.ServicioScDetalle = linea;
                instancia.ServicioScEstado = NormalizarEstadoServicioSc(linea);
            }
            else
            {
                instancia.ServicioScEstado = "DESCONOCIDO";
                instancia.ServicioScDetalle = "sc OK pero no se encontró línea STATE/ESTADO.";
            }

            instancia.ServicioScSalida = TruncarServicioScSalida(combined);
        }
        catch (Exception ex)
        {
            instancia.ServicioScEstado = "ERROR_SC";
            instancia.ServicioScDetalle = ex.Message;
            instancia.ServicioScSalida = null;
        }
    }

    private static string? TruncarServicioScSalida(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= MaxServicioScSalidaChars
            ? text
            : text[..MaxServicioScSalidaChars] + "...";
    }

    /// <summary>Valor estable para el dashboard (badges), a partir de la línea STATE/ESTADO de <c>sc</c> (EN/ES).</summary>
    private static string NormalizarEstadoServicioSc(string lineaState)
    {
        var u = lineaState.ToUpperInvariant();
        if (u.Contains("RUNNING")) return "RUNNING";
        if (u.Contains("STOPPED")) return "STOPPED";
        if (u.Contains("START_PENDING")) return "START_PENDING";
        if (u.Contains("STOP_PENDING")) return "STOP_PENDING";
        if (u.Contains("CONTINUE_PENDING")) return "CONTINUE_PENDING";
        if (u.Contains("PAUSE_PENDING")) return "PAUSE_PENDING";
        if (u.Contains("PAUSED")) return "PAUSED";
        // Windows en español (suele mantener RUNNING; por si acaso):
        if (u.Contains("EN EJECUCI")) return "RUNNING";
        if (u.Contains("DETENIDO") || u.Contains("PARADO")) return "STOPPED";
        if (u.Contains("PENDIENTE"))
        {
            if (u.Contains("INICIO") || u.Contains("START")) return "START_PENDING";
            if (u.Contains("DETEN") || u.Contains("STOP")) return "STOP_PENDING";
        }

        return "DESCONOCIDO";
    }

    private static string ObtenerOCrearMachineId()
    {
        // 1. Intentar UUID de hardware (estable entre reinstalaciones)
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
            foreach (ManagementObject obj in searcher.Get())
            {
                var uuid = obj["UUID"]?.ToString()?.Trim().ToLower();
                if (!string.IsNullOrEmpty(uuid)
                    && uuid != "ffffffff-ffff-ffff-ffff-ffffffffffff"
                    && uuid != "00000000-0000-0000-0000-000000000000")
                {
                    // Guardar en archivo para que UserAgent y otros componentes lo lean
                    try
                    {
                        var idFile = Path.Combine(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "cyberwatch_machine_id.txt");
                        File.WriteAllText(idFile, uuid);
                    }
                    catch { /* ignore */ }
                    return uuid;
                }
            }
        }
        catch { /* WMI no disponible, continuar con fallback */ }

        // 2. Fallback: archivo con GUID aleatorio (comportamiento anterior)
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

    private static bool ObtenerBitLockerActivo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"\\.\root\cimv2\Security\MicrosoftVolumeEncryption",
                "SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter = 'C:'");
            foreach (ManagementObject obj in searcher.Get())
            {
                // ProtectionStatus: 0=Off, 1=On, 2=Unknown
                var status = Convert.ToInt32(obj["ProtectionStatus"]);
                return status == 1;
            }
        }
        catch { /* WMI no disponible o BitLocker no instalado */ }
        return false;
    }

    private static bool ObtenerFirewallActivo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"\\.\root\StandardCimv2",
                "SELECT Enabled FROM MSFT_NetFirewallProfile WHERE Name='Domain' OR Name='Private' OR Name='Public'");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (Convert.ToInt32(obj["Enabled"]) != 1)
                    return false;
            }
            return true;
        }
        catch { /* ignore */ }
        return false;
    }

    private static List<string> ObtenerAdminsLocales()
    {
        var admins = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT PartComponent FROM Win32_GroupUser WHERE GroupComponent=\"Win32_Group.Domain='" +
                Environment.MachineName + "',Name='Administrators'\"");
            foreach (ManagementObject obj in searcher.Get())
            {
                var part = obj["PartComponent"]?.ToString() ?? "";
                // Formato: Win32_UserAccount.Domain="PC",Name="usuario"
                var match = System.Text.RegularExpressions.Regex.Match(part, @"Name=""([^""]+)""");
                if (match.Success)
                    admins.Add(match.Groups[1].Value);
            }
        }
        catch { /* WMI no disponible */ }
        return admins;
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

