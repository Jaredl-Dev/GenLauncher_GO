using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Updating.Clients;
using GenLauncherGO.Infrastructure.Updating.Models;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Clients;

public sealed class HttpDownloadFileMetadataReaderTests
{
    [Fact]
    public async Task ReadMetadataAsync_UsesHeadContentDispositionFileNameStarAsync()
    {
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(
            HttpStatusCode.OK,
            "attachment; filename*=UTF-8''Folder%2FPackage.big",
            123));
        HttpDownloadFileMetadataReader reader = CreateReader(handler);
        Uri uri = new("https://example.test/packages/package.big");

        DownloadFileMetadata metadata = await reader.ReadMetadataAsync(uri, CancellationToken.None);

        metadata.DownloadUri.Should().Be(uri);
        metadata.FileName.Should().Be("FolderPackage.big");
        metadata.TotalBytes.Should().Be(123);
        handler.Methods.Should().Equal(HttpMethod.Head);
    }

    /// <summary>
    ///     The star form carries the encoded name a server sends alongside an ASCII fallback, so it wins whenever both
    ///     are present.
    /// </summary>
    [Fact]
    public async Task ReadMetadataAsync_ContentDispositionCarriesBothFileNameForms_PrefersFileNameStarAsync()
    {
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(
            HttpStatusCode.OK,
            "attachment; filename=\"ascii.zip\"; filename*=UTF-8''unicode.zip"));
        HttpDownloadFileMetadataReader reader = CreateReader(handler);

        DownloadFileMetadata metadata = await reader.ReadMetadataAsync(
            new Uri("https://example.test/package"),
            CancellationToken.None);

        metadata.FileName.Should().Be("unicode.zip");
    }

    [Theory]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    [InlineData(HttpStatusCode.NotImplemented)]
    public async Task ReadMetadataAsync_FallsBackToGetWhenHeadIsNotAllowedAsync(HttpStatusCode headStatusCode)
    {
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(headStatusCode));
        handler.Enqueue(_ => CreateResponse(
            HttpStatusCode.OK,
            "attachment; filename=\"Package.zip\""));
        HttpDownloadFileMetadataReader reader = CreateReader(handler);

        DownloadFileMetadata metadata = await reader.ReadMetadataAsync(
            new Uri("https://example.test/package.zip"),
            CancellationToken.None);

        metadata.FileName.Should().Be("Package.zip");
        handler.Methods.Should().Equal(HttpMethod.Head, HttpMethod.Get);
    }

    /// <summary>
    ///     Only the two "method unsupported" statuses justify a second request; any other failure is the server's answer
    ///     and must surface instead of being retried as a GET.
    /// </summary>
    [Fact]
    public async Task ReadMetadataAsync_HeadFailsWithServerError_ThrowsWithoutFallbackRequestAsync()
    {
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(HttpStatusCode.InternalServerError));
        HttpDownloadFileMetadataReader reader = CreateReader(handler);

        Func<Task> act = () => reader.ReadMetadataAsync(
            new Uri("https://example.test/package.zip"),
            CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ReadMetadataAsync_ThrowsWhenNeitherRequestReturnsFileNameAsync()
    {
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(HttpStatusCode.OK));
        handler.Enqueue(_ => CreateResponse(HttpStatusCode.OK));
        HttpDownloadFileMetadataReader reader = CreateReader(handler);

        Func<Task> act = () => reader.ReadMetadataAsync(
            new Uri("https://example.test/package"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Download link is incorrect, please contact modification creator and try again later.");
        handler.Methods.Should().Equal(HttpMethod.Head, HttpMethod.Get);
    }

    [Fact]
    public async Task ReadMetadataAsync_ThrowsWhenSanitizedFileNameIsEmptyAsync()
    {
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(
            HttpStatusCode.OK,
            "attachment; filename=\"\\\\\""));
        HttpDownloadFileMetadataReader reader = CreateReader(handler);

        Func<Task> act = () => reader.ReadMetadataAsync(
            new Uri("https://example.test/package"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Download link is incorrect, please contact modification creator and try again later.");
    }

    private static HttpDownloadFileMetadataReader CreateReader(QueueHttpMessageHandler handler)
    {
        return new HttpDownloadFileMetadataReader(new HttpClient(handler));
    }

    private static HttpResponseMessage CreateResponse(
        HttpStatusCode statusCode,
        string? contentDisposition = null,
        long? contentLength = null)
    {
        HttpResponseMessage response = new(statusCode)
        {
            Content = new ByteArrayContent([])
        };
        if (contentDisposition is not null)
        {
            response.Content.Headers.ContentDisposition = ContentDispositionHeaderValue.Parse(contentDisposition);
        }

        response.Content.Headers.ContentLength = contentLength;
        return response;
    }
}
