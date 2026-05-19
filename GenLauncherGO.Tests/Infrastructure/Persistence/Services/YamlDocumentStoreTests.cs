using System;
using System.IO;
using GenLauncherGO.Infrastructure.Persistence.Services;
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

    /// <summary>
    ///     Launcher state decides what gets deployed into a user's game folder. A document reached through a link is
    ///     state somebody else placed there, so it is discarded in favour of the caller's default rather than trusted.
    /// </summary>
    [Fact]
    public void Load_WhenDocumentPathCrossesALink_ReturnsDefaultDocument()
    {
        using var directory = new TestDirectory();
        string linkPath = directory.GetPath("Linked");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, linkPath);
        File.WriteAllText(Path.Combine(junction.TargetDirectory, "state.yaml"), "Name: planted");
        var defaultDocument = new TestDocument { Name = "default" };
        IYamlDocumentStore<TestDocument> store = CreateStore(Path.Combine(linkPath, "state.yaml"));

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
