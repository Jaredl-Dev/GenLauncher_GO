using System;
using System.IO;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Tests.Core.Mods.Models;

public sealed class OwnedContentPathTests
{
    [Fact]
    public void ConstructorNormalizesOwnedChildAndExposesRelativePath()
    {
        string ownerRoot = Path.GetFullPath(Path.Combine("GenLauncherGO.Tests", "Mods"));
        string fullPath = Path.Combine(ownerRoot, "ShockWave", "..", "ShockWave", "1.2");

        var result = new OwnedContentPath(ownerRoot, fullPath);

        result.OwnerRoot.Should().Be(Path.TrimEndingDirectorySeparator(Path.GetFullPath(ownerRoot)));
        result.FullPath.Should().Be(Path.GetFullPath(Path.Combine(ownerRoot, "ShockWave", "1.2")));
        result.RelativePath.Should().Be("ShockWave/1.2");
    }

    [Theory]
    [InlineData("same")]
    [InlineData("outside")]
    public void ConstructorRejectsPathOutsideOwnershipBoundary(string scenario)
    {
        string ownerRoot = Path.GetFullPath(Path.Combine("GenLauncherGO.Tests", "OwnershipBoundary"));
        string fullPath = scenario == "same"
            ? ownerRoot
            : Path.Combine(ownerRoot, "..", "Outside");

        Action act = () => new OwnedContentPath(ownerRoot, fullPath);

        act.Should().Throw<ArgumentException>();
    }
}
