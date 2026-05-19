using System;
using System.IO;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Tests.Core.Mods.Models;

public sealed class OwnedContentPathTests
{
    [Fact]
    public void Constructor_NormalizesOwnedChildAndExposesRelativePath()
    {
        string ownerRoot = TestLauncherPaths.CreateVirtualRoot("OwnedContentPath").ModsDirectory;
        string fullPath = Path.Combine(ownerRoot, "ShockWave", "..", "ShockWave", "1.2");

        var result = new OwnedContentPath(ownerRoot, fullPath);

        result.OwnerRoot.Should().Be(ownerRoot);
        result.FullPath.Should().Be(Path.Combine(ownerRoot, "ShockWave", "1.2"));
        result.RelativePath.Should().Be("ShockWave/1.2");
    }

    [Theory]
    [InlineData(".")]
    [InlineData(@"..\Outside")]
    public void Constructor_RejectsPathOutsideOwnershipBoundary(string relativeFragment)
    {
        string ownerRoot = TestLauncherPaths.CreateVirtualRoot("OwnershipBoundary").ModsDirectory;
        string fullPath = Path.Combine(ownerRoot, relativeFragment);

        Action act = () => new OwnedContentPath(ownerRoot, fullPath);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("fullPath");
    }
}
