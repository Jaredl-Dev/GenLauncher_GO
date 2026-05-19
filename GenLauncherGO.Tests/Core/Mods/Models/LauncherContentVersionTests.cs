using System;
using System.Collections.Generic;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Tests.Core.Mods.Models;

public sealed class LauncherContentVersionTests
{
    [Theory]
    [InlineData("", "", 0)]
    [InlineData("", "release", 0)]
    [InlineData("release", "beta", 0)]
    [InlineData("0", "release", 1)]
    [InlineData("1", "2", -1)]
    [InlineData("1.2", "1.20", 0)]
    [InlineData("1.2-beta", "1.2", 0)]
    [InlineData("1.2-rc1", "1.2-rc2", -1)]
    [InlineData("009", "1.2", -1)]
    public void VersionComparer_PreservesLegacyNumericProjection(
        string left,
        string right,
        int expectedSign)
    {
        var leftVersion = new LauncherContentVersion { Version = left };
        var rightVersion = new LauncherContentVersion { Version = right };

        int comparison = leftVersion.CompareTo(rightVersion);

        Math.Sign(comparison).Should().Be(expectedSign);
    }

    /// <summary>
    ///     The comparer is declared as <see cref="IComparer{T}" /> over a nullable string, so a missing label is
    ///     part of the contract it offers rather than an impossible input: it has to order against one the same way
    ///     it orders a label carrying no digits, instead of throwing part-way through sorting a card's versions.
    /// </summary>
    [Fact]
    public void VersionComparer_WithAMissingLabel_OrdersItAsCarryingNoDigits()
    {
        IComparer<string?> comparer = LauncherContentVersionComparer.Instance;

        comparer.Compare(null, "1.2").Should().BeNegative();
        comparer.Compare("1.2", null).Should().BePositive();
        comparer.Compare(null, null).Should().Be(0);
        comparer.Compare(null, "release").Should().Be(0);
    }

    [Fact]
    public void CompareTo_HandlesVeryLargeDigitSequencesWithoutOverflow()
    {
        var older = new LauncherContentVersion { Version = new string('8', 1_000) };
        var newer = new LauncherContentVersion { Version = new string('9', 1_000) };

        int comparison = older.CompareTo(newer);

        comparison.Should().BeNegative();
    }

    [Theory]
    [InlineData("https://s3.example.test", "mods", "ShockWave/1.2", "", ContentSourceKind.UnknownLegacy,
        ContentSourceKind.ManagedS3)]
    [InlineData("", "", "", "https://example.test/package.zip", ContentSourceKind.UnknownLegacy,
        ContentSourceKind.ManagedSingleFile)]
    [InlineData("", "", "", "", ContentSourceKind.Manual, ContentSourceKind.Manual)]
    public void ResolveContentSourceKind_UsesPackageMetadataPrecedence(
        string s3HostLink,
        string s3BucketName,
        string s3FolderName,
        string simpleDownloadLink,
        ContentSourceKind fallbackSourceKind,
        ContentSourceKind expectedSourceKind)
    {
        ContentSourceKind sourceKind = LauncherContentVersion.ResolveContentSourceKind(
            s3HostLink,
            s3BucketName,
            s3FolderName,
            simpleDownloadLink,
            fallbackSourceKind);

        sourceKind.Should().Be(expectedSourceKind);
    }
}
