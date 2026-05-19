using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Persistence.Services;

namespace GenLauncherGO.Tests.Testing;

internal sealed class RecordingAtomicFileWriter : IAtomicFileWriter
{
    public string? DestinationPath { get; private set; }

    public string? Contents { get; private set; }

    public CancellationToken? CancellationToken { get; private set; }

    public bool WasWriteAsyncCalled { get; private set; }

    public void WriteText(string destinationPath, string contents)
    {
        throw new NotSupportedException("This recorder only observes WriteAsync.");
    }

    public async Task WriteAsync(
        string destinationPath,
        Func<Stream, CancellationToken, Task> writeTemporaryFileAsync,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = new MemoryStream();
        await writeTemporaryFileAsync(stream, cancellationToken);
        DestinationPath = destinationPath;
        Contents = Encoding.UTF8.GetString(stream.ToArray());
        CancellationToken = cancellationToken;
        WasWriteAsyncCalled = true;
    }
}
