using System.Collections.Concurrent;
using System.Text;

namespace InventorValidator.Infrastructure;

public enum LogSeverity
{
    Info,
    Success,
    Warning,
    Error
}

public record LogEntry(DateTime Timestamp, LogSeverity Severity, string Message, string? Details = null)
{
    public override string ToString()
    {
        var level = Severity switch
        {
            LogSeverity.Success => "[OK ]",
            LogSeverity.Warning => "[WARN]",
            LogSeverity.Error   => "[ERR ]",
            _                   => "[INFO]"
        };

        var text = $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {level} {Message}";
        if (!string.IsNullOrEmpty(Details))
        {
            text += $"\n    Details: {Details}";
        }
        return text;
    }
}

public class DiagnosticsLogger
{
    private static readonly Lazy<DiagnosticsLogger> _instance = new(() => new DiagnosticsLogger());
    public static DiagnosticsLogger Instance => _instance.Value;

    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private const int MaxEntries = 1000;

    public event Action<LogEntry>? EntryLogged;

    public IReadOnlyList<LogEntry> Entries => _entries.ToArray();

    public void Log(LogSeverity severity, string message, string? details = null)
    {
        var entry = new LogEntry(DateTime.Now, severity, message, details);
        _entries.Enqueue(entry);

        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _))
        {
        }

        EntryLogged?.Invoke(entry);
    }

    public void Info(string message, string? details = null) => Log(LogSeverity.Info, message, details);
    public void Success(string message, string? details = null) => Log(LogSeverity.Success, message, details);
    public void Warn(string message, string? details = null) => Log(LogSeverity.Warning, message, details);
    public void Error(string message, Exception? ex = null) => Log(LogSeverity.Error, message, ex?.ToString());

    public string ExportToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== InventorValidator Diagnostics Log ===");
        sb.AppendLine($"Exported at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(new string('-', 50));
        foreach (var entry in _entries)
        {
            sb.AppendLine(entry.ToString());
        }
        return sb.ToString();
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }
}
