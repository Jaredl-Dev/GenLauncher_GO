using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.Tests.UI.Features.Mods;

public sealed class ModificationViewModelTests
{
    [Fact]
    public void Constructor_WhenLatestVersionInstalled_LabelsDisabledUpdateButtonAsUpToDate()
    {
        LauncherContent modification = new(TestLauncherContent.Version(installed: true));
        FakeStringLocalizer stringLocalizer = new(new Dictionary<string, string>
        {
            ["LatestVersion"] = "Latest version: ",
            ["Delete"] = "Delete",
            ["RemoveFromList"] = "Remove from list",
            ["Update"] = "Update!",
            ["UpToDate"] = "Up-to-date"
        });

        ModificationViewModel viewModel = TestModificationTile.Create(modification, stringLocalizer);

        viewModel.UpdateButtonEnabled.Should().BeFalse();
        viewModel.UpdateButtonContent.Should().Be("Up-to-date");
    }

    [Fact]
    public void Constructor_WhenSingleLatestVersionIsNotInstalled_LeavesInstallButtonText()
    {
        LauncherContent modification = new(TestLauncherContent.Version());
        FakeStringLocalizer stringLocalizer = new(new Dictionary<string, string>
        {
            ["LatestVersion"] = "Latest version: ",
            ["Delete"] = "Delete",
            ["RemoveFromList"] = "Remove from list",
            ["Update"] = "Update!",
            ["UpToDate"] = "Up-to-date"
        });

        ModificationViewModel viewModel = TestModificationTile.Create(modification, stringLocalizer);

        viewModel.UpdateButtonEnabled.Should().BeTrue();
        viewModel.UpdateButtonContent.Should().Be("Install");
        viewModel.UpdateButtonBlinking.Should().BeFalse();
        viewModel.IsVersionSelectorVisible.Should().BeFalse();
        viewModel.IsVersionActionVisible.Should().BeTrue();
        viewModel.VersionActionContent.Should().Be("Remove from list");
    }

    [Fact]
    public void Constructor_SuspendedDownload_RestoresPausedPresentation()
    {
        LauncherContent modification = new(TestLauncherContent.Version(
            downloadSuspended: true,
            suspendedProgressPercentage: 42));
        FakeStringLocalizer stringLocalizer = new(new Dictionary<string, string>
        {
            ["LatestVersion"] = "Latest version: ",
            ["Paused"] = "Paused",
            ["Resume"] = "Resume"
        });

        ModificationViewModel viewModel = TestModificationTile.Create(modification, stringLocalizer);

        viewModel.ProgressValue.Should().Be(42);
        viewModel.ProgressMessage.Should().Be("Paused");
        viewModel.UpdateButtonContent.Should().Be("Resume");
        viewModel.IsUpdateButtonVisible.Should().BeTrue();
        viewModel.UpdateButtonEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(ModificationType.Mod, false, true)]
    [InlineData(ModificationType.Mod, true, false)]
    [InlineData(ModificationType.Advertising, false, false)]
    public void NotifyInstallAvailable_BlinksOnlyTheNewUninstalledMod(
        ModificationType modificationType,
        bool installed,
        bool expected)
    {
        LauncherContent modification = new(TestLauncherContent.Version(
            type: modificationType,
            installed: installed));
        ModificationViewModel viewModel = TestModificationTile.Create(modification, new FakeStringLocalizer());

        viewModel.NotifyInstallAvailable();

        viewModel.UpdateButtonBlinking.Should().Be(expected);
    }

    [Fact]
    public void AvailableUpdate_KeepsSelectorForPreviouslyInstalledVersion()
    {
        LauncherContentVersion installedVersion = TestLauncherContent.Version(version: "1.0", installed: true);
        LauncherContentVersion updateVersion = TestLauncherContent.Version(version: "2.0");
        LauncherContent modification = TestLauncherContent.From(installedVersion, updateVersion);

        ModificationViewModel viewModel = TestModificationTile.Create(modification, new FakeStringLocalizer());

        viewModel.IsVersionSelectorVisible.Should().BeTrue();
        viewModel.IsVersionActionVisible.Should().BeFalse();
        viewModel.VersionOptions.Should().ContainSingle().Which.VersionName.Should().Be("1.0");
    }

