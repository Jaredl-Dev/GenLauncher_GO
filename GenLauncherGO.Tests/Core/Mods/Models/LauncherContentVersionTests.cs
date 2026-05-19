using System;
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
    public void VersionComparerPreservesLegacyNumericProjection(
        string left,
        string right,
        int expectedSign)
    {
        var leftVersion = new LauncherContentVersion { Version = left };
        var rightVersion = new LauncherContentVersion { Version = right };

        int comparison = leftVersion.CompareTo(rightVersion);

        Math.Sign(comparison).Should().Be(expectedSign);
    }

    [Fact]
    public void CompareToHandlesVeryLargeDigitSequencesWithoutOverflow()
    {
        var older = new LauncherContentVersion { Version = new string('8', 1_000) };
        var newer = new LauncherContentVersion { Version = new string('9', 1_000) };

        int comparison = older.CompareTo(newer);

        comparison.Should().BeNegative();
    }

    [Theory]
    [InlineData("https://s3.example.test", "mods", "ShockWave/1.2", "", ContentSourceKind.UnknownLegacy, ContentSourceKind.ManagedS3)]
    [InlineData("", "", "", "https://example.test/package.zip", ContentSourceKind.UnknownLegacy, ContentSourceKind.ManagedSingleFile)]
    [InlineData("", "", "", "", ContentSourceKind.Manual, ContentSourceKind.Manual)]
    public void ResolveContentSourceKindUsesPackageMetadataPrecedence(
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
