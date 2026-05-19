using System;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Support;

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

    /// <summary>
    ///     Throttling must never swallow the terminal report: the transfer reaching the package total is published even
    ///     though the caller did not force it and the report interval has not elapsed.
    /// </summary>
    [Fact]
    public void Update_ThrottlesRepeatedReportsUntilComplete()
    {
        ManualTimeProvider timeProvider = new();
        PackageProgressTracker tracker = new(100, timeProvider: timeProvider);

        PackageUpdateProgress? firstProgress = tracker.Update("file-a", 10);
        PackageUpdateProgress? throttledProgress = tracker.Update("file-a", 20);
        PackageUpdateProgress? completedProgress = tracker.Update("file-a", 100);

        firstProgress.Should().NotBeNull();
        throttledProgress.Should().BeNull();
        completedProgress.Should().NotBeNull();
        completedProgress!.ProgressPercentage.Should().Be(100);
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
        PackageProgressTracker tracker = new(200, timeProvider: timeProvider);

        timeProvider.Advance(TimeSpan.FromMilliseconds(300));
        PackageUpdateProgress? progress = tracker.Update("file-a", 50, true);

        progress.Should().NotBeNull();
        progress!.DownloadSpeedBytesPerSecond.Should().BeGreaterThan(0);
        progress.EstimatedTimeRemaining.Should().NotBeNull();
    }

    /// <summary>
    ///     A resumed transfer reports against the whole package, so the bar continues from where it stopped and the
    ///     total stays the package's real size rather than shrinking to whatever is left.
    /// </summary>
    [Fact]
    public void ResumedBytes_CountTowardsProgressImmediately()
    {
        PackageProgressTracker tracker = new(1000, 400);

        PackageUpdateProgress? progress = tracker.Update("file-a", 100, true);

        progress.Should().NotBeNull();
        progress!.TotalBytes.Should().Be(1000);
        progress.BytesRead.Should().Be(500);
        progress.ProgressPercentage.Should().Be(50);
    }

    /// <summary>
    ///     Bytes that were already on disk were not moved by this session, so crediting them to the transfer rate
    ///     would report a speed and an estimate the connection is not actually achieving.
    /// </summary>
    [Fact]
    public void ReportedTransferRate_ResumedBytes_ExcludesPreviouslyDownloadedBytes()
    {
        ManualTimeProvider timeProvider = new();
        PackageProgressTracker tracker = new(1000, 400, timeProvider);

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        PackageUpdateProgress? progress = tracker.Update("file-a", 100, true);

        progress.Should().NotBeNull();
        progress!.DownloadSpeedBytesPerSecond.Should().Be(50);
    }

    /// <summary>
    ///     Throttling is measured from the previous published report, so the first update after the interval has passed
    ///     reaches the UI instead of waiting for the one after it.
    /// </summary>
    [Fact]
    public void Update_OnTheReportInterval_PublishesTheReport()
    {
        ManualTimeProvider timeProvider = new();
        PackageProgressTracker tracker = new(1000, timeProvider: timeProvider);
        tracker.Update("file-a", 100);

        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        PackageUpdateProgress? progress = tracker.Update("file-a", 200);

        progress.Should().NotBeNull();
    }

    /// <summary>
    ///     A transfer that has been running for a while still throttles: the interval is measured from the last report,
    ///     not from the start, so a chatty downloader cannot flood the UI later in the transfer.
    /// </summary>
    [Fact]
    public void Update_WithinTheReportIntervalOfTheLastReport_PublishesNothing()
    {
        ManualTimeProvider timeProvider = new();
        PackageProgressTracker tracker = new(1000, timeProvider: timeProvider);
        timeProvider.Advance(TimeSpan.FromMilliseconds(200));
        tracker.Update("file-a", 100);

        timeProvider.Advance(TimeSpan.FromMilliseconds(50));
        PackageUpdateProgress? progress = tracker.Update("file-a", 150);

        progress.Should().BeNull();
    }

    /// <summary>
    ///     An empty manifest gives a package size of zero, which has no percentage to show; dividing by it would put a
    ///     <see cref="double.NaN" /> on the progress bar.
    /// </summary>
    [Fact]
    public void Update_EmptyPackage_ReportsNoPercentage()
    {
        PackageProgressTracker tracker = new(0);

        PackageUpdateProgress? progress = tracker.Update("file-a", 0, true);

        progress.Should().NotBeNull();
        progress!.ProgressPercentage.Should().BeNull();
    }

    /// <summary>
    ///     A failed hash costs a retry that enlarges the expected total, so the same transferred bytes are suddenly a
    ///     smaller share of the package. The bar holds its position rather than jumping backwards.
    /// </summary>
    [Fact]
    public void Update_ExpectedTotalGrowsAfterAReport_HoldsTheReportedPercentage()
    {
        PackageProgressTracker tracker = new(100);
        tracker.Update("file-a", 80, true);
        tracker.AddExpectedBytes(100);

        PackageUpdateProgress? progress = tracker.Update("file-a", 80, true);

        progress.Should().NotBeNull();
        progress!.ProgressPercentage.Should().Be(80);
    }

    /// <summary>
    ///     A rate measured over a quarter second or less is noise rather than a transfer speed, so none is reported
    ///     until the sampling window has actually passed.
    /// </summary>
    [Fact]
    public void Update_OnTheRateSamplingWindow_ReportsNoTransferRate()
    {
        ManualTimeProvider timeProvider = new();
        PackageProgressTracker tracker = new(1000, timeProvider: timeProvider);

        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        PackageUpdateProgress? progress = tracker.Update("file-a", 100, true);

        progress.Should().NotBeNull();
        progress!.DownloadSpeedBytesPerSecond.Should().BeNull();
    }

    /// <summary>
    ///     A resumed transfer that has not yet received a new byte has no measured rate at all, which is not the same
    ///     claim as a measured rate of zero.
    /// </summary>
    [Fact]
    public void Update_NoBytesTransferredSinceResuming_ReportsNoTransferRate()
    {
        ManualTimeProvider timeProvider = new();
        PackageProgressTracker tracker = new(1000, 400, timeProvider);

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        PackageUpdateProgress? progress = tracker.Update("file-a", 0, true);

        progress.Should().NotBeNull();
        progress!.DownloadSpeedBytesPerSecond.Should().BeNull();
    }

    /// <summary>
    ///     A package whose size the provider never declared still has a measurable transfer rate, but nothing to
    ///     subtract it from, so no time estimate can be offered.
    /// </summary>
    [Fact]
    public void Update_UnknownPackageSize_ReportsATransferRateButNoEstimate()
    {
        ManualTimeProvider timeProvider = new();
        PackageProgressTracker tracker = new(null, timeProvider: timeProvider);

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        PackageUpdateProgress? progress = tracker.Update("file-a", 500, true);

        progress.Should().NotBeNull();
        progress!.DownloadSpeedBytesPerSecond.Should().Be(250);
        progress.EstimatedTimeRemaining.Should().BeNull();
    }

    /// <summary>
    ///     The estimate the panel counts down is the bytes still outstanding divided by the rate measured so far.
    /// </summary>
    [Fact]
    public void Update_EstimatesTheTimeRemainingFromTheOutstandingBytesAndMeasuredRate()
    {
        ManualTimeProvider timeProvider = new();
        PackageProgressTracker tracker = new(1000, timeProvider: timeProvider);

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        PackageUpdateProgress? progress = tracker.Update("file-a", 500, true);

        progress.Should().NotBeNull();
        progress!.EstimatedTimeRemaining.Should().Be(TimeSpan.FromSeconds(2));
    }
}
