using Avalonia.Controls;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Mods;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

[Collection("Avalonia")]
public sealed class LauncherLaunchReadinessCoordinatorTests
{
    [Fact]
    public void EnsureExecutableAvailable_ReturnsTrueWhenExecutableExists()
    {
        StaTestRunner.Run(async () =>
        {
            IGameExecutableDiscoveryService discovery = Substitute.For<IGameExecutableDiscoveryService>();
            discovery.IsExecutableAvailable("generals.exe").Returns(true);
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(discovery);
            Window owner = new();

            bool available = await coordinator.EnsureExecutableAvailableAsync(
                "generals.exe",
                "Missing executable",
                owner);

            available.Should().BeTrue();
        });
    }

    [Fact]
    public void EnsureExecutableAvailable_ShowsErrorWhenExecutableIsMissing()
    {
        StaTestRunner.Run(async () =>
        {
            IGameExecutableDiscoveryService discovery = Substitute.For<IGameExecutableDiscoveryService>();
            discovery.IsExecutableAvailable("missing.exe").Returns(false);
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(
                discovery,
                dialogService);
            Window owner = new();

            bool available = await coordinator.EnsureExecutableAvailableAsync(
                "missing.exe",
                "Missing executable",
                owner);

            available.Should().BeFalse();
            await dialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Launch aborted" &&
                    request.DetailMessage == "Missing executable"),
                owner);
        });
    }

    [Fact]
    public void EnsureSelectedContent_WhenAdvertisingIsSelected_ShowsErrorAndRejectsLaunch()
    {
        StaTestRunner.Run(async () =>
        {
            ModificationViewModel advertising = CreateViewModel(CreateModification(
                "Sponsor",
                ModificationType.Advertising,
                CreateVersion("Sponsor", "1.0", false)));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool canLaunch = await coordinator.EnsureSelectedContentCanLaunchAsync(
                new[] { advertising },
                owner);

            canLaunch.Should().BeFalse();
            await dialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Launch aborted" &&
                    request.DetailMessage == "Advertisements cannot be launched."),
                owner);
        });
    }

    [Theory]
    [InlineData(ModificationType.Mod, "ShockWave")]
    [InlineData(ModificationType.Patch, "Balance")]
    [InlineData(ModificationType.Addon, "Music Pack")]
    public void EnsureSelectedContentCanLaunchAsync_WhenTheSelectionIsBusy_NamesIt(
        ModificationType modificationType,
        string contentName)
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            ModificationViewModel selected = CreateViewModel(contentName, modificationType);
            selected.BeginIntegrityProgress("Repairing");
            Window owner = new();

            bool canLaunch = await coordinator.EnsureSelectedContentCanLaunchAsync(
                new[] { selected },
                owner);

            canLaunch.Should().BeFalse();
            await dialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Launch aborted" &&
                    request.DetailMessage == $"{contentName} install running"),
                owner);
        });
    }

    [Fact]
    public void EnsureSelectedContentCanLaunchAsync_WhenSeveralSelectionsAreBusy_NamesTheFirst()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            ModificationViewModel selectedMod = CreateViewModel("ShockWave", ModificationType.Mod);
            ModificationViewModel selectedPatch = CreateViewModel("Balance", ModificationType.Patch);
            ModificationViewModel selectedAddon = CreateViewModel("Music Pack", ModificationType.Addon);
            selectedMod.BeginIntegrityProgress("Repairing");
            selectedPatch.BeginIntegrityProgress("Repairing");
            selectedAddon.BeginIntegrityProgress("Repairing");
            Window owner = new();

            bool canLaunch = await coordinator.EnsureSelectedContentCanLaunchAsync(
                new[] { selectedMod, selectedPatch, selectedAddon },
                owner);

            canLaunch.Should().BeFalse();
            await dialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Launch aborted" &&
                    request.DetailMessage == "ShockWave install running"),
                owner);
        });
    }

    [Theory]
    [InlineData(ModificationType.Mod, "ShockWave")]
    [InlineData(ModificationType.Patch, "Balance")]
    [InlineData(ModificationType.Addon, "Music Pack")]
    public void EnsureSelectedContentCanLaunchAsync_WhenTheSelectionIsNotInstalled_NamesIt(
        ModificationType modificationType,
        string contentName)
    {
        StaTestRunner.Run(async () =>
        {
            ModificationViewModel selected = CreateViewModel(CreateModification(
                contentName,
                modificationType,
                CreateVersion(contentName, "1.0", false)));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool canLaunch = await coordinator.EnsureSelectedContentCanLaunchAsync(
                new[] { selected },
                owner);

            canLaunch.Should().BeFalse();
            await dialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Launch aborted" &&
                    request.DetailMessage == $"{contentName} is not installed"),
                owner);
        });
    }

    [Fact]
    public void EnsureSelectedContentCanLaunchAsync_WhenAnInstalledModPrecedesAnUninstalledPatch_NamesThePatch()
    {
        StaTestRunner.Run(async () =>
        {
            ModificationViewModel selectedMod = CreateViewModel(CreateModification(
                "ShockWave",
                ModificationType.Mod,
                CreateVersion("ShockWave", "1.0", true)));
            ModificationViewModel selectedPatch = CreateViewModel(CreateModification(
                "Balance",
                ModificationType.Patch,
                CreateVersion("Balance", "2.0", false)));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool canLaunch = await coordinator.EnsureSelectedContentCanLaunchAsync(
                new[] { selectedMod, selectedPatch },
                owner);

            canLaunch.Should().BeFalse();
            await dialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Launch aborted" &&
                    request.DetailMessage == "Balance is not installed"),
                owner);
        });
    }

    [Fact]
    public void EnsureSelectedContent_CanLaunchReturnsTrueWhenSelectionIsReadyAndInstalled()
    {
        StaTestRunner.Run(async () =>
        {
            ModificationViewModel selectedMod = CreateViewModel(CreateModification(
                "ShockWave",
                ModificationType.Mod,
                CreateVersion("ShockWave", "1.0", true)));
            ModificationViewModel selectedPatch = CreateViewModel(CreateModification(
                "Balance",
                ModificationType.Patch,
                CreateVersion("Balance", "2.0", true)));
            ModificationViewModel selectedAddon = CreateViewModel(CreateModification(
                "Music Pack",
                ModificationType.Addon,
                CreateVersion("Music Pack", "1.0", true)));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool canLaunch = await coordinator.EnsureSelectedContentCanLaunchAsync(
                new[] { selectedMod, selectedPatch, selectedAddon },
                owner);

            canLaunch.Should().BeTrue();
            await dialogService.DidNotReceive().ShowErrorAsync(
                Arg.Any<LauncherInfoDialogRequest>(),
                Arg.Any<Window?>());
        });
    }

    [Fact]
    public void ConfirmSelectedContentWarnings_UsesUpdateConfirmationWhenLatestVersionIsNotInstalled()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContent modification = CreateModification(
                "ShockWave",
                ModificationType.Mod,
                CreateVersion("ShockWave", "1.0", true),
                CreateVersion("ShockWave", "2.0", false));
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(false);
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool confirmed = await coordinator.ConfirmSelectedContentWarningsAsync(
                new[] { CreateViewModel(modification) },
                owner);

            confirmed.Should().BeFalse();
            await dialogService.Received(1).ShowWarningConfirmationAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Updates available" &&
                    request.DetailMessage == "ShockWave update is not installed"),
                null,
                owner);
        });
    }

    [Fact]
    public void ConfirmSelectedContentWarnings_UsesUpdateConfirmationForUninstalledPatchUpdate()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContent modification = CreateModification(
                "ShockWave",
                ModificationType.Mod,
                CreateVersion("ShockWave", "1.0", true),
                CreateVersion("ShockWave", "2.0", false));
            LauncherContent patch = CreateModification(
                "Balance",
                ModificationType.Patch,
                CreateVersion("Balance", "1.0", true),
                CreateVersion("Balance", "2.0", false));
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(false);
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool confirmed = await coordinator.ConfirmSelectedContentWarningsAsync(
                new[] { CreateViewModel(modification), CreateViewModel(patch) },
                owner);

            confirmed.Should().BeFalse();
            await dialogService.Received(1).ShowWarningConfirmationAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Updates available" &&
                    request.DetailMessage == "Balance update is not installed"),
                null,
                owner);
        });
    }

    [Fact]
    public void ConfirmSelectedContentWarnings_UsesUpdateConfirmationForUninstalledAddonUpdate()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContent modification = CreateModification(
                "ShockWave",
                ModificationType.Mod,
                CreateVersion("ShockWave", "1.0", true),
                CreateVersion("ShockWave", "2.0", false));
            LauncherContent patch = CreateModification(
                "Balance",
                ModificationType.Patch,
                CreateVersion("Balance", "1.0", true),
                CreateVersion("Balance", "2.0", false));
            LauncherContent addon = CreateModification(
                "Music Pack",
                ModificationType.Addon,
                CreateVersion("Music Pack", "1.0", true),
                CreateVersion("Music Pack", "2.0", false));
            LauncherContent secondAddon = CreateModification(
                "Portrait Pack",
                ModificationType.Addon,
                CreateVersion("Portrait Pack", "1.0", true),
                CreateVersion("Portrait Pack", "2.0", false));
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(false);
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool confirmed = await coordinator.ConfirmSelectedContentWarningsAsync(
                new[]
                {
                    CreateViewModel(modification),
                    CreateViewModel(patch),
                    CreateViewModel(addon),
                    CreateViewModel(secondAddon)
                },
                owner);

            confirmed.Should().BeFalse();
            await dialogService.Received(1).ShowWarningConfirmationAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Updates available" &&
                    request.DetailMessage == "Music Pack update is not installed"),
                null,
                owner);
        });
    }

    /// <summary>
    ///     A deprecated modification is not a compatibility warning on its own: only the child content that has to
    ///     match it is. The child comes first here so the warning cannot be a by-product of selection order.
    /// </summary>
    [Fact]
    public void ConfirmSelectedContentWarnings_UsesCompatibilityConfirmationForDeprecatedPatch()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContent modification = CreateModification(
                "ShockWave",
                ModificationType.Mod,
                CreateVersion("ShockWave", "1.0", true, deprecated: true));
            LauncherContent patch = CreateModification(
                "Balance",
                ModificationType.Patch,
                CreateVersion("Balance", "1.0", true, deprecated: true));
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(true);
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool confirmed = await coordinator.ConfirmSelectedContentWarningsAsync(
                new[] { CreateViewModel(patch), CreateViewModel(modification) },
                owner);

            confirmed.Should().BeTrue();
            await dialogService.Received(1).ShowWarningConfirmationAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Compatibility" &&
                    request.DetailMessage == "Balance is deprecated"),
                null,
                owner);
        });
    }

    [Fact]
    public void ConfirmSelectedContentWarnings_ForADeprecatedModificationAlone_NeedsNoConfirmation()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContent modification = CreateModification(
                "ShockWave",
                ModificationType.Mod,
                CreateVersion("ShockWave", "1.0", true, deprecated: true));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool confirmed = await coordinator.ConfirmSelectedContentWarningsAsync(
                new[] { CreateViewModel(modification) },
                owner);

            confirmed.Should().BeTrue();
            await dialogService.DidNotReceiveWithAnyArgs().ShowWarningConfirmationAsync(
                default!,
                default,
                default);
        });
    }

    [Fact]
    public void ConfirmSelectedContentWarnings_UsesCompatibilityConfirmationForDeprecatedAddon()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContent patch = CreateModification(
                "Balance",
                ModificationType.Patch,
                CreateVersion("Balance", "1.0", true, deprecated: true));
            LauncherContent addon = CreateModification(
                "Music Pack",
                ModificationType.Addon,
                CreateVersion("Music Pack", "1.0", true, deprecated: true));
            LauncherContent secondAddon = CreateModification(
                "Portrait Pack",
                ModificationType.Addon,
                CreateVersion("Portrait Pack", "1.0", true, deprecated: true));
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(true);
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool confirmed = await coordinator.ConfirmSelectedContentWarningsAsync(
                new[]
                {
                    CreateViewModel(patch),
                    CreateViewModel(addon),
                    CreateViewModel(secondAddon)
                },
                owner);

            confirmed.Should().BeTrue();
            await dialogService.Received(1).ShowWarningConfirmationAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Compatibility" &&
                    request.DetailMessage == "Music Pack is deprecated"),
                null,
                owner);
        });
    }

    private static LauncherLaunchReadinessCoordinator CreateCoordinator(
        IGameExecutableDiscoveryService? gameExecutableDiscoveryService = null,
        ILauncherDialogService? dialogService = null)
    {
        return new LauncherLaunchReadinessCoordinator(
            gameExecutableDiscoveryService ?? Substitute.For<IGameExecutableDiscoveryService>(),
            dialogService ?? Substitute.For<ILauncherDialogService>(),
            FakeStringLocalizer.Create(TestLocalizedStrings.Launch));
    }

    private static ModificationViewModel CreateViewModel(
        string name,
        ModificationType modificationType,
        string parentContentName = "")
    {
        return CreateViewModel(
            CreateModification(name, modificationType, CreateVersion(name, "1.0", true, parentContentName)));
    }

    private static ModificationViewModel CreateViewModel(LauncherContent modification)
    {
        return new ModificationViewModel(
            modification,
            new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance),
            TestLauncherRuntimeContext.Create(),
            Substitute.For<IModificationImageFileService>(),
            new FakeStringLocalizer(),
            new LauncherPackageActivityService(),
            NullLogger<ModificationViewModel>.Instance);
    }

    private static LauncherContent CreateModification(
        string name,
        ModificationType modificationType,
        params LauncherContentVersion[] versions)
    {
        var data = new LauncherData();
        LauncherContentVersion? firstVersion = null;
        foreach (LauncherContentVersion version in versions)
        {
            var typedVersion = new LauncherContentVersion(version.Installation)
            {
                Name = name,
                Version = version.Version,
                ModificationType = modificationType,
                ParentContentName = modificationType == ModificationType.Addon &&
                                    string.IsNullOrEmpty(version.ParentContentName)
                    ? "Parent"
                    : version.ParentContentName,
                Deprecated = version.Deprecated
            };
            firstVersion ??= typedVersion;
            data.AddOrUpdate(typedVersion);
        }

        return modificationType == ModificationType.Advertising
            ? new LauncherContent(firstVersion!)
            : data.FindContent(firstVersion!.ContentKey)!;
    }

    private static LauncherContentVersion CreateVersion(
        string name,
        string version,
        bool installed,
        string parentContentName = "",
        bool deprecated = false)
    {
        return new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { Installed = installed },
            Name = name,
            Version = version,
            ModificationType = ModificationType.Mod,
            ParentContentName = parentContentName,
            Deprecated = deprecated
        };
    }
}
