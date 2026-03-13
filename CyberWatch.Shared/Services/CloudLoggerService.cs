using Google.Api;
using Google.Cloud.Logging.Type;
using Google.Cloud.Logging.V2;
using Google.Protobuf.WellKnownTypes;

namespace CyberWatch.Shared.Services;

/// <summary>
/// Envía logs a Google Cloud Logging (Cloud Operations).
/// Los logs se pueden ver en Google Cloud Console → Logging → Log Explorer.
/// El service account requiere el rol "Logs Writer" en IAM.
/// </summary>
public class CloudLoggerService
{
    private readonly LoggingServiceV2Client _client;
    private readonly string _projectId;
    private readonly string _logName;
    private readonly string _machineId;

    public CloudLoggerService(string projectId, string credentialPath, string logName, string machineId)
    {
        Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialPath);
        _client = LoggingServiceV2Client.Create();
        _projectId = projectId;
        _logName = logName;
        _machineId = machineId;
    }

    public async Task LogAsync(string mensaje, string nivel = "INFO")
    {
        var logNameResource = new LogName(_projectId, _logName);

        var resource = new MonitoredResource { Type = "global" };
        resource.Labels["project_id"] = _projectId;

        var entry = new LogEntry
        {
            LogNameAsLogName = logNameResource,
            Severity = nivel.ToUpperInvariant() switch
            {
                "ERROR"            => LogSeverity.Error,
                "WARNING" or "WARN"=> LogSeverity.Warning,
                "DEBUG"            => LogSeverity.Debug,
                "CRITICAL"         => LogSeverity.Critical,
                _                  => LogSeverity.Info
            },
            TextPayload = $"[{_machineId}] {mensaje}",
            Timestamp   = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        await _client.WriteLogEntriesAsync(logNameResource, resource, null, new[] { entry });
    }

    public Task LogInfoAsync(string mensaje)    => LogAsync(mensaje, "INFO");
    public Task LogWarningAsync(string mensaje) => LogAsync(mensaje, "WARNING");
    public Task LogErrorAsync(string mensaje)   => LogAsync(mensaje, "ERROR");
}
