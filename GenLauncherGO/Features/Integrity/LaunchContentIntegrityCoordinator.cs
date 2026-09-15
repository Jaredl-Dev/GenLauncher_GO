using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Features.Updating;
using GenLauncherGO.Shared.Dialogs;
using GenLauncherGO.Shared.Formatting;
using GenLauncherGO.Shared.Localization;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Integrity;

/// <summary>
///     Coordinates launch content integrity review, repair, and snapshot workflows for the launcher.
/// </summary>
internal sealed class LaunchContentIntegrityCoordinator
{
    private readonly ILauncherContentCatalog _catalog;
    private readonly ILauncherDialogService _dialogService;
    private readonly ILogger<LaunchContentIntegrityCoordinator> _logger;
    private readonly LauncherPackageActivityService _packageActivityService;
    private readonly ILaunchContentIntegrityResolutionService _resolutionService;
    private readonly LauncherRuntimePathContext _runtimePaths;
    private readonly ILauncherStringLocalizer _stringLocalizer;

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
        _packageActivityService =
            packageActivityService ?? throw new ArgumentNullException(nameof(packageActivityService));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Verifies active launch content and resolves user-approved issues before launch.
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
            LaunchContentIntegrityVerificationResult verification;
            try
            {
                verification = await _resolutionService.VerifyAsync(
                    CreateTargetRequest(launcherPaths, activeVersions),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Active launch content verification failed.");
                await ShowBlockedVerificationDialogAsync(owner, exception.Message);
                return false;
            }

            ContentIntegrityReport report = verification.Report;
            IReadOnlyList<LaunchContentIntegrityTargetContext> contexts = verification.TargetContexts;
            if (!report.HasIssues)
            {
                return true;
            }

            try
            {
                if (await _resolutionService.InitializeUntrackedManagedContentAsync(
                        verification,
                        cancellationToken))
                {
                    continue;
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to initialize managed remote content integrity.");
                await ShowBlockedVerificationDialogAsync(owner, exception.Message);
                return false;
            }

            bool resolutionConfirmed = await _dialogService.ShowIntegrityReviewAsync(
                report,
                owner);

            if (!resolutionConfirmed || report.HasBlockingIssues)
            {
                return false;
            }

            LauncherPackageActivityService.LauncherPackageActivityLease? activityLease = null;
            try
            {
                if (RequiresPackageActivity(report) &&
                    !_packageActivityService.TryBegin(
                        _stringLocalizer["LaunchVerificationRunning"],
                        out activityLease))
                {
                    await ShowBlockedVerificationDialogAsync(
                        owner,
                        _packageActivityService.ActiveDisplayName);
                    return false;
                }

                resolutionProgress.Begin(report, contexts);
                await _resolutionService.ResolveAsync(
                    verification,
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
    ///     Registers a manually imported version as trusted manual content.
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
                CreateTargetRequest(_runtimePaths.ActivePaths, new[] { version }),
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
    ///     Captures an integrity snapshot for a managed installed version.
    /// </summary>
    public async Task CaptureManagedInstallSnapshotAsync(LauncherContentVersion version)
    {
        if (version == null || !version.EffectiveContentSourceKind.IsManagedRemote())
        {
            return;
        }

        try
        {
            await _resolutionService.CaptureManagedInstallSnapshotAsync(
                CreateTargetRequest(_runtimePaths.ActivePaths, new[] { version }),
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
            _catalog.Data.GetAllModificationVersions(),
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
                message)
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
        private readonly HashSet<ILaunchContentIntegrityProgressTarget> _activeProgressTargets = [];

        private readonly Dispatcher _dispatcher;

        private readonly LauncherPackageActivityService _packageActivityService;

        private readonly Dictionary<LauncherContentKey, ILaunchContentIntegrityProgressTarget>
            _progressTargetsByVersion;

        private readonly ILauncherStringLocalizer _stringLocalizer;

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
                .GroupBy(target => target.ActiveIntegrityVersion.ContentKey)
                .ToDictionary(group => group.Key, group => group.First());
        }

        public void Report(LaunchContentIntegrityResolutionProgress value)
        {
            RunOnDispatcher(() => ReportOnDispatcher(value));
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
        ///     Applies a progress update on the dispatcher that owns the bound UI targets.
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
        ///     Runs an action on the dispatcher that owns the bound UI targets.
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
