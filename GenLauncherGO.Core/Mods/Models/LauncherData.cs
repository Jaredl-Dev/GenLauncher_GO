using System;
using System.Collections.Generic;
using System.Linq;

namespace GenLauncherGO.Core.Mods.Models;

/// <summary>
///     Stores the active launcher content catalog and local state.
/// </summary>
public sealed class LauncherData
{
    private readonly List<LauncherContent> _addons = [];
    private readonly List<LauncherContent> _modifications = [];
    private readonly List<LauncherContent> _patches = [];

    public LauncherData()
    {
        Addons = _addons.AsReadOnly();
        Modifications = _modifications.AsReadOnly();
        Patches = _patches.AsReadOnly();
    }

    public IReadOnlyList<LauncherContent> Addons { get; }

    public IReadOnlyList<LauncherContent> Modifications { get; }

    public IReadOnlyList<LauncherContent> Patches { get; }

    /// <summary>
    ///     Enumerates every modification, add-on, and patch in catalog order.
    /// </summary>
    public IEnumerable<LauncherContent> AllContent => _modifications.Concat(_addons).Concat(_patches);

    public LauncherContent? GetSelectedMod()
    {
        return _modifications.FirstOrDefault(modification => modification.IsSelected);
    }

    /// <summary>
    ///     Gets patches associated with the supplied modification, or the original game when none is supplied.
    /// </summary>
    public IReadOnlyList<LauncherContent> GetPatchesFor(LauncherContent? modification)
    {
        LauncherContentKey parentKey = modification?.ContentKey ?? LauncherContentKey.OriginalGame;

        return _patches
            .Where(patch => patch.ContentKey.IsChildOf(parentKey))
            .ToList();
    }

    /// <summary>
    ///     Gets add-ons associated with the supplied modification or patch.
    /// </summary>
    public IReadOnlyList<LauncherContent> GetAddonsFor(
        LauncherContent? modification,
        LauncherContent? patch)
    {
        LauncherContentKey parentKey = modification?.ContentKey ?? LauncherContentKey.OriginalGame;
        LauncherContentKey? patchKey = patch?.ContentKey;

        // Direct add-ons stay ahead of patch add-ons regardless of catalog insertion order.
        return _addons
            .Where(addon => addon.ContentKey.IsChildOf(parentKey))
            .Union(_addons.Where(addon =>
                patchKey.HasValue &&
                addon.ContentKey.IsChildOf(patchKey.Value)))
            .ToList();
    }

    public IReadOnlyList<LauncherContentVersion> GetAllModificationVersions()
    {
        return _modifications
            .SelectMany(modification => modification.Versions)
            .ToList();
    }

    public LauncherContent? FindContent(LauncherContentKey contentKey)
    {
        List<LauncherContent>? contentStorage = GetContentStorage(contentKey.ContentType);
        return contentStorage is null
            ? null
            : FindContentCard(contentStorage, contentKey);
    }

    /// <summary>
    ///     Adds a supported content version or merges it into the matching content card.
    /// </summary>
    public void AddOrUpdate(LauncherContentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (version.ModificationType == ModificationType.Addon &&
            string.IsNullOrEmpty(version.ParentContentName))
        {
            return;
        }

        List<LauncherContent>? contentStorage = GetContentStorage(version.ModificationType);
        if (contentStorage != null)
        {
            AddOrUpdateContentVersion(contentStorage, version);
        }
    }

    /// <summary>
    ///     Deletes a version and removes dependent patch or add-on cards when their parent is removed.
    /// </summary>
    public void DeleteVersion(LauncherContentKey contentKey)
    {
        List<LauncherContent>? contentStorage = GetContentStorage(contentKey.ContentType);
        if (contentStorage is null)
        {
            return;
        }

        bool removedContentCard = DeleteContentVersion(contentStorage, contentKey);
        if (!removedContentCard)
        {
            return;
        }

        DeleteDependentContent(contentKey);
    }

    /// <summary>
    ///     Deletes an entire content card and any patch or add-on cards that depend on it.
    /// </summary>
    public void DeleteContent(LauncherContentKey contentKey)
    {
        List<LauncherContent>? contentStorage = GetContentStorage(contentKey.ContentType);
        if (contentStorage is null)
        {
            return;
        }

        LauncherContent? content = FindContentCard(contentStorage, contentKey);
        if (content is null)
        {
            return;
        }

        contentStorage.Remove(content);
        DeleteDependentContent(contentKey);
    }

    private void DeleteDependentContent(LauncherContentKey contentKey)
    {
        if (contentKey.ContentType == ModificationType.Mod)
        {
            var dependentPatches = _patches
                .Where(patch => IsDependentOn(patch, contentKey))
                .ToList();

            foreach (LauncherContent patch in dependentPatches)
            {
                DeleteDependentAddons(patch.ContentKey);
                _patches.Remove(patch);
            }
        }

        if (contentKey.ContentType is ModificationType.Mod or ModificationType.Patch)
        {
            DeleteDependentAddons(contentKey);
        }
    }

    private static void AddOrUpdateContentVersion(
        List<LauncherContent> contentStorage,
        LauncherContentVersion version)
    {
        LauncherContent? savedContent = FindContentCard(contentStorage, version.ContentKey);

        if (savedContent != null)
        {
            savedContent.AddOrMergeVersion(version);
        }
        else
        {
            contentStorage.Add(new LauncherContent(version));
        }
    }

    private static bool DeleteContentVersion(
        List<LauncherContent> contentStorage,
        LauncherContentKey contentKey)
    {
        LauncherContent? savedContent = FindContentCard(contentStorage, contentKey);
        if (savedContent is null)
        {
            return false;
        }

        savedContent.RemoveVersion(contentKey);

        if (savedContent.Versions.Count == 0)
        {
            contentStorage.Remove(savedContent);
            return true;
        }

        return false;
    }

    private static LauncherContent? FindContentCard(
        List<LauncherContent> contentStorage,
        LauncherContentKey contentKey)
    {
        LauncherContentKey cardKey = contentKey.WithoutVersion();
        return contentStorage.Find(content => content.ContentKey == cardKey);
    }

    private void DeleteDependentAddons(LauncherContentKey parentKey)
    {
        _addons.RemoveAll(addon => IsDependentOn(addon, parentKey));
    }

    private static bool IsDependentOn(
        LauncherContent modification,
        LauncherContentKey parentKey)
    {
        return !string.IsNullOrWhiteSpace(parentKey.Name) &&
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
