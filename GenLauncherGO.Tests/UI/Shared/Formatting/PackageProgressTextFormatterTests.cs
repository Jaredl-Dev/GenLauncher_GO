using System;
using System.Collections.Generic;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.UI.Shared.Formatting;

namespace GenLauncherGO.Tests.UI.Shared.Formatting;

public sealed class PackageProgressTextFormatterTests
{
    [Fact]
    public void TryFormat_ReturnsFalseWhenProgressHasNoPercentage()
    {
        PackageUpdateProgress progress = new(null, 0, null, null);

        bool formatted = PackageProgressTextFormatter.TryFormat(
            progress,
            new FakeStringLocalizer(),
            out string message,
            out int percentage);

        formatted.Should().BeFalse();
        message.Should().BeEmpty();
        percentage.Should().Be(0);
    }

    /// <summary>
    ///     The remaining time drops its hour segment below an hour, so both shapes are pinned together: an hour
    ///     exactly is the boundary where a bare minutes:seconds reading would understate the wait by an hour.
    /// </summary>
    [Theory]
    [InlineData(90, "01:30")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3723, "1:02:03")]
    public void TryFormat_FormatsBytesSpeedAndRemainingTime(int etaSeconds, string expectedEta)
    {
        PackageUpdateProgress progress = new(
            2_097_152,
            1_048_576,
            50,
            "package.big",
            1_048_576,
            TimeSpan.FromSeconds(etaSeconds));

        bool formatted = PackageProgressTextFormatter.TryFormat(
            progress,
            CreateLocalizer(),
            out string message,
            out int percentage);

        formatted.Should().BeTrue();
        message.Should().Be($"1 MB of 2 MB - 1 MB/s - Remaining: {expectedEta}");
        percentage.Should().Be(50);
    }

    /// <summary>
    ///     A download reports zero bytes per second until it has measured a rate, and "0 B/s" would read as a stalled
    ///     transfer rather than as one that has not been timed yet.
    /// </summary>
    [Fact]
    public void TryFormat_WithoutAMeasuredSpeed_OmitsTheSpeedSegment()
    {
        PackageUpdateProgress progress = new(
            2_097_152,
            1_048_576,
            50,
            "package.big",
            0,
            TimeSpan.FromSeconds(90));

        bool formatted = PackageProgressTextFormatter.TryFormat(
            progress,
            CreateLocalizer(),
            out string message,
            out int percentage);

        formatted.Should().BeTrue();
        message.Should().Be("1 MB of 2 MB - Remaining: 01:30");
        percentage.Should().Be(50);
    }

    [Fact]
    public void TryFormat_UsesPreparingTextAtOneHundredPercent()
    {
        PackageUpdateProgress progress = new(
            1_048_576,
            1_048_576,
            100,
            null,
            1_048_576,
            TimeSpan.Zero);
        FakeStringLocalizer localizer = new(new Dictionary<string, string>
        {
            ["DownloadInProgress"] = "{0}/{1}",
            ["UnpackingPreparing"] = "Preparing files"
        });

        bool formatted = PackageProgressTextFormatter.TryFormat(
            progress,
            localizer,
            out string message,
            out int percentage);

        formatted.Should().BeTrue();
        message.Should().Be("Preparing files");
        percentage.Should().Be(100);
    }

    private static FakeStringLocalizer CreateLocalizer()
    {
        return new FakeStringLocalizer(new Dictionary<string, string>
        {
            ["DownloadInProgress"] = "{0} of {1}",
            ["EstimatedTimeRemaining"] = "Remaining: {0}",
            ["UnpackingPreparing"] = "Preparing"
        });
    }
}
