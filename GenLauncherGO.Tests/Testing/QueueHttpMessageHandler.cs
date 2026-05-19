using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenLauncherGO.Tests.Testing;

internal sealed class QueueHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public IEnumerable<HttpMethod> Methods => Requests.Select(request => request.Method);

    public IEnumerable<string?> RangeHeaders =>
        Requests.Select(request => request.Headers.Range?.ToString());

    public List<HttpRequestMessage> Requests { get; } = new();

    public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        ArgumentNullException.ThrowIfNull(responseFactory);

        _responses.Enqueue(responseFactory);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_responses.Dequeue()(request));
    }
}
