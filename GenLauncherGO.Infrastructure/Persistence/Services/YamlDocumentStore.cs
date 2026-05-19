using System;
using System.IO;
using GenLauncherGO.Infrastructure.Common;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;

namespace GenLauncherGO.Infrastructure.Persistence.Services;

internal sealed class YamlDocumentStore<TDocument> : IYamlDocumentStore<TDocument>
    where TDocument : class
{
    private readonly IAtomicFileWriter _atomicFileWriter;
    private readonly string _documentFilePath;

    private readonly ILogger<YamlDocumentStore<TDocument>> _logger;

    public YamlDocumentStore(
        string documentFilePath,
        IAtomicFileWriter atomicFileWriter,
        ILogger<YamlDocumentStore<TDocument>> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentFilePath);

        _documentFilePath = documentFilePath;
        _atomicFileWriter = atomicFileWriter ?? throw new ArgumentNullException(nameof(atomicFileWriter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool DocumentExists => File.Exists(_documentFilePath);

    public TDocument Load(TDocument defaultDocument)
    {
        ArgumentNullException.ThrowIfNull(defaultDocument);

        if (!File.Exists(_documentFilePath))
        {
            return defaultDocument;
        }

        try
        {
            FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
                _documentFilePath,
                "YAML document paths");
            IDeserializer deserializer = new DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build();

            using TextReader reader = File.OpenText(_documentFilePath);
            return deserializer.Deserialize<TDocument>(reader) ?? defaultDocument;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to load {DocumentType} from {DocumentFileName}.",
                typeof(TDocument).Name,
                Path.GetFileName(_documentFilePath));
            return defaultDocument;
        }
    }

    public void Save(TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        try
        {
            ISerializer serializer = new Serializer();
            string yaml = serializer.Serialize(document);
            _atomicFileWriter.WriteText(_documentFilePath, yaml);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to save {DocumentType} to {DocumentFileName}.",
                typeof(TDocument).Name,
                Path.GetFileName(_documentFilePath));
            throw;
        }
    }
}
