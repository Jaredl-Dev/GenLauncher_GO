using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Serilog.Events;
using Serilog.Formatting;

namespace GenLauncherGO.Infrastructure.Logging;

/// <summary>
/// Formats log events while removing local paths and obvious secret-bearing URL values.
/// </summary>
internal sealed class SensitiveDataRedactingTextFormatter : ITextFormatter
{
    /// <summary>
    /// Replaces source-file paths emitted by exception stack traces.
    /// </summary>
    private static readonly Regex _stackTraceSourcePathPattern = new(
        @"\sin\s[A-Za-z]:\\[^\r\n]*:line\s(?<line>\d+)",
        RegexOptions.Compiled);

    /// <summary>
    /// Replaces UNC paths before drive-letter paths so adjacent path values cannot consume the UNC introducer.
    /// </summary>
    private static readonly Regex _uncWindowsPathPattern = new(
        @"\\\\[^\\/\r\n:*?""<>|]+[\\/][^\\/\r\n:*?""<>|]+(?:[\\/][^\\/\r\n:*?""<>|]*)*",
        RegexOptions.Compiled);

    /// <summary>
    /// Replaces absolute drive-letter paths using either Windows path separator.
    /// </summary>
    private static readonly Regex _absoluteDriveWindowsPathPattern = new(
        @"(?<![A-Za-z0-9])[A-Za-z]:[\\/](?:[^\\/\r\n:*?""<>|]+[\\/])*[^\\/\r\n:*?""<>|]*",
        RegexOptions.Compiled);

    /// <summary>
    /// Replaces URI user-info credentials.
    /// </summary>
    private static readonly Regex _uriUserInfoPattern = new(
        @"(?i)(?<scheme>\b[a-z][a-z0-9+.-]*://)[^/\s?#@]+@",
        RegexOptions.Compiled);

    /// <summary>
    /// Replaces common token, key, credential, secret, signature, and password query-string values.
    /// </summary>
    private static readonly Regex _sensitiveQueryValuePattern = new(
        @"(?i)(?<key>[?&](?:access[_-]?token|api[_-]?key|credential|secret|token|session[_-]?token|" +
        @"security[_-]?token|password|signature|sig|x-amz-(?:credential|signature|security-token))=)[^&\s]+",
        RegexOptions.Compiled);

    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        output.Write(logEvent.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
        output.Write(" [");
        output.Write(GetLevelAbbreviation(logEvent.Level));
        output.Write("] ");
        output.WriteLine(Redact(logEvent.RenderMessage(CultureInfo.InvariantCulture)));

        if (logEvent.Exception != null)
        {
            output.WriteLine(Redact(logEvent.Exception.ToString()));
        }
    }

    private static string GetLevelAbbreviation(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Verbose => "VRB",
            LogEventLevel.Debug => "DBG",
            LogEventLevel.Information => "INF",
            LogEventLevel.Warning => "WRN",
            LogEventLevel.Error => "ERR",
            LogEventLevel.Fatal => "FTL",
            _ => level.ToString().ToUpperInvariant(),
        };
    }

    private static string Redact(string value)
    {
        string redacted = _stackTraceSourcePathPattern.Replace(value, " in [local source]:line ${line}");
        redacted = _uriUserInfoPattern.Replace(redacted, "${scheme}[redacted]@");
        redacted = _sensitiveQueryValuePattern.Replace(redacted, "${key}[redacted]");
        redacted = _uncWindowsPathPattern.Replace(redacted, "[local path]");
        return _absoluteDriveWindowsPathPattern.Replace(redacted, "[local path]");
    }
}
