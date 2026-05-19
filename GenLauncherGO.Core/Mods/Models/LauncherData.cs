using System;
using System.Collections.Generic;
using System.Linq;

namespace GenLauncherGO.Core.Mods.Models;

/// <summary>
/// Stores the active launcher content catalog and local state.
/// </summary>
public sealed class LauncherData
{
    private readonly List<LauncherContent> _addons = new();
    private readonly List<LauncherContent> _modifications = new();
    private readonly List<LauncherContent> _patches = new();

    public LauncherData()
    {
        Addons = _addons.AsReadOnly();
        Modifications = _modifications.AsReadOnly();
        Patches = _patches.AsReadOnly();
    }

    public IReadOnlyList<LauncherContent> Addons { get; }

    public IReadOnlyList<LauncherContent> Modifications { get; }

    public IReadOnlyList<LauncherContent> Patches { get; }

    public LauncherContent? GetSelectedMod()
    {
        return _modifications.FirstOrDefault(modification => modification.IsSelected);
    }

    /// <summary>
    /// Gets patches associated with the supplied modification, or the original game when none is supplied.
    /// </summary>
    public IReadOnlyList<LauncherContent> GetPatchesFor(LauncherContent? modification)
    {
        LauncherContentKey parentKey = modification?.ContentKey ?? LauncherContentKey.OriginalGame;

        return _patches
            .Where(patch => patch.ContentKey.IsChildOf(parentKey))
            .ToList();
    }

    /// <summary>
    /// Gets add-ons associated with the supplied modification or patch.
    /// </summary>
    public IReadOnlyList<LauncherContent> GetAddonsFor(
        LauncherContent? modification,
        LauncherContent? patch)
    {
        LauncherContentKey parentKey = modification?.ContentKey ?? LauncherContentKey.OriginalGame;
        LauncherContentKey? patchKey = patch?.ContentKey;

        return _addons
            .Where(addon => addon.ContentKey.IsChildOf(parentKey))
            .Union(_addons.Where(addon =>
                patchKey.HasValue &&
                addon.ContentKey.IsChildOf(patchKey.Value)))
            .ToList();
    }

    public IReadOnlyList<LauncherContentVersion> GetAllModsVersionsList()
    {
        return _modifications
            .SelectMany(modification => modification.Versions)
            .ToList();
    }

    public LauncherContent? FindContent(LauncherContentKey contentKey)
    {
        List<LauncherContent>? contentStorage = GetContentStorage(contentKey.ContentType);
        LauncherContentKey cardKey = contentKey.WithoutVersion();
        return contentStorage?.FirstOrDefault(content => content.ContentKey == cardKey);
    }

    /// <summary>
    /// Adds a supported content version or merges it into the matching content card.
    /// </summary>
    public void AddOrUpdate(LauncherContentVersion modificationVersion)
    {
        ArgumentNullException.ThrowIfNull(modificationVersion);

        if (modificationVersion.ModificationType == ModificationType.Addon &&
            String.IsNullOrEmpty(modificationVersion.ParentContentName))
        {
            return;
        }

        List<LauncherContent>? contentStorage = GetContentStorage(modificationVersion.ModificationType);
        if (contentStorage != null)
        {
            AddOrUpdateModificationVersion(contentStorage, modificationVersion);
        }
    }

    /// <summary>
    /// Deletes a version and removes dependent patch or add-on cards when their parent is removed.
    /// </summary>
    public void DeleteVersion(LauncherContentKey contentKey)
    {
        List<LauncherContent>? contentStorage = GetContentStorage(contentKey.ContentType);
        if (contentStorage is null)
        {
            return;
        }

        bool removedContentCard = DeleteModificationVersion(contentStorage, contentKey);
        if (!removedContentCard)
        {
            return;
        }

        DeleteDependentContent(contentKey);
    }

    /// <summary>
    /// Deletes an entire content card and any patch or add-on cards that depend on it.
    /// </summary>
    public void DeleteContent(LauncherContentKey contentKey)
    {
        List<LauncherContent>? contentStorage = GetContentStorage(contentKey.ContentType);
        if (contentStorage is null)
        {
            return;
        }

        LauncherContentKey cardKey = contentKey.WithoutVersion();
        int removedCount = contentStorage.RemoveAll(content => content.ContentKey == cardKey);
        if (removedCount == 0)
        {
            return;
        }

        DeleteDependentContent(contentKey);
    }

    private void DeleteDependentContent(LauncherContentKey contentKey)
    {
        if (contentKey.ContentType == ModificationType.Mod)
        {
            DeleteDependentContent(contentKey, _addons, _patches);
        }
        else if (contentKey.ContentType == ModificationType.Patch)
        {
            DeleteDependentAddons(contentKey, _addons);
        }
    }

    private static void AddOrUpdateModificationVersion(
        List<LauncherContent> modificationStorage,
        LauncherContentVersion modificationVersion)
    {
        LauncherContentKey cardKey = modificationVersion.ContentKey.WithoutVersion();
        int modificationIndex = modificationStorage.FindIndex(savedModification =>
            savedModification.ContentKey == cardKey);

        if (modificationIndex >= 0)
        {
            LauncherContent savedModificationData = modificationStorage[modificationIndex];
            savedModificationData.AddOrMergeVersion(modificationVersion);
        }
        else
        {
            modificationStorage.Add(new LauncherContent(modificationVersion));
        }
    }

    private static bool DeleteModificationVersion(
        List<LauncherContent> modificationStorage,
        LauncherContentKey contentKey)
    {
        LauncherContentKey cardKey = contentKey.WithoutVersion();
        int modificationIndex = modificationStorage.FindIndex(savedModification =>
            savedModification.ContentKey == cardKey);

        if (modificationIndex < 0)
        {
            return false;
        }

        LauncherContent savedModificationData = modificationStorage[modificationIndex];
        savedModificationData.RemoveVersion(contentKey);

        if (savedModificationData.Versions.Count == 0)
        {
            modificationStorage.RemoveAt(modificationIndex);
            return true;
        }

        return false;
    }

    private static void DeleteDependentContent(
        LauncherContentKey contentKey,
        List<LauncherContent> addons,
        List<LauncherContent> patches)
    {
        var dependentPatches = patches
            .Where(patch => IsDependentOn(patch, contentKey))
            .ToList();

        foreach (LauncherContent patch in dependentPatches)
        {
            DeleteDependentAddons(patch.ContentKey, addons);
            patches.Remove(patch);
        }

        DeleteDependentAddons(contentKey, addons);
    }

    private static void DeleteDependentAddons(
        LauncherContentKey parentKey,
        List<LauncherContent> addons)
    {
        addons.RemoveAll(addon => IsDependentOn(addon, parentKey));
    }

    private static bool IsDependentOn(
        LauncherContent modification,
        LauncherContentKey parentKey)
    {
        return !String.IsNullOrWhiteSpace(parentKey.Name) &&
               modification.ContentKey.IsChildOf(parentKey);
    }

    private List<LauncherContent>? GetContentStorage(ModificationType contentType)
    {
        return contentType switch
        {
            ModificationType.Mod => _modifications,
            ModificationType.Addon => _addons,
            ModificationType.Patch => _patches,
            _ => null
        };
    }
}
