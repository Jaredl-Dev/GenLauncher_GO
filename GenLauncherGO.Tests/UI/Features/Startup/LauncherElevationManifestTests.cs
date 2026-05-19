using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace GenLauncherGO.Tests.UI.Features.Startup;

public sealed class LauncherElevationManifestTests
{
    [Fact]
    public void UiProjectUsesManifestThatRequiresAdministrator()
    {
        string assetsDirectory = Path.Combine(AppContext.BaseDirectory, "StartupAssets");
        var project = XDocument.Load(Path.Combine(assetsDirectory, "GenLauncherGO.UI.csproj"));
        var manifest = XDocument.Load(Path.Combine(assetsDirectory, "app.manifest"));

        project.Descendants("ApplicationManifest")
            .Select(element => element.Value)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("app.manifest");

        XElement executionLevel = manifest
            .Descendants()
            .Single(element => element.Name.LocalName == "requestedExecutionLevel");
        executionLevel.Attribute("level")?.Value.Should().Be("requireAdministrator");
        executionLevel.Attribute("uiAccess")?.Value.Should().Be("false");
    }
}
