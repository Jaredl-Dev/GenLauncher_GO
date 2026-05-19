using System;
using System.IO;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Mods.Support;
using GenLauncherGO.Infrastructure.Persistence.Services;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Mods.Services;

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

    public void Save(string modificationName, string version, LauncherContentTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        if (!TryResolveDocumentPath(modificationName, version, out string documentPath))
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
        catch (Exception exception) when (IsRecoverableCacheFailure(exception))
        {
            // A palette that cannot be cached only costs the offline re-skin, so never fail the catalog for it.
            _logger.LogWarning(
                exception,
                "Could not cache the published palette for {ModificationName} {Version}.",
                modificationName,
                version);
        }
    }

    public LauncherContentTheme? Load(string modificationName, string version)
    {
        if (!TryResolveDocumentPath(modificationName, version, out string documentPath))
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
        catch (Exception exception) when (IsRecoverableCacheFailure(exception))
        {
            _logger.LogWarning(
                exception,
                "Could not read the cached palette for {ModificationName} {Version}.",
                modificationName,
                version);
            return null;
        }
    }

    private bool TryResolveDocumentPath(string modificationName, string version, out string documentPath)
    {
        documentPath = string.Empty;
        if (string.IsNullOrWhiteSpace(modificationName) || string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        try
        {
            LauncherPaths paths = _runtimePathContext.ActivePaths;
            documentPath = ModificationImageCachePath.ResolvePath(
                paths,
                paths.GetModificationImageFilePath(
                    modificationName,
                    LauncherContentTheme.ResolveCacheBaseName(version) + ".yaml"));
            return true;
        }
        catch (Exception exception) when (IsRecoverableCacheFailure(exception))
        {
            _logger.LogWarning(
                exception,
                "Could not resolve the palette cache path for {ModificationName} {Version}.",
                modificationName,
                version);
            return false;
        }
    }

    private IYamlDocumentStore<LauncherContentTheme> CreateDocumentStore(string documentPath)
    {
        return new YamlDocumentStore<LauncherContentTheme>(documentPath, _atomicFileWriter, _documentLogger);
    }

    private static bool IsRecoverableCacheFailure(Exception exception)
    {
        return exception is InvalidDataException or IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException;
    }
}
