using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CyberWatch.Shared.Logging;

/// <summary>
/// Escribe logs en un archivo para poder revisar actualizaciones, errores, etc.
/// Usa un stream abierto con AutoFlush y FileShare.Read para que el archivo se actualice en disco
/// y se pueda ver en tiempo real desde otro proceso (p. ej. editor que recarga).
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly string _logFilePath;
    private readonly LogLevel _minLevel;
    private readonly object _streamLock = new();

    public FileLoggerProvider(string logFileName, string? basePath = null, LogLevel minLevel = LogLevel.Information)
    {
        var dir = string.IsNullOrEmpty(basePath) ? AppDomain.CurrentDomain.BaseDirectory : basePath;
        Directory.CreateDirectory(dir);
        _logFilePath = Path.Combine(dir, logFileName);
        _minLevel = minLevel;

        var stream = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.None);
        _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(_writer, _streamLock, categoryName, _minLevel);

    public void Dispose() => _writer?.Dispose();
}

file sealed class FileLogger : ILogger
{
    private readonly StreamWriter _writer;
    private readonly object _lock;
    private readonly string _category;
    private readonly LogLevel _minLevel;

    public FileLogger(StreamWriter writer, object lockObj, string category, LogLevel minLevel)
    {
        _writer = writer;
        _lock = lockObj;
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
            lock (_lock)
            {
                _writer.WriteLine(line);
                _writer.Flush();
            }
        }
        catch { /* no fallar si no se puede escribir */ }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
