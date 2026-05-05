using CyberWatch.Shared.Config;
using CyberWatch.Shared.Helpers;
using CyberWatch.Shared.Models;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberWatch.Service.Services;

/// <summary>
/// Snapshot inicial + <see cref="DocumentReference.Listen"/> sobre <c>config/servicios</c>.
/// </summary>
public sealed class ConfigServiciosFirestoreService : BackgroundService, IConfigServiciosCache
{
    private readonly FirebaseSettings _firebase;
    private readonly ILogger<ConfigServiciosFirestoreService> _logger;

    private readonly object _sync = new();
    private HashSet<string> _hashesPermitidos = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _nombresExcluidos = new(StringComparer.OrdinalIgnoreCase);
    private string? _ultimaMod;
    private bool _docExistia;

    public ConfigServiciosFirestoreService(
        IOptions<FirebaseSettings> firebase,
        ILogger<ConfigServiciosFirestoreService> logger)
    {
        _firebase = firebase.Value;
        _logger = logger;
    }

    public bool UltimoSnapshotExistia
    {
        get { lock (_sync) return _docExistia; }
    }

    public string? UltimaModificacionRaw
    {
        get { lock (_sync) return _ultimaMod; }
    }

    public bool EsNombreScmExcluido(string? nombreServicio)
    {
        if (string.IsNullOrWhiteSpace(nombreServicio))
            return false;

        var nombre = nombreServicio.Trim();
        var sinExt = nombre.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? nombre[..^4]
            : nombre;

        lock (_sync)
        {
            foreach (var p in _nombresExcluidos)
            {
                if (string.Equals(p, nombre, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p, sinExt, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    public bool EsHashPermitido(string sha256Hex)
    {
        if (string.IsNullOrWhiteSpace(sha256Hex))
            return false;
        var norm = NormalizarHashHex(sha256Hex);
        if (norm.Length == 0)
            return false;

        lock (_sync)
            return _hashesPermitidos.Contains(norm);
    }

    /// <summary>Hex minúsculas sin espacios; vacío si formato inválido.</summary>
    internal static string NormalizarHashHex(string raw)
    {
        var s = raw.Trim().Replace(" ", string.Empty).ToLowerInvariant();
        if (s.Length != 64)
            return "";
        foreach (var c in s)
        {
            var ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!ok)
                return "";
        }

        return s;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_firebase.IsAdminConfigured)
        {
            _logger.LogInformation(
                "ConfigServicios: Firebase sin credencial; monitor anómalo usará solo hashes/nombres vacíos hasta configurar.");
            return;
        }

        FirestoreDb db;
        try
        {
            var cred = _firebase.GetEffectiveCredentialPath();
            if (cred == null)
                return;
            db = FirestoreDbFactory.Create(_firebase.ProjectId, cred);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ConfigServicios: no se pudo crear FirestoreDb.");
            return;
        }

        var docRef = db.Collection(_firebase.FirestoreConfigRedCollection)
            .Document(_firebase.FirestoreConfigServiciosDocumentId);

        try
        {
            var inicial = await docRef.GetSnapshotAsync(stoppingToken).ConfigureAwait(false);
            AplicarSnapshot(inicial);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ConfigServicios: error en snapshot inicial.");
        }

        var listener = docRef.Listen(async (snapshot, ct) =>
        {
            try
            {
                AplicarSnapshot(snapshot);
                await Task.CompletedTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ConfigServicios: error en listener.");
            }
        });

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await listener.StopAsync().ConfigureAwait(false);
        }
    }

    private void AplicarSnapshot(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists)
        {
            lock (_sync)
            {
                _docExistia = false;
                _hashesPermitidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _nombresExcluidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _ultimaMod = null;
            }

            _logger.LogWarning(
                "ConfigServicios: documento {Path} no existe; listas remotas vacías.",
                $"{_firebase.FirestoreConfigRedCollection}/{_firebase.FirestoreConfigServiciosDocumentId}");
            return;
        }

        ConfigServiciosDocument? modelo;
        try
        {
            modelo = snapshot.ConvertTo<ConfigServiciosDocument>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ConfigServicios: deserialización fallida.");
            return;
        }

        var hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (modelo.HashesPermitidos != null)
        {
            foreach (var h in modelo.HashesPermitidos)
            {
                var n = NormalizarHashHex(h);
                if (n.Length == 64)
                    hashSet.Add(n);
            }
        }

        var nomSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (modelo.NombresExcluidos != null)
        {
            foreach (var n in modelo.NombresExcluidos)
            {
                if (!string.IsNullOrWhiteSpace(n))
                    nomSet.Add(n.Trim());
            }
        }

        lock (_sync)
        {
            _docExistia = true;
            _hashesPermitidos = hashSet;
            _nombresExcluidos = nomSet;
            _ultimaMod = modelo.UltimaModificacion;
        }

        _logger.LogInformation(
            "ConfigServicios aplicada: hashes={Hashes}, nombres_excluidos={Nombres}, ultima_modificacion={Ultima}",
            hashSet.Count,
            nomSet.Count,
            modelo.UltimaModificacion ?? "(vacío)");
    }
}
