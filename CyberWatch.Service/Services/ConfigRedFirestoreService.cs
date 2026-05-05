using CyberWatch.Shared.Config;
using CyberWatch.Shared.Helpers;
using CyberWatch.Shared.Models;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberWatch.Service.Services;

/// <summary>
/// Snapshot inicial + <see cref="DocumentReference.Listen"/> sobre <c>config/red</c>.
/// Política: si no hay documento o Firebase no está configurado, la caché queda vacía y el monitor usa solo JSON embebido.
/// </summary>
public sealed class ConfigRedFirestoreService : BackgroundService, IConfigRedCache
{
    private readonly FirebaseSettings _firebase;
    private readonly ILogger<ConfigRedFirestoreService> _logger;

    private readonly object _sync = new();
    private int[] _puertosRemotos = Array.Empty<int>();
    private HashSet<string> _procesosExcluidos = new(StringComparer.OrdinalIgnoreCase);
    private bool _bloqueoEstricto;
    private string? _ultimaMod;
    private bool _docExistia;

    public ConfigRedFirestoreService(
        IOptions<FirebaseSettings> firebase,
        ILogger<ConfigRedFirestoreService> logger)
    {
        _firebase = firebase.Value;
        _logger = logger;
    }

    public IReadOnlyCollection<int> PuertosGlobalesRemotos
    {
        get
        {
            lock (_sync)
                return _puertosRemotos.Length == 0
                    ? Array.Empty<int>()
                    : (int[])_puertosRemotos.Clone();
        }
    }

    public bool UltimoSnapshotExistia
    {
        get { lock (_sync) return _docExistia; }
    }

    public bool BloqueoEstrictoActivo
    {
        get { lock (_sync) return _bloqueoEstricto; }
    }

    public string? UltimaModificacionRaw
    {
        get { lock (_sync) return _ultimaMod; }
    }

    public bool EsProcesoRedExcluido(string? nombreProceso)
    {
        if (string.IsNullOrWhiteSpace(nombreProceso))
            return false;

        var nombre = nombreProceso.Trim();
        var sinExt = nombre.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? nombre[..^4]
            : nombre;

        lock (_sync)
        {
            foreach (var p in _procesosExcluidos)
            {
                if (string.Equals(p, nombre, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p, sinExt, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_firebase.IsAdminConfigured)
        {
            _logger.LogInformation(
                "ConfigRed: Firebase sin credencial; monitor de puertos usará solo whitelist embebida.");
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
            _logger.LogWarning(ex, "ConfigRed: no se pudo crear FirestoreDb; solo whitelist embebida.");
            return;
        }

        var docRef = db.Collection(_firebase.FirestoreConfigRedCollection)
            .Document(_firebase.FirestoreConfigRedDocumentId);

        try
        {
            var inicial = await docRef.GetSnapshotAsync(stoppingToken).ConfigureAwait(false);
            AplicarSnapshot(inicial);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ConfigRed: error en snapshot inicial; se sigue con listener.");
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
                _logger.LogWarning(ex, "ConfigRed: error procesando snapshot del listener.");
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
                _puertosRemotos = Array.Empty<int>();
                _procesosExcluidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _bloqueoEstricto = false;
                _ultimaMod = null;
            }

            _logger.LogWarning(
                "ConfigRed: documento {Path} no existe; usando solo whitelist embebida.",
                $"{_firebase.FirestoreConfigRedCollection}/{_firebase.FirestoreConfigRedDocumentId}");
            return;
        }

        ConfigRedDocument? modelo;
        try
        {
            modelo = snapshot.ConvertTo<ConfigRedDocument>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ConfigRed: no se pudo deserializar el documento; se ignora contenido remoto.");
            return;
        }

        var puertos = modelo.PuertosGlobalesPermitidos?.Distinct().ToArray() ?? Array.Empty<int>();
        var procSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (modelo.ProcesosRedExcluidos != null)
        {
            foreach (var p in modelo.ProcesosRedExcluidos)
            {
                if (!string.IsNullOrWhiteSpace(p))
                    procSet.Add(p.Trim());
            }
        }

        lock (_sync)
        {
            _docExistia = true;
            _puertosRemotos = puertos;
            _procesosExcluidos = procSet;
            _bloqueoEstricto = modelo.BloqueoEstrictoActivo;
            _ultimaMod = modelo.UltimaModificacion;
        }

        if (modelo.BloqueoEstrictoActivo)
            _logger.LogInformation(
                "ConfigRed: bloqueo_estricto_activo=true (reservado; sin terminación de procesos en esta versión).");

        _logger.LogInformation(
            "ConfigRed aplicada: puertos_remotos={Puertos}, procesos_excluidos={Proc}, ultima_modificacion={Ultima}, bloqueo_estricto={Estricto}",
            puertos.Length,
            procSet.Count,
            modelo.UltimaModificacion ?? "(vacío)",
            modelo.BloqueoEstrictoActivo);
    }
}
