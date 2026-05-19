using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Exceptions;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Shared.Formatting;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.UI.Features.Integrity;

/// <summary>
/// Coordinates launch content integrity review, repair, and snapshot workflows for the launcher.
/// </summary>
internal sealed class LaunchContentIntegrityCoordinator
{
    private readonly ILaunchContentIntegrityResolutionService _resolutionService;
    private readonly ILauncherContentCatalog _catalog;
    private readonly LauncherRuntimePathContext _runtimePaths;
    private readonly LauncherPackageActivityService _packageActivityService;
    private readonly ILauncherStringLocalizer _stringLocalizer;
    private readonly ILauncherDialogService _dialogService;
    private readonly ILogger<LaunchContentIntegrityCoordinator> _logger;

    public LaunchContentIntegrityCoordinator(
        ILaunchContentIntegrityResolutionService resolutionService,
        ILauncherContentCatalog catalog,
        LauncherRuntimePathContext runtimePaths,
        LauncherPackageActivityService packageActivityService,
        ILauncherStringLocalizer stringLocalizer,
        ILauncherDialogService dialogService,
        ILogger<LaunchContentIntegrityCoordinator> logger)
    {
        _resolutionService = resolutionService ?? throw new ArgumentNullException(nameof(resolutionService));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _runtimePaths = runtimePaths ?? throw new ArgumentNullException(nameof(runtimePaths));
        _packageActivityService = packageActivityService ?? throw new ArgumentNullException(nameof(packageActivityService));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Verifies active launch content and resolves user-approved issues before launch.
    /// </summary>
    public async Task<bool> EnsureReadyToLaunchAsync(
        IReadOnlyList<LauncherContentVersion> activeVersions,
        IReadOnlyList<ILaunchContentIntegrityProgressTarget> activeProgressTargets,
        Window owner)
    {
        ArgumentNullException.ThrowIfNull(activeVersions);
        ArgumentNullException.ThrowIfNull(activeProgressTargets);
        ArgumentNullException.ThrowIfNull(owner);

        IntegrityResolutionProgress resolutionProgress = new(
            activeProgressTargets,
            _stringLocalizer,
            Dispatcher.UIThread,
            _packageActivityService);
        CancellationToken cancellationToken = CancellationToken.None;
        LauncherPaths launcherPaths = _runtimePaths.ActivePaths;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            IReadOnlyList<LaunchContentIntegrityTargetContext> contexts;
            ContentIntegrityReport report;
            try
            {
                LaunchContentIntegrityVerificationResult verification =
                    await _resolutionService.VerifyAsync(
                        CreateTargetRequest(launcherPaths, activeVersions),
                        cancellationToken);
                contexts = verification.TargetContexts;
                report = verification.Report;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Active launch content verification failed.");
                await ShowBlockedVerificationDialogAsync(owner, exception.Message);
                return false;
            }

            if (!report.HasIssues)
            {
                return true;
            }

            try
            {
                if (await _resolutionService.InitializeUntrackedManagedCachesAsync(
                        new LaunchContentIntegrityResolutionRequest(
                            launcherPaths,
                            report,
                            contexts),
                        cancellationToken))
                {
                    continue;
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to initialize managed remote image integrity.");
                await ShowBlockedVerificationDialogAsync(owner, exception.Message);
                return false;
            }

            ContentIntegrityReport reviewReport = report;
            if (!report.HasBlockingIssues && report.HasUnknownLegacyIssues)
            {
                reviewReport = new ContentIntegrityReport(report.Issues
                    .Where(issue => issue.Action == IntegrityIssueAction.TrustAsManual)
                    .ToList());
            }

            bool resolutionConfirmed = await _dialogService.ShowIntegrityReviewAsync(
                reviewReport,
                owner);

            if (!resolutionConfirmed || reviewReport.HasBlockingIssues)
            {
                return false;
            }

            LauncherPackageActivityService.LauncherPackageActivityLease? activityLease = null;
            try
            {
                if (RequiresPackageActivity(reviewReport) &&
                    !_packageActivityService.TryBegin(
                        _stringLocalizer["LaunchVerificationRunning"],
                        out activityLease))
                {
                    await ShowBlockedVerificationDialogAsync(
                        owner,
                        _packageActivityService.ActiveDisplayName);
                    return false;
                }

                resolutionProgress.Begin(reviewReport, contexts);
                await _resolutionService.ResolveAsync(
                    new LaunchContentIntegrityResolutionRequest(
                        launcherPaths,
                        reviewReport,
                        contexts),
                    resolutionProgress,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to resolve active launch content integrity issues.");
                await ShowBlockedVerificationDialogAsync(owner, exception.Message);
                return false;
            }
            finally
            {
                activityLease?.Dispose();
                resolutionProgress.Reset();
            }
        }

        await ShowBlockedVerificationDialogAsync(
            owner,
            _stringLocalizer["IntegrityRepeatedFailure"]);
        return false;
    }

    /// <summary>
    /// Registers a manually imported version as trusted manual content.
    /// </summary>
    public async Task RegisterManualImportAsync(LauncherContentVersion version)
    {
        if (version == null)
        {
            return;
        }

        try
        {
            await _resolutionService.RegisterManualImportAsync(
                CreateVersionRequest(_runtimePaths.ActivePaths, version),
                CancellationToken.None);
        }
        catch (LauncherContentPersistenceException)
        {
            // The import exists on disk and in memory, but its manual classification must not be reported as
            // complete until it is durably saved. Let the owning UI operation show the failure and retry on close.
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not capture the initial manual integrity snapshot for {ContentName}; launch verification will migrate it.",
                version.DisplayName);
        }
    }

    /// <summary>
    /// Captures an integrity snapshot for a managed installed version.
    /// </summary>
    public async Task CaptureManagedInstallSnapshotAsync(LauncherContentVersion version)
    {
        if (version == null ||
            version.EffectiveContentSourceKind is not
            (ContentSourceKind.ManagedS3 or ContentSourceKind.ManagedSingleFile))
        {
            return;
        }

        try
        {
            await _resolutionService.CaptureManagedInstallSnapshotAsync(
                CreateVersionRequest(_runtimePaths.ActivePaths, version),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not initialize integrity snapshots for {ContentName}; launch verification will migrate them.",
                version.DisplayName);
        }
    }

    /// <summary>
    /// Captures an integrity snapshot for a manually assigned package image.
    /// </summary>
    public async Task CaptureManualImageSnapshotAsync(LauncherContentVersion version)
    {
        if (version == null || version.EffectiveContentSourceKind != ContentSourceKind.Manual)
        {
            return;
        }

        try
        {
            await _resolutionService.CaptureManualImageSnapshotAsync(
                CreateVersionRequest(_runtimePaths.ActivePaths, version),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not update the manual image integrity snapshot for {ContentName}; launch verification will review it.",
                version.DisplayName);
        }
    }

    public bool IsManual(LauncherContentVersion version)
    {
        return version != null && version.EffectiveContentSourceKind == ContentSourceKind.Manual;
    }

    private LaunchContentIntegrityTargetRequest CreateTargetRequest(
        LauncherPaths launcherPaths,
        IReadOnlyList<LauncherContentVersion> activeVersions)
    {
        return new LaunchContentIntegrityTargetRequest(
            launcherPaths,
            activeVersions,
            _catalog.Data.GetAllModsVersionsList(),
            _stringLocalizer["IntegrityCacheSuffix"]);
    }

    private LaunchContentIntegrityVersionRequest CreateVersionRequest(
        LauncherPaths launcherPaths,
        LauncherContentVersion version)
    {
        return new LaunchContentIntegrityVersionRequest(
            launcherPaths,
            version,
            _catalog.Data.GetAllModsVersionsList(),
            _stringLocalizer["IntegrityCacheSuffix"]);
    }

    private Task ShowBlockedVerificationDialogAsync(Window owner, string message)
    {
        ContentIntegrityReport report = new(new[]
        {
            new ContentIntegrityIssue(
                "verification",
                "GenLauncherGO",
                ContentSourceKind.UnknownLegacy,
                IntegrityIssueKind.VerificationError,
                IntegrityIssueAction.Block,
                ".",
                message),
        });
        return _dialogService.ShowIntegrityReviewAsync(report, owner);
    }

    private static bool RequiresPackageActivity(ContentIntegrityReport report)
    {
        return report.Issues.Any(issue =>
            issue.Action is IntegrityIssueAction.Repair or IntegrityIssueAction.Redownload);
    }

    private sealed class IntegrityResolutionProgress : IProgress<LaunchContentIntegrityResolutionProgress>
    {
        private readonly HashSet<ILaunchContentIntegrityProgressTarget> _activeProgressTargets = new();
        private readonly ILauncherStringLocalizer _stringLocalizer;

        private readonly Dispatcher _dispatcher;

        private readonly LauncherPackageActivityService _packageActivityService;

        private readonly Dictionary<LauncherContentKey, ILaunchContentIntegrityProgressTarget>
            _progressTargetsByVersion;

        private Dictionary<string, LaunchContentIntegrityTargetContext> _contextsByTargetId =
            new(StringComparer.Ordinal);

        public IntegrityResolutionProgress(
            IReadOnlyList<ILaunchContentIntegrityProgressTarget> activeProgressTargets,
            ILauncherStringLocalizer stringLocalizer,
            Dispatcher dispatcher,
            LauncherPackageActivityService packageActivityService)
        {
            _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _packageActivityService = packageActivityService ??
                                      throw new ArgumentNullException(nameof(packageActivityService));
            _progressTargetsByVersion = activeProgressTargets
                .Where(target => target is { CanReportIntegrityProgress: true })
                .Select(target => new
                {
                    Target = target,
                    Version = target.ActiveIntegrityVersion,
                })
                .Where(item => item.Version != null)
                .GroupBy(item => item.Version!.ContentKey)
                .ToDictionary(group => group.Key, group => group.First().Target);
        }

        public void Begin(
            ContentIntegrityReport report,
            IReadOnlyList<LaunchContentIntegrityTargetContext> contexts)
        {
            RunOnDispatcher(() =>
            {
                _contextsByTargetId = contexts.ToDictionary(
                    context => context.Target.Id,
                    StringComparer.Ordinal);

                var affectedTargetIds = report.Issues
                    .Where(issue => issue.Action != IntegrityIssueAction.Block)
                    .Select(issue => issue.TargetId)
                    .ToHashSet(StringComparer.Ordinal);

                foreach (LaunchContentIntegrityTargetContext context in contexts
                             .Where(context => affectedTargetIds.Contains(context.Target.Id)))
                {
                    Begin(context);
                }
            });
        }

        public void Report(LaunchContentIntegrityResolutionProgress value)
        {
            RunOnDispatcher(() => ReportOnDispatcher(value));
        }

        public void Reset()
        {
            RunOnDispatcher(() =>
            {
                foreach (ILaunchContentIntegrityProgressTarget progressTarget in _activeProgressTargets.ToList())
                {
                    progressTarget.CompleteIntegrityProgress();
                }

                _activeProgressTargets.Clear();
                _contextsByTargetId.Clear();
            });
        }

        /// <summary>
        /// Applies a progress update on the dispatcher that owns the bound UI targets.
        /// </summary>
        private void ReportOnDispatcher(LaunchContentIntegrityResolutionProgress value)
        {
            if (!_contextsByTargetId.TryGetValue(value.TargetId, out LaunchContentIntegrityTargetContext? context))
            {
                return;
            }

            ILaunchContentIntegrityProgressTarget? progressTarget = FindProgressTarget(context);
            if (progressTarget == null)
            {
                return;
            }

            if (value.Completed)
            {
                progressTarget.ReportIntegrityProgress(_stringLocalizer["UnpackingPreparing"], 100);
                return;
            }

            if (value.PackageProgress != null)
            {
                _packageActivityService.ReportProgress(value.PackageProgress.ProgressPercentage);
                Report(progressTarget, value.PackageProgress);
            }
        }

        private void Begin(LaunchContentIntegrityTargetContext context)
        {
            ILaunchContentIntegrityProgressTarget? progressTarget = FindProgressTarget(context);
            if (progressTarget == null)
            {
                return;
            }

            _activeProgressTargets.Add(progressTarget);
            progressTarget.BeginIntegrityProgress(_stringLocalizer["Preparing"]);
        }

        private ILaunchContentIntegrityProgressTarget? FindProgressTarget(LaunchContentIntegrityTargetContext context)
        {
            _progressTargetsByVersion.TryGetValue(context.Version.ContentKey,
                out ILaunchContentIntegrityProgressTarget? progressTarget);
            return progressTarget;
        }

        private void Report(ILaunchContentIntegrityProgressTarget progressTarget, PackageUpdateProgress progress)
        {
            if (!PackageProgressTextFormatter.TryFormat(
                    progress,
                    _stringLocalizer,
                    out string message,
                    out int percentage))
            {
                return;
            }

            progressTarget.ReportIntegrityProgress(message, percentage);
        }

        /// <summary>
        /// Runs an action on the dispatcher that owns the bound UI targets.
        /// </summary>
        private void RunOnDispatcher(Action action)
        {
            if (_dispatcher.CheckAccess())
            {
                action();
                return;
            }

            _dispatcher.Invoke(action);
        }
    }
}
