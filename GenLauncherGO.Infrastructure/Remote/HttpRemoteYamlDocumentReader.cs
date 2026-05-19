using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Remote.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YamlDotNet.Serialization;

namespace GenLauncherGO.Infrastructure.Remote;

/// <summary>
/// Reads YAML documents over HTTP.
/// </summary>
internal sealed class HttpRemoteYamlDocumentReader : IRemoteYamlDocumentReader
{
    private static readonly HttpClient _sharedHttpClient =
        SharedHttpClientFactory.Create(TimeSpan.FromSeconds(60));

    private readonly IDeserializer _deserializer;
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpRemoteYamlDocumentReader> _logger;

    public HttpRemoteYamlDocumentReader(
        HttpClient? httpClient = null,
        ILogger<HttpRemoteYamlDocumentReader>? logger = null)
    {
        _httpClient = httpClient ?? _sharedHttpClient;
        _logger = logger ?? NullLogger<HttpRemoteYamlDocumentReader>.Instance;
        _deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public async Task<T> ReadYamlAsync<T>(Uri documentUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documentUri);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, documentUri);
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using StreamReader reader = new(contentStream);

            return _deserializer.Deserialize<T>(reader);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to read remote YAML document from {Scheme}://{Host}.",
                documentUri.Scheme,
                documentUri.Host);
            throw;
        }
    }
}
