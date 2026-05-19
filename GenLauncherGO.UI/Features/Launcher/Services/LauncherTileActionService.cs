using System;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Mods;

namespace GenLauncherGO.UI.Features.Launcher.Services;

/// <summary>
/// Builds and applies actions for launcher modification tiles.
/// </summary>
internal sealed class LauncherTileActionService
{
    private readonly ILauncherContentCatalog _catalog;

    public LauncherTileActionService(ILauncherContentCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public static LauncherTileLinkAction GetAdvertisingDownloadAction(LauncherContent modification)
    {
        ArgumentNullException.ThrowIfNull(modification);

        if (modification.ModificationType == ModificationType.Advertising &&
            !String.IsNullOrEmpty(modification.LatestVersion.SimpleDownloadLink))
        {
            return new LauncherTileLinkAction(
                modification.LatestVersion.SimpleDownloadLink,
                ShowThankYouMessage: true);
        }

        return new LauncherTileLinkAction(null, ShowThankYouMessage: false);
    }

    public static LauncherTileLinkAction GetNewsAction(LauncherContent modification)
    {
        ArgumentNullException.ThrowIfNull(modification);

        return new LauncherTileLinkAction(
            String.IsNullOrEmpty(modification.LatestVersion.NewsLink)
                ? null
                : modification.LatestVersion.NewsLink,
            modification.ModificationType == ModificationType.Advertising);
    }

    public static LauncherTileLinkAction GetNetworkInfoAction(LauncherContent modification)
    {
        ArgumentNullException.ThrowIfNull(modification);

        return new LauncherTileLinkAction(
            String.IsNullOrEmpty(modification.LatestVersion.NetworkInfo)
                ? null
                : modification.LatestVersion.NetworkInfo,
            modification.ModificationType == ModificationType.Advertising);
    }

    public static LauncherTileLinkAction GetSupportAction(LauncherContent modification)
    {
        ArgumentNullException.ThrowIfNull(modification);

        return new LauncherTileLinkAction(
            String.IsNullOrEmpty(modification.LatestVersion.SupportLink)
                ? null
                : modification.LatestVersion.SupportLink,
            ShowThankYouMessage: true);
    }

    public static LauncherTileLinkAction GetModDbAction(LauncherContent modification)
    {
        ArgumentNullException.ThrowIfNull(modification);

        return new LauncherTileLinkAction(
            String.IsNullOrEmpty(modification.LatestVersion.ModDBLink)
                ? null
                : modification.LatestVersion.ModDBLink,
            ShowThankYouMessage: false);
    }

    public static LauncherTileLinkAction GetDiscordAction(LauncherContent modification)
    {
        ArgumentNullException.ThrowIfNull(modification);

        return new LauncherTileLinkAction(
            String.IsNullOrEmpty(modification.LatestVersion.DiscordLink)
                ? null
                : modification.LatestVersion.DiscordLink,
            ShowThankYouMessage: false);
    }

    /// <summary>
    /// Deletes a selected installed version from the catalog and refreshes local modification data.
    /// </summary>
    public bool DeleteVersion(ModificationVersionSelection versionData)
    {
        ArgumentNullException.ThrowIfNull(versionData);

        LauncherContentVersion selectedVersion = versionData.SelectedVersion;
        var contentKey = new LauncherContentKey(
            selectedVersion.ModificationType,
            ResolveParentContentName(versionData),
            selectedVersion.Name,
            versionData.VersionName);

        if (ShouldRemoveContentCard(selectedVersion))
        {
            _catalog.DiscardContent(contentKey);
            return true;
        }

        _catalog.UninstallVersion(contentKey);
        return false;
    }

    /// <summary>
    /// Deletes local files for a content tile and removes its latest version from the catalog.
    /// </summary>
    public void DiscardContentVersion(ModificationViewModel modification)
    {
        ArgumentNullException.ThrowIfNull(modification);

        _catalog.DiscardVersion(CreateDeletionKey(modification));
    }

    /// <summary>
    /// Deletes local files for a content tile while keeping its catalog entry.
    /// </summary>
    public void UninstallContentVersion(ModificationViewModel modification)
    {
        ArgumentNullException.ThrowIfNull(modification);

        _catalog.UninstallVersion(CreateDeletionKey(modification));
    }

    /// <summary>
    /// Creates the canonical deletion identity without retaining mutable tile state.
    /// </summary>
    private static LauncherContentKey CreateDeletionKey(ModificationViewModel modification)
    {
        LauncherContentVersion selectedVersion = modification.LatestVersion;
        return new LauncherContentKey(
            selectedVersion.ModificationType,
            !String.IsNullOrWhiteSpace(selectedVersion.ParentContentName)
                ? selectedVersion.ParentContentName
                : modification.ContainerModification.ContentKey.ParentIdentity,
            selectedVersion.Name,
            selectedVersion.Version);
    }

    /// <summary>
    /// Determines whether deleting the selected version should remove the whole visible content card.
    /// </summary>
    private static bool ShouldRemoveContentCard(LauncherContentVersion version)
    {
        return version.ModificationType == ModificationType.Mod ||
               version.EffectiveContentSourceKind == ContentSourceKind.Manual;
    }

    private static string ResolveParentContentName(ModificationVersionSelection versionData)
    {
        return !String.IsNullOrWhiteSpace(versionData.SelectedVersion.ParentContentName)
            ? versionData.SelectedVersion.ParentContentName
            : versionData.ModificationViewModel.ContainerModification.ContentKey.ParentIdentity;
    }
}
