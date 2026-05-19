using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Tests.Testing;

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<RecordingLogEntry> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new RecordingLogEntry(logLevel, formatter(state, exception), exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

internal sealed record RecordingLogEntry(
    LogLevel LogLevel,
    string Message,
    Exception? Exception);
