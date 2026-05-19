using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Exceptions;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Integrity;

public sealed class LaunchContentIntegrityCoordinatorTests
{
    [Fact]
    public void IsManualReturnsTrueOnlyForManualVersions()
    {
        LaunchContentIntegrityCoordinator coordinator = CreateCoordinator();

        coordinator.IsManual(CreateVersion(ContentSourceKind.Manual)).Should().BeTrue();
        coordinator.IsManual(CreateVersion(ContentSourceKind.ManagedSingleFile)).Should().BeFalse();
        coordinator.IsManual(null!).Should().BeFalse();
    }

    [Fact]
    public void EnsureReadyToLaunchAsyncWhenReportHasNoIssuesReturnsTrueAndBuildsTargetRequest()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContentVersion activeVersion = CreateVersion(ContentSourceKind.Manual, "Shockwave");
            LauncherContentVersion catalogVersion = CreateVersion(ContentSourceKind.ManagedSingleFile, "GenTool");
            LaunchContentIntegrityTargetRequest? capturedRequest = null;
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            resolutionService.VerifyAsync(
                    Arg.Do<LaunchContentIntegrityTargetRequest>(request => capturedRequest = request),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(CreateVerificationResult()));

            var catalog = new FakeLauncherContentCatalog();
            catalog.Data.AddOrUpdate(catalogVersion);
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LaunchContentIntegrityCoordinator coordinator = CreateCoordinator(
                resolutionService,
                catalog,
                dialogService: dialogService);
            Window owner = new();

            bool result = await coordinator.EnsureReadyToLaunchAsync(
                new[] { activeVersion },
                Array.Empty<ILaunchContentIntegrityProgressTarget>(),
                owner);

