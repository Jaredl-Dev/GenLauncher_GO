using System;
using System.Threading;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     A clock that only moves when a test advances it.
/// </summary>
/// <remarks>
///     Serves both the elapsed-time readings taken through <see cref="TimeProvider.GetTimestamp" /> and the wall-clock
///     readings taken through <see cref="GetUtcNow" />, so one fake covers every production clock seam.
/// </remarks>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _startUtc;

    private long _timestamp;

    public ManualTimeProvider()
        : this(new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero))
    {
    }

    public ManualTimeProvider(DateTimeOffset startUtc)
    {
        _startUtc = startUtc;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        return Interlocked.Read(ref _timestamp);
    }

    public override DateTimeOffset GetUtcNow()
    {
        return _startUtc.AddTicks(Interlocked.Read(ref _timestamp));
    }

    public void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);

        Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }
}
