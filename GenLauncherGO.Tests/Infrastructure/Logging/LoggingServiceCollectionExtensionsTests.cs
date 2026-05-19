using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GenLauncherGO.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Tests.Infrastructure.Logging;

public sealed partial class LoggingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGenLauncherGoLogging_CreatesReadableSessionLog()
    {
        using TestDirectory directory = new();
        string logDirectory = directory.GetPath("Logs");

        for (int index = 0; index < 2; index++)
        {
            var services = new ServiceCollection();
            services.AddGenLauncherGoLogging(logDirectory);
            using ServiceProvider provider = services.BuildServiceProvider();
            provider
                .GetRequiredService<ILogger<LoggingServiceCollectionExtensionsTests>>()
                .LogInformation("Session {SessionIndex}", index);
        }

        Directory.Exists(logDirectory).Should().BeTrue();
        string[] logFiles = Directory.GetFiles(logDirectory, "GenLauncherGO-*.log");
        logFiles.Should().HaveCount(2);
        logFiles.Should().OnlyContain(file =>
            SessionLogFileNamePattern().IsMatch(Path.GetFileName(file)));
    }

    [Fact]
    public void AddGenLauncherGoLogging_PrunesOldSessionLogs()
    {
        using TestDirectory directory = new();
        string logDirectory = directory.CreateDirectory("Logs");
        string[] oldLogFileNames = Enumerable.Range(1, 20)
            .Select(day => $"GenLauncherGO-2026-01-{day:00}-120000Z.log")
            .ToArray();
        for (int index = 0; index < oldLogFileNames.Length; index++)
        {
            string logFilePath = Path.Combine(logDirectory, oldLogFileNames[index]);
            File.WriteAllText(logFilePath, "old");
            File.SetLastWriteTimeUtc(logFilePath, DateTime.UtcNow.AddMinutes(-index - 1));
        }

        var services = new ServiceCollection();

        services.AddGenLauncherGoLogging(logDirectory);
        using (ServiceProvider provider = services.BuildServiceProvider())
        {
            provider
                .GetRequiredService<ILogger<LoggingServiceCollectionExtensionsTests>>()
                .LogInformation("Current session");
        }

        string[] retainedLogFileNames = Directory.GetFiles(logDirectory, "*.log")
            .Select(file => Path.GetFileName(file))
            .ToArray();
        retainedLogFileNames.Should().HaveCount(14);
        retainedLogFileNames.Should().Contain(oldLogFileNames.Take(13));
        retainedLogFileNames.Should().NotContain(oldLogFileNames.Skip(13));
        retainedLogFileNames.Except(oldLogFileNames).Should().ContainSingle();
    }

    /// <summary>
    ///     The retained logs are the most recent sessions, which the name of a log file does not decide: a restored
    ///     backup, a corrected clock, or a copied folder all leave names and ages disagreeing, and the sessions worth
    ///     keeping are still the ones that ran last.
    /// </summary>
    [Fact]
    public void AddGenLauncherGoLogging_RetainsTheMostRecentlyWrittenLogs()
    {
        using TestDirectory directory = new();
        string logDirectory = directory.CreateDirectory("Logs");
        string[] logFileNamesOldestFirst = Enumerable.Range(1, 20)
            .Select(day => $"GenLauncherGO-2026-01-{day:00}-120000Z.log")
            .ToArray();
        for (int index = 0; index < logFileNamesOldestFirst.Length; index++)
        {
            string logFilePath = Path.Combine(logDirectory, logFileNamesOldestFirst[index]);
            File.WriteAllText(logFilePath, "old");
            File.SetLastWriteTimeUtc(
                logFilePath,
                DateTime.UtcNow.AddMinutes(index - logFileNamesOldestFirst.Length));
        }

        var services = new ServiceCollection();

        services.AddGenLauncherGoLogging(logDirectory);
        using (ServiceProvider provider = services.BuildServiceProvider())
        {
            provider
                .GetRequiredService<ILogger<LoggingServiceCollectionExtensionsTests>>()
                .LogInformation("Current session");
        }

        string[] retainedLogFileNames = Directory.GetFiles(logDirectory, "*.log")
            .Select(file => Path.GetFileName(file))
            .ToArray();
        retainedLogFileNames.Should().Contain(logFileNamesOldestFirst.Skip(7));
        retainedLogFileNames.Should().NotContain(logFileNamesOldestFirst.Take(7));
    }

    /// <summary>
    ///     The log folder is somewhere a user browses to and drops files. Pruning is scoped to the launcher's own
    ///     session logs by name, so the oldest thing in the folder is not automatically the next thing deleted.
    /// </summary>
    [Fact]
    public void AddGenLauncherGoLogging_PrunesOnlyItsOwnSessionLogs()
    {
        using TestDirectory directory = new();
        string logDirectory = directory.CreateDirectory("Logs");
        string foreignFilePath = Path.Combine(logDirectory, "crash-report.txt");
        File.WriteAllText(foreignFilePath, "kept");
        File.SetLastWriteTimeUtc(foreignFilePath, DateTime.UtcNow.AddDays(-30));
        for (int day = 1; day <= 20; day++)
        {
            string logFilePath = Path.Combine(logDirectory, $"GenLauncherGO-2026-01-{day:00}-120000Z.log");
            File.WriteAllText(logFilePath, "old");
            File.SetLastWriteTimeUtc(logFilePath, DateTime.UtcNow.AddMinutes(-day));
        }

        var services = new ServiceCollection();

        services.AddGenLauncherGoLogging(logDirectory);
        using (ServiceProvider provider = services.BuildServiceProvider())
        {
            provider
                .GetRequiredService<ILogger<LoggingServiceCollectionExtensionsTests>>()
                .LogInformation("Current session");
        }

        File.ReadAllText(foreignFilePath).Should().Be("kept");
    }

    [Fact]
    public void AddGenLauncherGoLogging_RedactsLocalPathsAndSensitiveQueryValues()
    {
        using TestDirectory directory = new();
        string logDirectory = directory.GetPath("Logs");
        var services = new ServiceCollection();
        const string SensitiveUrl =
            "https://user:password@example.test/package?token=secret-value&X-Amz-Credential=aws-key" +
            "&X-Amz-Signature=aws-signature&X-Amz-Security-Token=aws-token&name=safe";

        services.AddGenLauncherGoLogging(logDirectory);
        using (ServiceProvider provider = services.BuildServiceProvider())
        {
            provider
                .GetRequiredService<ILogger<LoggingServiceCollectionExtensionsTests>>()
                .LogError(
                    new InvalidOperationException(@"Failed under C:\Users\Alice\Secrets\file.txt"),
                    "Could not open {Path} from {Uri}.",
                    @"C:\Users\Alice\Secrets\file.txt",
                    SensitiveUrl);
        }

        string logText = File.ReadAllText(Directory.GetFiles(logDirectory, "GenLauncherGO-*.log").Single());
        logText.Should().Contain("[local path]");
        logText.Should().Contain("https://[redacted]@example.test");
        logText.Should().Contain("token=[redacted]");
        logText.Should().Contain("X-Amz-Credential=[redacted]");
        logText.Should().Contain("X-Amz-Signature=[redacted]");
        logText.Should().Contain("X-Amz-Security-Token=[redacted]");
        logText.Should().NotContain("Alice");
        logText.Should().NotContain("password");
        logText.Should().NotContain("secret-value");
        logText.Should().NotContain("aws-key");
        logText.Should().NotContain("aws-signature");
        logText.Should().NotContain("aws-token");
    }

    [Fact]
    public void AddGenLauncherGoLogging_RedactsUncAndForwardSlashWindowsPaths()
    {
        using TestDirectory directory = new();
        string logDirectory = directory.GetPath("Logs");
        var services = new ServiceCollection();

        services.AddGenLauncherGoLogging(logDirectory);
        using (ServiceProvider provider = services.BuildServiceProvider())
        {
            provider
                .GetRequiredService<ILogger<LoggingServiceCollectionExtensionsTests>>()
                .LogWarning(
                    "Could not read {ForwardSlashPath} or {UncPath}.",
                    "C:/Users/Alice Example/Secrets/file.txt",
                    @"\\fileserver\profiles\Bob Example\Secrets\file.txt");
        }

        string logText = File.ReadAllText(Directory.GetFiles(logDirectory, "GenLauncherGO-*.log").Single());
        logText.Should().Contain("[local path]");
        logText.Should().NotContain("Alice Example");
        logText.Should().NotContain("Bob Example");
        logText.Should().NotContain("fileserver");
    }

    [GeneratedRegex(@"^GenLauncherGO-\d{4}-\d{2}-\d{2}-\d{6}Z(-\d+)?\.log$")]
    private static partial Regex SessionLogFileNamePattern();
}
