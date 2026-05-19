using System;
using System.Threading;

namespace GenLauncherGO.Tests.Testing;

internal sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        return Interlocked.Read(ref _timestamp);
    }

    public void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);

        Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }
}
