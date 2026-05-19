using System;
using System.Collections.Generic;
using Avalonia.Controls;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Mods;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

public sealed class LauncherLaunchReadinessCoordinatorTests
{
    [Fact]
    public void EnsureExecutableAvailableReturnsTrueWhenExecutableExists()
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
    public void EnsureExecutableAvailableShowsErrorWhenExecutableIsMissing()
    {
        StaTestRunner.Run(async () =>
        {
            IGameExecutableDiscoveryService discovery = Substitute.For<IGameExecutableDiscoveryService>();
            discovery.IsExecutableAvailable("missing.exe").Returns(false);
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(
                gameExecutableDiscoveryService: discovery,
                dialogService: dialogService);
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
    public void EnsureSelectedContentCanLaunchRejectsAdvertisingSelection()
    {
        StaTestRunner.Run(async () =>
        {
            ModificationViewModel advertising = CreateViewModel(CreateModification(
                "Sponsor",
                ModificationType.Advertising,
                CreateVersion("Sponsor", "1.0", installed: false)));
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator();
            Window owner = new();

            bool canLaunch = await coordinator.EnsureSelectedContentCanLaunchAsync(
                new[] { advertising },
                Array.Empty<ModificationViewModel>(),
                Array.Empty<ModificationViewModel>(),
                owner);

            canLaunch.Should().BeFalse();
        });
    }

    [Fact]
    public void EnsureSelectedContentCanLaunchShowsErrorForActiveSelectedModDownload()
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
                new[] { selectedMod },
                new[] { selectedPatch },
                new[] { selectedAddon },
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

    [Fact]
    public void EnsureSelectedContentCanLaunchShowsErrorForActiveSelectedPatchDownload()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            ModificationViewModel selectedPatch = CreateViewModel("Balance", ModificationType.Patch);
            selectedPatch.BeginIntegrityProgress("Repairing");
            Window owner = new();

            bool canLaunch = await coordinator.EnsureSelectedContentCanLaunchAsync(
                Array.Empty<ModificationViewModel>(),
                new[] { selectedPatch },
                Array.Empty<ModificationViewModel>(),
                owner);

            canLaunch.Should().BeFalse();
            await dialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Launch aborted" &&
                    request.DetailMessage == "Balance install running"),
                owner);
        });
    }

    [Fact]
    public void EnsureSelectedContentCanLaunchShowsInstallInProgressForActiveSelectedAddonDownload()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            ModificationViewModel selectedAddon = CreateViewModel("Music Pack", ModificationType.Addon);
            selectedAddon.BeginIntegrityProgress("Repairing");
            Window owner = new();

            bool canLaunch = await coordinator.EnsureSelectedContentCanLaunchAsync(
                Array.Empty<ModificationViewModel>(),
                Array.Empty<ModificationViewModel>(),
                new[] { selectedAddon },
                owner);

            canLaunch.Should().BeFalse();
            await dialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Launch aborted" &&
                    request.DetailMessage == "Music Pack install running"),
                owner);
        });
    }

    [Fact]
    public void EnsureSelectedContentCanLaunchShowsErrorForUninstalledSelectedMod()
    {
        StaTestRunner.Run(async () =>
        {
            ModificationViewModel selectedMod = CreateViewModel(CreateModification(
                "ShockWave",
                ModificationType.Mod,
                CreateVersion("ShockWave", "1.0", installed: false)));
            ModificationViewModel selectedPatch = CreateViewModel(CreateModification(
                "Balance",
                ModificationType.Patch,
                CreateVersion("Balance", "2.0", installed: false)));
            ModificationViewModel selectedAddon = CreateViewModel(CreateModification(
                "Music Pack",
                ModificationType.Addon,
                CreateVersion("Music Pack", "1.0", installed: false)));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool canLaunch = await coordinator.EnsureSelectedContentCanLaunchAsync(
                new[] { selectedMod },
                new[] { selectedPatch },
                new[] { selectedAddon },
                owner);

            canLaunch.Should().BeFalse();
            await dialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Launch aborted" &&
                    request.DetailMessage == "ShockWave is not installed"),
                owner);
        });
    }

    [Fact]
    public void EnsureSelectedContentCanLaunchShowsErrorForUninstalledSelectedPatch()
    {
        StaTestRunner.Run(async () =>
        {
            ModificationViewModel selectedMod = CreateViewModel(CreateModification(
                "ShockWave",
                ModificationType.Mod,
                CreateVersion("ShockWave", "1.0", installed: true)));
            ModificationViewModel selectedPatch = CreateViewModel(CreateModification(
                "Balance",
                ModificationType.Patch,
                CreateVersion("Balance", "2.0", installed: false)));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool canLaunch = await coordinator.EnsureSelectedContentCanLaunchAsync(
                new[] { selectedMod },
                new[] { selectedPatch },
                Array.Empty<ModificationViewModel>(),
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
    public void EnsureSelectedContentCanLaunchShowsErrorForUninstalledSelectedAddon()
    {
        StaTestRunner.Run(async () =>
        {
            ModificationViewModel selectedAddon = CreateViewModel(CreateModification(
                "Music Pack",
                ModificationType.Addon,
                CreateVersion("Music Pack", "1.0", installed: false)));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool canLaunch = await coordinator.EnsureSelectedContentCanLaunchAsync(
                Array.Empty<ModificationViewModel>(),
                Array.Empty<ModificationViewModel>(),
                new[] { selectedAddon },
                owner);

            canLaunch.Should().BeFalse();
            await dialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Launch aborted" &&
                    request.DetailMessage == "Music Pack is not installed"),
                owner);
        });
    }

    [Fact]
    public void EnsureSelectedContentCanLaunchReturnsTrueWhenSelectionIsReadyAndInstalled()
    {
        StaTestRunner.Run(async () =>
        {
            ModificationViewModel selectedMod = CreateViewModel(CreateModification(
                "ShockWave",
                ModificationType.Mod,
                CreateVersion("ShockWave", "1.0", installed: true)));
            ModificationViewModel selectedPatch = CreateViewModel(CreateModification(
                "Balance",
                ModificationType.Patch,
                CreateVersion("Balance", "2.0", installed: true)));
            ModificationViewModel selectedAddon = CreateViewModel(CreateModification(
                "Music Pack",
                ModificationType.Addon,
                CreateVersion("Music Pack", "1.0", installed: true)));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool canLaunch = await coordinator.EnsureSelectedContentCanLaunchAsync(
                new[] { selectedMod },
                new[] { selectedPatch },
                new[] { selectedAddon },
                owner);

            canLaunch.Should().BeTrue();
            await dialogService.DidNotReceive().ShowErrorAsync(
                Arg.Any<LauncherInfoDialogRequest>(),
                Arg.Any<Window?>());
        });
    }

    [Fact]
    public void ConfirmSelectedContentWarningsUsesUpdateConfirmationWhenLatestVersionIsNotInstalled()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContent modification = CreateModification(
                "ShockWave",
                ModificationType.Mod,
                CreateVersion("ShockWave", "1.0", installed: true),
                CreateVersion("ShockWave", "2.0", installed: false));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowWarningConfirmationAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string?>(),
                    Arg.Any<Window?>())
                .Returns(false);
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool confirmed = await coordinator.ConfirmSelectedContentWarningsAsync(
                new[] { CreateViewModel(modification) },
                Array.Empty<ModificationViewModel>(),
                Array.Empty<ModificationViewModel>(),
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
    public void ConfirmSelectedContentWarningsUsesUpdateConfirmationForUninstalledPatchUpdate()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContent modification = CreateModification(
                "ShockWave",
                ModificationType.Mod,
                CreateVersion("ShockWave", "1.0", installed: true),
                CreateVersion("ShockWave", "2.0", installed: false));
            LauncherContent patch = CreateModification(
                "Balance",
                ModificationType.Patch,
                CreateVersion("Balance", "1.0", installed: true),
                CreateVersion("Balance", "2.0", installed: false));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowWarningConfirmationAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string?>(),
                    Arg.Any<Window?>())
                .Returns(false);
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool confirmed = await coordinator.ConfirmSelectedContentWarningsAsync(
                new[] { CreateViewModel(modification) },
                new[] { CreateViewModel(patch) },
                Array.Empty<ModificationViewModel>(),
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
    public void ConfirmSelectedContentWarningsUsesUpdateConfirmationForUninstalledAddonUpdate()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContent modification = CreateModification(
                "ShockWave",
                ModificationType.Mod,
                CreateVersion("ShockWave", "1.0", installed: true),
                CreateVersion("ShockWave", "2.0", installed: false));
            LauncherContent patch = CreateModification(
                "Balance",
                ModificationType.Patch,
                CreateVersion("Balance", "1.0", installed: true),
                CreateVersion("Balance", "2.0", installed: false));
            LauncherContent addon = CreateModification(
                "Music Pack",
                ModificationType.Addon,
                CreateVersion("Music Pack", "1.0", installed: true),
                CreateVersion("Music Pack", "2.0", installed: false));
            LauncherContent secondAddon = CreateModification(
                "Portrait Pack",
                ModificationType.Addon,
                CreateVersion("Portrait Pack", "1.0", installed: true),
                CreateVersion("Portrait Pack", "2.0", installed: false));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowWarningConfirmationAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string?>(),
                    Arg.Any<Window?>())
                .Returns(false);
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool confirmed = await coordinator.ConfirmSelectedContentWarningsAsync(
                new[] { CreateViewModel(modification) },
                new[] { CreateViewModel(patch) },
                new[] { CreateViewModel(addon), CreateViewModel(secondAddon) },
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

    [Fact]
    public void ConfirmSelectedContentWarningsUsesCompatibilityConfirmationForDeprecatedPatch()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContent modification = CreateModification(
                "ShockWave",
                ModificationType.Mod,
                CreateVersion("ShockWave", "1.0", installed: true, deprecated: true));
            LauncherContent patch = CreateModification(
                "Balance",
                ModificationType.Patch,
                CreateVersion("Balance", "1.0", installed: true, deprecated: true));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowWarningConfirmationAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string?>(),
                    Arg.Any<Window?>())
                .Returns(true);
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool confirmed = await coordinator.ConfirmSelectedContentWarningsAsync(
                new[] { CreateViewModel(modification) },
                new[] { CreateViewModel(patch) },
                Array.Empty<ModificationViewModel>(),
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
    public void ConfirmSelectedContentWarningsUsesCompatibilityConfirmationForDeprecatedAddon()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContent patch = CreateModification(
                "Balance",
                ModificationType.Patch,
                CreateVersion("Balance", "1.0", installed: true, deprecated: true));
            LauncherContent addon = CreateModification(
                "Music Pack",
                ModificationType.Addon,
                CreateVersion("Music Pack", "1.0", installed: true, deprecated: true));
            LauncherContent secondAddon = CreateModification(
                "Portrait Pack",
                ModificationType.Addon,
                CreateVersion("Portrait Pack", "1.0", installed: true, deprecated: true));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowWarningConfirmationAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string?>(),
                    Arg.Any<Window?>())
                .Returns(true);
            LauncherLaunchReadinessCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            Window owner = new();

            bool confirmed = await coordinator.ConfirmSelectedContentWarningsAsync(
                Array.Empty<ModificationViewModel>(),
                new[] { CreateViewModel(patch) },
                new[] { CreateViewModel(addon), CreateViewModel(secondAddon) },
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
            new TestStringLocalizer(new Dictionary<string, string>
            {
                ["LaunchAborted"] = "Launch aborted",
                ["InstallInProgress"] = "{0} install running",
                ["NotInstalled"] = "{0} is not installed",
                ["UninstalledUpdate"] = "{0} update is not installed",
                ["ModificationsWithUpdate"] = "Updates available",
                ["Deprecated"] = "{0} is deprecated",
                ["Compatibility"] = "Compatibility",
            }));
    }

    private static ModificationViewModel CreateViewModel(
        string name,
        ModificationType modificationType,
        string parentContentName = "")
    {
        return CreateViewModel(
            CreateModification(name, modificationType, CreateVersion(name, "1.0", installed: true, parentContentName)));
    }

    private static ModificationViewModel CreateViewModel(LauncherContent modification)
    {
        return new ModificationViewModel(
            modification,
            new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance),
            TestLauncherRuntimeContext.Create(),
            Substitute.For<IModificationImageFileService>(),
            new TestStringLocalizer(),
            new GenLauncherGO.UI.Features.Integrity.LauncherPackageActivityService(),
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
                                 String.IsNullOrEmpty(version.ParentContentName)
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
