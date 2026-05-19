using System;
using System.Globalization;
using System.IO;
using System.Linq;
using GenLauncherGO.Infrastructure.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace GenLauncherGO.Tests.Infrastructure.Logging;

/// <summary>
///     The formatter is the last step before a log event reaches a file a user attaches to a bug report, so each test
///     asserts both that the private value is gone and that the diagnostics around it survived.
/// </summary>
public sealed class SensitiveDataRedactingTextFormatterTests
{
    private static readonly MessageTemplateParser _messageTemplateParser = new();

    private static readonly DateTimeOffset _eventTimestamp =
        new(2026, 1, 2, 3, 4, 5, 678, TimeSpan.FromHours(2));

    /// <summary>
    ///     A user's folder names carry their real name and their installed software, so no absolute local path may
    ///     survive in any of the spellings Windows accepts.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\Alice Example\Secrets\file.txt", "Alice Example")]
    [InlineData("C:/Users/Alice Example/Secrets/file.txt", "Alice Example")]
    [InlineData(@"d:\Games\Alice Example\game.dat", "Alice Example")]
    [InlineData(@"\\fileserver\profiles\Alice Example\file.txt", "fileserver")]
    public void Format_AbsoluteLocalPath_ReplacesPathAndKeepsSurroundingMessage(
        string path,
        string privateSegment)
    {
        var formatter = new SensitiveDataRedactingTextFormatter();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        LogEvent logEvent = CreateLogEvent("Could not read {Path} for the active profile.", ("Path", path));

        formatter.Format(logEvent, output);

        string text = output.ToString();
        text.Should().Contain("[local path]");
        text.Should().Contain("for the active profile.");
        text.Should().NotContain(privateSegment);
    }

    /// <summary>
    ///     Redaction has to stop at absolute local paths: a message that carries none must reach the log intact, or the
    ///     log stops being useful for support.
    /// </summary>
    [Fact]
    public void Format_MessageWithoutLocalPath_WritesTheRenderedMessageUnchanged()
    {
        var formatter = new SensitiveDataRedactingTextFormatter();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        LogEvent logEvent = CreateLogEvent(
            "Installed {Count} packages from {RelativePath}.",
            ("Count", 3),
            ("RelativePath", "Data/INI/GameData.ini"));

        formatter.Format(logEvent, output);

        string text = output.ToString();
        text.Should().Contain("Installed 3 packages");
        text.Should().Contain("Data/INI/GameData.ini");
    }

    /// <summary>
    ///     A download URL is logged whenever a transfer fails, and its user-info segment is a live credential. The host
    ///     has to stay so the failing mirror is still identifiable.
    /// </summary>
    [Fact]
    public void Format_UriUserInfo_ReplacesCredentialsAndKeepsTheHost()
    {
        var formatter = new SensitiveDataRedactingTextFormatter();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        LogEvent logEvent = CreateLogEvent(
            "Download from {Uri} failed.",
            ("Uri", "https://alice:s3cret-value@packages.example.test/mod.zip"));

        formatter.Format(logEvent, output);

        string text = output.ToString();
        text.Should().Contain("https://[redacted]@packages.example.test/mod.zip");
        text.Should().NotContain("s3cret-value");
        text.Should().NotContain("alice");
    }

    /// <summary>
    ///     Presigned S3 links and OAuth callbacks put live credentials in the query string. The parameter name must
    ///     survive so a reader can see which credential was in play, and unrelated parameters must survive with it.
    /// </summary>
    [Theory]
    [InlineData("access_token")]
    [InlineData("access-token")]
    [InlineData("accesstoken")]
    [InlineData("api_key")]
    [InlineData("apikey")]
    [InlineData("credential")]
    [InlineData("secret")]
    [InlineData("token")]
    [InlineData("session_token")]
    [InlineData("security_token")]
    [InlineData("password")]
    [InlineData("signature")]
    [InlineData("sig")]
    [InlineData("X-Amz-Credential")]
    [InlineData("X-Amz-Signature")]
    [InlineData("X-Amz-Security-Token")]
    public void Format_SensitiveQueryParameter_ReplacesValueAndKeepsKeyAndSafeParameters(string parameterName)
    {
        var formatter = new SensitiveDataRedactingTextFormatter();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        LogEvent logEvent = CreateLogEvent(
            "Requested {Uri}.",
            ("Uri", $"https://packages.example.test/mod.zip?{parameterName}=s3cret-value&name=safe"));

        formatter.Format(logEvent, output);

        string text = output.ToString();
        text.Should().Contain($"{parameterName}=[redacted]");
        text.Should().Contain("name=safe");
        text.Should().NotContain("s3cret-value");
    }

