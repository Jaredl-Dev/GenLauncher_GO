using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Persistence.Services;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Persistence.Services;

public sealed class YamlDocumentStoreTests
{
    [Fact]
    public void Load_WhenDocumentIsMissing_ReturnsDefaultDocument()
    {
        using var directory = new TestDirectory();
        var defaultDocument = new TestDocument { Name = "default" };
        IYamlDocumentStore<TestDocument> store = CreateStore(Path.Combine(directory.Path, "state.yaml"));

        TestDocument document = store.Load(defaultDocument);

        document.Should().BeSameAs(defaultDocument);
    }

    [Fact]
    public void Load_WhenDocumentIsMalformed_ReturnsDefaultDocument()
    {
        using var directory = new TestDirectory();
        string documentPath = Path.Combine(directory.Path, "state.yaml");
        var defaultDocument = new TestDocument { Name = "default" };
        File.WriteAllText(documentPath, "Name: [");
        IYamlDocumentStore<TestDocument> store = CreateStore(documentPath);

        TestDocument document = store.Load(defaultDocument);

        document.Should().BeSameAs(defaultDocument);
    }

    [Fact]
    public void Save_WritesDocumentThatCanBeLoaded()
    {
        using var directory = new TestDirectory();
        string documentPath = Path.Combine(directory.Path, "Runtime", "State", "state.yaml");
        IYamlDocumentStore<TestDocument> store = CreateStore(documentPath);
        var document = new TestDocument
        {
            Name = "ShockWave",
            Version = "1.2",
            Installed = true
        };

        store.Save(document);
        TestDocument loadedDocument = store.Load(new TestDocument());

        loadedDocument.Name.Should().Be("ShockWave");
        loadedDocument.Version.Should().Be("1.2");
        loadedDocument.Installed.Should().BeTrue();
        File.Exists(documentPath).Should().BeTrue();
    }

    [Fact]
    public void Save_WhenDocumentPathIsDirectory_PropagatesPersistenceFailure()
    {
        using var directory = new TestDirectory();
        string documentPath = Path.Combine(directory.Path, "State");
        Directory.CreateDirectory(documentPath);
        IYamlDocumentStore<TestDocument> store = CreateStore(documentPath);

        Action act = () => store.Save(new TestDocument { Name = "ShockWave" });

        act.Should().Throw<IOException>();
        Directory.Exists(documentPath).Should().BeTrue();
    }

    [Fact]
    public void AtomicWriter_WhenCommitFails_PreservesOriginalAndCleansTemporaryFile()
    {
        using var directory = new TestDirectory();
        string documentPath = Path.Combine(directory.Path, "state.yaml");
        var writer = new AtomicFileWriter();
        writer.WriteText(documentPath, "Name: original");
        using FileStream lockedDocument = new(
            documentPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        Action act = () => writer.WriteText(documentPath, "Name: replacement");

        act.Should().Throw<IOException>();
        File.ReadAllText(documentPath).Should().Be("Name: original");
        Directory.EnumerateFiles(directory.Path, ".*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task AtomicWriterAsync_WhenCanceledDuringWrite_PreservesOriginalAndCleansTemporaryFileAsync()
    {
        using var directory = new TestDirectory();
        string documentPath = Path.Combine(directory.Path, "state.yaml");
        await File.WriteAllTextAsync(documentPath, "Name: original");
        var writer = new AtomicFileWriter();
        using var cancellationTokenSource = new CancellationTokenSource();

        Func<Task> act = () => writer.WriteAsync(
            documentPath,
            async (stream, cancellationToken) =>
            {
                byte[] replacement = Encoding.UTF8.GetBytes("Name: replacement");
                await stream.WriteAsync(replacement.AsMemory(), cancellationToken);
                cancellationTokenSource.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            },
            cancellationTokenSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText(documentPath).Should().Be("Name: original");
        Directory.EnumerateFiles(directory.Path, ".*.tmp").Should().BeEmpty();
    }

    private static YamlDocumentStore<TestDocument> CreateStore(string documentPath)
    {
        return new YamlDocumentStore<TestDocument>(
            documentPath,
            new AtomicFileWriter(),
            NullLogger<YamlDocumentStore<TestDocument>>.Instance);
    }

    private sealed class TestDocument
    {
        public string Name { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        public bool Installed { get; set; }
    }
}
