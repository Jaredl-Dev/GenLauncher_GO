using System;
using System.IO;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Tests.Testing;

namespace GenLauncherGO.Tests.Infrastructure.Common;

public sealed class ManifestPathResolverTests
{
    [Theory]
    [InlineData("Data/INI/GameData.ini")]
    [InlineData(@"Data\INI\GameData.ini")]
    [InlineData(" Data/INI/GameData.ini ")]
    public void ResolvePathReturnsFullPathUnderRoot(string manifestFileName)
    {
        using TestDirectory directory = new();

        string result = ManifestPathResolver.ResolvePath(directory.Path, manifestFileName);

        result.Should().Be(Path.GetFullPath(Path.Combine(directory.Path, "Data", "INI", "GameData.ini")));
    }

    [Theory]
    [InlineData(@"C:\Package\Data.big")]
    [InlineData("C:Package/Data.big")]
    [InlineData("../Data.big")]
    [InlineData("./Data.big")]
    public void NormalizeRelativePathRejectsUnsafePaths(string manifestFileName)
    {
        Action act = () => ManifestPathResolver.NormalizeRelativePath(manifestFileName);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NormalizeForManifestIndexUsesSlashSeparators()
    {
        string result = ManifestPathResolver.NormalizeForManifestIndex(@"Data\INI\GameData.ini");

        result.Should().Be("Data/INI/GameData.ini");
    }
}
