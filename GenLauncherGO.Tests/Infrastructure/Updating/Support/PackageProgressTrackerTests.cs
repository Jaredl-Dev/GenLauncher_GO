using System;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Support;
using GenLauncherGO.Tests.Testing;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Support;

public sealed class PackageProgressTrackerTests
{
    [Fact]
    public void Update_ReturnsAggregateProgressForKnownTotal()
    {
        PackageProgressTracker tracker = new(200);

        PackageUpdateProgress? progress = tracker.Update("file-a", 50);

        progress.Should().NotBeNull();
        progress!.TotalBytes.Should().Be(200);
        progress.BytesRead.Should().Be(50);
        progress.ProgressPercentage.Should().Be(25);
    }

    [Fact]
    public void Update_DoesNotRegressWhenAnItemReportsFewerBytes()
    {
        PackageProgressTracker tracker = new(100);

        tracker.Update("file-a", 80);
        PackageUpdateProgress? progress = tracker.Update("file-a", -5, true);

        progress.Should().NotBeNull();
        progress!.BytesRead.Should().Be(80);
        progress.ProgressPercentage.Should().Be(80);
    }

    [Fact]
    public void Update_ClampsProgressAtOneHundredPercent()
    {
        PackageProgressTracker tracker = new(100);

        PackageUpdateProgress? progress = tracker.Update("file-a", 150, true);

        progress.Should().NotBeNull();
        progress!.BytesRead.Should().Be(150);
        progress.ProgressPercentage.Should().Be(100);
    }

    [Fact]
    public void Update_ThrottlesRepeatedReportsUntilForced()
    {
        PackageProgressTracker tracker = new(100);

        tracker.Update("file-a", 10);
        PackageUpdateProgress? throttledProgress = tracker.Update("file-a", 20);
        PackageUpdateProgress? forcedProgress = tracker.Update("file-a", 20, true);

        throttledProgress.Should().BeNull();
        forcedProgress.Should().NotBeNull();
    }

    [Fact]
    public void AddExpectedBytes_IncreasesKnownTotal()
    {
        PackageProgressTracker tracker = new(100);

        tracker.AddExpectedBytes(50);
        PackageUpdateProgress? progress = tracker.Update("file-a", 75);

        progress.Should().NotBeNull();
        progress!.TotalBytes.Should().Be(150);
        progress.ProgressPercentage.Should().Be(50);
    }

    [Fact]
    public void AddExpectedBytes_IgnoresNonPositiveValues()
    {
        PackageProgressTracker tracker = new(100);

        tracker.AddExpectedBytes(0);
        tracker.AddExpectedBytes(-1);
        PackageUpdateProgress? progress = tracker.Update("file-a", 50);

        progress.Should().NotBeNull();
        progress!.TotalBytes.Should().Be(100);
    }

    [Fact]
    public void Update_ReportsSpeedAndEtaAfterEnoughElapsedTime()
    {
        ManualTimeProvider timeProvider = new();
        PackageProgressTracker tracker = new(200, timeProvider);

        timeProvider.Advance(TimeSpan.FromMilliseconds(300));
        PackageUpdateProgress? progress = tracker.Update("file-a", 50, true);

        progress.Should().NotBeNull();
        progress!.DownloadSpeedBytesPerSecond.Should().BeGreaterThan(0);
        progress.EstimatedTimeRemaining.Should().NotBeNull();
    }
}
