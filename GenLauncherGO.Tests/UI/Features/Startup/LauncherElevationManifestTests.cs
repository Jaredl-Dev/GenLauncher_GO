using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace GenLauncherGO.Tests.UI.Features.Startup;

public sealed class LauncherElevationManifestTests
{
    [Fact]
    public void UiProject_UsesManifestThatRequiresAdministrator()
    {
        string assetsDirectory = Path.Combine(AppContext.BaseDirectory, "StartupAssets");
        XDocument project = LoadDocument(Path.Combine(assetsDirectory, "GenLauncherGO.UI.csproj"));
        XDocument manifest = LoadDocument(Path.Combine(assetsDirectory, "app.manifest"));

        project.Descendants("ApplicationManifest")
            .Select(element => element.Value)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("app.manifest");

        XElement executionLevel = manifest
            .Descendants()
            .Single(element => element.Name.LocalName == "requestedExecutionLevel");
        executionLevel.Attributes("level")
            .Should()
            .ContainSingle()
            .Which.Value.Should()
            .Be("requireAdministrator");
        executionLevel.Attributes("uiAccess")
            .Should()
            .ContainSingle()
            .Which.Value.Should()
            .Be("false");
    }

    /// <summary>
    ///     Loads through a stream: <see cref="XDocument.Load(string)" /> routes the path through
    ///     <see cref="Uri" />, which rejects a path past MAX_PATH with an unrelated "hostname could not be
    ///     parsed" error. Checkouts sit at arbitrary depths.
    /// </summary>
    private static XDocument LoadDocument(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return XDocument.Load(stream);
    }
}
