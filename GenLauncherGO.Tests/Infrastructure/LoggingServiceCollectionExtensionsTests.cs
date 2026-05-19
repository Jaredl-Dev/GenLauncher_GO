using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GenLauncherGO.Infrastructure.Logging;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Tests.Infrastructure;

public sealed class LoggingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGenLauncherGoLoggingCreatesReadableSessionLog()
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
            Regex.IsMatch(
                Path.GetFileName(file),
                @"^GenLauncherGO-\d{4}-\d{2}-\d{2}-\d{6}Z(-\d+)?\.log$"));
    }

    [Fact]
    public void AddGenLauncherGoLoggingPrunesOldSessionLogs()
    {
        using TestDirectory directory = new();
        string logDirectory = directory.CreateDirectory("Logs");
        for (int index = 0; index < 20; index++)
        {
            string logFilePath = Path.Combine(
                logDirectory,
                $"GenLauncherGO-2026-01-{index + 1:00}-120000Z.log");
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

        Directory.GetFiles(logDirectory, "*.log").Should().HaveCountLessThanOrEqualTo(14);
        File.Exists(Path.Combine(logDirectory, "GenLauncherGO-2026-01-20-120000Z.log")).Should().BeFalse();
    }

    [Fact]
    public void AddGenLauncherGoLoggingRedactsLocalPathsAndSensitiveQueryValues()
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
    public void AddGenLauncherGoLoggingRedactsUncAndForwardSlashWindowsPaths()
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
}
