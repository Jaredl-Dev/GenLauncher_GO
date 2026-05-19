using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Clients;
using GenLauncherGO.Infrastructure.Updating.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Clients;

public sealed class MinioS3ObjectManifestReaderTests
{
    [Fact]
    public async Task ReadManifestAsyncReturnsPrefixRelativeEntriesAsync()
    {
        S3ObjectManifestRequest? receivedRequest = null;
        using CancellationTokenSource cancellationTokenSource = new();
        S3ObjectManifestRequest request = CreateRequest(prefix: "ShockWave/1.2");
        MinioS3ObjectManifestReader reader = new(
            NullLogger<MinioS3ObjectManifestReader>.Instance,
            (manifestRequest, cancellationToken) =>
            {
                receivedRequest = manifestRequest;
                cancellationToken.Should().Be(cancellationTokenSource.Token);
                return EnumerateObjectsAsync(
                    new MinioS3ObjectManifestReader.S3ObjectManifestItem(
                        "ShockWave/1.2/files/launcher.big",
                        " \"ABC123\" ",
                        42),
                    new MinioS3ObjectManifestReader.S3ObjectManifestItem(
                        "outside-prefix.big",
                        "DEF456",
                        7));
            });

        IReadOnlyList<RemoteFileManifestEntry> entries = await reader.ReadManifestAsync(
            request,
            cancellationTokenSource.Token);

        entries.Should().Equal(
            new RemoteFileManifestEntry("files/launcher.big", "ABC123", 42),
            new RemoteFileManifestEntry("outside-prefix.big", "DEF456", 7));
        receivedRequest.Should().BeSameAs(request);
    }

    private static S3ObjectManifestRequest CreateRequest(string prefix = "ShockWave")
    {
        return new S3ObjectManifestRequest(
            "s3.example.test",
            "mods",
            prefix,
            "access",
            "secret");
    }

    private static async IAsyncEnumerable<MinioS3ObjectManifestReader.S3ObjectManifestItem> EnumerateObjectsAsync(
        params MinioS3ObjectManifestReader.S3ObjectManifestItem[] items)
    {
        await Task.Yield();

        foreach (MinioS3ObjectManifestReader.S3ObjectManifestItem item in items)
        {
            yield return item;
        }
    }
}