    [Fact]
    public void Constructor_UsesCanonicalVersionSelectionAndLatestPresentation()
    {
        LauncherContentVersion installedVersion = TestLauncherContent.Version(version: "1.0", installed: true);
        LauncherContentVersion remoteVersion = TestLauncherContent.Version(version: "2.0");
        LauncherContent modification = TestLauncherContent.From(remoteVersion, installedVersion);

        ModificationViewModel viewModel = TestModificationTile.Create(modification, new FakeStringLocalizer());

        viewModel.SelectedVersion.Should().BeSameAs(installedVersion);
        viewModel.LatestVersion.Should().BeSameAs(remoteVersion);
        viewModel.LatestVersionInfo.Should().Be("Latest version: 2.0");
    }

    [Theory]
    [InlineData(ContentSourceKind.UnknownLegacy, true)]
    [InlineData(ContentSourceKind.Manual, true)]
    [InlineData(ContentSourceKind.ManagedSingleFile, false)]
    [InlineData(ContentSourceKind.ManagedS3, false)]
    public void LocalModificationClassification_UsesEffectiveContentSourceKind(
        ContentSourceKind sourceKind,
        bool expected)
    {
        LauncherContent modification = new(TestLauncherContent.Version(
            installed: true,
            sourceKind: sourceKind));

        ModificationViewModel viewModel = TestModificationTile.Create(modification, new FakeStringLocalizer());

        viewModel.LocalMod.Should().Be(expected);
    }

    [Fact]
    public void AdvertisingVersionText_RemainsUnprefixed()
    {
        LauncherContent modification = new(TestLauncherContent.Version(
            version: "Summer",
            type: ModificationType.Advertising));

        ModificationViewModel viewModel = TestModificationTile.Create(modification, new FakeStringLocalizer());

        viewModel.LatestVersionInfo.Should().Be("Summer");
    }

    [Fact]
    public void SelectingAfterPackageActivity_UsesLatestInstalledVersion()
    {
        LauncherContentVersion earliestInstalled = TestLauncherContent.Version(
            version: "1.0",
            installed: true,
            isSelected: true);
        LauncherContentVersion latestInstalled = TestLauncherContent.Version(version: "2.0", installed: true);
        LauncherContent modification = TestLauncherContent.From(earliestInstalled, latestInstalled);
        ModificationViewModel viewModel = TestModificationTile.Create(modification, new FakeStringLocalizer());
        viewModel.BeginPackageActivityPresentation();

        viewModel.SelectItemInComboBox();

        viewModel.SelectedVersion.Should().BeSameAs(latestInstalled);
        viewModel.ReadyToRun.Should().BeTrue();
        viewModel.VersionOptions.Select(option => option.VersionName).Should().Equal("1.0", "2.0");
        earliestInstalled.Installation.IsSelected.Should().BeFalse();
        latestInstalled.Installation.IsSelected.Should().BeTrue();
    }

    [Fact]
    public async Task BeginPackageActivityPresentation_ShowsPauseAndCancelDownloadActionsAsync()
    {
        ColorsInfo colors = TestLauncherTheme.Create();
        LauncherPackageActivityService activityService = new();
        ModificationViewModel viewModel = TestModificationTile.Create(
            new LauncherContent(TestLauncherContent.Version()),
            new FakeStringLocalizer(new Dictionary<string, string>
            {
                ["LatestVersion"] = "Latest version: ",
                ["CancelDownloadAction"] = "Cancel Download",
                ["Delete"] = "Delete",
                ["RemoveFromList"] = "Remove from list",
                ["Pause"] = "Pause"
            }),
            activityService,
            colors);
        TaskCompletionSource<PackageDownloadResult> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        activityService.TryStartDownload(
                viewModel,
                "Test",
                (_, _, _) => completion.Task,
                viewModel.BeginPackageActivityPresentation,
                _ => { },
                () => { },
                viewModel.CompletePackageActivityPresentation,
                out Task<PackageDownloadResult>? lifecycle)
            .Should()
            .BeTrue();

        viewModel.UpdateButtonContent.Should().Be("Pause");
        viewModel.IsVersionSelectorVisible.Should().BeFalse();
        viewModel.IsVersionActionVisible.Should().BeTrue();
        viewModel.VersionActionContent.Should().Be("Cancel Download");
        viewModel.ProgressTextForeground.Should().BeSameAs(colors.GenLauncherDownloadTextColor);
        viewModel.ProgressBorderBrush.Should().BeSameAs(colors.GenLauncherBorderColor);

        completion.SetResult(PackageDownloadResult.Succeeded());
        await lifecycle!;

        viewModel.ProgressTextForeground.Should().BeSameAs(colors.GenLauncherDefaultTextColor);
        viewModel.ProgressBorderBrush.Should().BeSameAs(colors.GenLauncherInactiveBorder);
    }

