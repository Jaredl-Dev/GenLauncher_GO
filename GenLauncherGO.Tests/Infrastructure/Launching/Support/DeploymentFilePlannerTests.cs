using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenLauncherGO.Infrastructure.Launching.Support;
using GenLauncherGO.Tests.Testing;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Support;

public sealed class DeploymentFilePlannerTests
{
    [Fact]
    public void ResolveDeploymentFilesExcludesExecutableCodeFromDownloadedPackages()
    {
        using TestDirectory directory = new();
        string packageRoot = directory.CreateDirectory("Package");
        string dataDirectory = Directory.CreateDirectory(Path.Combine(packageRoot, "Data")).FullName;
        File.WriteAllText(Path.Combine(dataDirectory, "payload.txt"), "data");
        File.WriteAllText(Path.Combine(packageRoot, "community-client.EXE"), "executable");
        File.WriteAllText(Path.Combine(packageRoot, "community-plugin.DlL"), "library");

        IReadOnlyList<ResolvedDeploymentFile> result = DeploymentFilePlanner.ResolveDeploymentFiles(
            new[] { new DeploymentPackage(packageRoot, precedence: 0) });

        result.Select(file => file.TargetRelativePath)
            .Should()
            .Equal("Data/payload.txt");
    }
}
