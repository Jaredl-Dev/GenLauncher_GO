using System;
using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Models;

namespace GenLauncherGO.Infrastructure.Mods.Services;

/// <summary>
///     Maps compact legacy-compatible launcher content state to and from the active catalog.
/// </summary>
internal static class LauncherContentStateMapper
{
    /// <summary>
    ///     Rebuilds the catalog from persisted state, restoring each version's cached palette when one is supplied.
    /// </summary>
    /// <param name="state">The persisted local content state.</param>
    /// <param name="themeCache">
    ///     The palette cache, or <see langword="null" /> to rebuild without palettes. Persisted state deliberately
    ///     holds no remote manifest metadata, so a themed launcher only survives an offline restart because the
    ///     palette is restored here from its own cache.
    /// </param>
    public static LauncherData ToLauncherData(LauncherContentState state, IModificationThemeCache? themeCache = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        var launcherData = new LauncherData();
        AddStoredVersions(launcherData, state.Modifications, ModificationType.Mod, themeCache);
        AddStoredVersions(launcherData, state.Addons, ModificationType.Addon, themeCache);
        AddStoredVersions(launcherData, state.Patches, ModificationType.Patch, themeCache);
        return launcherData;
    }

    public static LauncherContentState ToLauncherContentState(LauncherData launcherData)
    {
        ArgumentNullException.ThrowIfNull(launcherData);

        return new LauncherContentState
        {
            Modifications = ToEntryStates(launcherData.Modifications, ModificationType.Mod),
            Addons = ToEntryStates(launcherData.Addons, ModificationType.Addon),
            Patches = ToEntryStates(launcherData.Patches, ModificationType.Patch)
        };
    }

    private static LauncherContentVersionState ToVersionState(
        LauncherContentVersion version,
        ModificationType fallbackType,
        bool entryIsSelected)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new LauncherContentVersionState
        {
            ModificationType = ResolvePersistedContentType(version.ModificationType, fallbackType),
            Name = version.Name ?? string.Empty,
            Version = version.Version ?? string.Empty,
            DependenceName = version.ParentContentName ?? string.Empty,
            Installed = version.Installation.Installed,
            IsSelected = entryIsSelected && version.Installation.IsSelected,
            DownloadSuspended = version.Installation.DownloadSuspended,
            SuspendedProgressPercentage = version.Installation.SuspendedProgressPercentage,
            ContentSourceKind = version.Installation.ContentSourceKind
        };
    }

    private static void AddStoredVersions(
        LauncherData launcherData,
        IEnumerable<LauncherContentEntryState> entries,
        ModificationType fallbackType,
        IModificationThemeCache? themeCache)
    {
        foreach (LauncherContentEntryState entry in entries ?? [])
        {
            LauncherContent? storedModification = null;
            foreach (LauncherContentVersionState version in entry.ModificationVersions ??
                                                            [])
            {
                LauncherContentVersion modificationVersion = ToModificationVersion(
                    entry,
                    version,
                    fallbackType,
                    themeCache);
                launcherData.AddOrUpdate(modificationVersion);
                storedModification ??= launcherData.FindContent(modificationVersion.ContentKey);
            }

            if (storedModification != null)
            {
                storedModification.IsSelected = entry.IsSelected;
                storedModification.NumberInList = entry.NumberInList;
            }
        }
    }

    private static LauncherContentVersion ToModificationVersion(
        LauncherContentEntryState entry,
        LauncherContentVersionState version,
        ModificationType fallbackType,
        IModificationThemeCache? themeCache)
    {
        ModificationType contentType = ResolveContentType(
            version.ModificationType,
            entry.ModificationType,
            fallbackType);

        var installation = new LauncherContentInstallation
        {
            Installed = version.Installed || entry.Installed,
            IsSelected = entry.IsSelected && version.IsSelected,
            DownloadSuspended = version.DownloadSuspended,
            SuspendedProgressPercentage = version.SuspendedProgressPercentage,
            ContentSourceKind = version.ContentSourceKind
        };
        string contentName = CoalesceStateText(version.Name, entry.Name);
        string contentVersion = version.Version ?? string.Empty;
        return new LauncherContentVersion(installation)
        {
            ModificationType = contentType,
            Name = contentName,
            Version = contentVersion,
            ParentContentName = CoalesceStateText(version.DependenceName, entry.DependenceName),
            Theme = themeCache?.Load(contentName, contentVersion)
        };
    }

    private static List<LauncherContentEntryState> ToEntryStates(
        IEnumerable<LauncherContent> modifications,
        ModificationType fallbackType)
    {
        var entries = new List<LauncherContentEntryState>();
        foreach (LauncherContent modification in modifications ?? [])
        {
            var versions = modification.Versions
                .Where(version => ShouldPersistVersion(version, fallbackType))
                .Select(version => ToVersionState(version, fallbackType, modification.IsSelected))
                .ToList();

            if (versions.Count == 0)
            {
                continue;
            }

            entries.Add(new LauncherContentEntryState
            {
                ModificationType = ResolvePersistedContentType(modification.ModificationType, fallbackType),
                Name = modification.Name ?? string.Empty,
                DependenceName = modification.ContentKey.ParentIdentity,
                Installed = modification.Installed,
                IsSelected = modification.IsSelected,
                NumberInList = modification.NumberInList,
                ModificationVersions = versions
            });
        }

        return entries;
    }

    private static bool ShouldPersistVersion(
        LauncherContentVersion version,
        ModificationType fallbackType)
    {
        return version.Installation.Installed ||
               version.Installation.IsSelected ||
               // A suspended download has partial content on disk but is not installed yet, so it has to persist
               // on its own merit or the next session would forget it and start over.
               version.Installation.DownloadSuspended ||
               (fallbackType == ModificationType.Mod &&
                version.EffectiveContentSourceKind.IsManagedRemote());
    }

    private static string CoalesceStateText(string value, string fallback)
    {
        return !string.IsNullOrWhiteSpace(value) ? value : fallback ?? string.Empty;
    }

    private static ModificationType ResolveContentType(
        ModificationType versionType,
        ModificationType entryType,
        ModificationType fallbackType)
    {
        if (versionType != ModificationType.Mod || fallbackType == ModificationType.Mod)
        {
            return versionType;
        }

        return entryType != ModificationType.Mod ? entryType : fallbackType;
    }

    private static ModificationType ResolvePersistedContentType(
        ModificationType type,
        ModificationType fallbackType)
    {
        return type switch
        {
            ModificationType.Addon or ModificationType.Patch => type,
            _ => fallbackType
        };
    }
}
