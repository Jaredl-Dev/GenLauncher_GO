using System.Collections.Generic;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Shared.Themes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Mods;

public sealed class ModificationViewModelTests
{
    [Fact]
    public void Constructor_WhenLatestVersionInstalled_LabelsDisabledUpdateButtonAsUpToDate()
    {
        LauncherContent modification = CreateModification(installed: true);
        TestStringLocalizer stringLocalizer = new(new Dictionary<string, string>
        {
            ["LatestVersion"] = "Latest version: ",
            ["Delete"] = "Delete",
            ["RemoveFromList"] = "Remove from list",
            ["Update"] = "Update!",
            ["UpToDate"] = "Up-to-date"
        });

        ModificationViewModel viewModel = CreateViewModel(modification, stringLocalizer);

        viewModel.UpdateButtonEnabled.Should().BeFalse();
        viewModel.UpdateButtonContent.Should().Be("Up-to-date");
    }

    [Fact]
    public void Constructor_WhenSingleLatestVersionIsNotInstalled_LeavesInstallButtonText()
    {
        LauncherContent modification = CreateModification(installed: false);
        TestStringLocalizer stringLocalizer = new(new Dictionary<string, string>
        {
            ["LatestVersion"] = "Latest version: ",
            ["Delete"] = "Delete",
            ["RemoveFromList"] = "Remove from list",
            ["Update"] = "Update!",
            ["UpToDate"] = "Up-to-date"
        });

        ModificationViewModel viewModel = CreateViewModel(modification, stringLocalizer);

        viewModel.UpdateButtonEnabled.Should().BeTrue();
        viewModel.UpdateButtonContent.Should().Be("Install");
        viewModel.UpdateButtonBlinking.Should().BeFalse();
        viewModel.IsVersionSelectorVisible.Should().BeFalse();
        viewModel.IsVersionActionVisible.Should().BeTrue();
        viewModel.VersionActionContent.Should().Be("Remove from list");
    }

    [Fact]
    public void NotifyInstallAvailableBlinksOnlyTheNewUninstalledMod()
    {
        ModificationViewModel viewModel = CreateViewModel(
            CreateModification(installed: false),
            new TestStringLocalizer());

        viewModel.NotifyInstallAvailable();

        viewModel.UpdateButtonBlinking.Should().BeTrue();
    }

    [Fact]
    public void AvailableUpdateKeepsSelectorForPreviouslyInstalledVersion()
    {
        LauncherContentVersion installedVersion = CreateModification(
            installed: true,
            versionName: "1.0").LatestVersion;
        LauncherContentVersion updateVersion = CreateModification(
            installed: false,
            versionName: "2.0").LatestVersion;
        LauncherContent modification = CreateVersionedModification(installedVersion, updateVersion);

        ModificationViewModel viewModel = CreateViewModel(
            modification,
            new TestStringLocalizer());

        viewModel.IsVersionSelectorVisible.Should().BeTrue();
        viewModel.IsVersionActionVisible.Should().BeFalse();
        viewModel.VersionOptions.Should().ContainSingle().Which.VersionName.Should().Be("1.0");
    }

    [Fact]
    public void ConstructorUsesCanonicalVersionSelectionAndLatestPresentation()
    {
        LauncherContentVersion installedVersion = new(new LauncherContentInstallation
        {
            Installed = true,
        })
        {
            Name = "ShockWave",
            Version = "1.0",
            ModificationType = ModificationType.Mod,
        };
        LauncherContentVersion remoteVersion = new()
        {
            Name = "ShockWave",
            Version = "2.0",
            ModificationType = ModificationType.Mod,
        };
        LauncherContent modification = CreateVersionedModification(remoteVersion, installedVersion);

        ModificationViewModel viewModel = CreateViewModel(modification, new TestStringLocalizer());

        viewModel.SelectedVersion.Should().BeSameAs(installedVersion);
        viewModel.LatestVersion.Should().BeSameAs(remoteVersion);
        viewModel.LatestVersionInfo.Should().Be("Latest version: 2.0");
    }

    [Theory]
    [InlineData(ContentSourceKind.UnknownLegacy, true)]
    [InlineData(ContentSourceKind.ManagedSingleFile, false)]
    public void LocalModificationClassificationUsesEffectiveContentSourceKind(
        ContentSourceKind sourceKind,
        bool expected)
    {
        LauncherContent modification = CreateModification(
            installed: true,
            contentSourceKind: sourceKind);

        ModificationViewModel viewModel = CreateViewModel(modification, new TestStringLocalizer());

        viewModel.LocalMod.Should().Be(expected);
    }

