using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Models;

namespace GenLauncherGO.Infrastructure.Mods.Support;

/// <summary>
///     Maps third-party backend manifest DTOs once into normalized launcher models.
/// </summary>
internal static class RemoteLauncherCatalogMapper
{
    /// <summary>
    ///     Maps a backend repository manifest to a normalized remote catalog.
    /// </summary>
    public static RemoteLauncherCatalog ToRemoteCatalog(LegacyLauncherCatalogDocument? repositoryData)
    {
        if (repositoryData is null)
        {
            return RemoteLauncherCatalog.Empty;
        }

        // globalAddonsData is a vestigial backend field that neither the predecessor nor this launcher exposes.
        return new RemoteLauncherCatalog(
            ToAdvertisingReferences(repositoryData.AdvData),
            ToModificationReferences(repositoryData.modDatas),
            ToStringList(repositoryData.originalGameAddons),
            ToStringList(repositoryData.originalGamePatches));
    }

    /// <summary>
    ///     Maps a backend content manifest directly to normalized domain metadata.
    /// </summary>
    public static LauncherContentVersion ToLauncherContentVersion(
        LegacyContentManifest? manifest,
        string? parentContentName = null)
    {
        if (manifest is null)
        {
            return new LauncherContentVersion
            {
                ParentContentName = parentContentName ?? string.Empty
            };
        }

        string simpleDownloadLink = manifest.SimpleDownloadLink ?? string.Empty;
        string s3HostLink = manifest.S3HostLink ?? string.Empty;
        string s3BucketName = manifest.S3BucketName ?? string.Empty;
        string s3FolderName = manifest.S3FolderName ?? string.Empty;
        var installation = new LauncherContentInstallation
        {
            ContentSourceKind = LauncherContentVersion.ResolveContentSourceKind(
                s3HostLink,
                s3BucketName,
                s3FolderName,
                simpleDownloadLink,
                manifest.ContentSourceKind)
        };

        return new LauncherContentVersion(installation)
        {
            ModificationType = manifest.ModificationType,
            Name = manifest.Name ?? string.Empty,
            Version = manifest.Version ?? string.Empty,
            SimpleDownloadLink = simpleDownloadLink,
            UIImageSourceLink = manifest.UIImageSourceLink ?? string.Empty,
            DiscordLink = manifest.DiscordLink ?? string.Empty,
            ModDBLink = manifest.ModDBLink ?? string.Empty,
            NewsLink = manifest.NewsLink ?? string.Empty,
            ParentContentName = parentContentName ?? manifest.DependenceName ?? string.Empty,
            S3HostLink = s3HostLink,
            S3BucketName = s3BucketName,
            S3FolderName = s3FolderName,
            S3HostPublicKey = manifest.S3HostPublicKey ?? string.Empty,
            S3HostSecretKey = manifest.S3HostSecretKey ?? string.Empty,
            NetworkInfo = manifest.NetworkInfo ?? string.Empty,
            Deprecated = manifest.Deprecated,
            SupportLink = manifest.SupportLink ?? string.Empty,
            Theme = ToLauncherContentTheme(manifest.ColorsInformation)
        };
    }

    /// <summary>
    ///     Maps the backend's per-modification palette, dropping a block that carries nothing usable.
    /// </summary>
    /// <remarks>
    ///     Slots stay independent on purpose: a modification may publish only the few colours it cares about, and the
    ///     launcher fills the rest from the active game's palette rather than rejecting the block.
    /// </remarks>
    private static LauncherContentTheme? ToLauncherContentTheme(LegacyContentThemeManifest? theme)
    {
        if (theme is null)
        {
            return null;
        }

        var mapped = new LauncherContentTheme
        {
            GenLauncherBorderColor = Normalize(theme.GenLauncherBorderColor),
            GenLauncherInactiveBorder = Normalize(theme.GenLauncherInactiveBorder),
            GenLauncherInactiveBorder2 = Normalize(theme.GenLauncherInactiveBorder2),
            GenLauncherActiveColor = Normalize(theme.GenLauncherActiveColor),
            GenLauncherDarkFillColor = Normalize(theme.GenLauncherDarkFillColor),
            GenLauncherDarkBackGround = Normalize(theme.GenLauncherDarkBackGround),
            GenLauncherLightBackGround = Normalize(theme.GenLauncherLightBackGround),
            GenLauncherDefaultTextColor = Normalize(theme.GenLauncherDefaultTextColor),
            GenLauncherDownloadTextColor = Normalize(theme.GenLauncherDownloadTextColor),
            GenLauncherListBoxSelectionColor1 = Normalize(theme.GenLauncherListBoxSelectionColor1),
            GenLauncherListBoxSelectionColor2 = Normalize(theme.GenLauncherListBoxSelectionColor2),
            GenLauncherButtonSelectionColor = Normalize(theme.GenLauncherButtonSelectionColor),
            GenLauncherBackgroundImageLink = Normalize(theme.GenLauncherBackgroundImageLink)
        };

        return mapped.HasValues ? mapped : null;
    }

    private static string Normalize(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static IReadOnlyList<RemoteCatalogModificationReference> ToModificationReferences(
        IEnumerable<LegacyCatalogModificationReference>? modificationReferences)
    {
        return (modificationReferences ?? [])
            .Select(reference => new RemoteCatalogModificationReference(
                reference.ModName,
                reference.ModLink,
                ToStringList(reference.ModPatches),
                ToStringList(reference.ModAddons)))
            .ToList();
    }

    private static IReadOnlyList<RemoteAdvertisingReference> ToAdvertisingReferences(
        IEnumerable<LegacyCatalogAdvertisingReference>? advertisingReferences)
    {
        return (advertisingReferences ?? [])
            .Select(reference => new RemoteAdvertisingReference(
                reference.ModName,
                reference.ModLink,
                ToStringList(reference.ImagesData)))
            .ToList();
    }

    private static IReadOnlyList<string> ToStringList(IEnumerable<string>? values)
    {
        return (values ?? [])
            .OfType<string>()
            .ToList();
    }
}
