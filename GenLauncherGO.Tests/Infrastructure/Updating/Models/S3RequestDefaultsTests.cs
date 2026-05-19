using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Support;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Models;

public sealed class S3RequestDefaultsTests
{
    [Fact]
    public void CreateManifestRequest_DefaultsToNonSslForLegacyCatalogEndpoints()
    {
        LauncherContentVersion version = new()
        {
            S3HostLink = "gen.insave.ovh:9000",
            S3BucketName = "mods",
            S3FolderName = "folder"
        };

        S3ObjectManifestRequest request = S3CatalogDefaults.CreateManifestRequest(version);

        request.UseSsl.Should().BeFalse();
    }

    /// <summary>
    ///     The keys are written out rather than read back from <see cref="S3CatalogDefaults" /> on purpose. Every
    ///     catalog entry that predates this fork resolves through these exact values, so they are a backend
    ///     compatibility contract, and an expectation that asked production for its own constant would follow a
    ///     changed key instead of reporting it. They are the legacy public credentials the original client already
    ///     shipped, not application secrets.
    /// </summary>
    [Fact]
    public void CreateManifestRequest_UsesPublicCatalogKeysWhenMetadataKeysAreMissing()
    {
        LauncherContentVersion version = new()
        {
            S3HostLink = "gen.insave.ovh:9000",
            S3BucketName = "mods",
            S3FolderName = "folder"
        };

        S3ObjectManifestRequest request = S3CatalogDefaults.CreateManifestRequest(version);

        request.AccessKey.Should().Be("S58TYR9ISEZV8PBP8QG1");
        request.SecretKey.Should().Be("b2RU1oqVU5toJRnb4gODrXX8sBSgoLcHRX6qPWxj");
    }

    [Fact]
    public void CreateManifestRequest_PreservesExplicitMetadataKeys()
    {
        LauncherContentVersion version = new()
        {
            S3HostLink = "gen.insave.ovh:9000",
            S3BucketName = "mods",
            S3FolderName = "folder",
            S3HostPublicKey = "custom-access",
            S3HostSecretKey = "custom-secret"
        };

        S3ObjectManifestRequest request = S3CatalogDefaults.CreateManifestRequest(version);

        request.AccessKey.Should().Be("custom-access");
        request.SecretKey.Should().Be("custom-secret");
    }
}
