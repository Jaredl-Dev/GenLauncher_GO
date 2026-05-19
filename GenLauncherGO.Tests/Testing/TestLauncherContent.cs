using System;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Builds launcher content fixtures. This is the single place a test describes a version, so a change to the
///     domain model lands once instead of in every test file that used to spell out its own factory.
/// </summary>
internal static class TestLauncherContent
{
    /// <summary>
    ///     The S3 endpoint every managed-package fixture publishes from.
    /// </summary>
    public const string S3Host = "https://s3.example.test";

    /// <summary>
    ///     The bucket every managed-package fixture publishes into.
    /// </summary>
    public const string S3Bucket = "mods";

    public static LauncherContentVersion Version(
        string name = "ShockWave",
        string version = "1.0",
        ModificationType type = ModificationType.Mod,
        string parentContentName = "",
        bool installed = false,
        bool isSelected = false,
        ContentSourceKind sourceKind = ContentSourceKind.UnknownLegacy,
        string simpleDownloadLink = "",
        bool deprecated = false,
        bool downloadSuspended = false,
        double suspendedProgressPercentage = 0,
        LauncherContentTheme? theme = null)
    {
        return new LauncherContentVersion(new LauncherContentInstallation
        {
            Installed = installed,
            IsSelected = isSelected,
            ContentSourceKind = sourceKind,
            DownloadSuspended = downloadSuspended,
            SuspendedProgressPercentage = suspendedProgressPercentage
        })
        {
            ModificationType = type,
            Name = name,
            Version = version,
            ParentContentName = parentContentName,
            SimpleDownloadLink = simpleDownloadLink,
            Deprecated = deprecated,
            Theme = theme
        };
    }

    /// <summary>
    ///     Builds a version whose metadata resolves to <see cref="ContentSourceKind.ManagedS3" />.
    /// </summary>
    public static LauncherContentVersion S3Version(
        string name = "ShockWave",
        string version = "1.0",
        ModificationType type = ModificationType.Mod,
        string parentContentName = "",
        bool installed = false,
        bool isSelected = false,
        string? s3FolderName = null)
    {
        return new LauncherContentVersion(new LauncherContentInstallation
        {
            Installed = installed,
            IsSelected = isSelected,
            ContentSourceKind = ContentSourceKind.ManagedS3
        })
        {
            ModificationType = type,
            Name = name,
            Version = version,
            ParentContentName = parentContentName,
            S3HostLink = S3Host,
            S3BucketName = S3Bucket,
            S3FolderName = s3FolderName ?? $"{name}/{version}"
        };
    }

    /// <summary>
    ///     Materializes a content card through a real <see cref="LauncherData" /> round trip, so the card a test
    ///     asserts against was assembled by the production merge policy rather than by the test.
    /// </summary>
    public static LauncherContent From(params LauncherContentVersion[] versions)
    {
        ArgumentNullException.ThrowIfNull(versions);

        if (versions.Length == 0)
        {
            throw new ArgumentException("At least one version is required.", nameof(versions));
        }

        var data = new LauncherData();
        foreach (LauncherContentVersion version in versions)
        {
            data.AddOrUpdate(version);
        }

        return data.FindContent(versions[0].ContentKey) ??
               throw new InvalidOperationException(
                   $"'{versions[0].DisplayName}' is not a content kind LauncherData stores.");
    }

    public static CatalogBuilder Catalog()
    {
        return new CatalogBuilder();
    }

    /// <summary>
    ///     Fills a <see cref="FakeLauncherContentCatalog" /> through the production catalog model.
    /// </summary>
    internal sealed class CatalogBuilder
    {
        private readonly FakeLauncherContentCatalog _catalog = new();

        public CatalogBuilder WithMod(
            string name,
            string version = "1.0",
            bool installed = true)
        {
            return Add(Version(name, version, ModificationType.Mod, installed: installed));
        }

        public CatalogBuilder WithPatch(
            string parentContentName,
            string name,
            string version = "1.0",
            bool installed = true)
        {
            return Add(Version(
                name,
                version,
                ModificationType.Patch,
                parentContentName,
                installed));
        }

        public CatalogBuilder WithAddon(
            string parentContentName,
            string name,
            string version = "1.0",
            bool installed = true)
        {
            return Add(Version(
                name,
                version,
                ModificationType.Addon,
                parentContentName,
                installed));
        }

        /// <summary>
        ///     Marks an already-added card and its versions as the user's selection.
        /// </summary>
        public CatalogBuilder Selected(
            string name,
            ModificationType type = ModificationType.Mod,
            string parentContentName = "")
        {
            LauncherContent content =
                _catalog.Data.FindContent(new LauncherContentKey(type, parentContentName, name, string.Empty)) ??
                throw new InvalidOperationException($"'{name}' was not added to the catalog.");
            content.IsSelected = true;
            foreach (LauncherContentVersion version in content.Versions)
            {
                version.Installation.IsSelected = true;
            }

            return this;
        }

        public FakeLauncherContentCatalog Build()
        {
            return _catalog;
        }

        private CatalogBuilder Add(LauncherContentVersion version)
        {
            _catalog.Data.AddOrUpdate(version);
            return this;
        }
    }
}