    /// <summary>
    ///     An exception is the payload of most bug reports, so its type and message have to reach the log on their own
    ///     line even though the paths inside them do not.
    /// </summary>
    [Fact]
    public void Format_EventWithException_WritesRedactedExceptionBelowTheMessage()
    {
        var formatter = new SensitiveDataRedactingTextFormatter();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        LogEvent logEvent = CreateLogEvent(
            "Launch preparation failed.",
            new InvalidOperationException(@"Could not stage C:\Users\Alice Example\Deploy"));

        formatter.Format(logEvent, output);

        string[] lines = ReadLines(output);
        lines.Should().HaveCount(2);
        lines[0].Should().EndWith("Launch preparation failed.");
        lines[1].Should().Contain(nameof(InvalidOperationException));
        lines[1].Should().Contain("[local path]");
        lines[1].Should().NotContain("Alice Example");
    }

    [Fact]
    public void Format_EventWithoutException_WritesOnlyTheMessageLine()
    {
        var formatter = new SensitiveDataRedactingTextFormatter();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        LogEvent logEvent = CreateLogEvent("Launch preparation succeeded.");

        formatter.Format(logEvent, output);

        ReadLines(output).Should().ContainSingle()
            .Which.Should().EndWith("Launch preparation succeeded.");
    }

    /// <summary>
    ///     A stack frame names the source file on the machine that built the launcher, which is a private path like any
    ///     other. The line number is the part that makes the frame worth logging, so it has to outlive the path.
    /// </summary>
    [Fact]
    public void Format_StackFrameSourcePath_ReplacesPathAndKeepsLineNumber()
    {
        var formatter = new SensitiveDataRedactingTextFormatter();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        LogEvent logEvent = CreateLogEvent(
            @"   at GenLauncherGO.Launch() in C:\build\Alice Example\Launcher.cs:line 4711");

        formatter.Format(logEvent, output);

        string text = output.ToString();
        text.Should().Contain("[local source]:line 4711");
        text.Should().NotContain("Alice Example");
    }

    /// <summary>
    ///     Log files are read in bulk and filtered by level, so every level has to reach the file as its own marker.
    /// </summary>
    [Theory]
    [InlineData(LogEventLevel.Verbose, "VRB")]
    [InlineData(LogEventLevel.Debug, "DBG")]
    [InlineData(LogEventLevel.Information, "INF")]
    [InlineData(LogEventLevel.Warning, "WRN")]
    [InlineData(LogEventLevel.Error, "ERR")]
    [InlineData(LogEventLevel.Fatal, "FTL")]
    public void Format_EachLevel_WritesItsOwnLevelMarker(LogEventLevel level, string expectedMarker)
    {
        var formatter = new SensitiveDataRedactingTextFormatter();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        LogEvent logEvent = CreateLogEvent("Ready.", level);

        formatter.Format(logEvent, output);

        output.ToString().Should().Contain($"[{expectedMarker}] Ready.");
    }

    /// <summary>
    ///     Support reads these files beside Windows event logs, so each line has to carry a sortable timestamp whose
    ///     offset makes it comparable with events recorded in another time zone.
    /// </summary>
    [Fact]
    public void Format_Timestamp_WritesTheEventTimestampWithItsOffset()
    {
        var formatter = new SensitiveDataRedactingTextFormatter();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        LogEvent logEvent = CreateLogEvent("Ready.");

        formatter.Format(logEvent, output);

        string timestampText = ReadLines(output)[0].Split(" [", StringSplitOptions.None)[0];
        DateTimeOffset.ParseExact(
                timestampText,
                "yyyy-MM-dd HH:mm:ss.fff zzz",
                CultureInfo.InvariantCulture)
            .Should().Be(_eventTimestamp);
    }

    private static string[] ReadLines(StringWriter output)
    {
        return output.ToString().Split(
            Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries);
    }

    private static LogEvent CreateLogEvent(
        string messageTemplate,
        params (string Name, object? Value)[] properties)
    {
        return CreateLogEvent(messageTemplate, LogEventLevel.Information, null, properties);
    }

    private static LogEvent CreateLogEvent(string messageTemplate, LogEventLevel level)
    {
        return CreateLogEvent(messageTemplate, level, null);
    }

    private static LogEvent CreateLogEvent(string messageTemplate, Exception exception)
    {
        return CreateLogEvent(messageTemplate, LogEventLevel.Information, exception);
    }

    private static LogEvent CreateLogEvent(
        string messageTemplate,
        LogEventLevel level,
        Exception? exception,
        params (string Name, object? Value)[] properties)
    {
        return new LogEvent(
            _eventTimestamp,
            level,
            exception,
            _messageTemplateParser.Parse(messageTemplate),
            properties.Select(property => new LogEventProperty(property.Name, new ScalarValue(property.Value))));
    }
}
