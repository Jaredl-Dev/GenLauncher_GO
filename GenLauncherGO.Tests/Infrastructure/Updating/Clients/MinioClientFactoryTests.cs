using Minio;
using Subject = GenLauncherGO.Infrastructure.Updating.Clients.MinioClientFactory;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Clients;

public sealed class MinioClientFactoryTests
{
    [Theory]
    [InlineData("s3.example.test", false, "http://s3.example.test", false)]
    [InlineData("http://s3.example.test:9000/path", true, "http://s3.example.test:9000", false)]
    [InlineData("https://s3.example.test/path", false, "https://s3.example.test", true)]
    [InlineData("s3.example.test:443", false, "https://s3.example.test:443", true)]
    public void Create_NormalizesEndpointAndResolvesTransportSecurity(
        string endpoint,
        bool useSsl,
        string expectedEndpoint,
        bool expectedSecure)
    {
        IMinioClient client = Subject.Create(endpoint, "access", "secret", useSsl);

        client.Config.Endpoint.Should().Be(expectedEndpoint);
        client.Config.Secure.Should().Be(expectedSecure);
    }
}
