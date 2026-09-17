using System.Globalization;
using SeminarSched.Application.Logging;

namespace SeminarSched.Infrastructure.Logging;

public sealed class FileAppLogger : IAppLogger
{
    private const int RetentionDays = 14;
    private readonly string _directory;
    private readonly object _gate = new();

    public FileAppLogger(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_directory);
        PurgeOldFiles();
    }

    public void Info(string message) => Write("INFO", message);
    public void Warning(string message) => Write("WARN", message);
    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}: {exception.GetType().Name}: {exception.Message}");

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:O} [{level}] {message}";
        var path = Path.Combine(_directory, $"app-{DateTime.Now:yyyyMMdd}.log");
        lock (_gate)
        {
            try { File.AppendAllLines(path, [line]); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void PurgeOldFiles()
    {
        var cutoff = DateTime.Now.Date.AddDays(-RetentionDays);
        foreach (var file in Directory.EnumerateFiles(_directory, "app-*.log"))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (!stem.StartsWith("app-", StringComparison.Ordinal)) continue;
            if (!DateTime.TryParseExact(stem["app-".Length..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            if (date >= cutoff) continue;
            try { File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
