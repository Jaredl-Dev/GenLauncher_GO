using System;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Support;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Support;

/// <summary>
///     Concurrent S3 file reporters publish their own view of one package transfer, so the reports arriving at the UI
///     are only usable once they are normalized into a progress value that never regresses.
/// </summary>
public sealed class MonotonicPackageProgressTests
{
    [Fact]
    public void Report_LateSmallerByteCount_KeepsTheFurthestReportedPosition()
    {
        RecordingProgress<PackageUpdateProgress> inner = new();
        MonotonicPackageProgress progress = new(inner);
        progress.Report(new PackageUpdateProgress(500, 300, null, "first.big"));

        progress.Report(new PackageUpdateProgress(500, 120, null, "second.big"));

        inner.Reports[^1].BytesRead.Should().Be(300);
    }

    [Fact]
    public void Report_NegativeByteCount_ReportsNoTransferredBytes()
    {
        RecordingProgress<PackageUpdateProgress> inner = new();
        MonotonicPackageProgress progress = new(inner);

        progress.Report(new PackageUpdateProgress(null, -5, null, null));

        inner.Reports.Should().ContainSingle().Which.BytesRead.Should().Be(0);
    }

    [Fact]
    public void Report_ShrinkingPackageSize_KeepsTheLargestKnownPackageSize()
    {
        RecordingProgress<PackageUpdateProgress> inner = new();
        MonotonicPackageProgress progress = new(inner);
        progress.Report(new PackageUpdateProgress(500, 100, null, null));

        progress.Report(new PackageUpdateProgress(200, 100, null, null));

        inner.Reports[^1].TotalBytes.Should().Be(500);
    }

    /// <summary>
    ///     A reporter that only knows its own byte count must not erase the package size an earlier reporter
    ///     established, or the bar loses its scale mid-transfer.
    /// </summary>
    [Fact]
    public void Report_PackageSizeOmitted_KeepsThePreviouslyReportedPackageSize()
    {
        RecordingProgress<PackageUpdateProgress> inner = new();
        MonotonicPackageProgress progress = new(inner);
        progress.Report(new PackageUpdateProgress(200, 50, null, null));

        progress.Report(new PackageUpdateProgress(null, 100, null, null));

        inner.Reports[^1].TotalBytes.Should().Be(200);
    }

    [Fact]
    public void Report_NegativePackageSize_ReportsNoKnownPackageSize()
    {
        RecordingProgress<PackageUpdateProgress> inner = new();
        MonotonicPackageProgress progress = new(inner);

        progress.Report(new PackageUpdateProgress(-500, 0, null, null));

        inner.Reports.Should().ContainSingle().Which.TotalBytes.Should().Be(0);
    }

    /// <summary>
    ///     A transfer that overruns its declared size is a stale size, not 400% progress, so the size grows to what has
    ///     actually arrived.
    /// </summary>
    [Fact]
    public void Report_ByteCountBeyondPackageSize_GrowsThePackageSizeToMatch()
    {
        RecordingProgress<PackageUpdateProgress> inner = new();
        MonotonicPackageProgress progress = new(inner);

        progress.Report(new PackageUpdateProgress(100, 400, null, null));

        inner.Reports.Should().ContainSingle().Which.TotalBytes.Should().Be(400);
    }

    [Fact]
    public void Report_PercentageOmitted_DerivesItFromTheNormalizedByteCounts()
    {
        RecordingProgress<PackageUpdateProgress> inner = new();
        MonotonicPackageProgress progress = new(inner);

        progress.Report(new PackageUpdateProgress(200, 50, null, null));

        inner.Reports.Should().ContainSingle().Which.ProgressPercentage.Should().Be(25D);
    }

    /// <summary>
    ///     An unknown package size cannot be turned into a percentage, and a bar told "0 of 0" would otherwise be asked
    ///     to render a division by zero.
    /// </summary>
    [Fact]
    public void Report_EmptyPackageSize_ReportsNoPercentage()
    {
        RecordingProgress<PackageUpdateProgress> inner = new();
        MonotonicPackageProgress progress = new(inner);

        progress.Report(new PackageUpdateProgress(0, 0, null, null));

        inner.Reports.Should().ContainSingle().Which.ProgressPercentage.Should().BeNull();
    }

    /// <summary>
    ///     A provider that publishes its own percentage is the authority on it: an S3 package reports one aggregate
    ///     percentage across concurrent files, which no single byte ratio reproduces.
    /// </summary>
    [Fact]
    public void Report_PercentageProvided_ForwardsItInsteadOfTheByteRatio()
    {
        RecordingProgress<PackageUpdateProgress> inner = new();
        MonotonicPackageProgress progress = new(inner);

        progress.Report(new PackageUpdateProgress(200, 50, 90D, null));

        inner.Reports.Should().ContainSingle().Which.ProgressPercentage.Should().Be(90D);
    }

    [Fact]
    public void Report_LowerPercentage_KeepsTheHighestReportedPercentage()
    {
        RecordingProgress<PackageUpdateProgress> inner = new();
        MonotonicPackageProgress progress = new(inner);
        progress.Report(new PackageUpdateProgress(200, 100, 50D, null));

        progress.Report(new PackageUpdateProgress(200, 100, 10D, null));

        inner.Reports[^1].ProgressPercentage.Should().Be(50D);
    }

    [Theory]
    [InlineData(5000D, 100D)]
    [InlineData(-20D, 0D)]
    public void Report_PercentageOutsideItsRange_ClampsItToTheBar(
        double reportedPercentage,
        double expectedPercentage)
    {
        RecordingProgress<PackageUpdateProgress> inner = new();
        MonotonicPackageProgress progress = new(inner);

        progress.Report(new PackageUpdateProgress(200, 0, reportedPercentage, null));

        inner.Reports.Should().ContainSingle().Which.ProgressPercentage.Should().Be(expectedPercentage);
    }

    /// <summary>
    ///     Only the three regression-prone values are normalized; the rest of the report is what the transfer panel
    ///     shows and must arrive unchanged.
    /// </summary>
    [Fact]
    public void Report_ForwardsTheReportedFileNameSpeedAndEstimate()
    {
        RecordingProgress<PackageUpdateProgress> inner = new();
        MonotonicPackageProgress progress = new(inner);

        progress.Report(new PackageUpdateProgress(
            200,
            50,
            null,
            "Data/english.big",
            2048D,
            TimeSpan.FromSeconds(3)));

        PackageUpdateProgress report = inner.Reports.Should().ContainSingle().Which;
        report.FileName.Should().Be("Data/english.big");
        report.DownloadSpeedBytesPerSecond.Should().Be(2048D);
        report.EstimatedTimeRemaining.Should().Be(TimeSpan.FromSeconds(3));
    }
}
