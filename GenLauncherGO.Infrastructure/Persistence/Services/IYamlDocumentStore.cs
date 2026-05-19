namespace GenLauncherGO.Infrastructure.Persistence.Services;

internal interface IYamlDocumentStore<TDocument>
    where TDocument : class
{
    bool DocumentExists { get; }

    /// <summary>
    ///     Loads the document from disk.
    /// </summary>
    /// <returns>The loaded document, or <paramref name="defaultDocument" />.</returns>
    TDocument Load(TDocument defaultDocument);

    /// <summary>
    ///     Saves the document to disk.
    /// </summary>
    /// <remarks>Persistence failures are logged and propagated to the caller.</remarks>
    void Save(TDocument document);
}
