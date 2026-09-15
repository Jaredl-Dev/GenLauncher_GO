using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Collects progress reports. Production reports from whatever thread the transfer is on, so the reports are
///     collected through a concurrent queue rather than a list.
/// </summary>
internal sealed class RecordingProgress<T> : IProgress<T>
{
    private readonly ConcurrentQueue<T> _reports = new();

    public IReadOnlyList<T> Reports => _reports.ToArray();

    public void Report(T value)
    {
        _reports.Enqueue(value);
    }
}
