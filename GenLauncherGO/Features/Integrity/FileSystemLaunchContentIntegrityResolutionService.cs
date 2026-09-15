using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Features.Updating;
using GenLauncherGO.Shared.IO;
using GenLauncherGO.Shared.Remote;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Integrity;

/// <summary>
///     Resolves launch-readiness integrity issues using persisted snapshots, package repair, and cache refresh.
/// </summary>
internal sealed class FileSystemLaunchContentIntegrityResolutionService : ILaunchContentIntegrityResolutionService
{
    private readonly IRemoteAssetDownloader _assetDownloader;

    private readonly ILauncherContentCatalog _catalog;

    private readonly IFileHashService _fileHashService;
    private readonly IContentIntegrityService _integrityService;

    private readonly ILogger<FileSystemLaunchContentIntegrityResolutionService> _logger;

    private readonly ManagedPackageSourceResolver _packageSourceResolver;

    private readonly IS3PackageUpdater _s3PackageUpdater;

    private readonly ISingleFilePackageUpdater _singleFilePackageUpdater;

    private readonly FileSystemLaunchContentIntegrityTargetBuilder _targetBuilder;

    public FileSystemLaunchContentIntegrityResolutionService(
        IContentIntegrityService integrityService,
        FileSystemLaunchContentIntegrityTargetBuilder targetBuilder,
        ManagedPackageSourceResolver packageSourceResolver,
        IS3PackageUpdater s3PackageUpdater,
        ISingleFilePackageUpdater singleFilePackageUpdater,
        IRemoteAssetDownloader assetDownloader,
        ILauncherContentCatalog catalog,
        IFileHashService fileHashService,
        ILogger<FileSystemLaunchContentIntegrityResolutionService> logger)
    {
        _integrityService = integrityService ?? throw new ArgumentNullException(nameof(integrityService));
        _targetBuilder = targetBuilder ?? throw new ArgumentNullException(nameof(targetBuilder));
        _packageSourceResolver = packageSourceResolver ??
                                 throw new ArgumentNullException(nameof(packageSourceResolver));
        _s3PackageUpdater = s3PackageUpdater ?? throw new ArgumentNullException(nameof(s3PackageUpdater));
        _singleFilePackageUpdater = singleFilePackageUpdater ??
                                    throw new ArgumentNullException(nameof(singleFilePackageUpdater));
        _assetDownloader = assetDownloader ?? throw new ArgumentNullException(nameof(assetDownloader));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _fileHashService = fileHashService ?? throw new ArgumentNullException(nameof(fileHashService));
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
        return new LaunchContentIntegrityVerificationResult(request.Paths, report, contexts);
    }

    public async Task<bool> InitializeUntrackedManagedContentAsync(
        LaunchContentIntegrityVerificationResult verification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(verification);

        IReadOnlySet<string> untrackedManagedTargetIds = GetUntrackedManagedTargetIds(verification.Report);
        var untrackedContexts = verification.TargetContexts
            .Where(context => untrackedManagedTargetIds.Contains(context.Target.Id))
            .ToList();

        bool initializedAny = false;
        foreach (LaunchContentIntegrityTargetContext context in untrackedContexts)
        {
            bool initialized = context.IsCache
                ? await _integrityService.CaptureSnapshotIfMatchesExpectedFileSetAsync(
                    verification.Paths,
                    context.Target,
                    BuildExpectedRemoteCachePaths(context),
                    cancellationToken).ConfigureAwait(false)
                : await AdoptUntrackedManagedPackageAsync(verification.Paths, context, cancellationToken)
                    .ConfigureAwait(false);
            if (!initialized)
            {
                continue;
            }

            _logger.LogInformation(
                "Initialized managed remote integrity for {TargetName}.",
                context.Target.DisplayName);
            initializedAny = true;
        }

        return initializedAny;
    }