    [Fact]
    public void AdvertisingVersionTextRemainsUnprefixed()
    {
        LauncherContent modification = CreateModification(
            installed: false,
            modificationType: ModificationType.Advertising,
            versionName: "Summer");

        ModificationViewModel viewModel = CreateViewModel(modification, new TestStringLocalizer());

        viewModel.LatestVersionInfo.Should().Be("Summer");
    }

    [Fact]
    public void SelectingAfterPackageActivityUsesLatestInstalledVersion()
    {
        LauncherContentVersion latestInstalled = CreateModification(
            installed: true,
            versionName: "2.0").LatestVersion;
        LauncherContentVersion earliestInstalled = CreateModification(
            installed: true,
            versionName: "1.0").LatestVersion;
        earliestInstalled.Installation.IsSelected = true;
        LauncherContent modification = CreateVersionedModification(
            latestInstalled,
            earliestInstalled);
        ModificationViewModel viewModel = CreateViewModel(modification, new TestStringLocalizer());
        viewModel.BeginPackageActivityPresentation();

        viewModel.SelectItemInComboBox();

        viewModel.SelectedVersion.Should().BeSameAs(latestInstalled);
        viewModel.ReadyToRun.Should().BeTrue();
        earliestInstalled.Installation.IsSelected.Should().BeFalse();
        latestInstalled.Installation.IsSelected.Should().BeTrue();
    }

    [Fact]
    public void BeginPackageActivityPresentationShowsPauseAndCancelDownloadActions()
    {
        LauncherPackageActivityService activityService = new();
        ModificationViewModel viewModel = CreateViewModel(
            CreateModification(installed: false),
            new TestStringLocalizer(new Dictionary<string, string>
            {
                ["LatestVersion"] = "Latest version: ",
                ["CancelDownloadAction"] = "Cancel Download",
                ["Delete"] = "Delete",
                ["RemoveFromList"] = "Remove from list",
                ["Pause"] = "Pause"
            }),
            TestLauncherTheme.Create(),
            activityService);
        TaskCompletionSource<PackageDownloadResult> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        activityService.TryStartDownload(
            viewModel,
            "Test",
            (_, _, _) => completion.Task,
            viewModel.BeginPackageActivityPresentation,
            _ => { },
            () => { },
            _ => { },
            out _);

        viewModel.UpdateButtonContent.Should().Be("Pause");
        viewModel.IsVersionSelectorVisible.Should().BeFalse();
        viewModel.IsVersionActionVisible.Should().BeTrue();
        viewModel.VersionActionContent.Should().Be("Cancel Download");

        completion.SetResult(PackageDownloadResult.Succeeded());
    }

    [Fact]
    public void PackageDownloadPauseStateChangesUpdateAction()
    {
        ModificationViewModel viewModel = CreateViewModel(
            CreateModification(installed: false),
            new TestStringLocalizer(new Dictionary<string, string>
            {
                ["Pause"] = "Pause",
                ["Resume"] = "Resume"
            }));

        viewModel.SetPackageDownloadPaused(isPaused: true);
        viewModel.UpdateButtonContent.Should().Be("Resume");

        viewModel.SetPackageDownloadPaused(isPaused: false);
        viewModel.UpdateButtonContent.Should().Be("Pause");
    }

