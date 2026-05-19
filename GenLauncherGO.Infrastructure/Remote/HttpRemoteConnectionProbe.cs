using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Remote;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Remote;

/// <summary>
///     Checks remote HTTP endpoint connectivity.
/// </summary>
internal sealed class HttpRemoteConnectionProbe : IRemoteConnectionProbe
{
    private static readonly HttpClient _sharedHttpClient =
        SharedHttpClientFactory.Create(TimeSpan.FromSeconds(30));

    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpRemoteConnectionProbe> _logger;

    public HttpRemoteConnectionProbe(
        ILogger<HttpRemoteConnectionProbe> logger,
        HttpClient? httpClient = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? _sharedHttpClient;
    }

    /// <summary>
    ///     Checks whether the remote endpoint can be reached through HEAD or GET without downloading the response body.
    /// </summary>
    public async Task<bool> CanConnectAsync(Uri endpointUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpointUri);

        try
        {
            return await HttpHeadFallbackRequest.SendAsync(
                _httpClient,
                endpointUri,
                static response => response.IsSuccessStatusCode,
                static connected => !connected,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(
                exception,
                "Remote connection probe failed for {Scheme}://{Host}.",
                endpointUri.Scheme,
                endpointUri.Host);
            return false;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                exception,
                "Remote connection probe timed out for {Scheme}://{Host}.",
                endpointUri.Scheme,
                endpointUri.Host);
            return false;
        }
    }

}
