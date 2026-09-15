using System;
using System.IO;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Shared.IO;
using GenLauncherGO.Shared.Persistence;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Mods;

/// <summary>
///     Caches published modification palettes as YAML beside the artwork they belong to.
/// </summary>
/// <remarks>
///     Reusing the modification image cache directory is deliberate: that folder already has an ownership boundary,
///     reparse-point defences, and removal when the content card goes away, so the palette inherits all of it instead
///     of needing a second cache location with its own lifetime rules.
/// </remarks>
internal sealed class FileSystemModificationThemeCache : IModificationThemeCache
{
    private readonly IAtomicFileWriter _atomicFileWriter;

    private readonly ILogger<YamlDocumentStore<LauncherContentTheme>> _documentLogger;

    private readonly ILogger<FileSystemModificationThemeCache> _logger;
    private readonly LauncherRuntimePathContext _runtimePathContext;

    public FileSystemModificationThemeCache(
        LauncherRuntimePathContext runtimePathContext,
        IAtomicFileWriter atomicFileWriter,
        ILogger<YamlDocumentStore<LauncherContentTheme>> documentLogger,
        ILogger<FileSystemModificationThemeCache> logger)
    {
        _runtimePathContext = runtimePathContext ?? throw new ArgumentNullException(nameof(runtimePathContext));
        _atomicFileWriter = atomicFileWriter ?? throw new ArgumentNullException(nameof(atomicFileWriter));
        _documentLogger = documentLogger ?? throw new ArgumentNullException(nameof(documentLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Save(LauncherContentKey contentKey, LauncherContentTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        if (!TryResolveDocumentPath(contentKey, out string documentPath))
        {
            return;
        }

        try
        {
            OwnedDirectoryTree.EnsureExists(
                _runtimePathContext.ActivePaths.ImagesDirectory,
                Path.GetDirectoryName(documentPath)!);
            CreateDocumentStore(documentPath).Save(theme);
        }
        catch (Exception exception) when (ModificationCacheFailure.IsRecoverable(exception))
        {
            // A palette that cannot be cached only costs the offline re-skin, so never fail the catalog for it.
            _logger.LogWarning(
                exception,
                "Could not cache the published palette for {ModificationName} {Version}.",
                contentKey.Name,
                contentKey.Version);
        }
    }

    public LauncherContentTheme? Load(LauncherContentKey contentKey)
    {
        if (!TryResolveDocumentPath(contentKey, out string documentPath))
        {
            return null;
        }

        try
        {
            IYamlDocumentStore<LauncherContentTheme> store = CreateDocumentStore(documentPath);
            if (!store.DocumentExists)
            {
                return null;
            }

            LauncherContentTheme empty = new();
            LauncherContentTheme cached = store.Load(empty);
            return ReferenceEquals(cached, empty) ? null : cached;
        }
        catch (Exception exception) when (ModificationCacheFailure.IsRecoverable(exception))
        {
            _logger.LogWarning(
                exception,
                "Could not read the cached palette for {ModificationName} {Version}.",
                contentKey.Name,
                contentKey.Version);
            return null;
        }
    }

    private bool TryResolveDocumentPath(LauncherContentKey contentKey, out string documentPath)
    {
        documentPath = string.Empty;
        if (string.IsNullOrWhiteSpace(contentKey.Name) || string.IsNullOrWhiteSpace(contentKey.Version))
        {
            return false;
        }

        try
        {
            LauncherPaths paths = _runtimePathContext.ActivePaths;
            documentPath = ModificationImageCachePath.ResolveImagePath(
                paths,
                contentKey.ContentType,
                contentKey.Name,
                LauncherContentTheme.ResolveCacheBaseName(contentKey.Version) + ".yaml");
            return true;
        }
        catch (Exception exception) when (ModificationCacheFailure.IsRecoverable(exception))
        {
            _logger.LogWarning(
                exception,
                "Could not resolve the palette cache path for {ModificationName} {Version}.",
                contentKey.Name,
                contentKey.Version);
            return false;
        }
    }

    private IYamlDocumentStore<LauncherContentTheme> CreateDocumentStore(string documentPath)
    {
        return new YamlDocumentStore<LauncherContentTheme>(documentPath, _atomicFileWriter, _documentLogger);
    }

}
