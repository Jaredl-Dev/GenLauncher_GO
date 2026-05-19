using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Updating.Clients;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Tests.Testing;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Clients;

public sealed class HttpDownloadFileMetadataReaderTests
{
    [Fact]
    public async Task ReadMetadataAsync_UsesHeadContentDispositionFileNameStarAsync()
    {
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(
            HttpStatusCode.OK,
            contentDisposition: "attachment; filename*=UTF-8''Folder%2FPackage.big",
            contentLength: 123));
        HttpDownloadFileMetadataReader reader = CreateReader(handler);
        Uri uri = new("https://example.test/packages/package.big");

        DownloadFileMetadata metadata = await reader.ReadMetadataAsync(uri, CancellationToken.None);

        metadata.DownloadUri.Should().Be(uri);
        metadata.FileName.Should().Be("FolderPackage.big");
        metadata.TotalBytes.Should().Be(123);
        handler.Methods.Should().Equal(HttpMethod.Head);
    }

    [Fact]
    public async Task ReadMetadataAsync_FallsBackToGetWhenHeadIsNotAllowedAsync()
    {
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(HttpStatusCode.MethodNotAllowed));
        handler.Enqueue(_ => CreateResponse(
            HttpStatusCode.OK,
            contentDisposition: "attachment; filename=\"Package.zip\""));
        HttpDownloadFileMetadataReader reader = CreateReader(handler);

        DownloadFileMetadata metadata = await reader.ReadMetadataAsync(
            new Uri("https://example.test/package.zip"),
            CancellationToken.None);

        metadata.FileName.Should().Be("Package.zip");
        handler.Methods.Should().Equal(HttpMethod.Head, HttpMethod.Get);
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
            contentDisposition: "attachment; filename=\"\\\\\""));
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
            Content = new ByteArrayContent(Array.Empty<byte>()),
        };
        if (contentDisposition is not null)
        {
            response.Content.Headers.ContentDisposition = ContentDispositionHeaderValue.Parse(contentDisposition);
        }

        response.Content.Headers.ContentLength = contentLength;
        return response;
    }

}
