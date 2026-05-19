using System;
using System.IO;
using GenLauncherGO.Infrastructure.Common;

namespace GenLauncherGO.Tests.Infrastructure.Common;

public sealed class ManifestPathResolverTests
{
    [Theory]
    [InlineData("Data/INI/GameData.ini")]
    [InlineData(@"Data\INI\GameData.ini")]
    [InlineData(" Data/INI/GameData.ini ")]
    public void ResolvePath_ReturnsFullPathUnderRoot(string manifestFileName)
    {
        using TestDirectory directory = new();

        string result = ManifestPathResolver.ResolvePath(directory.Path, manifestFileName);

        result.Should().Be(Path.GetFullPath(Path.Combine(directory.Path, "Data", "INI", "GameData.ini")));
    }

    /// <summary>
    ///     Manifest file names arrive from a remote catalog, and a rooted one resolves against the volume rather than
    ///     the package folder. A leading separator roots a path just as a drive letter does, so both spellings have to
    ///     be refused.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Package\Data.big")]
    [InlineData("C:Package/Data.big")]
    [InlineData("/Package/Data.big")]
    [InlineData(@"\Package\Data.big")]
    [InlineData("../Data.big")]
    [InlineData("./Data.big")]
    public void NormalizeRelativePath_RejectsUnsafePaths(string manifestFileName)
    {
        Action act = () => ManifestPathResolver.NormalizeRelativePath(manifestFileName);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NormalizeForManifestIndex_UsesSlashSeparators()
    {
        string result = ManifestPathResolver.NormalizeForManifestIndex(@"Data\INI\GameData.ini");

        result.Should().Be("Data/INI/GameData.ini");
    }
}