            result.Should().BeTrue();
            capturedRequest.Should().NotBeNull();
            capturedRequest!.ActiveVersions.Should().ContainSingle().Which.Should().BeSameAs(activeVersion);
            capturedRequest.AllVersions.Should().ContainSingle().Which.Should().BeSameAs(catalogVersion);
            capturedRequest.CacheDisplayNameSuffix.Should().Be(" cache");
            await resolutionService.DidNotReceive().InitializeUntrackedManagedCachesAsync(
                Arg.Any<LaunchContentIntegrityResolutionRequest>(),
                Arg.Any<CancellationToken>());
            await dialogService.DidNotReceive().ShowIntegrityReviewAsync(
                Arg.Any<ContentIntegrityReport>(),
                Arg.Any<Window?>());
        });
    }

    [Fact]
    public void EnsureReadyToLaunchAsyncWhenRepairIsConfirmedResolvesAndReportsProgress()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContentVersion version = CreateVersion(ContentSourceKind.ManagedSingleFile, "Shockwave");
            ContentIntegrityTarget target = CreateTarget("target-one", version);
            LaunchContentIntegrityTargetContext context = new(target, version, isCache: false);
            ContentIntegrityReport issueReport = new(new[]
            {
                CreateIssue(target.Id, target.DisplayName, ContentSourceKind.ManagedSingleFile, IntegrityIssueAction.Repair)
            });
            ILaunchContentIntegrityProgressTarget progressTarget = Substitute.For<ILaunchContentIntegrityProgressTarget>();
            progressTarget.CanReportIntegrityProgress.Returns(true);
            progressTarget.ActiveIntegrityVersion.Returns(version);

            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            resolutionService.VerifyAsync(
                    Arg.Any<LaunchContentIntegrityTargetRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(
                    Task.FromResult(CreateVerificationResult(issueReport, new[] { context })),
                    Task.FromResult(CreateVerificationResult()));
            resolutionService.InitializeUntrackedManagedCachesAsync(
                    Arg.Any<LaunchContentIntegrityResolutionRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(false));
            resolutionService.ResolveAsync(
                    Arg.Any<LaunchContentIntegrityResolutionRequest>(),
                    Arg.Any<IProgress<LaunchContentIntegrityResolutionProgress>>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    IProgress<LaunchContentIntegrityResolutionProgress> progress =
                        callInfo.ArgAt<IProgress<LaunchContentIntegrityResolutionProgress>>(1);
                    progress.Report(LaunchContentIntegrityResolutionProgress.Package(
                        target.Id,
                        new PackageUpdateProgress(10485760, 5242880, 50, "package.big")));
                    progress.Report(LaunchContentIntegrityResolutionProgress.Complete(target.Id));
                    return Task.CompletedTask;
                });

            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowIntegrityReviewAsync(
                    Arg.Any<ContentIntegrityReport>(),
                    Arg.Any<Window?>())
                .Returns(true);
            LauncherPackageActivityService packageActivityService = new();
            List<double?> reportedPackageActivityProgress = new();
            packageActivityService.ActivityChanged += (_, _) =>
                reportedPackageActivityProgress.Add(packageActivityService.ProgressPercentage);
            LaunchContentIntegrityCoordinator coordinator = CreateCoordinator(
                resolutionService,
                packageActivityService: packageActivityService,
                dialogService: dialogService);
            Window owner = new();

            bool result = await coordinator.EnsureReadyToLaunchAsync(
                new[] { version },
                new[] { progressTarget },
                owner);

            result.Should().BeTrue();
            packageActivityService.IsActive.Should().BeFalse();
            reportedPackageActivityProgress.Should().Contain(50D);
            progressTarget.Received(1).BeginIntegrityProgress("Preparing");
            progressTarget.Received().ReportIntegrityProgress("Downloaded 5 MB of 10 MB", 50);
            progressTarget.Received().ReportIntegrityProgress("Unpacking", 100);
            progressTarget.Received(1).CompleteIntegrityProgress();
            await resolutionService.Received(1).ResolveAsync(
                Arg.Is<LaunchContentIntegrityResolutionRequest>(request =>
                    request != null &&
                    request.Report == issueReport &&
                    request.TargetContexts.Count == 1 &&
                    request.TargetContexts[0] == context),
                Arg.Any<IProgress<LaunchContentIntegrityResolutionProgress>>(),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void EnsureReadyToLaunchAsyncWhenManagedCachesAreInitializedRetriesVerification()
    {
        StaTestRunner.Run(async () =>
        {
            ContentIntegrityReport issueReport = new(new[]
            {
                CreateIssue(
                    "managed-target",
                    "Managed",
                    ContentSourceKind.ManagedSingleFile,
                    IntegrityIssueAction.Repair)
            });
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            resolutionService.VerifyAsync(
                    Arg.Any<LaunchContentIntegrityTargetRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(
                    Task.FromResult(CreateVerificationResult(issueReport)),
                    Task.FromResult(CreateVerificationResult()));
            resolutionService.InitializeUntrackedManagedCachesAsync(
                    Arg.Any<LaunchContentIntegrityResolutionRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(true));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LaunchContentIntegrityCoordinator coordinator = CreateCoordinator(
                resolutionService,
                dialogService: dialogService);

            bool result = await coordinator.EnsureReadyToLaunchAsync(
                Array.Empty<LauncherContentVersion>(),
                Array.Empty<ILaunchContentIntegrityProgressTarget>(),
                new Window());

            result.Should().BeTrue();
            await resolutionService.Received(2).VerifyAsync(
                Arg.Any<LaunchContentIntegrityTargetRequest>(),
                Arg.Any<CancellationToken>());
            await resolutionService.Received(1).InitializeUntrackedManagedCachesAsync(
                Arg.Any<LaunchContentIntegrityResolutionRequest>(),
                Arg.Any<CancellationToken>());
            await dialogService.DidNotReceive().ShowIntegrityReviewAsync(
                Arg.Any<ContentIntegrityReport>(),
                Arg.Any<Window?>());
        });
    }

    [Fact]
    public void EnsureReadyToLaunchAsyncWhenPackageActivityIsBusyShowsBlockedReview()
    {
        StaTestRunner.Run(async () =>
        {
            ContentIntegrityReport issueReport = new(new[]
            {
                CreateIssue(
                    "managed-target",
                    "Managed",
                    ContentSourceKind.ManagedSingleFile,
                    IntegrityIssueAction.Repair)
            });
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            resolutionService.VerifyAsync(
                    Arg.Any<LaunchContentIntegrityTargetRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(CreateVerificationResult(issueReport)));
            resolutionService.InitializeUntrackedManagedCachesAsync(
                    Arg.Any<LaunchContentIntegrityResolutionRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(false));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowIntegrityReviewAsync(
                    issueReport,
                    Arg.Any<Window?>())
                .Returns(true);
            LauncherPackageActivityService packageActivityService = new();
            packageActivityService.TryBegin(
                    "Existing",
                    out LauncherPackageActivityService.LauncherPackageActivityLease? lease)
                .Should()
                .BeTrue();
            LaunchContentIntegrityCoordinator coordinator = CreateCoordinator(
                resolutionService,
                packageActivityService: packageActivityService,
                dialogService: dialogService);

            try
            {
                bool result = await coordinator.EnsureReadyToLaunchAsync(
                    Array.Empty<LauncherContentVersion>(),
                    Array.Empty<ILaunchContentIntegrityProgressTarget>(),
                    new Window());

                result.Should().BeFalse();
                await dialogService.Received(1).ShowIntegrityReviewAsync(
                    Arg.Is<ContentIntegrityReport>(report =>
                        report != null &&
                        report.HasBlockingIssues &&
                        report.Issues[0].Message == "Existing"),
                    Arg.Any<Window?>());
                await resolutionService.DidNotReceive().ResolveAsync(
                    Arg.Any<LaunchContentIntegrityResolutionRequest>(),
                    Arg.Any<IProgress<LaunchContentIntegrityResolutionProgress>>(),
                    Arg.Any<CancellationToken>());
            }
            finally
            {
                lease?.Dispose();
            }
        });
    }

    [Fact]
    public void EnsureReadyToLaunchAsyncWhenBlockingReportIsConfirmedStillStopsLaunch()
    {
        StaTestRunner.Run(async () =>
        {
            ContentIntegrityReport blockingReport = new(new[]
            {
                new ContentIntegrityIssue(
                    "blocking",
                    "Blocking",
                    ContentSourceKind.UnknownLegacy,
                    IntegrityIssueKind.VerificationError,
                    IntegrityIssueAction.Block,
                    ".",
                    "blocked")
            });
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            resolutionService.VerifyAsync(
                    Arg.Any<LaunchContentIntegrityTargetRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(CreateVerificationResult(blockingReport)));
            resolutionService.InitializeUntrackedManagedCachesAsync(
                    Arg.Any<LaunchContentIntegrityResolutionRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(false));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowIntegrityReviewAsync(
                    Arg.Any<ContentIntegrityReport>(),
                    Arg.Any<Window?>())
                .Returns(true);
            LaunchContentIntegrityCoordinator coordinator = CreateCoordinator(
                resolutionService,
                dialogService: dialogService);

            bool result = await coordinator.EnsureReadyToLaunchAsync(
                Array.Empty<LauncherContentVersion>(),
                Array.Empty<ILaunchContentIntegrityProgressTarget>(),
                new Window());

            result.Should().BeFalse();
            await resolutionService.DidNotReceive().ResolveAsync(
                Arg.Any<LaunchContentIntegrityResolutionRequest>(),
                Arg.Any<IProgress<LaunchContentIntegrityResolutionProgress>>(),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void EnsureReadyToLaunchAsyncWhenUnknownLegacyIssuesExistReviewsOnlyLegacyIssues()
    {
        StaTestRunner.Run(async () =>
        {
            ContentIntegrityReport report = new(new[]
            {
                CreateIssue("manual-target", "Manual", ContentSourceKind.Manual, IntegrityIssueAction.Absorb),
                CreateIssue("legacy-target", "Legacy", ContentSourceKind.UnknownLegacy, IntegrityIssueAction.TrustAsManual),
            });
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            resolutionService.VerifyAsync(
                    Arg.Any<LaunchContentIntegrityTargetRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(CreateVerificationResult(report)));
            resolutionService.InitializeUntrackedManagedCachesAsync(
                    Arg.Any<LaunchContentIntegrityResolutionRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(false));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowIntegrityReviewAsync(
                    Arg.Any<ContentIntegrityReport>(),
                    Arg.Any<Window?>())
                .Returns(false);
            LaunchContentIntegrityCoordinator coordinator = CreateCoordinator(
                resolutionService,
                dialogService: dialogService);
            Window owner = new();

            bool result = await coordinator.EnsureReadyToLaunchAsync(
                Array.Empty<LauncherContentVersion>(),
                Array.Empty<ILaunchContentIntegrityProgressTarget>(),
                owner);

            result.Should().BeFalse();
            await dialogService.Received(1).ShowIntegrityReviewAsync(
                Arg.Is<ContentIntegrityReport>(reviewReport =>
                    reviewReport != null &&
                    reviewReport.Issues.Count == 1 &&
                    reviewReport.Issues[0].TargetId == "legacy-target"),
                owner);
            await resolutionService.DidNotReceive().ResolveAsync(
                Arg.Any<LaunchContentIntegrityResolutionRequest>(),
                Arg.Any<IProgress<LaunchContentIntegrityResolutionProgress>>(),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void EnsureReadyToLaunchAsyncWhenVerificationThrowsShowsBlockedReview()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            resolutionService.VerifyAsync(
                    Arg.Any<LaunchContentIntegrityTargetRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns<Task<LaunchContentIntegrityVerificationResult>>(_ =>
                    throw new InvalidOperationException("verification failed"));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LaunchContentIntegrityCoordinator coordinator = CreateCoordinator(
                resolutionService,
                dialogService: dialogService);
            Window owner = new();

            bool result = await coordinator.EnsureReadyToLaunchAsync(
                Array.Empty<LauncherContentVersion>(),
                Array.Empty<ILaunchContentIntegrityProgressTarget>(),
                owner);

            result.Should().BeFalse();
            await dialogService.Received(1).ShowIntegrityReviewAsync(
                Arg.Is<ContentIntegrityReport>(report =>
                    report != null &&
                    report.HasBlockingIssues &&
                    report.Issues[0].Message == "verification failed"),
                owner);
        });
    }

    [Fact]
    public void SnapshotMethodsCallResolutionServiceOnlyForMatchingSourceKinds()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            LauncherContentVersion allVersion = CreateVersion(ContentSourceKind.Manual, "Catalog");
            var catalog = new FakeLauncherContentCatalog();
            catalog.Data.AddOrUpdate(allVersion);
            LaunchContentIntegrityCoordinator coordinator = CreateCoordinator(
                resolutionService,
                catalog);
            LauncherContentVersion manualVersion = CreateVersion(ContentSourceKind.Manual, "Manual");
            LauncherContentVersion managedVersion = CreateVersion(
                ContentSourceKind.ManagedSingleFile,
                "Managed",
                "https://example.test/package.zip");

            await coordinator.RegisterManualImportAsync(manualVersion);
            await coordinator.RegisterManualImportAsync(null!);
            await coordinator.CaptureManagedInstallSnapshotAsync(managedVersion);
            await coordinator.CaptureManagedInstallSnapshotAsync(manualVersion);
            await coordinator.CaptureManualImageSnapshotAsync(manualVersion);
            await coordinator.CaptureManualImageSnapshotAsync(managedVersion);

            await resolutionService.Received(1).RegisterManualImportAsync(
                Arg.Is<LaunchContentIntegrityVersionRequest>(request =>
                    request != null &&
                    request.Version == manualVersion &&
                    request.AllVersions.Any(version => version == allVersion)),
                Arg.Any<CancellationToken>());
            await resolutionService.Received(1).CaptureManagedInstallSnapshotAsync(
                Arg.Is<LaunchContentIntegrityVersionRequest>(request =>
                    request != null && request.Version == managedVersion),
                Arg.Any<CancellationToken>());
            await resolutionService.Received(1).CaptureManualImageSnapshotAsync(
                Arg.Is<LaunchContentIntegrityVersionRequest>(request =>
                    request != null && request.Version == manualVersion),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void SnapshotMethodsSwallowResolutionExceptions()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            resolutionService.RegisterManualImportAsync(
                    Arg.Any<LaunchContentIntegrityVersionRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns<Task>(_ => throw new InvalidOperationException("manual failed"));
            resolutionService.CaptureManagedInstallSnapshotAsync(
                    Arg.Any<LaunchContentIntegrityVersionRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns<Task>(_ => throw new InvalidOperationException("managed failed"));
            resolutionService.CaptureManualImageSnapshotAsync(
                    Arg.Any<LaunchContentIntegrityVersionRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns<Task>(_ => throw new InvalidOperationException("image failed"));
            LaunchContentIntegrityCoordinator coordinator = CreateCoordinator(resolutionService);
            LauncherContentVersion manualVersion = CreateVersion(ContentSourceKind.Manual, "Manual");
            LauncherContentVersion managedVersion = CreateVersion(
                ContentSourceKind.ManagedSingleFile,
                "Managed",
                "https://example.test/package.zip");

            Func<Task> act = async () =>
            {
                await coordinator.RegisterManualImportAsync(manualVersion);
                await coordinator.CaptureManagedInstallSnapshotAsync(managedVersion);
                await coordinator.CaptureManualImageSnapshotAsync(manualVersion);
            };

            await act.Should().NotThrowAsync();
        });
    }

    [Fact]
    public void RegisterManualImportAsync_WhenCatalogPersistenceFails_PropagatesForUserNotification()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            var persistenceException =
                new LauncherContentPersistenceException(new IOException("Catalog file is locked."));
            resolutionService.RegisterManualImportAsync(
                    Arg.Any<LaunchContentIntegrityVersionRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns<Task>(_ => throw persistenceException);
            LaunchContentIntegrityCoordinator coordinator = CreateCoordinator(resolutionService);

            Func<Task> act = () => coordinator.RegisterManualImportAsync(
                CreateVersion(ContentSourceKind.Manual, "Manual"));

            await act.Should().ThrowAsync<LauncherContentPersistenceException>()
                .Where(exception => exception == persistenceException);
        });
    }

    private static LaunchContentIntegrityCoordinator CreateCoordinator(
        ILaunchContentIntegrityResolutionService? resolutionService = null,
        ILauncherContentCatalog? catalog = null,
        LauncherPackageActivityService? packageActivityService = null,
        ILauncherDialogService? dialogService = null,
        ILauncherStringLocalizer? stringLocalizer = null)
    {
        return new LaunchContentIntegrityCoordinator(
            resolutionService ?? Substitute.For<ILaunchContentIntegrityResolutionService>(),
            catalog ?? new FakeLauncherContentCatalog(),
            TestLauncherPaths.CreateRuntimePathContext(TestLauncherPaths.Create()),
            packageActivityService ?? new LauncherPackageActivityService(),
            stringLocalizer ?? CreateStringLocalizer(),
            dialogService ?? Substitute.For<ILauncherDialogService>(),
            NullLogger<LaunchContentIntegrityCoordinator>.Instance);
    }

    private static ILauncherStringLocalizer CreateStringLocalizer()
    {
        return new TestStringLocalizer(new Dictionary<string, string>
        {
            ["AbsorbAndLaunch"] = "Absorb and launch",
            ["CancelLaunch"] = "Cancel launch",
            ["DownloadInProgress"] = "Downloaded {0} of {1}",
            ["FixAbsorbAndLaunch"] = "Fix, absorb, and launch",
            ["FixAndLaunch"] = "Fix and launch",
            ["IntegrityAbsorbGroup"] = "Absorb",
            ["IntegrityBlockGroup"] = "Blocked",
            ["IntegrityBlockedDescription"] = "Remove blocked entries or restore content",
            ["IntegrityCacheSuffix"] = " cache",
            ["IntegrityDeleteGroup"] = "Delete",
            ["IntegrityDialogTitle"] = "Integrity",
            ["IntegrityIssueEmptyDirectoryLabel"] = "Empty folder",
            ["IntegrityIssueGroupSummaryMultiple"] = "{0} changes",
            ["IntegrityIssueGroupSummarySingle"] = "{0} change",
            ["IntegrityIssueMissingFileLabel"] = "Missing",
            ["IntegrityIssueModifiedFileLabel"] = "Modified",
            ["IntegrityIssueUnexpectedFileLabel"] = "Added",
            ["IntegrityIssueUnsafeLinkLabel"] = "Unsafe link",
            ["IntegrityIssueUntrackedLabel"] = "Untracked",
            ["IntegrityIssueVerificationErrorLabel"] = "Verification error",
            ["IntegrityLegacyDescription"] = "Trust {0}",
            ["IntegrityManagedDescription"] = "Repair {0}",
            ["IntegrityManualDescription"] = "Absorb {0}",
            ["IntegrityMixedDescription"] = "Repair {0}; absorb {1}",
            ["IntegrityRepairGroup"] = "Repair",
            ["IntegrityRepeatedFailure"] = "Repeated failure",
            ["IntegrityRedownloadGroup"] = "Redownload",
            ["IntegrityTrustGroup"] = "Trust",
            ["LaunchVerificationRunning"] = "Launch verification",
            ["Preparing"] = "Preparing",
            ["TrustAsManual"] = "Trust as manual",
            ["UnpackingPreparing"] = "Unpacking",
        });
    }

    private static LaunchContentIntegrityVerificationResult CreateVerificationResult(
        ContentIntegrityReport? report = null,
        IReadOnlyList<LaunchContentIntegrityTargetContext>? contexts = null)
    {
        return new LaunchContentIntegrityVerificationResult(
            report ?? new ContentIntegrityReport(Array.Empty<ContentIntegrityIssue>()),
            contexts ?? Array.Empty<LaunchContentIntegrityTargetContext>());
    }

    private static ContentIntegrityIssue CreateIssue(
        string targetId,
        string targetName,
        ContentSourceKind sourceKind,
        IntegrityIssueAction action)
    {
        return new ContentIntegrityIssue(
            targetId,
            targetName,
            sourceKind,
            IntegrityIssueKind.ModifiedFile,
            action,
            "Data/file.big");
    }

    private static ContentIntegrityTarget CreateTarget(
        string targetId,
        LauncherContentVersion version)
    {
        return new ContentIntegrityTarget(
            targetId,
            version.DisplayName,
            @"C:\Games\ZeroHour\GenLauncherGO\Mods\Shockwave\1.0",
            version.EffectiveContentSourceKind,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static LauncherContentVersion CreateVersion(
        ContentSourceKind sourceKind,
        string name = "Shockwave",
        string simpleDownloadLink = "")
    {
        return new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { ContentSourceKind = sourceKind },
            Name = name,
            Version = "1.0",
            ModificationType = ModificationType.Mod,
            SimpleDownloadLink = simpleDownloadLink
        };
    }

}
