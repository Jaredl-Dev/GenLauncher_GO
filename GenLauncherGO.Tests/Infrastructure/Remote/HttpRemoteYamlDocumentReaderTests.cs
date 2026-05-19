using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Remote;
using GenLauncherGO.Tests.Testing;

namespace GenLauncherGO.Tests.Infrastructure.Remote;

public sealed class HttpRemoteYamlDocumentReaderTests
{
    [Fact]
    public async Task ReadYamlAsync_DeserializesRemoteYamlAsync()
    {
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Name: ShockWave\nVersion: '1.2'\n", Encoding.UTF8),
        });
        HttpRemoteYamlDocumentReader reader = new(new HttpClient(handler));

        RemoteDocument document = await reader.ReadYamlAsync<RemoteDocument>(
            new Uri("https://example.test/catalog.yml"),
            CancellationToken.None);

        document.Name.Should().Be("ShockWave");
        document.Version.Should().Be("1.2");
        handler.Requests.Should().ContainSingle()
            .Which.Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task ReadYamlAsync_ThrowsForUnsuccessfulResponseAsync()
    {
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        HttpRemoteYamlDocumentReader reader = new(new HttpClient(handler));

        Func<Task> act = () => reader.ReadYamlAsync<RemoteDocument>(
            new Uri("https://example.test/catalog.yml"),
            CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private sealed class RemoteDocument
    {
        public string Name { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;
    }

}
