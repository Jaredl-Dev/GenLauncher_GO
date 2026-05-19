using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Exceptions;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Contracts;
using GenLauncherGO.Infrastructure.Mods.Models;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Mods.Services;

/// <summary>
/// Coordinates remote launcher catalog data, local content state, cached images, and selection persistence.
/// </summary>
internal sealed class LauncherContentCatalogService : ILauncherContentCatalog
{
    private const int MaxConcurrentImageCacheUpdates = 8;

    private readonly ILauncherContentStateStore _contentStateStore;

    private readonly RemoteLauncherCatalogClient _remoteCatalogClient;

    private readonly LauncherCatalogImageCache _imageCache;

    private readonly LauncherLocalContentReconciler _localContentReconciler;

    private readonly ILogger<LauncherContentCatalogService> _logger;

    private CatalogSessionState _state = new();

    /// <summary>
    /// Serializes asynchronous catalog mutation so in-flight work cannot cross a game-session boundary.
    /// </summary>
    private readonly SemaphoreSlim _catalogMutationGate = new(1, 1);

    public LauncherContentCatalogService(
        ILauncherContentStateStore contentStateStore,
        RemoteLauncherCatalogClient remoteCatalogClient,
        LauncherCatalogImageCache imageCache,
        LauncherLocalContentReconciler localContentReconciler,
        ILogger<LauncherContentCatalogService> logger)
    {
        _contentStateStore = contentStateStore ?? throw new ArgumentNullException(nameof(contentStateStore));
        _remoteCatalogClient = remoteCatalogClient ?? throw new ArgumentNullException(nameof(remoteCatalogClient));
        _imageCache = imageCache ?? throw new ArgumentNullException(nameof(imageCache));
        _localContentReconciler =
            localContentReconciler ?? throw new ArgumentNullException(nameof(localContentReconciler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public LauncherData Data => _state.Data;

    public LauncherContentVersion? Advertising => _state.Advertising;

    public IReadOnlyList<string>? RepositoryModificationNames => _state.RepositoryModificationNames;

    public async Task InitDataAsync(
        LauncherContentCatalogInitializationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Paths);

        await _catalogMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        CatalogSessionState previousState = _state;
        try
        {
            _state = new CatalogSessionState(request.Paths);
            await InitializeDataCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _state = previousState;
            throw;
        }
        finally
        {
            _catalogMutationGate.Release();
        }
    }

    public async Task ReadOriginalGameAddonsAndPatchesAsync(CancellationToken cancellationToken)
    {
        await _catalogMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReadOriginalGameAddonsAndPatchesCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _catalogMutationGate.Release();
        }
    }

    public async Task<LauncherContentVersion> GetRepositoryModificationMetadataAsync(
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await _catalogMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RemoteModificationManifest manifest = await GetRepositoryModificationManifestAsync(
                name,
                cancellationToken).ConfigureAwait(false);
            return manifest.Content;
        }
        finally
        {
            _catalogMutationGate.Release();
        }
    }

