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
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Integrity;

[Collection("Avalonia")]
public sealed class LaunchContentIntegrityCoordinatorTests
{
    [Fact]
    public void EnsureReadyToLaunchAsyncWhenReport_HasNoIssuesReturnsTrueAndBuildsTargetRequest()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContentVersion activeVersion = TestLauncherContent.Version(sourceKind: ContentSourceKind.Manual);
            LauncherContentVersion catalogVersion = TestLauncherContent.Version(
                "GenTool",
                sourceKind: ContentSourceKind.ManagedSingleFile);
            LaunchContentIntegrityTargetRequest? capturedRequest = null;
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            resolutionService.VerifyAsync(
                    Arg.Do<LaunchContentIntegrityTargetRequest>(request => capturedRequest = request),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(CreateVerificationResult()));

            var catalog = new FakeLauncherContentCatalog();
            catalog.Data.AddOrUpdate(catalogVersion);
            RecordingLauncherDialogService dialogService = new();
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
            dialogService.IntegrityReviewRequests.Should().BeEmpty();
        });
    }

    [Fact]
    public void EnsureReadyToLaunchAsyncWhenRepair_IsConfirmedResolvesAndReportsProgress()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContentVersion version =
                TestLauncherContent.Version(sourceKind: ContentSourceKind.ManagedSingleFile);
            ContentIntegrityTarget target = CreateTarget("target-one", version);
            LaunchContentIntegrityTargetContext context = new(target, version, false);
            ContentIntegrityReport issueReport = new(new[]
            {
                CreateIssue(target.Id, target.DisplayName, ContentSourceKind.ManagedSingleFile,
                    IntegrityIssueAction.Repair)
            });
            ILaunchContentIntegrityProgressTarget progressTarget =
                Substitute.For<ILaunchContentIntegrityProgressTarget>();
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

            RecordingLauncherDialogService dialogService = new() { IntegrityReviewResult = true };
            LauncherPackageActivityService packageActivityService = new();
            List<double?> reportedPackageActivityProgress = [];
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
    public void EnsureReadyToLaunchAsync_ResolutionThrows_StopsLaunchAndClearsProgress()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContentVersion version =
                TestLauncherContent.Version(sourceKind: ContentSourceKind.ManagedSingleFile);
            ContentIntegrityTarget target = CreateTarget("target-one", version);
            LaunchContentIntegrityTargetContext context = new(target, version, false);
            ContentIntegrityReport issueReport = new(new[]
            {
                CreateIssue(target.Id, target.DisplayName, ContentSourceKind.ManagedSingleFile,
                    IntegrityIssueAction.Repair)
            });
            ILaunchContentIntegrityProgressTarget progressTarget =
                Substitute.For<ILaunchContentIntegrityProgressTarget>();
            progressTarget.ActiveIntegrityVersion.Returns(version);
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            resolutionService.VerifyAsync(
                    Arg.Any<LaunchContentIntegrityTargetRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(CreateVerificationResult(issueReport, new[] { context })));
            resolutionService.InitializeUntrackedManagedCachesAsync(
                    Arg.Any<LaunchContentIntegrityResolutionRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(false));
            resolutionService.ResolveAsync(
                    Arg.Any<LaunchContentIntegrityResolutionRequest>(),
                    Arg.Any<IProgress<LaunchContentIntegrityResolutionProgress>>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => throw new InvalidOperationException("resolve failed"));
            RecordingLauncherDialogService dialogService = new() { IntegrityReviewResult = true };
            LauncherPackageActivityService packageActivityService = new();
            LaunchContentIntegrityCoordinator coordinator = CreateCoordinator(
                resolutionService,
                packageActivityService: packageActivityService,
                dialogService: dialogService);

            bool result = await coordinator.EnsureReadyToLaunchAsync(
                new[] { version },
                new[] { progressTarget },
                new Window());

            result.Should().BeFalse();
            packageActivityService.IsActive.Should().BeFalse();
            progressTarget.Received(1).CompleteIntegrityProgress();
            ContentIntegrityReport blockedReport = dialogService.IntegrityReviewRequests.Last();
            blockedReport.HasBlockingIssues.Should().BeTrue();
            blockedReport.Issues.Should().ContainSingle().Which.Message.Should().Be("resolve failed");
        });
    }

    [Fact]
    public void EnsureReadyToLaunchAsyncWhenManaged_CachesAreInitializedRetriesVerification()
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
            RecordingLauncherDialogService dialogService = new();
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
            dialogService.IntegrityReviewRequests.Should().BeEmpty();
        });
    }

    [Fact]
    public void EnsureReadyToLaunchAsync_CacheInitializationNeverSettles_StopsAfterFourVerifications()
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
                .Returns(Task.FromResult(true));
            RecordingLauncherDialogService dialogService = new();
            LaunchContentIntegrityCoordinator coordinator = CreateCoordinator(
                resolutionService,
                dialogService: dialogService);

            bool result = await coordinator.EnsureReadyToLaunchAsync(
                Array.Empty<LauncherContentVersion>(),
                Array.Empty<ILaunchContentIntegrityProgressTarget>(),
                new Window());

            result.Should().BeFalse();
            await resolutionService.Received(4).VerifyAsync(
                Arg.Any<LaunchContentIntegrityTargetRequest>(),
                Arg.Any<CancellationToken>());
            ContentIntegrityReport blockedReport =
                dialogService.IntegrityReviewRequests.Should().ContainSingle().Which;
            blockedReport.HasBlockingIssues.Should().BeTrue();
            blockedReport.Issues.Should().ContainSingle().Which.Message.Should().Be("Repeated failure");
        });
    }

    [Fact]
    public void EnsureReadyToLaunchAsyncWhenPackageActivity_IsBusyShowsBlockedReview()
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
            RecordingLauncherDialogService dialogService = new() { IntegrityReviewResult = true };
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
                ContentIntegrityReport blockedReport = dialogService.IntegrityReviewRequests.Last();
                blockedReport.HasBlockingIssues.Should().BeTrue();
                blockedReport.Issues.Should().ContainSingle().Which.Message.Should().Be("Existing");
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
    public void EnsureReadyToLaunchAsyncWhenBlockingReport_IsConfirmedStillStopsLaunch()
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
            RecordingLauncherDialogService dialogService = new() { IntegrityReviewResult = true };
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
    public void EnsureReadyToLaunchAsync_UnknownLegacyIssues_ReviewsOnlyLegacyIssues()
    {
        StaTestRunner.Run(async () =>
        {
            ContentIntegrityReport report = new(new[]
            {
                CreateIssue("manual-target", "Manual", ContentSourceKind.Manual, IntegrityIssueAction.Absorb),
                CreateIssue("legacy-target", "Legacy", ContentSourceKind.UnknownLegacy,
                    IntegrityIssueAction.TrustAsManual)
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
            RecordingLauncherDialogService dialogService = new();
            LaunchContentIntegrityCoordinator coordinator = CreateCoordinator(
                resolutionService,
                dialogService: dialogService);
            Window owner = new();

            bool result = await coordinator.EnsureReadyToLaunchAsync(
                Array.Empty<LauncherContentVersion>(),
                Array.Empty<ILaunchContentIntegrityProgressTarget>(),
                owner);

            result.Should().BeFalse();
            ContentIntegrityReport reviewReport =
                dialogService.IntegrityReviewRequests.Should().ContainSingle().Which;
            reviewReport.Issues.Should().ContainSingle().Which.TargetId.Should().Be("legacy-target");
            await resolutionService.DidNotReceive().ResolveAsync(
                Arg.Any<LaunchContentIntegrityResolutionRequest>(),
                Arg.Any<IProgress<LaunchContentIntegrityResolutionProgress>>(),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void EnsureReadyToLaunchAsyncWhenVerification_ThrowsShowsBlockedReview()
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
            RecordingLauncherDialogService dialogService = new();
            LaunchContentIntegrityCoordinator coordinator = CreateCoordinator(
                resolutionService,
                dialogService: dialogService);
            Window owner = new();

            bool result = await coordinator.EnsureReadyToLaunchAsync(
                Array.Empty<LauncherContentVersion>(),
                Array.Empty<ILaunchContentIntegrityProgressTarget>(),
                owner);

            result.Should().BeFalse();
            ContentIntegrityReport blockedReport =
                dialogService.IntegrityReviewRequests.Should().ContainSingle().Which;
            blockedReport.HasBlockingIssues.Should().BeTrue();
            blockedReport.Issues.Should().ContainSingle().Which.Message.Should().Be("verification failed");
        });
    }

    [Fact]
    public void SnapshotMethods_MatchingSourceKinds_CallOnlyResolutionService()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            LauncherContentVersion allVersion = TestLauncherContent.Version(
                "Catalog",
                sourceKind: ContentSourceKind.Manual);
            var catalog = new FakeLauncherContentCatalog();
            catalog.Data.AddOrUpdate(allVersion);
            LaunchContentIntegrityCoordinator coordinator = CreateCoordinator(
                resolutionService,
                catalog);
            LauncherContentVersion manualVersion = TestLauncherContent.Version(
                "Manual",
                sourceKind: ContentSourceKind.Manual);
            LauncherContentVersion managedVersion = TestLauncherContent.Version(
                "Managed",
                sourceKind: ContentSourceKind.ManagedSingleFile,
                simpleDownloadLink: "https://example.test/package.zip");
            LauncherContentVersion s3Version = TestLauncherContent.S3Version("ManagedS3");

            await coordinator.RegisterManualImportAsync(manualVersion);
            await coordinator.RegisterManualImportAsync(null!);
            await coordinator.CaptureManagedInstallSnapshotAsync(managedVersion);
            await coordinator.CaptureManagedInstallSnapshotAsync(s3Version);
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
            await resolutionService.Received(1).CaptureManagedInstallSnapshotAsync(
                Arg.Is<LaunchContentIntegrityVersionRequest>(request =>
                    request != null && request.Version == s3Version),
                Arg.Any<CancellationToken>());
            await resolutionService.Received(1).CaptureManualImageSnapshotAsync(
                Arg.Is<LaunchContentIntegrityVersionRequest>(request =>
                    request != null && request.Version == manualVersion),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void SnapshotMethods_SwallowResolutionExceptions()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            resolutionService.RegisterManualImportAsync(
                    Arg.Any<LaunchContentIntegrityVersionRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => throw new InvalidOperationException("manual failed"));
            resolutionService.CaptureManagedInstallSnapshotAsync(
                    Arg.Any<LaunchContentIntegrityVersionRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => throw new InvalidOperationException("managed failed"));
            resolutionService.CaptureManualImageSnapshotAsync(
                    Arg.Any<LaunchContentIntegrityVersionRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => throw new InvalidOperationException("image failed"));
            LaunchContentIntegrityCoordinator coordinator = CreateCoordinator(resolutionService);
            LauncherContentVersion manualVersion = TestLauncherContent.Version(
                "Manual",
                sourceKind: ContentSourceKind.Manual);
            LauncherContentVersion managedVersion = TestLauncherContent.Version(
                "Managed",
                sourceKind: ContentSourceKind.ManagedSingleFile,
                simpleDownloadLink: "https://example.test/package.zip");

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
                .Returns(_ => throw persistenceException);
            LaunchContentIntegrityCoordinator coordinator = CreateCoordinator(resolutionService);

            Func<Task> act = () => coordinator.RegisterManualImportAsync(
                TestLauncherContent.Version(
                "Manual",
                sourceKind: ContentSourceKind.Manual));

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
            stringLocalizer ?? FakeStringLocalizer.Create(TestLocalizedStrings.Integrity),
            dialogService ?? new RecordingLauncherDialogService(),
            NullLogger<LaunchContentIntegrityCoordinator>.Instance);
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
}
