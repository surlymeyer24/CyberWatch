using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;

namespace CyberWatch.Shared.Logging;

/// <summary>
/// Envía logs a Firestore (colección cyberwatch_logs) para visualización centralizada
/// en el dashboard. Fire-and-forget: nunca bloquea ni crashea el servicio.
/// </summary>
public sealed class FirestoreLoggerProvider : ILoggerProvider
{
    private readonly CollectionReference _collection;
    private readonly string _service;
    private readonly string _machineId;
    private readonly string _hostname;
    private readonly LogLevel _minLevel;

    public FirestoreLoggerProvider(FirestoreDb db, string serviceName, string machineId, string hostname, LogLevel minLevel = LogLevel.Information)
    {
        _collection = db.Collection("cyberwatch_logs");
        _service = serviceName;
        _machineId = machineId;
        _hostname = hostname;
        _minLevel = minLevel;
    }

    public ILogger CreateLogger(string categoryName) =>
        new FirestoreLogger(_collection, _service, _machineId, _hostname, categoryName, _minLevel);

    public void Dispose() { }
}

file sealed class FirestoreLogger : ILogger
{
    private readonly CollectionReference _collection;
    private readonly string _service;
    private readonly string _machineId;
    private readonly string _hostname;
    private readonly string _category;
    private readonly LogLevel _minLevel;

    public FirestoreLogger(CollectionReference collection, string service, string machineId, string hostname, string category, LogLevel minLevel)
    {
        _collection = collection;
        _service = service;
        _machineId = machineId;
        _hostname = hostname;
        _category = category;
        _minLevel = minLevel;
    }

    IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel && logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var levelStr = logLevel switch
        {
            LogLevel.Critical    => "Critical",
            LogLevel.Error       => "Error",
            LogLevel.Warning     => "Warning",
            LogLevel.Information => "Info",
            _                    => "Info"
        };

        var doc = new Dictionary<string, object>
        {
            ["timestamp"] = FieldValue.ServerTimestamp,
            ["level"]     = levelStr,
            ["service"]   = _service,
            ["machineId"] = _machineId,
            ["hostname"]  = _hostname,
            ["category"]  = _category,
            ["message"]   = message,
        };

        if (exception != null)
            doc["exception"] = exception.ToString();

        // Fire-and-forget: no bloquear el hilo, no crashear si Firestore falla
        _ = Task.Run(async () =>
        {
            try { await _collection.AddAsync(doc); }
            catch { /* silencioso */ }
        });
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
