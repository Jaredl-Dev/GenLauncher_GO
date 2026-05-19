using System;
using System.Collections.Generic;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Tests.Testing;
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
            new TestStringLocalizer(),
            out string message,
            out int percentage);

        formatted.Should().BeFalse();
        message.Should().BeEmpty();
        percentage.Should().Be(0);
    }

    [Fact]
    public void TryFormat_FormatsBytesSpeedAndShortEta()
    {
        PackageUpdateProgress progress = new(
            2_097_152,
            1_048_576,
            50,
            "package.big",
            1_048_576,
            TimeSpan.FromSeconds(90));
        TestStringLocalizer localizer = new(new Dictionary<string, string>
        {
            ["DownloadInProgress"] = "{0} of {1}",
            ["EstimatedTimeRemaining"] = "Remaining: {0}",
            ["UnpackingPreparing"] = "Preparing",
        });

        bool formatted = PackageProgressTextFormatter.TryFormat(
            progress,
            localizer,
            out string message,
            out int percentage);

        formatted.Should().BeTrue();
        message.Should().Be("1 MB of 2 MB - 1 MB/s - Remaining: 01:30");
        percentage.Should().Be(50);
    }

    [Fact]
    public void TryFormat_FormatsLongEtaWithHours()
    {
        PackageUpdateProgress progress = new(
            4_194_304,
            1_048_576,
            25,
            null,
            null,
            new TimeSpan(1, 2, 3));
        TestStringLocalizer localizer = new(new Dictionary<string, string>
        {
            ["DownloadInProgress"] = "{0}/{1}",
            ["EstimatedTimeRemaining"] = "Remaining: {0}",
            ["UnpackingPreparing"] = "Preparing",
        });

        bool formatted = PackageProgressTextFormatter.TryFormat(
            progress,
            localizer,
            out string message,
            out int percentage);

        formatted.Should().BeTrue();
        message.Should().Be("1 MB/4 MB - Remaining: 1:02:03");
        percentage.Should().Be(25);
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
        TestStringLocalizer localizer = new(new Dictionary<string, string>
        {
            ["DownloadInProgress"] = "{0}/{1}",
            ["UnpackingPreparing"] = "Preparing files",
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
}
