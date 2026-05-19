using System;
using System.Linq;
using GenLauncherGO.Core.Integrity.Models;

namespace GenLauncherGO.Core.Mods.Models;

/// <summary>
///     Describes immutable metadata for one launcher content version and its separate mutable local installation state.
/// </summary>
/// <remarks>
///     Infrastructure maps third-party backend documents into this normalized domain model. Canonical identity is always
///     provided by <see cref="LauncherContentKey" />; object equality is intentionally not an identity mechanism.
/// </remarks>
public sealed class LauncherContentVersion : IComparable<LauncherContentVersion>
{
    public LauncherContentVersion()
        : this(new LauncherContentInstallation())
    {
    }

    public LauncherContentVersion(LauncherContentInstallation installation)
    {
        Installation = installation ?? throw new ArgumentNullException(nameof(installation));
    }

    public ModificationType ModificationType { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string SimpleDownloadLink { get; init; } = string.Empty;

    // ReSharper disable once InconsistentNaming
    public string UIImageSourceLink { get; init; } = string.Empty;

    public string DiscordLink { get; init; } = string.Empty;

    // ReSharper disable once InconsistentNaming
    public string ModDBLink { get; init; } = string.Empty;

    public string NewsLink { get; init; } = string.Empty;

    public string ParentContentName { get; init; } = string.Empty;

    public string S3HostLink { get; init; } = string.Empty;

    public string S3BucketName { get; init; } = string.Empty;

    public string S3FolderName { get; init; } = string.Empty;

    public string S3HostPublicKey { get; init; } = string.Empty;

    public string S3HostSecretKey { get; init; } = string.Empty;

    public string NetworkInfo { get; init; } = string.Empty;

    public bool Deprecated { get; init; }

    public string SupportLink { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the palette this version asks the launcher to wear while it is selected, when it published one.
    /// </summary>
    public LauncherContentTheme? Theme { get; init; }

    public LauncherContentInstallation Installation { get; init; }

    /// <summary>
    ///     Gets the content source kind after applying package metadata precedence.
    /// </summary>
    public ContentSourceKind EffectiveContentSourceKind =>
        ResolveContentSourceKind(
            S3HostLink,
            S3BucketName,
            S3FolderName,
            SimpleDownloadLink,
            Installation.ContentSourceKind);

    public string DisplayName => string.Join(" ", new[] { Name, Version }
        .Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>
    ///     Gets the canonical identity of this content version.
    /// </summary>
    public LauncherContentKey ContentKey =>
        new(ModificationType, ParentContentName, Name, Version);

    public int CompareTo(LauncherContentVersion? other)
    {
        return other is null
            ? 1
            : LauncherContentVersionComparer.Instance.Compare(Version, other.Version);
    }

    public override string ToString()
    {
        return Name;
    }

    /// <summary>
    ///     Resolves the content source kind from package metadata.
    /// </summary>
    public static ContentSourceKind ResolveContentSourceKind(
        string? s3HostLink,
        string? s3BucketName,
        string? s3FolderName,
        string? simpleDownloadLink,
        ContentSourceKind fallbackSourceKind)
    {
        if (!string.IsNullOrWhiteSpace(s3HostLink) &&
            !string.IsNullOrWhiteSpace(s3BucketName) &&
            !string.IsNullOrWhiteSpace(s3FolderName))
        {
            return ContentSourceKind.ManagedS3;
        }

        if (!string.IsNullOrWhiteSpace(simpleDownloadLink))
        {
            return ContentSourceKind.ManagedSingleFile;
        }

        return fallbackSourceKind;
    }
}
