using System;
using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.Integrity.Models;

namespace GenLauncherGO.Core.Mods.Models;

/// <summary>
///     Owns one launcher content card, its versions, and the single policy for merging catalog and local state.
/// </summary>
public sealed class LauncherContent
{
    private readonly List<LauncherContentVersion> _versions = [];

    public LauncherContent(LauncherContentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        ContentKey = version.ContentKey.WithoutVersion();
        Versions = _versions.AsReadOnly();
        AddOrMergeVersion(version);
    }

    public LauncherContentKey ContentKey { get; }

    public IReadOnlyList<LauncherContentVersion> Versions { get; }

    /// <summary>
    ///     Gets the latest known version, which is the card's presentation metadata authority.
    /// </summary>
    public LauncherContentVersion LatestVersion =>
        _versions.OrderBy(version => version).Last();

    /// <summary>
    ///     Gets the latest installed version, or <see langword="null" /> when no version is installed.
    /// </summary>
    public LauncherContentVersion? LatestInstalledVersion =>
        _versions
            .Where(version => version.Installation.Installed)
            .OrderBy(version => version)
            .LastOrDefault();

    public ModificationType ModificationType => ContentKey.ContentType;

    public string Name => ContentKey.Name;

    public bool Installed => _versions.Any(version => version.Installation.Installed);

    public bool IsSelected { get; set; }

    public int NumberInList { get; set; }

    /// <summary>
    ///     Gets the persisted installed selection, falling back to the earliest installed or known version.
    /// </summary>
    /// <remarks>
    ///     The fallback preserves the launcher's legacy behavior when saved selection state is missing.
    /// </remarks>
    public LauncherContentVersion? GetSelectedVersion()
    {
        return _versions.FirstOrDefault(version =>
                   version.Installation.Installed &&
                   version.Installation.IsSelected) ??
               _versions
                   .Where(version => version.Installation.Installed)
                   .OrderBy(version => version)
                   .FirstOrDefault() ??
               _versions
                   .OrderBy(version => version)
                   .FirstOrDefault();
    }

    /// <summary>
    ///     Adds a new version or merges metadata and local installation state into the matching version.
    /// </summary>
    internal void AddOrMergeVersion(LauncherContentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (version.ContentKey.WithoutVersion() != ContentKey)
        {
            throw new ArgumentException(
                "A launcher content version cannot be merged into a different content card.",
                nameof(version));
        }

        int versionIndex = _versions.FindIndex(candidate =>
            candidate.ContentKey == version.ContentKey);
        if (versionIndex < 0)
        {
            _versions.Add(version);
        }
        else
        {
            _versions[versionIndex] = MergeVersion(_versions[versionIndex], version);
        }

        IsSelected |= version.Installation.IsSelected;
    }

    /// <summary>
    ///     Removes the matching version from this content card.
    /// </summary>
    internal void RemoveVersion(LauncherContentKey contentKey)
    {
        int versionIndex = _versions.FindIndex(candidate =>
            candidate.ContentKey == contentKey);
        if (versionIndex >= 0)
        {
            _versions.RemoveAt(versionIndex);
        }
    }

    /// <summary>
    ///     Applies the legacy-compatible catalog merge precedence once while retaining one shared local-state object.
    /// </summary>
    private static LauncherContentVersion MergeVersion(
        LauncherContentVersion existing,
        LauncherContentVersion incoming)
    {
        LauncherContentInstallation installation = existing.Installation;
        installation.Installed |= incoming.Installation.Installed;
        installation.IsSelected |= incoming.Installation.IsSelected;
        if (installation.ContentSourceKind == ContentSourceKind.UnknownLegacy &&
            incoming.Installation.ContentSourceKind != ContentSourceKind.UnknownLegacy)
        {
            installation.ContentSourceKind = incoming.Installation.ContentSourceKind;
        }

        var merged = new LauncherContentVersion(installation)
        {
            ModificationType = existing.ModificationType,
            Name = existing.Name,
            Version = existing.Version,
            SimpleDownloadLink = FirstNonEmpty(existing.SimpleDownloadLink, incoming.SimpleDownloadLink),
            UIImageSourceLink = FirstNonEmpty(existing.UIImageSourceLink, incoming.UIImageSourceLink),
            DiscordLink = FirstNonEmpty(existing.DiscordLink, incoming.DiscordLink),
            ModDBLink = FirstNonEmpty(existing.ModDBLink, incoming.ModDBLink),
            NewsLink = FirstNonEmpty(existing.NewsLink, incoming.NewsLink),
            ParentContentName = existing.ParentContentName,
            S3HostLink = FirstNonEmpty(existing.S3HostLink, incoming.S3HostLink),
            S3BucketName = FirstNonEmpty(existing.S3BucketName, incoming.S3BucketName),
            S3FolderName = FirstNonEmpty(existing.S3FolderName, incoming.S3FolderName),
            S3HostPublicKey = FirstNonEmpty(existing.S3HostPublicKey, incoming.S3HostPublicKey),
            S3HostSecretKey = FirstNonEmpty(existing.S3HostSecretKey, incoming.S3HostSecretKey),
            NetworkInfo = FirstNonEmpty(existing.NetworkInfo, incoming.NetworkInfo),
            Deprecated = incoming.Deprecated,
            SupportLink = FirstNonEmpty(existing.SupportLink, incoming.SupportLink),
            Theme = incoming.Theme ?? existing.Theme
        };

        installation.ContentSourceKind = merged.EffectiveContentSourceKind;
        return merged;
    }

    private static string FirstNonEmpty(string existing, string incoming)
    {
        return !string.IsNullOrEmpty(existing) ? existing : incoming;
    }
}
