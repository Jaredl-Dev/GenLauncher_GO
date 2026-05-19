using Minio;
using Subject = GenLauncherGO.Infrastructure.Updating.Clients.MinioClientFactory;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Clients;

public sealed class MinioClientFactoryTests
{
    [Theory]
    [InlineData("s3.example.test")]
    [InlineData("http://s3.example.test:9000/path")]
    [InlineData("https://s3.example.test")]
    [InlineData("s3.example.test:443")]
    public void CreateBuildsClientForSupportedEndpointForms(string endpoint)
    {
        IMinioClient client = Subject.Create(
            endpoint,
            "access",
            "secret",
            useSsl: false);

        client.Should().NotBeNull();
    }
}
