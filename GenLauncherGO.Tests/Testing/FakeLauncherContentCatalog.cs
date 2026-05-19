using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Tests.Testing;

internal sealed class FakeLauncherContentCatalog : ILauncherContentCatalog
{
    private readonly HashSet<LauncherContentKey> _repositoryContent = new();

    public LauncherData Data { get; set; } = new();

    public LauncherContentVersion? Advertising { get; set; }

    public IReadOnlyList<string>? RepositoryModificationNames { get; set; }

    public List<LauncherContentCatalogInitializationRequest> InitializationRequests { get; } = new();

    public List<string> DownloadRequests { get; } = new();

    public List<string> MetadataRequests { get; } = new();

    public List<LauncherContentKey> ChildManifestRequests { get; } = new();

    public List<LauncherContentKey> UninstalledVersions { get; } = new();

    public List<LauncherContentKey> DiscardedVersions { get; } = new();

    public List<LauncherContentKey> DiscardedContents { get; } = new();

    public int OriginalGameChildManifestReadCount { get; private set; }

    public int LocalDataUpdateCount { get; private set; }

    public int SaveCount { get; private set; }

    public Func<LauncherContentCatalogInitializationRequest, CancellationToken, Task>? InitializationHandler
    {
        get;
        set;
    }

    public Func<CancellationToken, Task>? OriginalGameChildManifestReadHandler { get; set; }

    public Func<string, CancellationToken, Task<LauncherContentVersion>>? DownloadHandler { get; set; }

    public Func<string, CancellationToken, Task<LauncherContentVersion>>? MetadataHandler { get; set; }

    public Func<LauncherContentKey, CancellationToken, Task>? ChildManifestReadHandler { get; set; }

    public Action<LauncherContentKey>? UninstallVersionHandler { get; set; }

    public Action<LauncherContentKey>? DiscardVersionHandler { get; set; }

    public Action<LauncherContentKey>? DiscardContentHandler { get; set; }

    public Action? LocalDataUpdateHandler { get; set; }

    public Action? SaveHandler { get; set; }

    public Task InitDataAsync(
        LauncherContentCatalogInitializationRequest request,
        CancellationToken cancellationToken)
    {
        InitializationRequests.Add(request);
        return InitializationHandler?.Invoke(request, cancellationToken) ?? Task.CompletedTask;
    }

    public Task ReadOriginalGameAddonsAndPatchesAsync(CancellationToken cancellationToken)
    {
        OriginalGameChildManifestReadCount++;
        return OriginalGameChildManifestReadHandler?.Invoke(cancellationToken) ?? Task.CompletedTask;
    }

    public Task<LauncherContentVersion> GetRepositoryModificationMetadataAsync(
        string name,
        CancellationToken cancellationToken)
    {
        MetadataRequests.Add(name);
        return MetadataHandler?.Invoke(name, cancellationToken) ??
               Task.FromException<LauncherContentVersion>(
                   new InvalidOperationException($"No metadata result was configured for '{name}'."));
    }

    public async Task<LauncherContentVersion> AddRepositoryModificationAsync(
        string name,
        CancellationToken cancellationToken)
    {
        DownloadRequests.Add(name);
        LauncherContentVersion modification = await (DownloadHandler?.Invoke(name, cancellationToken) ??
            Task.FromException<LauncherContentVersion>(
                new InvalidOperationException($"No download result was configured for '{name}'.")));
        Data.AddOrUpdate(modification);
        _repositoryContent.Add(modification.ContentKey);
        return modification;
    }

    public Task ReadPatchesAndAddonsForModAsync(
        LauncherContentKey modificationKey,
        CancellationToken cancellationToken)
    {
        ChildManifestRequests.Add(modificationKey);
        return ChildManifestReadHandler?.Invoke(modificationKey, cancellationToken) ?? Task.CompletedTask;
    }

    public void UninstallVersion(LauncherContentKey contentKey)
    {
        UninstalledVersions.Add(contentKey);
        LauncherContentVersion? version = Data.FindContent(contentKey)?.Versions
            .FirstOrDefault(candidate => candidate.ContentKey == contentKey);
        if (version is not null)
        {
            if (_repositoryContent.Contains(contentKey) ||
                version.EffectiveContentSourceKind is
                    ContentSourceKind.ManagedS3 or ContentSourceKind.ManagedSingleFile)
            {
                version.Installation.Installed = false;
            }
            else
            {
                Data.DeleteVersion(contentKey);
            }
        }

        UninstallVersionHandler?.Invoke(contentKey);
        UpdateLocalModificationsData();
    }

    public void DiscardVersion(LauncherContentKey contentKey)
    {
        DiscardedVersions.Add(contentKey);
        Data.DeleteVersion(contentKey);
        _repositoryContent.Remove(contentKey);
        DiscardVersionHandler?.Invoke(contentKey);
        UpdateLocalModificationsData();
    }

    public void DiscardContent(LauncherContentKey contentKey)
    {
        DiscardedContents.Add(contentKey);
        Data.DeleteContent(contentKey);
        _repositoryContent.RemoveWhere(candidate =>
            candidate.ContentType == contentKey.ContentType &&
            String.Equals(candidate.ParentIdentity, contentKey.ParentIdentity, StringComparison.OrdinalIgnoreCase) &&
            String.Equals(candidate.Name, contentKey.Name, StringComparison.OrdinalIgnoreCase));
        DiscardContentHandler?.Invoke(contentKey);
        UpdateLocalModificationsData();
    }

    public void UpdateLocalModificationsData()
    {
        LocalDataUpdateCount++;
        LocalDataUpdateHandler?.Invoke();
    }

    public void SaveLauncherData()
    {
        SaveCount++;
        SaveHandler?.Invoke();
    }
}