    [Fact]
    public void SelectionDoesNotMutateControlAvailability()
    {
        LauncherContent modification = CreateModification(
            installed: true,
            newsLink: "https://example.test/news",
            networkInfo: "https://example.test/network",
            supportLink: "https://example.test/support");
        ModificationViewModel viewModel = CreateViewModel(modification, new TestStringLocalizer());

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
    public void SelectionGatedActionsRemainVisibleForAdvertising(
        ModificationType modificationType,
        bool isSelected,
        bool expected)
    {
        ModificationViewModel viewModel = CreateViewModel(
            CreateModification(installed: true, modificationType: modificationType),
            new TestStringLocalizer());

        viewModel.IsSelected = isSelected;

        viewModel.IsSelectedOrAdvertising.Should().Be(expected);
    }

    [Fact]
    public async Task PackageActivityProjectsCanonicalLifecycleOwnerAsync()
    {
        var activityService = new GenLauncherGO.UI.Features.Integrity.LauncherPackageActivityService();
        ModificationViewModel viewModel = CreateViewModel(
            CreateModification(installed: false),
            new TestStringLocalizer(),
            TestLauncherTheme.Create(),
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

    [Fact]
    public void CompletePackageActivityPresentationAppliesCanceledState()
    {
        TestStringLocalizer localizer = new(new Dictionary<string, string>
        {
            ["Canceled"] = "Canceled",
            ["LatestVersion"] = "Latest version: ",
        });
        ModificationViewModel viewModel = CreateViewModel(CreateModification(installed: false), localizer);

        viewModel.CompletePackageActivityPresentation(PackageDownloadResult.Canceled());

        viewModel.ProgressMessage.Should().Be("Canceled");
        viewModel.ReadyToRun.Should().BeFalse();
    }

    [Theory]
    [InlineData(ContentSourceKind.Manual, ModificationType.Mod, true)]
    [InlineData(ContentSourceKind.ManagedSingleFile, ModificationType.Mod, false)]
    [InlineData(ContentSourceKind.Manual, ModificationType.Advertising, false)]
    public void CanSetImageRequiresManualNonAdvertisingContent(
        ContentSourceKind contentSourceKind,
        ModificationType modificationType,
        bool expected)
    {
        LauncherContent modification = CreateModification(
            installed: true,
            contentSourceKind: contentSourceKind,
            modificationType: modificationType);

        ModificationViewModel viewModel = CreateViewModel(modification, new TestStringLocalizer());

        viewModel.CanSetImage.Should().Be(expected);
    }

    [Fact]
    public void ContextMenuLinkAvailabilityComesFromStableModelFields()
    {
        LauncherContent modification = CreateModification(
            installed: true,
            modDbLink: "https://example.test/moddb");

        ModificationViewModel viewModel = CreateViewModel(modification, new TestStringLocalizer());

        viewModel.CanOpenModDb.Should().BeTrue();
        viewModel.CanOpenDiscord.Should().BeFalse();
    }

    private static ModificationViewModel CreateViewModel(ColorsInfo colors)
    {
        return CreateViewModel(CreateModification(installed: false), new TestStringLocalizer(), colors);
    }

    private static ModificationViewModel CreateViewModel(
        LauncherContent modification,
        TestStringLocalizer stringLocalizer)
    {
        return CreateViewModel(modification, stringLocalizer, TestLauncherTheme.Create());
    }

    private static ModificationViewModel CreateViewModel(
        LauncherContent modification,
        TestStringLocalizer stringLocalizer,
        ColorsInfo colors,
        GenLauncherGO.UI.Features.Integrity.LauncherPackageActivityService? packageActivityService = null)
    {
        return new ModificationViewModel(
            modification,
            new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance),
            TestLauncherRuntimeContext.Create(colors: colors),
            Substitute.For<IModificationImageFileService>(),
            stringLocalizer,
            packageActivityService ?? new GenLauncherGO.UI.Features.Integrity.LauncherPackageActivityService(),
            NullLogger<ModificationViewModel>.Instance);
    }

    private static LauncherContent CreateModification(
        bool installed,
        ContentSourceKind contentSourceKind = ContentSourceKind.UnknownLegacy,
        ModificationType modificationType = ModificationType.Mod,
        string newsLink = "",
        string networkInfo = "",
        string supportLink = "",
        string modDbLink = "",
        string discordLink = "",
        string versionName = "1.0")
    {
        LauncherContentVersion version = new()
        {
            Installation = new LauncherContentInstallation
            {
                Installed = installed,
                ContentSourceKind = contentSourceKind
            },
            Name = "ShockWave",
            Version = versionName,
            ModificationType = modificationType,
            NewsLink = newsLink,
            NetworkInfo = networkInfo,
            SupportLink = supportLink,
            ModDBLink = modDbLink,
            DiscordLink = discordLink
        };

        return new LauncherContent(version);
    }

    private static LauncherContent CreateVersionedModification(
        params LauncherContentVersion[] versions)
    {
        var data = new LauncherData();
        foreach (LauncherContentVersion version in versions)
        {
            data.AddOrUpdate(version);
        }

        return data.FindContent(versions[0].ContentKey)!;
    }

}
