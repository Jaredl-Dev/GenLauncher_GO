using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Remote;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Shell.Contracts;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Updating.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Launcher.Support;
using GenLauncherGO.UI.Features.Launcher.ViewModels;
using GenLauncherGO.UI.Features.Launcher.Views;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Settings.ViewModels;
using GenLauncherGO.UI.Features.Settings.Views;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Features.Startup.Services;
using GenLauncherGO.UI.Shared.Errors;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

public sealed partial class LauncherWindowWorkflowCoordinatorTests
{
    private const string GeneralsDirectory = @"C:\Games\Generals";

    private const string ZeroHourDirectory = @"C:\Games\ZeroHour";

    private static MainWindow CreateMainWindow(
        MainWindowViewModel viewModel,
        LauncherWindowWorkflowCoordinator coordinator,
        IUiExceptionBoundary exceptionBoundary,
        LauncherContentActionCoordinator? contentActionCoordinator = null)
    {
        return new MainWindow(
            viewModel,
            new LauncherDragDropController(),
            TestLauncherRuntimeContext.Create(),
            coordinator,
            contentActionCoordinator ?? CreateContentActionCoordinator(),
            exceptionBoundary);
    }

    private static LauncherSettingsWindow CreateAutoClosingSettingsWindow()
    {
        var window = new LauncherSettingsWindow();
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => window.Close(false));
        return window;
    }

    /// <summary>
    ///     Switching the language preference is the only route through which the settings window records
    ///     <see cref="LauncherSettingsWindow.RestartRequested" />, so a restart test has to go through it.
    /// </summary>
    private static LauncherSettingsWindow CreateRestartRequestingSettingsWindow()
    {
        return CreateSettingsWindow((_, viewModel) => viewModel.UseEnglishLanguage = true);
    }

    /// <summary>
    ///     Presses the settings window's own switch-game button, which is what runs the game-management callback the
    ///     main-window workflow registered, and records the window so the test can close it afterwards.
    /// </summary>
    private static LauncherSettingsWindow CaptureSettingsWindow(List<LauncherSettingsWindow> settingsWindows)
    {
        LauncherSettingsWindow window = CreateSettingsWindow((opened, _) => opened
            .FindControl<Button>("SwitchGameButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        settingsWindows.Add(window);
        return window;
    }

    /// <summary>
    ///     Builds the settings window the way composition does and runs one interaction once it is open.
    /// </summary>
    private static LauncherSettingsWindow CreateSettingsWindow(
        Action<LauncherSettingsWindow, LauncherSettingsViewModel> whenOpened)
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences
        {
            Installations = new LauncherInstallations
            {
                Generals = GeneralsDirectory,
                ZeroHour = ZeroHourDirectory
            },
            LastSelectedGame = SupportedGame.ZeroHour
        });
        LauncherRuntimeContext runtimeContext = TestLauncherRuntimeContext.Create();
        FakeStringLocalizer stringLocalizer = CreateStringLocalizer();
        IGameExecutableDiscoveryService executableDiscovery = Substitute.For<IGameExecutableDiscoveryService>();
        LauncherSettingsViewModel viewModel = new(
            preferencesService,
            Substitute.For<ILauncherShellService>(),
            runtimeContext,
            TestLauncherInstallations.CreateViewModel(stringLocalizer: stringLocalizer),
            stringLocalizer);
        LauncherSettingsWindow window = new(
            viewModel,
            stringLocalizer,
            preferencesService,
            new RecordingStartupDialogService(),
            new LauncherExecutableManagementViewModel(
                preferencesService,
                executableDiscovery,
                new LauncherExecutableSelectionService(
                    executableDiscovery,
                    runtimeContext,
                    preferencesService,
                    stringLocalizer),
                runtimeContext,
                stringLocalizer),
            new StubLauncherFilePicker(),
            StubLauncherDialogService.AnsweringWarningConfirmations(true));
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => whenOpened(window, viewModel));
        return window;
    }

    /// <summary>
    ///     Builds the managed single-file Shockwave card the download workflows act on.
    /// </summary>
    private static ModificationViewModel CreateManagedShockwaveTile(
        LauncherPackageActivityService packageActivityService)
    {
        return CreateTile(
            CreateModification(
                "Shockwave",
                ModificationType.Mod,
                CreateVersion("Shockwave", ContentSourceKind.ManagedSingleFile)),
            packageActivityService);
    }

    /// <summary>
    ///     Runs a tile through the coordinator against the launcher window the fixture built, which is the only
    ///     shape these workflows are ever called in.
    /// </summary>
    private static Task UpdateModificationAsync(
        LauncherContentActionCoordinator coordinator,
        WorkflowFixture fixture,
        ModificationViewModel tile)
    {
        return coordinator.UpdateModificationAsync(fixture.Context, tile);
    }

    private static Task DeleteModificationAsync(
        LauncherContentActionCoordinator coordinator,
        WorkflowFixture fixture,
        ModificationViewModel tile)
    {
        return coordinator.DeleteModificationAsync(fixture.Context, tile);
    }

    private static LauncherWindowWorkflowCoordinator CreateCoordinator(
        LauncherPackageActivityService? packageActivityService = null,
        ILauncherDialogService? dialogService = null,
        FakeLauncherContentCatalog? catalog = null,
        IGameExecutableDiscoveryService? executableDiscovery = null,
        ILauncherPreferencesService? preferencesService = null,
        ILaunchPreparationService? launchPreparationService = null,
        IGameProcessLauncher? gameProcessLauncher = null,
        LauncherLaunchCoordinator? launchCoordinator = null,
        Func<LauncherSettingsWindow>? launcherSettingsWindowFactory = null,
        ILauncherShellService? launcherShellService = null)
    {
        LauncherPackageActivityService resolvedPackageActivityService =
            packageActivityService ?? new LauncherPackageActivityService();
        ILauncherDialogService resolvedDialogService = dialogService ?? Substitute.For<ILauncherDialogService>();
        FakeLauncherContentCatalog resolvedCatalog = catalog ?? CreateCatalog();
        ILauncherShellService resolvedShellService =
            launcherShellService ?? Substitute.For<ILauncherShellService>();
        ILauncherPreferencesService resolvedPreferencesService =
            preferencesService ?? Substitute.For<ILauncherPreferencesService>();
        if (preferencesService == null)
        {
            resolvedPreferencesService.Current.Returns(new LauncherPreferences());
        }

        FakeStringLocalizer stringLocalizer = CreateStringLocalizer();
        LauncherLaunchCoordinator resolvedLaunchCoordinator = launchCoordinator ??
                                                              TestLauncherLaunchCoordinator.Create(
                                                                  resolvedPackageActivityService,
                                                                  resolvedPreferencesService,
                                                                  resolvedCatalog,
                                                                  stringLocalizer,
                                                                  launchPreparationService,
                                                                  gameProcessLauncher,
                                                                  resolvedDialogService);
        LauncherCloseGuard closeGuard = new(
            resolvedLaunchCoordinator,
            resolvedPackageActivityService,
            resolvedDialogService,
            stringLocalizer,
            NullLogger<LauncherCloseGuard>.Instance);
        LauncherRestartCoordinator restartCoordinator = new(
            closeGuard,
            NullLogger<LauncherRestartCoordinator>.Instance);
        LauncherRuntimeContext runtimeContext = TestLauncherRuntimeContext.Create();

        return new LauncherWindowWorkflowCoordinator(
            resolvedLaunchCoordinator,
            CreateLaunchReadinessCoordinator(resolvedDialogService, executableDiscovery),
            CreateGameSessionCoordinator(
                resolvedLaunchCoordinator,
                resolvedCatalog,
                resolvedPackageActivityService,
                resolvedDialogService,
                resolvedPreferencesService,
                runtimeContext,
                stringLocalizer),
            resolvedPackageActivityService,
            resolvedShellService,
            resolvedPreferencesService,
            stringLocalizer,
            launcherSettingsWindowFactory ?? (() => null!),
            resolvedDialogService,
            closeGuard,
            restartCoordinator);
    }

    private static LauncherContentActionCoordinator CreateContentActionCoordinator(
        LauncherPackageActivityService? packageActivityService = null,
        ILauncherDialogService? dialogService = null,
        LauncherContent? manualImportContent = null,
        ILauncherFilePicker? filePicker = null,
        IModificationImageFileService? modificationImageFileService = null,
        FakeLauncherContentCatalog? catalog = null,
        IPackageDownloadService? packageDownloadService = null,
        ILauncherShellService? launcherShellService = null)
    {
        LauncherPackageActivityService resolvedPackageActivityService =
            packageActivityService ?? new LauncherPackageActivityService();
        ILauncherDialogService resolvedDialogService = dialogService ?? Substitute.For<ILauncherDialogService>();
        FakeLauncherContentCatalog resolvedCatalog = catalog ?? CreateCatalog();
        ILauncherShellService resolvedShellService =
            launcherShellService ?? Substitute.For<ILauncherShellService>();
        FakeStringLocalizer stringLocalizer = CreateStringLocalizer();
        LauncherTileActionService tileActionService = new(resolvedCatalog);
        LaunchContentIntegrityCoordinator integrityCoordinator = TestLaunchContentIntegrityCoordinator.Create(
            catalog: resolvedCatalog,
            packageActivityService: resolvedPackageActivityService,
            dialogService: resolvedDialogService,
            stringLocalizer: stringLocalizer);
        LauncherPackageActivityAdmissionService activityAdmissionService = new(
            resolvedPackageActivityService,
            resolvedDialogService,
            stringLocalizer,
            NullLogger<LauncherPackageActivityAdmissionService>.Instance);

        return new LauncherContentActionCoordinator(
            tileActionService,
            CreateManualImportCoordinator(manualImportContent, resolvedCatalog),
            CreateDownloadCoordinator(
                resolvedPackageActivityService,
                resolvedDialogService,
                resolvedCatalog,
                tileActionService,
                packageDownloadService,
                activityAdmissionService),
            activityAdmissionService,
            resolvedShellService,
            filePicker ?? new StubLauncherFilePicker(),
            integrityCoordinator,
            stringLocalizer,
            modificationImageFileService ?? Substitute.For<IModificationImageFileService>(),
            resolvedDialogService,
            NullLogger<LauncherContentActionCoordinator>.Instance);
    }

    private static LauncherLaunchReadinessCoordinator CreateLaunchReadinessCoordinator(
        ILauncherDialogService dialogService,
        IGameExecutableDiscoveryService? executableDiscovery = null)
    {
        IGameExecutableDiscoveryService resolvedExecutableDiscovery =
            executableDiscovery ?? Substitute.For<IGameExecutableDiscoveryService>();
        if (executableDiscovery == null)
        {
            resolvedExecutableDiscovery.IsExecutableAvailable(Arg.Any<string?>()).Returns(true);
        }

        return new LauncherLaunchReadinessCoordinator(
            resolvedExecutableDiscovery,
            dialogService,
            CreateStringLocalizer());
    }

    private static LauncherManualImportCoordinator CreateManualImportCoordinator(
        LauncherContent? manualImportContent,
        FakeLauncherContentCatalog catalog)
    {
        ILauncherFilePicker filePicker = new StubLauncherFilePicker
        {
            ManualPackageFilesResult = manualImportContent == null
                ? Array.Empty<string>()
                : [@"C:\Downloads\manual.zip"]
        };
        ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
        if (manualImportContent != null)
        {
            LauncherContentVersion version = manualImportContent.Versions[0];
            dialogService.ShowManualModificationImportAsync(
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<Window?>())
                .Returns(new ManualModificationDialogResult(
                    manualImportContent.Name,
                    version.Version));
        }

        if (manualImportContent != null)
        {
            catalog.Data.AddOrUpdate(manualImportContent.Versions[0]);
        }

        return new LauncherManualImportCoordinator(
            filePicker,
            dialogService,
            catalog,
            TestLauncherPaths.CreateRuntimePathContext(TestLauncherPaths.Create()),
            Substitute.For<IManualModificationImporter>(),
            TestLaunchContentIntegrityCoordinator.Create(catalog: catalog, dialogService: dialogService),
            NullLogger<LauncherManualImportCoordinator>.Instance);
    }

    private static LauncherModificationDownloadCoordinator CreateDownloadCoordinator(
        LauncherPackageActivityService packageActivityService,
        ILauncherDialogService dialogService,
        FakeLauncherContentCatalog catalog,
        LauncherTileActionService tileActionService,
        IPackageDownloadService? packageDownloadService = null,
        LauncherPackageActivityAdmissionService? activityAdmissionService = null)
    {
        ILauncherPreferencesService preferencesService = Substitute.For<ILauncherPreferencesService>();
        preferencesService.Current.Returns(new LauncherPreferences());
        return new LauncherModificationDownloadCoordinator(
            preferencesService,
            catalog,
            packageDownloadService ?? Substitute.For<IPackageDownloadService>(),
            TestLaunchContentIntegrityCoordinator.Create(
                catalog: catalog,
                packageActivityService: packageActivityService,
                dialogService: dialogService),
            packageActivityService,
            activityAdmissionService ?? new LauncherPackageActivityAdmissionService(
                packageActivityService,
                dialogService,
                CreateStringLocalizer(),
                NullLogger<LauncherPackageActivityAdmissionService>.Instance),
            tileActionService,
            dialogService,
            CreateStringLocalizer(),
            NullLogger<LauncherModificationDownloadCoordinator>.Instance);
    }

    private static MainWindowViewModel CreateMainWindowViewModel(
        FakeLauncherContentCatalog catalog,
        LauncherPackageActivityService packageActivityService,
        ILauncherPreferencesService? preferencesService = null,
        LauncherLaunchCoordinator? launchCoordinator = null)
    {
        LauncherRuntimeContext runtimeContext = TestLauncherRuntimeContext.Create();
        FakeStringLocalizer stringLocalizer = CreateStringLocalizer();

        ILauncherPreferencesService resolvedPreferencesService =
            preferencesService ?? Substitute.For<ILauncherPreferencesService>();
        if (preferencesService == null)
        {
            resolvedPreferencesService.Current.Returns(new LauncherPreferences());
        }

        return new MainWindowViewModel(
            resolvedPreferencesService,
            new LauncherExecutableSelectionService(
                Substitute.For<IGameExecutableDiscoveryService>(),
                runtimeContext,
                resolvedPreferencesService,
                stringLocalizer),
            catalog,
            runtimeContext,
            stringLocalizer,
            new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance),
            Substitute.For<IModificationImageFileService>(),
            packageActivityService,
            NullLogger<ModificationViewModel>.Instance,
            launchCoordinator ?? TestLauncherLaunchCoordinator.Create(
                packageActivityService,
                resolvedPreferencesService,
                catalog,
                stringLocalizer),
            NullLogger<MainWindowViewModel>.Instance);
    }

    private static LauncherGameSessionCoordinator CreateGameSessionCoordinator(
        LauncherLaunchCoordinator launchCoordinator,
        FakeLauncherContentCatalog catalog,
        LauncherPackageActivityService packageActivityService,
        ILauncherDialogService dialogService,
        ILauncherPreferencesService preferencesService,
        LauncherRuntimeContext runtimeContext,
        FakeStringLocalizer stringLocalizer)
    {
        return new LauncherGameSessionCoordinator(
            runtimeContext,
            preferencesService,
            new FakeGameInstallationService(),
            Substitute.For<ILauncherPathResolver>(),
            new FakeLaunchPreparationService(),
            Substitute.For<IRemoteConnectionProbe>(),
            catalog,
            packageActivityService,
            launchCoordinator,
            dialogService,
            stringLocalizer,
            NullLogger<LauncherGameSessionCoordinator>.Instance);
    }

    /// <summary>
    ///     Adds an installed, selected Shockwave card to both the catalog and the launcher's mod list.
    /// </summary>
    private static ModificationViewModel AddSelectedShockwaveTile(
        WorkflowFixture fixture,
        FakeLauncherContentCatalog catalog)
    {
        LauncherContentVersion installedVersion = CreateVersion("Shockwave", ContentSourceKind.Manual);
        catalog.Data.AddOrUpdate(installedVersion);
        ModificationViewModel tile = CreateTile(catalog.Data.FindContent(installedVersion.ContentKey)!);
        fixture.AddTile(tile);
        tile.IsSelected = true;
        return tile;
    }

    /// <summary>
    ///     Changes the catalog behind an already-built tile, which is what a launcher refresh has to pick up.
    /// </summary>
    private static void AddNewerSelectedShockwaveVersionAndPatch(FakeLauncherContentCatalog catalog)
    {
        LauncherContent shockwave = catalog.Data.Modifications.Single();
        foreach (LauncherContentVersion version in shockwave.Versions)
        {
            version.Installation.IsSelected = false;
        }

        catalog.Data.AddOrUpdate(TestLauncherContent.Version(
            "Shockwave",
            "2.0",
            installed: true,
            isSelected: true,
            sourceKind: ContentSourceKind.Manual));
        catalog.Data.AddOrUpdate(TestLauncherContent.Version(
            "Balance",
            type: ModificationType.Patch,
            parentContentName: "Shockwave",
            installed: true,
            sourceKind: ContentSourceKind.Manual));
    }

    /// <summary>
    ///     Opens the settings workflow, waits for the settings window's game switch to finish, and closes it.
    /// </summary>
    private static async Task<List<bool>> RunSettingsGameSwitchAsync(
        LauncherWindowWorkflowCoordinator coordinator,
        WorkflowFixture fixture,
        IReadOnlyList<LauncherSettingsWindow> settingsWindows)
    {
        // Only transitions are recorded: reloading the launcher republishes the current state several times, and
        // what a caller can observe is that the controls were locked once and released once.
        List<bool> enabledStates = [];
        var switchFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(MainWindowViewModel.MainControlsEnabled))
            {
                return;
            }

            bool isEnabled = fixture.ViewModel.MainControlsEnabled;
            if (enabledStates.Count == 0 || enabledStates[^1] != isEnabled)
            {
                enabledStates.Add(isEnabled);
            }

            if (isEnabled)
            {
                switchFinished.TrySetResult();
            }
        };
        fixture.Owner.Show();

        Task options = coordinator.OpenOptionsAsync(
            fixture.Context,
            CancellationToken.None);
        await switchFinished.Task.WaitAsync(TestTimeouts.Wait);
        settingsWindows.Single().Close(false);
        await options.WaitAsync(TestTimeouts.Wait);

        fixture.Owner.Close();
        return enabledStates;
    }

    private static FakeLauncherContentCatalog CreateCatalog()
    {
        return new FakeLauncherContentCatalog
        {
            RepositoryModificationNames = new[] { "Shockwave" }
        };
    }

    private static ModificationViewModel CreateTile(
        LauncherContent modification,
        LauncherPackageActivityService? packageActivityService = null)
    {
        return TestModificationTile.Create(modification, CreateStringLocalizer(), packageActivityService);
    }

    private static LauncherContent CreateModification(
        string name,
        ModificationType modificationType,
        LauncherContentVersion version,
        string simpleDownloadLink = "",
        string supportLink = "",
        string newsLink = "",
        string networkInfo = "",
        string modDbLink = "",
        string discordLink = "")
    {
        var contentVersion = new LauncherContentVersion(version.Installation)
        {
            Name = name,
            Version = version.Version,
            ModificationType = modificationType,
            SimpleDownloadLink = simpleDownloadLink,
            SupportLink = supportLink,
            NewsLink = newsLink,
            NetworkInfo = networkInfo,
            ModDBLink = modDbLink,
            DiscordLink = discordLink
        };
        return new LauncherContent(contentVersion);
    }

    private static LauncherContentVersion CreateVersion(
        string name,
        ContentSourceKind sourceKind)
    {
        return TestLauncherContent.Version(
            name,
            installed: true,
            isSelected: true,
            sourceKind: sourceKind,
            simpleDownloadLink: sourceKind == ContentSourceKind.ManagedSingleFile
                ? "https://example.test/package.zip"
                : string.Empty);
    }

    private static FakeStringLocalizer CreateStringLocalizer()
    {
        return FakeStringLocalizer.Create(TestLocalizedStrings.Launcher);
    }

    private sealed class ControlledUiExceptionBoundary : IUiExceptionBoundary
    {
        private readonly TaskCompletionSource _windowOperationRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _holdNextWindowOperation;
        private bool _trackNextOperationAsClose;

        public int CloseAttemptCount { get; private set; }

        public bool HeldWindowOperationSucceeded { get; private set; }

        public TaskCompletionSource WindowOperationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ClosePreparationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CloseAttemptCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<UiOperationOutcome> ExecuteAsync(
            string operationContext,
            Func<Task> operation,
            Window? owner = null)
        {
            bool holdWindowOperation = _holdNextWindowOperation;
            _holdNextWindowOperation = false;
            bool isClosePreparation = _trackNextOperationAsClose;
            _trackNextOperationAsClose = false;
            if (isClosePreparation)
            {
                CloseAttemptCount++;
                ClosePreparationStarted.TrySetResult();
            }

            try
            {
                if (holdWindowOperation)
                {
                    WindowOperationStarted.TrySetResult();
                    await _windowOperationRelease.Task;
                }

                await operation();
                if (holdWindowOperation)
                {
                    HeldWindowOperationSucceeded = true;
                }

                return UiOperationOutcome.Succeeded;
            }
            catch (OperationCanceledException)
            {
                return UiOperationOutcome.Canceled;
            }
            catch
            {
                return UiOperationOutcome.Failed;
            }
            finally
            {
                if (isClosePreparation)
                {
                    CloseAttemptCompleted.TrySetResult();
                }
            }
        }

        public Task<UiOperationOutcome> HandleUnexpectedAsync(
            Exception exception,
            string operationContext,
            Window? owner = null)
        {
            return Task.FromResult(UiOperationOutcome.Failed);
        }

        public void ReleaseWindowOperation()
        {
            _windowOperationRelease.TrySetResult();
        }

        public void HoldNextWindowOperation()
        {
            _holdNextWindowOperation = true;
        }

        public void TrackNextCloseOperation()
        {
            _trackNextOperationAsClose = true;
        }
    }

    private sealed class WorkflowFixture
    {
        public WorkflowFixture(
            FakeLauncherContentCatalog? catalog = null,
            LauncherPackageActivityService? packageActivityService = null,
            ILauncherPreferencesService? preferencesService = null,
            LauncherLaunchCoordinator? launchCoordinator = null)
        {
            Catalog = catalog ?? CreateCatalog();
            LauncherPackageActivityService resolvedPackageActivityService =
                packageActivityService ?? new LauncherPackageActivityService();

            ViewModel = CreateMainWindowViewModel(
                Catalog,
                resolvedPackageActivityService,
                preferencesService,
                launchCoordinator);

            ModsList = new ListBox
            {
                Name = "ModsList",
                SelectionMode = SelectionMode.Single,
                ItemsSource = ViewModel.ModsListSource
            };
            PatchesList = new ListBox
            {
                Name = "PatchesList",
                SelectionMode = SelectionMode.Single,
                ItemsSource = ViewModel.PatchesListSource
            };
            AddonsList = new ListBox
            {
                Name = "AddonsList",
                SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle,
                ItemsSource = ViewModel.AddonsListSource
            };

            Content = new LauncherWindowListController(
                ViewModel,
                TestLauncherRuntimeContext.Create(),
                ModsList,
                PatchesList,
                AddonsList);
            Context = new LauncherWindowContext(ViewModel, Content, Owner);
        }

        public Window Owner { get; } = new();

        public MainWindowViewModel ViewModel { get; }

        public LauncherWindowListController Content { get; }

        public LauncherWindowContext Context { get; }

        public FakeLauncherContentCatalog Catalog { get; }

        public ListBox ModsList { get; }

        public ListBox PatchesList { get; }

        public ListBox AddonsList { get; }

        public void AddTile(ModificationViewModel modification)
        {
            switch (modification.ContainerModification.ModificationType)
            {
                case ModificationType.Mod:
                case ModificationType.Advertising:
                    ViewModel.ModsListSource.Add(modification);
                    break;
                case ModificationType.Patch:
                    ViewModel.PatchesListSource.Add(modification);
                    break;
                case ModificationType.Addon:
                    ViewModel.AddonsListSource.Add(modification);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(modification),
                        modification.ContainerModification.ModificationType,
                        "Unsupported launcher content type.");
            }
        }
    }
}
