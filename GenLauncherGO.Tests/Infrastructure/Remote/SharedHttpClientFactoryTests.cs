using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Remote;

namespace GenLauncherGO.Tests.Infrastructure.Remote;

/// <summary>
///     Pins the transport contract shared by every launcher HTTP client.
/// </summary>
/// <remarks>
///     Automatic decompression has to stay off. The resumable downloader resumes at a byte offset and measures its
///     progress against the length the server declared, so a handler that silently inflated the body would write
///     more bytes than that length and leave every resumed transfer corrupt. The server here is a loopback socket
///     so the invariant is observed the way a real server would expose it, without reaching the network.
/// </remarks>
public sealed class SharedHttpClientFactoryTests
{
    [Fact]
    public async Task Create_LeavesContentEncodingUnnegotiatedAndUnappliedAsync()
    {
        byte[] compressedBody = Compress("payload");
        (int Port, Task<string> Request) server = StartLoopbackServer(compressedBody);
        byte[] received;

        using (HttpClient httpClient = SharedHttpClientFactory.Create(TestTimeouts.Wait))
        {
            received = await httpClient.GetByteArrayAsync(
                new Uri($"http://127.0.0.1:{server.Port}/asset"));
        }

        string request = await server.Request;
        received.Should().Equal(
            compressedBody,
            "a decompressing handler would hand back the inflated body instead of the bytes on the wire");
        request.Should().NotContain(
            "Accept-Encoding",
            "advertising an encoding invites the compressed response that breaks resume arithmetic");
        request.Should().Contain("User-Agent: GenLauncherGO/1");
    }

    [Fact]
    public void Create_AppliesTheRequestedTimeout()
    {
        using HttpClient httpClient = SharedHttpClientFactory.Create(TimeSpan.FromSeconds(37));

        httpClient.Timeout.Should().Be(TimeSpan.FromSeconds(37));
    }

    /// <summary>
    ///     Starts a loopback server that serves one gzip-encoded response. The returned task owns the listener and
    ///     closes it, so the caller never holds a socket it might dispose while the exchange is still running.
    /// </summary>
    private static (int Port, Task<string> Request) StartLoopbackServer(byte[] body)
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return (((IPEndPoint)listener.LocalEndpoint).Port, RespondOnceAsync(listener, body));
    }

    /// <summary>
    ///     Serves one gzip-encoded response and returns the raw request text the client sent.
    /// </summary>
    private static async Task<string> RespondOnceAsync(TcpListener listener, byte[] body)
    {
        try
        {
            return await ExchangeAsync(listener, body);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<string> ExchangeAsync(TcpListener listener, byte[] body)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync();
        await using NetworkStream stream = client.GetStream();
        string request = await ReadRequestHeadersAsync(stream);
        string responseHeaders =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Encoding: gzip\r\n" +
            "Content-Type: application/octet-stream\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n";

        await stream.WriteAsync(Encoding.ASCII.GetBytes(responseHeaders));
        await stream.WriteAsync(body);
        await stream.FlushAsync();
        return request;
    }

    private static async Task<string> ReadRequestHeadersAsync(NetworkStream stream)
    {
        StringBuilder text = new();
        byte[] buffer = new byte[1];
        while (!EndsWithBlankLine(text))
        {
            int read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                break;
            }

            text.Append((char)buffer[0]);
        }

        return text.ToString();
    }

    private static bool EndsWithBlankLine(StringBuilder text)
    {
        return text.Length >= 4 &&
               text[^4] == '\r' &&
               text[^3] == '\n' &&
               text[^2] == '\r' &&
               text[^1] == '\n';
    }

    private static byte[] Compress(string payload)
    {
        using MemoryStream output = new();
        using (GZipStream gzip = new(output, CompressionLevel.Optimal, true))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }
}
