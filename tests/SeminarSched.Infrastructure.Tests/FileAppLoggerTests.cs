using SeminarSched.Infrastructure.Logging;

namespace SeminarSched.Infrastructure.Tests;

public sealed class FileAppLoggerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SeminarSched.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Info_WritesTimestampedLineToTodaysLogFile()
    {
        var logger = new FileAppLogger(_directory);
        logger.Info("Project opened");

        var path = Path.Combine(_directory, $"app-{DateTime.Now:yyyyMMdd}.log");
        Assert.True(File.Exists(path));
        var line = Assert.Single(File.ReadAllLines(path));
        Assert.Contains("[INFO]", line);
        Assert.Contains("Project opened", line);
    }

    [Fact]
    public void Error_WithException_IncludesExceptionTypeAndMessage()
    {
        var logger = new FileAppLogger(_directory);
        logger.Error("Schedule run failed", new InvalidOperationException("no candidates"));

        var path = Path.Combine(_directory, $"app-{DateTime.Now:yyyyMMdd}.log");
        var line = Assert.Single(File.ReadAllLines(path));
        Assert.Contains("[ERROR]", line);
        Assert.Contains("InvalidOperationException", line);
        Assert.Contains("no candidates", line);
    }

    [Fact]
    public void Constructor_PurgesLogFilesOlderThanRetention()
    {
        Directory.CreateDirectory(_directory);
        var staleName = $"app-{DateTime.Now.AddDays(-30):yyyyMMdd}.log";
        var recentName = $"app-{DateTime.Now.AddDays(-1):yyyyMMdd}.log";
        File.WriteAllText(Path.Combine(_directory, staleName), "old");
        File.WriteAllText(Path.Combine(_directory, recentName), "recent");

        _ = new FileAppLogger(_directory);

        Assert.False(File.Exists(Path.Combine(_directory, staleName)));
        Assert.True(File.Exists(Path.Combine(_directory, recentName)));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
