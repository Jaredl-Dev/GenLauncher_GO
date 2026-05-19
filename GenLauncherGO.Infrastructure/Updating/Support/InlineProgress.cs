using System;

namespace GenLauncherGO.Infrastructure.Updating.Support;

/// <summary>
/// Invokes an internal progress callback inline so provider aggregation completes before the owning operation.
/// </summary>
internal sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> _report;

    public InlineProgress(Action<T> report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
    }

    public void Report(T value)
    {
        _report(value);
    }
}
