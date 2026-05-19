using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Support;
using GenLauncherGO.Infrastructure.Remote.Contracts;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Mods.Services;

/// <summary>
/// Reads legacy-compatible remote launcher catalog YAML documents.
/// </summary>
/// <remarks>
/// The remote catalog schema is owned by a third-party backend. This client must continue using the legacy manifest
/// DTOs and field names unless a future change adds explicit dual-schema read support and backend compatibility tests.
/// </remarks>
internal sealed class RemoteLauncherCatalogClient
{
    private const int MaxConcurrentManifestReads = 6;

    private readonly IRemoteYamlDocumentReader _yamlDocumentReader;

    private readonly ILogger<RemoteLauncherCatalogClient> _logger;

    public RemoteLauncherCatalogClient(
        IRemoteYamlDocumentReader yamlDocumentReader,
        ILogger<RemoteLauncherCatalogClient> logger)
    {
        _yamlDocumentReader = yamlDocumentReader ?? throw new ArgumentNullException(nameof(yamlDocumentReader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RemoteLauncherCatalog> ReadCatalogAsync(Uri manifestUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifestUri);

        LegacyLauncherCatalogDocument? repositoryData =
            await _yamlDocumentReader.ReadYamlAsync<LegacyLauncherCatalogDocument>(
            manifestUri,
            cancellationToken).ConfigureAwait(false);
        return RemoteLauncherCatalogMapper.ToRemoteCatalog(repositoryData);
    }

    public IReadOnlyList<string> GetModificationNames(RemoteLauncherCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return catalog.Modifications
            .Select(modification => modification.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<RemoteModificationManifest>> DownloadInstalledModDataAsync(
        RemoteLauncherCatalog catalog,
        IReadOnlyCollection<string> installedModNames,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(installedModNames);

        var downloadedModNames = installedModNames
            .Select(LauncherContentKey.ForModificationName)
            .ToHashSet();
        var installedModData = catalog.Modifications
            .Where(reference =>
                String.IsNullOrEmpty(reference.Name) ||
                downloadedModNames.Contains(LauncherContentKey.ForModificationName(reference.Name)))
            .ToList();

        using var semaphore = new SemaphoreSlim(MaxConcurrentManifestReads);
        RemoteModificationManifest?[] results = await Task.WhenAll(
            installedModData.Select(reference => DownloadModDataIfAvailableAsync(
                reference,
                semaphore,
                cancellationToken))).ConfigureAwait(false);

        var mods = new Dictionary<LauncherContentKey, RemoteModificationManifest>();
        foreach (RemoteModificationManifest? result in results)
        {
            if (result is null)
            {
                continue;
            }

            var key = LauncherContentKey.ForModificationName(result.Content.Name);
            if (!mods.ContainsKey(key))
            {
                mods.Add(key, result);
            }
        }

        return mods.Values.ToList();
    }

    public async Task<RemoteModificationManifest> DownloadModDataByNameAsync(
        RemoteLauncherCatalog catalog,
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        RemoteCatalogModificationReference reference = catalog.Modifications
            .First(data => LauncherContentKey.ForModificationName(data.Name) ==
                           LauncherContentKey.ForModificationName(name));

        return await DownloadModDataAsync(reference, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RemoteChildManifestLoadResult> ReadChildManifestsAsync(
        IEnumerable<string> manifestUrls,
        string? parentContentName,
        CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(MaxConcurrentManifestReads);
        LauncherContentVersion?[] contentVersions = await Task.WhenAll(
            (manifestUrls ?? new List<string>()).Select(url => ReadChildManifestIfAvailableAsync(
                url,
                parentContentName,
                semaphore,
                cancellationToken))).ConfigureAwait(false);

        IReadOnlyList<LauncherContentVersion> successfulVersions =
            contentVersions.Where(version => version != null).ToList()!;
        return new RemoteChildManifestLoadResult(
            successfulVersions,
            contentVersions.Count(version => version is null));
    }

    /// <summary>
    /// Downloads one modification manifest while respecting the startup refresh concurrency limit.
    /// </summary>
    private async Task<RemoteModificationManifest?> DownloadModDataIfAvailableAsync(
        RemoteCatalogModificationReference reference,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                return await DownloadModDataAsync(reference, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "Failed to download remote modification manifest for {ModificationName}: {FailureReason}.",
                    reference.Name,
                    exception.Message);
                return null;
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Reads one child manifest while respecting the startup refresh concurrency limit.
    /// </summary>
    private async Task<LauncherContentVersion?> ReadChildManifestIfAvailableAsync(
        string url,
        string? parentContentName,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                return await ReadModificationYamlAsync(
                    url,
                    parentContentName,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "Failed to read child modification manifest: {FailureReason}.",
                    exception.Message);
                return null;
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<LauncherContentVersion?> DownloadAdvertisingInfoAsync(
        string manifestUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReadModificationYamlAsync(
                manifestUrl,
                parentContentName: null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Failed to download advertising manifest: {FailureReason}.",
                exception.Message);
        }

        return null;
    }

    private async Task<RemoteModificationManifest> DownloadModDataAsync(
        RemoteCatalogModificationReference reference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);

        LauncherContentVersion modification = await ReadModificationYamlAsync(
            reference.ManifestUrl,
            parentContentName: null,
            cancellationToken).ConfigureAwait(false);
        return new RemoteModificationManifest(
            modification,
            reference.PatchManifestUrls,
            reference.AddonManifestUrls);
    }

    private async Task<LauncherContentVersion> ReadModificationYamlAsync(
        string documentUrl,
        string? parentContentName,
        CancellationToken cancellationToken)
    {
        LegacyContentManifest manifest = await _yamlDocumentReader.ReadYamlAsync<LegacyContentManifest>(
            new Uri(documentUrl, UriKind.Absolute),
            cancellationToken).ConfigureAwait(false);
        return RemoteLauncherCatalogMapper.ToLauncherContentVersion(manifest, parentContentName);
    }
}
