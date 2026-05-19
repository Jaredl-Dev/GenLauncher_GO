using System;
using System.Globalization;
using System.IO;
using GenLauncherGO.Core.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace GenLauncherGO.Infrastructure.Logging;

public static class LoggingServiceCollectionExtensions
{
    private const int RetainedLogFileCount = 14;

    private const string LogFilePrefix = "GenLauncherGO";

    /// <summary>
    /// Registers the standard GenLauncherGO logging pipeline with rolling file logs.
    /// </summary>
    public static IServiceCollection AddGenLauncherGoLogging(
        this IServiceCollection services,
        string logDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);

        Directory.CreateDirectory(logDirectory);

        string logFilePath = CreateLogFilePath(logDirectory);
        PruneOldLogFiles(logDirectory, logFilePath);
        Serilog.ILogger logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                new SensitiveDataRedactingTextFormatter(),
                logFilePath,
                shared: false)
            .CreateLogger();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(logger, dispose: true);
        });

        return services;
    }

    private static string CreateLogFilePath(string logDirectory)
    {
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd-HHmmss'Z'", CultureInfo.InvariantCulture);
        string baseLogFileName = $"{LogFilePrefix}-{timestamp}";
        string logFilePath = Path.Combine(logDirectory, baseLogFileName + ".log");
        int collisionIndex = 2;
        while (File.Exists(logFilePath))
        {
            logFilePath = Path.Combine(logDirectory, $"{baseLogFileName}-{collisionIndex}.log");
            collisionIndex++;
        }

        return logFilePath;
    }

    private static void PruneOldLogFiles(string logDirectory, string activeLogFilePath)
    {
        FileInfo[] logFiles = new DirectoryInfo(logDirectory).GetFiles($"{LogFilePrefix}-*.log");
        Array.Sort(logFiles, (left, right) => right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc));

        string activePath = LexicalPath.NormalizeFullPath(activeLogFilePath);
        int retainedCount = 1;
        foreach (FileInfo logFile in logFiles)
        {
            if (string.Equals(
                    LexicalPath.NormalizeFullPath(logFile.FullName),
                    activePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (retainedCount < RetainedLogFileCount)
            {
                retainedCount++;
                continue;
            }

            logFile.Delete();
        }
    }
}
