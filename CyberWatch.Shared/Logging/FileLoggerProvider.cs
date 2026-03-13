using Microsoft.Extensions.Logging;

namespace CyberWatch.Shared.Logging;

/// <summary>
/// Escribe logs en un archivo para poder revisar actualizaciones, errores, etc.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logFilePath;
    private readonly LogLevel _minLevel;

    public FileLoggerProvider(string logFileName, string? basePath = null, LogLevel minLevel = LogLevel.Information)
    {
        var dir = string.IsNullOrEmpty(basePath) ? AppDomain.CurrentDomain.BaseDirectory : basePath;
        _logFilePath = Path.Combine(dir, logFileName);
        _minLevel = minLevel;
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(_logFilePath, categoryName, _minLevel);

    public void Dispose() { }
}

file sealed class FileLogger : ILogger
{
    private static readonly object Lock = new();
    private readonly string _path;
    private readonly string _category;
    private readonly LogLevel _minLevel;

    public FileLogger(string path, string category, LogLevel minLevel)
    {
        _path = path;
        _category = category;
        _minLevel = minLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel && logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var levelStr = logLevel switch
        {
            LogLevel.Critical => "CRIT",
            LogLevel.Error    => "ERR ",
            LogLevel.Warning  => "WARN",
            LogLevel.Information => "INFO",
            LogLevel.Debug    => "DBG ",
            _                 => "    "
        };
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{levelStr}] [{_category}] {message}";
        if (exception != null)
            line += Environment.NewLine + exception;

        try
        {
            lock (Lock)
                File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch { /* no fallar si no se puede escribir */ }
    }
}