    public async Task ResolveAsync(
        LaunchContentIntegrityVerificationResult verification,
        IProgress<LaunchContentIntegrityResolutionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(verification);

        var contextIndex =
            verification.TargetContexts.ToDictionary(context => context.Target.Id, StringComparer.Ordinal);
        (IReadOnlySet<string> RepairTargetIds, IReadOnlyList<string> ManagedTargetIdsInReportOrder) issueIndex =
            IndexResolutionIssues(verification.Report);

        await _integrityService.ApplyCleanupAsync(
            verification.Report,
            verification.TargetContexts.Select(context => context.Target).ToList(),
            cancellationToken).ConfigureAwait(false);

        foreach (LaunchContentIntegrityTargetContext context in verification.TargetContexts.Where(context =>
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
                    verification.Paths,
                    context,
                    verification.Report,
                    new TargetPackageProgress(context.Target.Id, progress),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (string targetId in issueIndex.ManagedTargetIdsInReportOrder)
        {
            if (contextIndex.TryGetValue(targetId, out LaunchContentIntegrityTargetContext? context))
            {
                await _integrityService.CaptureSnapshotAsync(verification.Paths, context.Target, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    public async Task RegisterManualImportAsync(
        LaunchContentIntegrityTargetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LauncherContentVersion version = GetSingleVersion(request);

        version.Installation.ContentSourceKind = ContentSourceKind.Manual;
        _catalog.SaveLauncherData();

        // Only managed content is snapshot-tracked. An import whose name and version match a published package is
        // still verified against that remote source, so it needs the initial snapshot a download would have left.
        if (!version.EffectiveContentSourceKind.IsManagedRemote())
        {
            return;
        }

        IReadOnlyList<LaunchContentIntegrityTargetContext> contexts = _targetBuilder.BuildTargets(request);
        await _integrityService.CaptureSnapshotAsync(
            request.Paths,
            contexts.First(context => !context.IsCache).Target,
            cancellationToken).ConfigureAwait(false);

        if (version.ModificationType == ModificationType.Mod)
        {
            await _integrityService.CaptureSnapshotAsync(
                request.Paths,
                contexts.First(context => context.IsCache).Target,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task CaptureManagedInstallSnapshotAsync(
        LaunchContentIntegrityTargetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LauncherContentVersion version = GetSingleVersion(request);

        if (!version.EffectiveContentSourceKind.IsManagedRemote())
        {
            return;
        }

        IReadOnlyList<LaunchContentIntegrityTargetContext> contexts = _targetBuilder.BuildTargets(request);
        await _integrityService.CaptureSnapshotAsync(
            request.Paths,
            contexts.First(context => !context.IsCache).Target,
            cancellationToken).ConfigureAwait(false);

        if (version.ModificationType == ModificationType.Mod)
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

    private static LauncherContentVersion GetSingleVersion(LaunchContentIntegrityTargetRequest request)
    {
        if (request.ActiveVersions.Count != 1)
        {
            throw new ArgumentException("Exactly one active version is required.", nameof(request));
        }

        return request.ActiveVersions[0];
    }

    /// <summary>
    ///     Adopts an untracked managed package whose installed files already match the remote manifest, so a package the
    ///     launcher never installed itself is trusted without redownloading bytes the user already has.
    /// </summary>
    /// <remarks>
    ///     Reading the manifest reaches the network, and a package that was tampered with is exactly the case this must
    ///     not wave through. Every failure and every mismatch therefore leaves the package untracked, so the launch
    ///     review and its repair path still run: adoption is never the reason a launch is blocked, and never the reason
    ///     unverified content is trusted.
    /// </remarks>
    private async Task<bool> AdoptUntrackedManagedPackageAsync(
        LauncherPaths paths,
        LaunchContentIntegrityTargetContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            ManagedPackageSourceResolver.PackageSource? source = await _packageSourceResolver.ResolveAsync(
                context.Version,
                cancellationToken).ConfigureAwait(false);

            // Single-file packages publish one archive and no per-file manifest, so what they install cannot be proven
            // from remote metadata alone.
            if (source is not ManagedPackageSourceResolver.PackageSource.S3 s3Source)
            {
                return false;
            }

            HashSet<string> expectedRelativePaths = new(StringComparer.OrdinalIgnoreCase);
            foreach (RemoteFileManifestEntry file in s3Source.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string installedFilePath = ManifestPathResolver.ResolveInstalledPath(
                    context.Target.RootDirectory,
                    file.FileName);
                if (!await S3PackageFileMatch.MatchesAsync(
                        _fileHashService,
                        file,
                        installedFilePath,
                        S3HashValidationPolicy.InstallHashCheckedExtensions,
                        cancellationToken).ConfigureAwait(false))
                {
                    _logger.LogInformation(
                        "Untracked managed package {TargetName} differs from its remote manifest at {FileName}.",
                        context.Target.DisplayName,
                        file.FileName);
                    return false;
                }

                expectedRelativePaths.Add(ContentIntegrityPath.GetRelativePath(
                    context.Target.RootDirectory,
                    installedFilePath));
            }

            // The conditional capture re-reads the folder, so an installed file the manifest never listed still keeps
            // the package untracked. Matching every manifest entry is necessary, not sufficient.
            return await _integrityService.CaptureSnapshotIfMatchesExpectedFileSetAsync(
                paths,
                context.Target,
                expectedRelativePaths,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not compare untracked managed package {TargetName} against its remote manifest; launch verification will review it.",
                context.Target.DisplayName);
            return false;
        }
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
        var installedPath = new OwnedContentPath(paths.ModsDirectory, context.Target.RootDirectory);
        var packagePaths = PackageUpdatePathSet.Create(
            paths,
            installedPath,
            installedPath);
        ManagedPackageSourceResolver.PackageSource? source = await _packageSourceResolver.ResolveAsync(
            context.Version,
            cancellationToken).ConfigureAwait(false);
        if (source is ManagedPackageSourceResolver.PackageSource.S3 s3Source)
        {
            IReadOnlySet<string> hashCheckedExtensions =
                S3HashValidationPolicy.CreateRepairHashCheckedExtensions(s3Source.Files);

            IReadOnlyList<RemoteFileManifestEntry> repairFiles = SelectS3FileRepairEntries(
                report,
                context.Target.Id,
                s3Source.Files);
            if (repairFiles.Count > 0)
            {
                _logger.LogInformation(
                    "Repairing {FileCount} S3 package file(s) in place for {ContentName}.",
                    repairFiles.Count,
                    context.Version.DisplayName);
                await _s3PackageUpdater.RepairFilesAsync(
                    new S3PackageFileRepairRequest(
                        repairFiles,
                        s3Source.Request,
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
                    s3Source.Files,
                    s3Source.Request,
                    packagePaths,
                    hashCheckedExtensions),
                progress,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (source is ManagedPackageSourceResolver.PackageSource.SingleFile singleFileSource)
        {
            await _singleFilePackageUpdater.UpdateAsync(
                singleFileSource.Metadata,
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
        IReadOnlySet<string> RepairTargetIds,
        IReadOnlyList<string> ManagedTargetIdsInReportOrder) IndexResolutionIssues(ContentIntegrityReport report)
    {
        HashSet<string> repairTargetIds = new(StringComparer.Ordinal);
        HashSet<string> seenManagedTargetIds = new(StringComparer.Ordinal);
        List<string> managedTargetIds = [];
        foreach (ContentIntegrityIssue issue in report.Issues)
        {
            if (issue.Action is IntegrityIssueAction.Repair or IntegrityIssueAction.Redownload)
            {
                repairTargetIds.Add(issue.TargetId);
            }

            if (issue.SourceKind.IsManagedRemote() && seenManagedTargetIds.Add(issue.TargetId))
            {
                managedTargetIds.Add(issue.TargetId);
            }
        }

        return (repairTargetIds, managedTargetIds);
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

        IReadOnlyList<(Uri SourceUri, string DestinationPath)> assets =
            ModificationImageCachePath.ResolveRemoteAssets(context.Version, target.RootDirectory);
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

        foreach ((Uri sourceUri, string destinationPath) in assets)
        {
            await _assetDownloader.DownloadIfMissingAsync(
                sourceUri,
                destinationPath,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static HashSet<string> BuildExpectedRemoteCachePaths(LaunchContentIntegrityTargetContext context)
    {
        return ModificationImageCachePath.ResolveRemoteAssets(context.Version, context.Target.RootDirectory)
            .Select(asset => ContentIntegrityPath.GetRelativePath(
                context.Target.RootDirectory,
                asset.DestinationPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
}
