using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Remote;

namespace GenLauncherGO.Tests.Infrastructure.Remote;

public sealed class HttpRemoteYamlDocumentReaderTests
{
    /// <summary>
    ///     The backend adds keys the launcher has never seen without a schema change, so the fixture carries a scalar
    ///     and a nested mapping the document does not declare.
    /// </summary>
    [Fact]
    public async Task ReadYamlAsync_DeserializesRemoteYamlAsync()
    {
        const string RemoteYaml = """
                                  Name: ShockWave
                                  Version: '1.2'
                                  FutureField: whatever
                                  FutureSection:
                                    Nested: value
                                    Deeper:
                                      Key: 1

                                  """;
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(RemoteYaml, Encoding.UTF8)
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

    /// <summary>
    ///     A caller-requested cancellation must reach the caller unchanged instead of being reported as a failed read.
    /// </summary>
    [Fact]
    public async Task ReadYamlAsync_CanceledToken_PropagatesCancellationAsync()
    {
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => throw new TaskCanceledException("canceled"));
        HttpRemoteYamlDocumentReader reader = new(new HttpClient(handler));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> act = () => reader.ReadYamlAsync<RemoteDocument>(
            new Uri("https://example.test/catalog.yml"),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// A stand-in for a remote YAML document.
    /// </summary>
    /// <remarks>
    /// The setters exist for the deserializer, not for this file, so they look unused to a "make it read-only"
    /// inspection. Removing them makes deserialization silently yield empty values instead of failing to build.
    /// </remarks>
    private sealed class RemoteDocument
    {
        public string Name { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;
    }
}
