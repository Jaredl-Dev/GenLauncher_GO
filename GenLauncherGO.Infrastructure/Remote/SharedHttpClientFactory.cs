using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace GenLauncherGO.Infrastructure.Remote;

internal static class SharedHttpClientFactory
{
    /// <summary>
    /// Creates an HTTP client with pooled connections, no automatic decompression, and a GenLauncherGO user agent.
    /// </summary>
    public static HttpClient Create(TimeSpan timeout)
    {
        SocketsHttpHandler handler = new()
        {
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            MaxConnectionsPerServer = 16,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        };

        HttpClient httpClient = new(handler)
        {
            Timeout = timeout,
        };

        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("GenLauncherGO", "1"));

        return httpClient;
    }
}
