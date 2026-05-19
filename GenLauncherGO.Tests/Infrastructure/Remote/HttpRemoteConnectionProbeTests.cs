using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Remote;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Remote;

public sealed class HttpRemoteConnectionProbeTests
{
    [Fact]
    public async Task CanConnectAsync_ReturnsTrueWhenHeadSucceedsAsync()
    {
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        HttpRemoteConnectionProbe probe = CreateProbe(handler);

        bool canConnect = await probe.CanConnectAsync(
            new Uri("https://example.test/catalog.yml"),
            CancellationToken.None);

        canConnect.Should().BeTrue();
        handler.Methods.Should().Equal(HttpMethod.Head);
    }

    [Theory]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    [InlineData(HttpStatusCode.NotImplemented)]
    public async Task CanConnectAsync_FallsBackToGetWhenHeadIsNotAllowedAsync(HttpStatusCode headStatusCode)
    {
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => new HttpResponseMessage(headStatusCode));
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK));
        HttpRemoteConnectionProbe probe = CreateProbe(handler);

        bool canConnect = await probe.CanConnectAsync(
            new Uri("https://example.test/catalog.yml"),
            CancellationToken.None);

        canConnect.Should().BeTrue();
        handler.Methods.Should().Equal(HttpMethod.Head, HttpMethod.Get);
    }

    [Fact]
    public async Task CanConnectAsync_FallsBackToGetWhenHeadReturnsAnotherFailureAsync()
    {
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK));
        HttpRemoteConnectionProbe probe = CreateProbe(handler);

        bool canConnect = await probe.CanConnectAsync(
            new Uri("https://example.test/catalog.yml"),
            CancellationToken.None);

        canConnect.Should().BeTrue();
        handler.Methods.Should().Equal(HttpMethod.Head, HttpMethod.Get);
    }

    [Fact]
    public async Task CanConnectAsync_ReturnsFalseWhenRequestsFailAsync()
    {
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => throw new HttpRequestException("network down"));
        HttpRemoteConnectionProbe probe = CreateProbe(handler);

        bool canConnect = await probe.CanConnectAsync(
            new Uri("https://example.test/catalog.yml"),
            CancellationToken.None);

        canConnect.Should().BeFalse();
    }

    [Fact]
    public async Task CanConnectAsync_ReturnsFalseWhenProbeTimesOutAsync()
    {
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => throw new TaskCanceledException("timeout"));
        HttpRemoteConnectionProbe probe = CreateProbe(handler);

        bool canConnect = await probe.CanConnectAsync(
            new Uri("https://example.test/catalog.yml"),
            CancellationToken.None);

        canConnect.Should().BeFalse();
    }

    /// <summary>
    ///     A caller-requested cancellation must reach the caller instead of being reported as an unreachable endpoint.
    /// </summary>
    [Fact]
    public async Task CanConnectAsync_CanceledToken_PropagatesCancellationAsync()
    {
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => throw new TaskCanceledException("canceled"));
        HttpRemoteConnectionProbe probe = CreateProbe(handler);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> act = () => probe.CanConnectAsync(
            new Uri("https://example.test/catalog.yml"),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static HttpRemoteConnectionProbe CreateProbe(QueueHttpMessageHandler handler)
    {
        return new HttpRemoteConnectionProbe(
            NullLogger<HttpRemoteConnectionProbe>.Instance,
            new HttpClient(handler));
    }
}
