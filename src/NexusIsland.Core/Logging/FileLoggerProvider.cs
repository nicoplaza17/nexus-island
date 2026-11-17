using System.Text;
using Microsoft.Extensions.Logging;

namespace NexusIsland.Core.Logging;

/// <summary>
/// Logger mínimo a archivo: un archivo por día (nexus-AAAAMMDD.log), conserva los últimos N días.
/// Deliberadamente simple para no sumar dependencias ni consumo de memoria.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly LogLevel _minLevel;
    private readonly object _sync = new();
    private StreamWriter? _writer;
    private DateOnly _currentDay;

    public FileLoggerProvider(string directory, LogLevel minLevel = LogLevel.Information, int retainDays = 7)
    {
        _directory = directory;
        _minLevel = minLevel;
        Directory.CreateDirectory(directory);
        DeleteOldFiles(retainDays);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= _minLevel;

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var now = DateTime.Now;
        var sb = new StringBuilder(128)
            .Append(now.ToString("HH:mm:ss.fff")).Append(' ')
            .Append(LevelTag(level)).Append(' ')
            .Append('[').Append(ShortCategory(category)).Append("] ")
            .Append(message);
        if (exception is not null)
            sb.AppendLine().Append(exception);

        lock (_sync)
        {
            try
            {
                var today = DateOnly.FromDateTime(now);
                if (_writer is null || today != _currentDay)
                {
                    _writer?.Dispose();
                    _currentDay = today;
                    var path = Path.Combine(_directory, $"nexus-{now:yyyyMMdd}.log");
                    _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                        new UTF8Encoding(false)) { AutoFlush = true };
                }
                _writer.WriteLine(sb.ToString());
            }
            catch (IOException)
            {
                // Nunca dejar que un fallo de logging tire la app.
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void DeleteOldFiles(int retainDays)
    {
        try
        {
            var limit = DateTime.Now.AddDays(-retainDays);
            foreach (var file in Directory.EnumerateFiles(_directory, "nexus-*.log"))
            {
                if (File.GetLastWriteTime(file) < limit)
                    File.Delete(file);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    private static string ShortCategory(string category)
    {
        var i = category.LastIndexOf('.');
        return i >= 0 ? category[(i + 1)..] : category;
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            provider.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