    [Fact]
    public void BeginIntegrityProgress_PublishesPackageActivityChangedOnce()
    {
        ModificationViewModel viewModel = TestModificationTile.Create(
            new LauncherContent(TestLauncherContent.Version()),
            new FakeStringLocalizer());
        int notificationCount = 0;
        viewModel.PackageActivityChanged += (_, _) => notificationCount++;

        viewModel.BeginIntegrityProgress("Preparing");

        notificationCount.Should().Be(1);
        viewModel.HasActivePackageActivity.Should().BeTrue();
        viewModel.ProgressMessage.Should().Be("Preparing");
        viewModel.ProgressValue.Should().Be(0);
    }

    [Fact]
    public void ReportForwardedChildPackageActivity_PublishesPackageActivityChangedOnce()
    {
        ModificationViewModel viewModel = TestModificationTile.Create(
            new LauncherContent(TestLauncherContent.Version()),
            new FakeStringLocalizer());
        int notificationCount = 0;
        viewModel.PackageActivityChanged += (_, _) => notificationCount++;

        viewModel.ReportForwardedChildPackageActivity("Downloading child", 42);

        notificationCount.Should().Be(1);
        viewModel.HasActivePackageActivity.Should().BeTrue();
        viewModel.ProgressMessage.Should().Be("Downloading child");
        viewModel.ProgressValue.Should().Be(42);
    }

    [Fact]
    public async Task ReportForwardedChildPackageActivity_DuringOwnDownload_IsIgnoredAsync()
    {
        LauncherPackageActivityService activityService = new();
        ModificationViewModel viewModel = TestModificationTile.Create(
            new LauncherContent(TestLauncherContent.Version()),
            new FakeStringLocalizer(),
            activityService);
        TaskCompletionSource<PackageDownloadResult> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        activityService.TryStartDownload(
                viewModel,
                "ShockWave",
                (_, _, _) => completion.Task,
                viewModel.BeginPackageActivityPresentation,
                _ => { },
                () => { },
                viewModel.CompletePackageActivityPresentation,
                out Task<PackageDownloadResult>? lifecycle)
            .Should()
            .BeTrue();
        viewModel.ReportPackageProgress("Downloading", 42);

        viewModel.ReportForwardedChildPackageActivity("Child", 10);

        viewModel.ProgressMessage.Should().Be("Downloading");
        viewModel.ProgressValue.Should().Be(42);

        completion.SetResult(PackageDownloadResult.Succeeded());
        await lifecycle!;

        viewModel.HasActivePackageActivity.Should().BeFalse();
    }

    [Fact]
    public void PackageDownloadPauseState_ChangesUpdateAction()
    {
        ModificationViewModel viewModel = TestModificationTile.Create(
            new LauncherContent(TestLauncherContent.Version()),
            new FakeStringLocalizer(new Dictionary<string, string>
            {
                ["Pause"] = "Pause",
                ["Resume"] = "Resume"
            }));

        viewModel.SetPackageDownloadPaused(true);
        viewModel.UpdateButtonContent.Should().Be("Resume");

        viewModel.SetPackageDownloadPaused(false);
        viewModel.UpdateButtonContent.Should().Be("Pause");
    }

    [Fact]
    public void Selection_DoesNotMutateControlAvailability()
    {
        LauncherContent modification = new(new LauncherContentVersion(new LauncherContentInstallation
        {
            Installed = true
        })
        {
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.0",
            NewsLink = "https://example.test/news",
            NetworkInfo = "https://example.test/network",
            SupportLink = "https://example.test/support"
        });
        ModificationViewModel viewModel = TestModificationTile.Create(modification, new FakeStringLocalizer());

        viewModel.IsSelected = true;
        viewModel.IsSelected = false;

        viewModel.IsVersionSelectorVisible.Should().BeTrue();
        viewModel.IsChangeLogVisible.Should().BeTrue();
        viewModel.IsNetworkInfoVisible.Should().BeTrue();
        viewModel.IsSupportButtonVisible.Should().BeTrue();
    }

    [Theory]
    [InlineData(ModificationType.Mod, false, false)]
    [InlineData(ModificationType.Mod, true, true)]
    [InlineData(ModificationType.Advertising, false, true)]
    public void SelectionGatedActions_AdvertisingContent_RemainVisible(
        ModificationType modificationType,
        bool isSelected,
        bool expected)
    {
        LauncherContent modification = new(TestLauncherContent.Version(
            type: modificationType,
            installed: true));
        ModificationViewModel viewModel = TestModificationTile.Create(modification, new FakeStringLocalizer());

        viewModel.IsSelected = isSelected;

        viewModel.IsSelectedOrAdvertising.Should().Be(expected);
    }