    public async Task<LauncherContentVersion> AddRepositoryModificationAsync(
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await _catalogMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RemoteModificationManifest manifest = await GetRepositoryModificationManifestAsync(
                name,
                cancellationToken).ConfigureAwait(false);
            AddRemoteModificationManifest(manifest);

            await _imageCache.CacheModificationImagesAsync(manifest.Content, Paths, cancellationToken)
                .ConfigureAwait(false);
            AddDownloadedModificationData(manifest.Content);
            return manifest.Content;
        }
        finally
        {
            _catalogMutationGate.Release();
        }
    }

    /// <summary>
    /// Reads and caches one manifest while the caller owns the catalog mutation gate.
    /// </summary>
    private async Task<RemoteModificationManifest> GetRepositoryModificationManifestAsync(
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var contentKey = LauncherContentKey.ForModificationName(name);
        if (_state.RepositoryMetadataCache.TryGetValue(
                contentKey,
                out RemoteModificationManifest? cachedManifest))
        {
            return cachedManifest;
        }

        RemoteModificationManifest manifest = await _remoteCatalogClient.DownloadModDataByNameAsync(
            _state.RepositoryData ?? RemoteLauncherCatalog.Empty,
            name,
            cancellationToken).ConfigureAwait(false);
        _state.RepositoryMetadataCache[contentKey] = manifest;
        return manifest;
    }

    public void UninstallVersion(LauncherContentKey contentKey)
    {
        _localContentReconciler.DeleteVersion(contentKey, Paths);
        UpdateLocalModificationsData();
    }

    public void DiscardVersion(LauncherContentKey contentKey)
    {
        _localContentReconciler.DeleteVersion(contentKey, Paths);
        _state.Data.DeleteVersion(contentKey);
        UpdateLocalModificationsData();
    }

    public void DiscardContent(LauncherContentKey contentKey)
    {
        _localContentReconciler.DeleteContent(contentKey, Paths);
        _state.Data.DeleteContent(contentKey);
        UpdateLocalModificationsData();
    }

    public async Task ReadPatchesAndAddonsForModAsync(
        LauncherContentKey modificationKey,
        CancellationToken cancellationToken)
    {
        await _catalogMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReadPatchesAndAddonsForModCoreAsync(modificationKey, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _catalogMutationGate.Release();
        }
    }

    public void UpdateLocalModificationsData()
    {
        _localContentReconciler.Reconcile(_state.Data, _state.DownloadedRepositoryContent, Paths);
    }

    private void ReadLocalModsData()
    {
        _state.Data = LauncherContentStateMapper.ToLauncherData(_contentStateStore.Load(Paths));
    }

    public void SaveLauncherData()
    {
        try
        {
            _contentStateStore.Save(Paths, LauncherContentStateMapper.ToLauncherContentState(_state.Data));
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to persist launcher content state. The current in-memory catalog remains available for retry.");
            throw new LauncherContentPersistenceException(exception);
        }
    }

    private LauncherPaths Paths =>
        _state.Paths ?? throw new InvalidOperationException("Launcher content catalog has not been initialized.");

    /// <summary>
    /// Loads one game-specific catalog after the previous state has been isolated for rollback.
    /// </summary>
    private async Task InitializeDataCoreAsync(
        LauncherContentCatalogInitializationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RemoteManifestUri is not null)
        {
            await ReadMainManifestAsync(request.RemoteManifestUri, cancellationToken).ConfigureAwait(false);
        }

        ReadLocalModsData();
        UpdateLocalModificationsData();

        if (_state.RepositoryData is null)
        {
            LogCatalogInitialized();
            return;
        }

        RemoteLauncherCatalog repositoryData = _state.RepositoryData;
        var installedMods = _state.Data.Modifications.Select(mod => mod.Name).ToList();
        _state.RepositoryModificationNames = _remoteCatalogClient.GetModificationNames(repositoryData);

        IReadOnlyList<RemoteModificationManifest> installedManifests = await _remoteCatalogClient
            .DownloadInstalledModDataAsync(
                repositoryData,
                installedMods,
                cancellationToken)
            .ConfigureAwait(false);
        _state.ModificationsAndAddons = ToManifestDictionary(installedManifests);
        var reposMods = installedManifests.Select(manifest => manifest.Content).ToList();

        await CacheInstalledModificationImagesAsync(reposMods, cancellationToken).ConfigureAwait(false);

        foreach (LauncherContentVersion reposMod in reposMods)
        {
            AddDownloadedModificationData(reposMod);
        }

        LauncherContent? selectedMod = _state.Data.GetSelectedMod();
        if (selectedMod != null)
        {
            await ReadPatchesAndAddonsForModCoreAsync(selectedMod.ContentKey, cancellationToken)
                .ConfigureAwait(false);
        }

        LogCatalogInitialized();
    }

    /// <summary>
    /// Loads original-game children while the caller owns the catalog mutation gate.
    /// </summary>
    private async Task ReadOriginalGameAddonsAndPatchesCoreAsync(CancellationToken cancellationToken)
    {
        if (_state.RepositoryData is null)
        {
            return;
        }

        RemoteLauncherCatalog repositoryData = _state.RepositoryData;
        LauncherContentKey originalGameKey = LauncherContentKey.OriginalGame;
        if (_state.DownloadedModificationInfo.Contains(originalGameKey))
        {
            return;
        }

        Task<RemoteChildManifestLoadResult> reposPatchesTask = _remoteCatalogClient.ReadChildManifestsAsync(
            repositoryData.OriginalGamePatchManifestUrls,
            originalGameKey.Name,
            cancellationToken);
        Task<RemoteChildManifestLoadResult> reposAddonsTask = _remoteCatalogClient.ReadChildManifestsAsync(
            repositoryData.OriginalGameAddonManifestUrls,
            originalGameKey.Name,
            cancellationToken);
        RemoteChildManifestLoadResult[] childManifestLoads =
            await Task.WhenAll(reposPatchesTask, reposAddonsTask).ConfigureAwait(false);
        RemoteChildManifestLoadResult patchLoad = childManifestLoads[0];
        RemoteChildManifestLoadResult addonLoad = childManifestLoads[1];

        foreach (LauncherContentVersion patch in patchLoad.ContentVersions)
        {
            _state.Data.AddOrUpdate(patch);
            _state.DownloadedRepositoryContent.Add(patch.ContentKey);
        }

        foreach (LauncherContentVersion addon in addonLoad.ContentVersions)
        {
            _state.Data.AddOrUpdate(addon);
            _state.DownloadedRepositoryContent.Add(addon.ContentKey);
        }

        if (patchLoad.Succeeded && addonLoad.Succeeded)
        {
            _state.DownloadedModificationInfo.Add(originalGameKey);
        }
    }

    /// <summary>
    /// Loads one modification's children while the caller owns the catalog mutation gate.
    /// </summary>
    private async Task ReadPatchesAndAddonsForModCoreAsync(
        LauncherContentKey modificationKey,
        CancellationToken cancellationToken)
    {
        if (_state.RepositoryData is null)
        {
            return;
        }

        var keyModification = LauncherContentKey.ForModificationName(modificationKey.Name);
        if (_state.DownloadedModificationInfo.Contains(keyModification))
        {
            return;
        }

        if (!_state.ModificationsAndAddons.TryGetValue(
                keyModification,
                out RemoteModificationManifest? modData))
        {
            return;
        }

        Task<RemoteChildManifestLoadResult> reposPatchesTask = _remoteCatalogClient.ReadChildManifestsAsync(
            modData.PatchManifestUrls,
            parentContentName: null,
            cancellationToken);
        Task<RemoteChildManifestLoadResult> reposAddonsTask = _remoteCatalogClient.ReadChildManifestsAsync(
            modData.AddonManifestUrls,
            parentContentName: null,
            cancellationToken);
        RemoteChildManifestLoadResult[] childManifestLoads =
            await Task.WhenAll(reposPatchesTask, reposAddonsTask).ConfigureAwait(false);
        RemoteChildManifestLoadResult patchLoad = childManifestLoads[0];
        RemoteChildManifestLoadResult addonLoad = childManifestLoads[1];

        foreach (LauncherContentVersion patch in patchLoad.ContentVersions)
        {
            AddDownloadedModificationData(patch);
        }

        foreach (LauncherContentVersion addon in addonLoad.ContentVersions)
        {
            AddDownloadedModificationData(addon);
        }

        if (patchLoad.Succeeded && addonLoad.Succeeded)
        {
            _state.DownloadedModificationInfo.Add(keyModification);
        }
    }

    /// <summary>
    /// Reads the top-level remote manifest and related advertising metadata.
    /// </summary>
    private async Task ReadMainManifestAsync(Uri manifestUri, CancellationToken cancellationToken)
    {
        RemoteLauncherCatalog repositoryData = await _remoteCatalogClient.ReadCatalogAsync(
            manifestUri,
            cancellationToken).ConfigureAwait(false);
        _state.RepositoryData = repositoryData;

        if (repositoryData.AdvertisingEntries.Count > 0)
        {
            await DownloadAdvertisingDataAsync(
                repositoryData.AdvertisingEntries[0],
                cancellationToken).ConfigureAwait(false);
        }
    }

    private void AddDownloadedModificationData(LauncherContentVersion version)
    {
        _state.Data.AddOrUpdate(version);
        _state.DownloadedRepositoryContent.Add(version.ContentKey);
    }

    /// <summary>
    /// Caches installed modification images with bounded parallelism.
    /// </summary>
    private async Task CacheInstalledModificationImagesAsync(
        IReadOnlyList<LauncherContentVersion> modifications,
        CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(MaxConcurrentImageCacheUpdates);
        await Task.WhenAll(modifications.Select(modification => CacheModificationImagesAsync(
            modification,
            semaphore,
            cancellationToken))).ConfigureAwait(false);
    }

    /// <summary>
    /// Caches one modification's images while respecting the startup cache concurrency limit.
    /// </summary>
    private async Task CacheModificationImagesAsync(
        LauncherContentVersion modification,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _imageCache.CacheModificationImagesAsync(modification, Paths, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task DownloadAdvertisingDataAsync(
        RemoteAdvertisingReference advertisingData,
        CancellationToken cancellationToken)
    {
        _state.Advertising = await _remoteCatalogClient.DownloadAdvertisingInfoAsync(
            advertisingData.ManifestUrl,
            cancellationToken).ConfigureAwait(false);
        if (_state.Advertising is null)
        {
            return;
        }

        await _imageCache.CacheAdvertisingImagesAsync(advertisingData, Paths, cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<LauncherContentKey, RemoteModificationManifest> ToManifestDictionary(
        IEnumerable<RemoteModificationManifest> manifests)
    {
        var result = new Dictionary<LauncherContentKey, RemoteModificationManifest>();
        foreach (RemoteModificationManifest manifest in manifests)
        {
            var key = LauncherContentKey.ForModificationName(manifest.Content.Name);
            if (!result.ContainsKey(key))
            {
                result.Add(key, manifest);
            }
        }

        return result;
    }

    private void AddRemoteModificationManifest(RemoteModificationManifest manifest)
    {
        var key = LauncherContentKey.ForModificationName(manifest.Content.Name);
        if (!_state.ModificationsAndAddons.ContainsKey(key))
        {
            _state.ModificationsAndAddons.Add(key, manifest);
        }
    }

    /// <summary>
    /// Logs a compact catalog initialization summary without local paths or remote URLs.
    /// </summary>
    private void LogCatalogInitialized()
    {
        _logger.LogInformation(
            "Initialized launcher content catalog. Connected: {Connected}; modifications: {ModificationCount}; " +
            "patches: {PatchCount}; add-ons: {AddonCount}; versions: {VersionCount}; " +
            "repository modifications: {RepositoryModificationCount}.",
            _state.RepositoryData is not null,
            _state.Data.Modifications.Count,
            _state.Data.Patches.Count,
            _state.Data.Addons.Count,
            CountVersions(_state.Data),
            RepositoryModificationNames?.Count ?? 0);
    }

    private sealed class CatalogSessionState
    {
        public CatalogSessionState(LauncherPaths? paths = null)
        {
            Paths = paths;
        }

        public LauncherData Data { get; set; } = new();

        public Dictionary<LauncherContentKey, RemoteModificationManifest> ModificationsAndAddons { get; set; } =
            new();

        public Dictionary<LauncherContentKey, RemoteModificationManifest> RepositoryMetadataCache { get; } = new();

        public HashSet<LauncherContentKey> DownloadedModificationInfo { get; } = new();

        public HashSet<LauncherContentKey> DownloadedRepositoryContent { get; } = new();

        public LauncherContentVersion? Advertising { get; set; }

        public LauncherPaths? Paths { get; }

        public RemoteLauncherCatalog? RepositoryData { get; set; }

        public IReadOnlyList<string>? RepositoryModificationNames { get; set; }
    }

    private static int CountVersions(LauncherData data)
    {
        return data.Modifications.Sum(modification => modification.Versions.Count) +
               data.Patches.Sum(modification => modification.Versions.Count) +
               data.Addons.Sum(modification => modification.Versions.Count);
    }
}
