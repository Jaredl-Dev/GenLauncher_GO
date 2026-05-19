using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Support;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Models;

public sealed class S3RequestDefaultsTests
{
    [Fact]
    public void S3ObjectManifestRequest_DefaultsHostOnlyEndpointToNonSsl()
    {
        S3ObjectManifestRequest request = new(
            "gen.insave.ovh:9000",
            "mods",
            "folder",
            "access",
            "secret");

        request.UseSsl.Should().BeFalse();
    }

    [Fact]
    public void CreateManifestRequest_UsesPublicCatalogKeysWhenMetadataKeysAreMissing()
    {
        LauncherContentVersion version = new()
        {
            S3HostLink = "gen.insave.ovh:9000",
            S3BucketName = "mods",
            S3FolderName = "folder",
        };

        S3ObjectManifestRequest request = S3CatalogDefaults.CreateManifestRequest(version);

        request.AccessKey.Should().Be(S3CatalogDefaults.PublicAccessKey);
        request.SecretKey.Should().Be(S3CatalogDefaults.PublicSecretKey);
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
            S3HostSecretKey = "custom-secret",
        };

        S3ObjectManifestRequest request = S3CatalogDefaults.CreateManifestRequest(version);

        request.AccessKey.Should().Be("custom-access");
        request.SecretKey.Should().Be("custom-secret");
    }
}