    [Fact]
    public async Task PackageActivity_ProjectsCanonicalLifecycleOwnerAsync()
    {
        var activityService = new LauncherPackageActivityService();
        ModificationViewModel viewModel = TestModificationTile.Create(
            new LauncherContent(TestLauncherContent.Version()),
            new FakeStringLocalizer(),
            activityService);
        var completion = new TaskCompletionSource<PackageDownloadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        activityService.TryStartDownload(
                viewModel,
                "ShockWave",
                (_, _, _) => completion.Task,
                viewModel.BeginPackageActivityPresentation,
                _ => { },
                () => { },
                viewModel.CompletePackageActivityPresentation,
                out Task<PackageDownloadResult>? lifecycle)
            .Should()
            .BeTrue();

        activityService.GetActiveDownloadTask(viewModel).Should().BeSameAs(lifecycle);
        viewModel.HasActivePackageActivity.Should().BeTrue();

        completion.SetResult(PackageDownloadResult.Succeeded());
        await lifecycle!;

        activityService.GetActiveDownloadTask(viewModel).Should().BeNull();
        viewModel.HasActivePackageActivity.Should().BeFalse();
    }

    [Theory]
    [InlineData(PackageDownloadStatus.Canceled, "Canceled", false)]
    [InlineData(PackageDownloadStatus.Suspended, "Paused", true)]
    [InlineData(PackageDownloadStatus.RecoverableFailure, "Error: Try again", false)]
    [InlineData(PackageDownloadStatus.UnexpectedFailure, "Error: Unexpected", false)]
    public void CompletePackageActivityPresentation_ProjectsTerminalState(
        PackageDownloadStatus status,
        string expectedMessage,
        bool expectedSuspended)
    {
        FakeStringLocalizer localizer = new(new Dictionary<string, string>
        {
            ["Canceled"] = "Canceled",
            ["Error"] = "Error: ",
            ["LatestVersion"] = "Latest version: ",
            ["Paused"] = "Paused",
            ["Resume"] = "Resume",
            ["UnexpectedErrorDetails"] = "Unexpected"
        });
        ModificationViewModel viewModel = TestModificationTile.Create(
            new LauncherContent(TestLauncherContent.Version()),
            localizer);
        viewModel.ReportPackageProgress("Downloading", 42);

        viewModel.CompletePackageActivityPresentation(CreateDownloadResult(status));

        viewModel.ProgressMessage.Should().Be(expectedMessage);
        viewModel.ReadyToRun.Should().BeFalse();
        viewModel.LatestVersion.Installation.DownloadSuspended.Should().Be(expectedSuspended);
        viewModel.LatestVersion.Installation.SuspendedProgressPercentage.Should()
            .Be(expectedSuspended ? 42 : 0);
    }

    [Theory]
    [InlineData(ContentSourceKind.Manual, ModificationType.Mod, true)]
    [InlineData(ContentSourceKind.ManagedSingleFile, ModificationType.Mod, false)]
    [InlineData(ContentSourceKind.Manual, ModificationType.Advertising, false)]
    public void CanSetImage_RequiresManualNonAdvertisingContent(
        ContentSourceKind contentSourceKind,
        ModificationType modificationType,
        bool expected)
    {
        LauncherContent modification = new(TestLauncherContent.Version(
            type: modificationType,
            installed: true,
            sourceKind: contentSourceKind));

        ModificationViewModel viewModel = TestModificationTile.Create(modification, new FakeStringLocalizer());

        viewModel.CanSetImage.Should().Be(expected);
    }

    [Fact]
    public void ContextMenuLinkAvailabilityComes_FromStableModelFields()
    {
        LauncherContent modification = new(new LauncherContentVersion(new LauncherContentInstallation
        {
            Installed = true
        })
        {
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.0",
            ModDBLink = "https://example.test/moddb"
        });

        ModificationViewModel viewModel = TestModificationTile.Create(modification, new FakeStringLocalizer());

        viewModel.CanOpenModDb.Should().BeTrue();
        viewModel.CanOpenDiscord.Should().BeFalse();
    }

    private static PackageDownloadResult CreateDownloadResult(PackageDownloadStatus status)
    {
        return status switch
        {
            PackageDownloadStatus.Canceled => PackageDownloadResult.Canceled(),
            PackageDownloadStatus.Suspended => PackageDownloadResult.Suspended(),
            PackageDownloadStatus.RecoverableFailure => PackageDownloadResult.RecoverableFailure("Try again"),
            PackageDownloadStatus.UnexpectedFailure => PackageDownloadResult.UnexpectedFailure("diagnostic"),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported test status.")
        };
    }
}
