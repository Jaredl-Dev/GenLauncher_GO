using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Updating.Models;

namespace GenLauncherGO.Tests.Core.Launching.Models;

public sealed class LaunchContentIntegrityResolutionProgressTests
{
    [Fact]
    public void Package_HasProgressAndIsNotComplete()
    {
        PackageUpdateProgress packageProgress = new(null, 10, null, "package.zip");

        var progress =
            LaunchContentIntegrityResolutionProgress.Package("target", packageProgress);

        progress.PackageProgress.Should().BeSameAs(packageProgress);
        progress.Completed.Should().BeFalse();
    }

    [Fact]
    public void Complete_HasNoProgressAndIsComplete()
    {
        var progress =
            LaunchContentIntegrityResolutionProgress.Complete("target");

        progress.PackageProgress.Should().BeNull();
        progress.Completed.Should().BeTrue();
    }
}
