using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Integrity.Contracts;
using GenLauncherGO.Infrastructure.Integrity.Support;
using GenLauncherGO.Infrastructure.Launching.Contracts;
using GenLauncherGO.Infrastructure.Mods.Support;
using GenLauncherGO.Infrastructure.Remote.Contracts;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Support;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Launching.Services;

/// <summary>
///     Resolves launch-readiness integrity issues using persisted snapshots, package repair, and cache refresh.
/// </summary>
internal sealed class FileSystemLaunchContentIntegrityResolutionService : ILaunchContentIntegrityResolutionService
{
    private readonly IRemoteAssetDownloader _assetDownloader;

    private readonly ILauncherContentCatalog _catalog;
    private readonly IContentIntegrityService _integrityService;

    private readonly ILogger<FileSystemLaunchContentIntegrityResolutionService> _logger;

    private readonly IS3ObjectManifestReader _manifestReader;

    private readonly IS3PackageUpdater _s3PackageUpdater;

    private readonly ISingleFilePackageUpdater _singleFilePackageUpdater;

    private readonly ILaunchContentIntegrityTargetBuilder _targetBuilder;

    public FileSystemLaunchContentIntegrityResolutionService(
        IContentIntegrityService integrityService,
        ILaunchContentIntegrityTargetBuilder targetBuilder,
        IS3ObjectManifestReader manifestReader,
        IS3PackageUpdater s3PackageUpdater,
        ISingleFilePackageUpdater singleFilePackageUpdater,
        IRemoteAssetDownloader assetDownloader,
        ILauncherContentCatalog catalog,
        ILogger<FileSystemLaunchContentIntegrityResolutionService> logger)
    {
        _integrityService = integrityService ?? throw new ArgumentNullException(nameof(integrityService));
        _targetBuilder = targetBuilder ?? throw new ArgumentNullException(nameof(targetBuilder));
        _manifestReader = manifestReader ?? throw new ArgumentNullException(nameof(manifestReader));
        _s3PackageUpdater = s3PackageUpdater ?? throw new ArgumentNullException(nameof(s3PackageUpdater));
        _singleFilePackageUpdater = singleFilePackageUpdater ??
                                    throw new ArgumentNullException(nameof(singleFilePackageUpdater));
        _assetDownloader = assetDownloader ?? throw new ArgumentNullException(nameof(assetDownloader));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<LaunchContentIntegrityVerificationResult> VerifyAsync(
        LaunchContentIntegrityTargetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<LaunchContentIntegrityTargetContext> contexts = _targetBuilder.BuildTargets(request);
        ContentIntegrityReport report = await _integrityService.VerifyAsync(
            request.Paths,
            contexts.Select(context => context.Target).ToList(),
            cancellationToken).ConfigureAwait(false);
        return new LaunchContentIntegrityVerificationResult(report, contexts);
    }

    public async Task<bool> InitializeUntrackedManagedCachesAsync(
        LaunchContentIntegrityResolutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlySet<string> untrackedManagedTargetIds = GetUntrackedManagedTargetIds(request.Report);
        var cacheContexts = request.TargetContexts
            .Where(context =>
                context.IsCache &&
                untrackedManagedTargetIds.Contains(context.Target.Id))
            .ToList();

        bool initializedAny = false;
        foreach (LaunchContentIntegrityTargetContext context in cacheContexts)
        {
            if (!await _integrityService.CaptureSnapshotIfMatchesExpectedFileSetAsync(
                    request.Paths,
                    context.Target,
                    BuildExpectedRemoteCachePaths(context),
                    cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            _logger.LogInformation(
                "Initialized managed remote image integrity for {ContentName}.",
                context.Version.DisplayName);
            initializedAny = true;
        }

        return initializedAny;
    }

    public async Task ResolveAsync(
        LaunchContentIntegrityResolutionRequest request,
        IProgress<LaunchContentIntegrityResolutionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextIndex =
            request.TargetContexts.ToDictionary(context => context.Target.Id, StringComparer.Ordinal);
        (IReadOnlySet<string> TrustAsManualTargetIds, IReadOnlySet<string> AbsorbTargetIds, IReadOnlySet<string> RepairTargetIds, IReadOnlyList<string> ManagedTargetIdsInReportOrder) issueIndex = IndexResolutionIssues(request.Report);

        foreach (LaunchContentIntegrityTargetContext context in request.TargetContexts.Where(context =>
                     issueIndex.TrustAsManualTargetIds.Contains(context.Target.Id)))
        {
            context.Version.Installation.ContentSourceKind = ContentSourceKind.Manual;
            ContentIntegrityTarget manualTarget = context.Target with
            {
                SourceKind = ContentSourceKind.Manual
            };
            await _integrityService.CaptureSnapshotAsync(request.Paths, manualTarget, cancellationToken).ConfigureAwait(false);
        }

        _catalog.SaveLauncherData();

        foreach (LaunchContentIntegrityTargetContext context in request.TargetContexts.Where(context =>
                     issueIndex.AbsorbTargetIds.Contains(context.Target.Id)))
        {
            await _integrityService.CaptureSnapshotAsync(request.Paths, context.Target, cancellationToken).ConfigureAwait(false);
        }

        await _integrityService.ApplyCleanupAsync(
            request.Report,
            request.TargetContexts.Select(context => context.Target).ToList(),
            cancellationToken).ConfigureAwait(false);

        foreach (LaunchContentIntegrityTargetContext context in request.TargetContexts.Where(context =>
                     issueIndex.RepairTargetIds.Contains(context.Target.Id)))
        {
            if (context.IsCache)
            {
                await RefreshManagedCacheAsync(context, cancellationToken).ConfigureAwait(false);
                progress?.Report(LaunchContentIntegrityResolutionProgress.Complete(context.Target.Id));
            }
            else
            {
                await RepairManagedPackageAsync(
                    request.Paths,
                    context,
                    request.Report,
                    new TargetPackageProgress(context.Target.Id, progress),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (string targetId in issueIndex.ManagedTargetIdsInReportOrder)
        {
            if (contextIndex.TryGetValue(targetId, out LaunchContentIntegrityTargetContext? context))
            {
                await _integrityService.CaptureSnapshotAsync(request.Paths, context.Target, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task RegisterManualImportAsync(
        LaunchContentIntegrityVersionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Version.Installation.ContentSourceKind = ContentSourceKind.Manual;
        _catalog.SaveLauncherData();

        IReadOnlyList<LaunchContentIntegrityTargetContext> contexts = BuildSingleVersionContexts(request);
        await _integrityService.CaptureSnapshotAsync(
            request.Paths,
            contexts.First(context => !context.IsCache).Target,
            cancellationToken).ConfigureAwait(false);

        if (request.Version.ModificationType == ModificationType.Mod)
        {
            await _integrityService.CaptureSnapshotAsync(
                request.Paths,
                contexts.First(context => context.IsCache).Target,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task CaptureManagedInstallSnapshotAsync(
        LaunchContentIntegrityVersionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Version.EffectiveContentSourceKind.IsManagedRemote())
        {
            return;
        }

        IReadOnlyList<LaunchContentIntegrityTargetContext> contexts = BuildSingleVersionContexts(request);
        await _integrityService.CaptureSnapshotAsync(
            request.Paths,
            contexts.First(context => !context.IsCache).Target,
            cancellationToken).ConfigureAwait(false);

        if (request.Version.ModificationType == ModificationType.Mod)
        {
            LaunchContentIntegrityTargetContext cacheContext = contexts.First(context => context.IsCache);
            if (!await _integrityService.CaptureSnapshotIfMatchesExpectedFileSetAsync(
                    request.Paths,
                    cacheContext.Target,
                    BuildExpectedRemoteCachePaths(cacheContext),
                    cancellationToken).ConfigureAwait(false))
            {
                await RefreshManagedCacheAsync(cacheContext, cancellationToken).ConfigureAwait(false);
                await _integrityService.CaptureSnapshotAsync(request.Paths, cacheContext.Target, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task CaptureManualImageSnapshotAsync(
        LaunchContentIntegrityVersionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Version.EffectiveContentSourceKind != ContentSourceKind.Manual)
        {
            return;
        }

        await _integrityService.CaptureSnapshotAsync(
            request.Paths,
            BuildSingleVersionContexts(request).First(context => context.IsCache).Target,
            cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<LaunchContentIntegrityTargetContext> BuildSingleVersionContexts(
        LaunchContentIntegrityVersionRequest request)
    {
        return _targetBuilder.BuildTargets(
            new LaunchContentIntegrityTargetRequest(
                request.Paths,
                new[] { request.Version },
                request.AllVersions,
                request.CacheDisplayNameSuffix));
    }

    /// <summary>
    ///     Repairs a managed remote package.
    /// </summary>
    private async Task RepairManagedPackageAsync(
        LauncherPaths paths,
        LaunchContentIntegrityTargetContext context,
        ContentIntegrityReport report,
        IProgress<PackageUpdateProgress> progress,
        CancellationToken cancellationToken)
    {
        ContentSourceKind sourceKind = context.Version.EffectiveContentSourceKind;
        var installedPath = new OwnedContentPath(paths.ModsDirectory, context.Target.RootDirectory);
        var packagePaths = PackageUpdatePathSet.Create(
            paths,
            installedPath,
            installedPath);
        if (sourceKind == ContentSourceKind.ManagedS3)
        {
            S3ObjectManifestRequest manifestRequest = S3CatalogDefaults.CreateManifestRequest(context.Version);
            IReadOnlyList<RemoteFileManifestEntry> files = await _manifestReader.ReadManifestAsync(
                manifestRequest,
                cancellationToken).ConfigureAwait(false);
            var hashCheckedExtensions = files
                .Select(file => Path.GetExtension(file.FileName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            hashCheckedExtensions.Add(LauncherContentFileTypes.GibExtension);

            IReadOnlyList<RemoteFileManifestEntry> repairFiles = SelectS3FileRepairEntries(
                report,
                context.Target.Id,
                files);
            if (repairFiles.Count > 0)
            {
                _logger.LogInformation(
                    "Repairing {FileCount} S3 package file(s) in place for {ContentName}.",
                    repairFiles.Count,
                    context.Version.DisplayName);
                await _s3PackageUpdater.RepairFilesAsync(
                    new S3PackageFileRepairRequest(
                        repairFiles,
                        manifestRequest,
                        installedPath,
                        hashCheckedExtensions),
                    progress,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            _logger.LogInformation(
                "Repairing S3 package {ContentName} with full package replacement.",
                context.Version.DisplayName);

            await _s3PackageUpdater.UpdateAsync(
                new S3PackageUpdateRequest(
                    files,
                    manifestRequest,
                    packagePaths,
                    hashCheckedExtensions),
                progress,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (sourceKind == ContentSourceKind.ManagedSingleFile)
        {
            await _singleFilePackageUpdater.UpdateAsync(
                DownloadLinkResolver.ResolveDownloadUri(context.Version.SimpleDownloadLink),
                packagePaths,
                progress,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException("Only managed remote content can be repaired automatically.");
    }

    /// <summary>
    ///     Selects the S3 manifest entries that correspond to file-level repair issues for one integrity target.
    /// </summary>
    /// <param name="report">The complete integrity report.</param>
    /// <param name="targetId">The target identifier to inspect.</param>
    /// <param name="files">The remote manifest entries.</param>
    /// <returns>
    ///     The manifest entries that can be repaired in place, or an empty collection when the issue set requires a full
    ///     package repair.
    /// </returns>
    private static IReadOnlyList<RemoteFileManifestEntry> SelectS3FileRepairEntries(
        ContentIntegrityReport report,
        string targetId,
        IReadOnlyList<RemoteFileManifestEntry> files)
    {
        var repairIssues = report.Issues
            .Where(issue =>
                string.Equals(issue.TargetId, targetId, StringComparison.Ordinal) &&
                issue.Action is IntegrityIssueAction.Repair or IntegrityIssueAction.Redownload)
            .ToList();
        if (repairIssues.Count == 0 ||
            repairIssues.Any(issue =>
                issue.Action != IntegrityIssueAction.Repair ||
                issue.Kind is not (IntegrityIssueKind.MissingFile or IntegrityIssueKind.ModifiedFile)))
        {
            return Array.Empty<RemoteFileManifestEntry>();
        }

        var remainingIssuePaths = repairIssues
            .Select(issue => LexicalPath.NormalizeRelativePath(issue.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<RemoteFileManifestEntry> selectedFiles = [];
        foreach (RemoteFileManifestEntry file in files)
        {
            string manifestRelativePath = ManifestPathResolver.NormalizeForManifestIndex(file.FileName);
            string installedRelativePath =
                ManifestPathResolver.NormalizeInstalledPathForManifestIndex(file.FileName);
            bool matchesIssue = remainingIssuePaths.Remove(manifestRelativePath);
            matchesIssue |= remainingIssuePaths.Remove(installedRelativePath);
            if (matchesIssue)
            {
                selectedFiles.Add(file);
            }
        }

        return remainingIssuePaths.Count == 0
            ? selectedFiles
            : Array.Empty<RemoteFileManifestEntry>();
    }

    private static IReadOnlySet<string> GetUntrackedManagedTargetIds(ContentIntegrityReport report)
    {
        HashSet<string> untrackedManagedTargetIds = new(StringComparer.Ordinal);
        HashSet<string> targetIdsWithOtherIssues = new(StringComparer.Ordinal);
        foreach (ContentIntegrityIssue issue in report.Issues)
        {
            if (issue.Kind != IntegrityIssueKind.Untracked)
            {
                targetIdsWithOtherIssues.Add(issue.TargetId);
            }
            else if (issue.SourceKind.IsManagedRemote())
            {
                untrackedManagedTargetIds.Add(issue.TargetId);
            }
        }

        untrackedManagedTargetIds.ExceptWith(targetIdsWithOtherIssues);
        return untrackedManagedTargetIds;
    }

    private static (
        IReadOnlySet<string> TrustAsManualTargetIds,
        IReadOnlySet<string> AbsorbTargetIds,
        IReadOnlySet<string> RepairTargetIds,
        IReadOnlyList<string> ManagedTargetIdsInReportOrder) IndexResolutionIssues(ContentIntegrityReport report)
    {
        HashSet<string> trustAsManualTargetIds = new(StringComparer.Ordinal);
        HashSet<string> absorbTargetIds = new(StringComparer.Ordinal);
        HashSet<string> repairTargetIds = new(StringComparer.Ordinal);
        HashSet<string> seenManagedTargetIds = new(StringComparer.Ordinal);
        List<string> managedTargetIds = [];
        foreach (ContentIntegrityIssue issue in report.Issues)
        {
            switch (issue.Action)
            {
                case IntegrityIssueAction.TrustAsManual:
                    trustAsManualTargetIds.Add(issue.TargetId);
                    break;
                case IntegrityIssueAction.Absorb:
                    absorbTargetIds.Add(issue.TargetId);
                    break;
                case IntegrityIssueAction.Repair:
                case IntegrityIssueAction.Redownload:
                    repairTargetIds.Add(issue.TargetId);
                    break;
            }

            if (issue.SourceKind.IsManagedRemote() && seenManagedTargetIds.Add(issue.TargetId))
            {
                managedTargetIds.Add(issue.TargetId);
            }
        }

        return (trustAsManualTargetIds, absorbTargetIds, repairTargetIds, managedTargetIds);
    }

    /// <summary>
    ///     Refreshes a managed launcher-owned cache target from remote asset links.
    /// </summary>
    private async Task RefreshManagedCacheAsync(
        LaunchContentIntegrityTargetContext context,
        CancellationToken cancellationToken)
    {
        ContentIntegrityTarget target = context.Target;
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            target.RootDirectory,
            "Content metadata paths");
        Directory.CreateDirectory(target.RootDirectory);
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            target.RootDirectory,
            "Content metadata paths");

        IReadOnlyList<RemoteCacheAsset> assets = BuildRemoteCacheAssets(context.Version, target.RootDirectory);
        foreach (string filePath in EnumerateFilesWithoutLinks(target.RootDirectory).ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = ContentIntegrityPath.GetRelativePath(target.RootDirectory, filePath);
            if (ContentIntegrityPath.IsIgnored(target, relativePath))
            {
                continue;
            }

            File.Delete(filePath);
        }

        foreach (string directory in EnumerateDirectoriesWithoutLinks(target.RootDirectory)
                     .OrderByDescending(path => path.Length)
                     .ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }

        foreach (RemoteCacheAsset asset in assets)
        {
            await _assetDownloader.DownloadIfMissingAsync(
                asset.SourceUri,
                asset.DestinationPath,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static HashSet<string> BuildExpectedRemoteCachePaths(LaunchContentIntegrityTargetContext context)
    {
        return BuildRemoteCacheAssets(context.Version, context.Target.RootDirectory)
            .Select(asset => ContentIntegrityPath.GetRelativePath(
                context.Target.RootDirectory,
                asset.DestinationPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<RemoteCacheAsset> BuildRemoteCacheAssets(
        LauncherContentVersion version,
        string cacheDirectory)
    {
        var assets = new List<RemoteCacheAsset>(2);
        AddRemoteCacheAsset(
            assets,
            version.UIImageSourceLink,
            cacheDirectory,
            version.Version);

        if (version.Theme != null)
        {
            AddRemoteCacheAsset(
                assets,
                version.Theme.GenLauncherBackgroundImageLink,
                cacheDirectory,
                LauncherContentTheme.ResolveBackgroundImageBaseName(version.Version));
        }

        return assets;
    }

    private static void AddRemoteCacheAsset(
        ICollection<RemoteCacheAsset> assets,
        string link,
        string cacheDirectory,
        string imageBaseName)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out Uri? sourceUri))
        {
            return;
        }

        assets.Add(new RemoteCacheAsset(
            sourceUri,
            ModificationImageCachePath.ResolveRemoteImagePath(
                cacheDirectory,
                imageBaseName,
                sourceUri)));
    }

    /// <summary>
    ///     Enumerates files without following linked directories into paths the launcher does not own.
    /// </summary>
    private static IEnumerable<string> EnumerateFilesWithoutLinks(string rootDirectory)
    {
        return Directory.EnumerateFiles(
            rootDirectory,
            "*",
            FileSystemPathSafety.CreateRecursiveNoLinksOptions());
    }

    /// <summary>
    ///     Enumerates directories without following linked directories into paths the launcher does not own.
    /// </summary>
    private static IEnumerable<string> EnumerateDirectoriesWithoutLinks(string rootDirectory)
    {
        return Directory.EnumerateDirectories(
            rootDirectory,
            "*",
            FileSystemPathSafety.CreateRecursiveNoLinksOptions());
    }

    /// <summary>
    ///     Bridges package updater progress to launch integrity progress by target id.
    /// </summary>
    private sealed class TargetPackageProgress : IProgress<PackageUpdateProgress>
    {
        private readonly IProgress<LaunchContentIntegrityResolutionProgress>? _progress;
        private readonly string _targetId;

        public TargetPackageProgress(
            string targetId,
            IProgress<LaunchContentIntegrityResolutionProgress>? progress)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetId);

            _targetId = targetId;
            _progress = progress;
        }

        public void Report(PackageUpdateProgress value)
        {
            _progress?.Report(LaunchContentIntegrityResolutionProgress.Package(_targetId, value));
        }
    }

    private sealed record RemoteCacheAsset(
        Uri SourceUri,
        string DestinationPath);
}
