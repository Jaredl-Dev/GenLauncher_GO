using System;
using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Models;

namespace GenLauncherGO.Infrastructure.Mods.Services;

/// <summary>
/// Maps compact legacy-compatible launcher content state to and from the active catalog.
/// </summary>
internal static class LauncherContentStateMapper
{
    public static LauncherData ToLauncherData(LauncherContentState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var launcherData = new LauncherData();
        AddStoredVersions(launcherData, state.Modifications, ModificationType.Mod);
        AddStoredVersions(launcherData, state.Addons, ModificationType.Addon);
        AddStoredVersions(launcherData, state.Patches, ModificationType.Patch);
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
        ModificationType fallbackType)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new LauncherContentVersionState
        {
            ModificationType = ResolvePersistedContentType(version.ModificationType, fallbackType),
            Name = version.Name ?? string.Empty,
            Version = version.Version ?? string.Empty,
            DependenceName = version.ParentContentName ?? string.Empty,
            Installed = version.Installation.Installed,
            IsSelected = version.Installation.IsSelected,
            ContentSourceKind = version.Installation.ContentSourceKind
        };
    }

    private static void AddStoredVersions(
        LauncherData launcherData,
        IEnumerable<LauncherContentEntryState> entries,
        ModificationType fallbackType)
    {
        foreach (LauncherContentEntryState entry in entries ?? Enumerable.Empty<LauncherContentEntryState>())
        {
            LauncherContent? storedModification = null;
            foreach (LauncherContentVersionState version in entry.ModificationVersions ??
                                                            new List<LauncherContentVersionState>())
            {
                LauncherContentVersion modificationVersion = ToModificationVersion(entry, version, fallbackType);
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
        ModificationType fallbackType)
    {
        ModificationType contentType = ResolveContentType(
            version.ModificationType,
            entry.ModificationType,
            fallbackType);

        var installation = new LauncherContentInstallation
        {
            Installed = version.Installed || entry.Installed,
            IsSelected = entry.IsSelected && version.IsSelected,
            ContentSourceKind = version.ContentSourceKind
        };
        return new LauncherContentVersion(installation)
        {
            ModificationType = contentType,
            Name = CoalesceStateText(version.Name, entry.Name),
            Version = version.Version ?? string.Empty,
            ParentContentName = CoalesceStateText(version.DependenceName, entry.DependenceName)
        };
    }

    private static List<LauncherContentEntryState> ToEntryStates(
        IEnumerable<LauncherContent> modifications,
        ModificationType fallbackType)
    {
        var entries = new List<LauncherContentEntryState>();
        foreach (LauncherContent modification in modifications ?? Enumerable.Empty<LauncherContent>())
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
               fallbackType == ModificationType.Mod &&
               version.EffectiveContentSourceKind is
                   ContentSourceKind.ManagedS3 or ContentSourceKind.ManagedSingleFile;
    }

    private static LauncherContentVersionState ToVersionState(
        LauncherContentVersion version,
        ModificationType fallbackType,
        bool entryIsSelected)
    {
        LauncherContentVersionState versionState = ToVersionState(version, fallbackType);
        versionState.IsSelected = entryIsSelected && versionState.IsSelected;
        return versionState;
    }

    private static string CoalesceStateText(string value, string fallback)
    {
        return !String.IsNullOrWhiteSpace(value) ? value : fallback ?? string.Empty;
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
