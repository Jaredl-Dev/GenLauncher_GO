using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenLauncherGO.Infrastructure.Remote;

/// <summary>
///     Sends a headers-only HEAD request and falls back to GET when the endpoint or response reader requires it.
/// </summary>
internal static class HttpHeadFallbackRequest
{
    public static async Task<TResult> SendAsync<TResult>(
        HttpClient httpClient,
        Uri endpointUri,
        Func<HttpResponseMessage, TResult> readResponse,
        Func<TResult, bool> shouldFallback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(endpointUri);
        ArgumentNullException.ThrowIfNull(readResponse);
        ArgumentNullException.ThrowIfNull(shouldFallback);

        (TResult HeadResult, bool HeadUnsupported) head = await SendAsync(
            httpClient,
            endpointUri,
            HttpMethod.Head,
            readResponse,
            cancellationToken).ConfigureAwait(false);
        if (!head.HeadUnsupported && !shouldFallback(head.HeadResult))
        {
            return head.HeadResult;
        }

        (TResult getResult, _) = await SendAsync(
            httpClient,
            endpointUri,
            HttpMethod.Get,
            readResponse,
            cancellationToken).ConfigureAwait(false);
        return getResult;
    }

    private static async Task<(TResult Result, bool HeadUnsupported)> SendAsync<TResult>(
        HttpClient httpClient,
        Uri endpointUri,
        HttpMethod method,
        Func<HttpResponseMessage, TResult> readResponse,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, endpointUri);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        bool headUnsupported = method == HttpMethod.Head &&
                               response.StatusCode is HttpStatusCode.MethodNotAllowed or
                                   HttpStatusCode.NotImplemented;
        return headUnsupported
            ? (default!, true)
            : (readResponse(response), false);
    }
}
