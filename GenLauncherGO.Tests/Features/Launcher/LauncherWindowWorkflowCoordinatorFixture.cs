using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using GenLauncherGO.Features.Integrity;
using GenLauncherGO.Features.Launcher;
using GenLauncherGO.Features.Launcher.Views;
using GenLauncherGO.Features.Launching;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Features.Settings;
using GenLauncherGO.Features.Settings.Views;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Features.Updating;
using GenLauncherGO.Shared.Dialogs;
using GenLauncherGO.Shared.Errors;
using GenLauncherGO.Shared.Remote;
using GenLauncherGO.Shared.Shell;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Features.Launcher;

public sealed partial class LauncherWindowWorkflowCoordinatorTests
{
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
            TestLauncherApplicationUpdateCoordinator.Create(out _, downloadedVersion: null),
            exceptionBoundary);
    }

    private static LauncherSettingsWindow CreateAutoClosingSettingsWindow()
    {
        var window = new LauncherSettingsWindow();
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => window.Close(false));
        return window;
    }

    private static ModificationViewModel CreateManagedShockwaveTile(
        LauncherPackageActivityService packageActivityService)
    {
        return CreateTile(
            new LauncherContent(CreateVersion("Shockwave", ContentSourceKind.ManagedSingleFile)),
            packageActivityService);
    }

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
        IGameProcessLauncher? gameProcessLauncher = null,
        LauncherLaunchCoordinator? launchCoordinator = null,
        Func<LauncherSettingsWindow>? launcherSettingsWindowFactory = null)
    {
        LauncherPackageActivityService resolvedPackageActivityService =
            packageActivityService ?? new LauncherPackageActivityService();
        ILauncherDialogService resolvedDialogService = dialogService ?? Substitute.For<ILauncherDialogService>();
        FakeLauncherContentCatalog resolvedCatalog = catalog ?? CreateCatalog();
        ILauncherPreferencesService resolvedPreferencesService =
            preferencesService ?? Substitute.For<ILauncherPreferencesService>();
        if (preferencesService == null)
        {
            resolvedPreferencesService.Current.Returns(new LauncherPreferences());
        }

        FakeStringLocalizer stringLocalizer = new();
        LauncherLaunchCoordinator resolvedLaunchCoordinator = launchCoordinator ??
                                                              TestLauncherLaunchCoordinator.Create(
                                                                  resolvedPackageActivityService,
                                                                  resolvedPreferencesService,
                                                                  resolvedCatalog,
                                                                  stringLocalizer,
                                                                  processLauncher: gameProcessLauncher,
                                                                  dialogService: resolvedDialogService);
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
        ILaunchPreparationService launchPreparation = Substitute.For<ILaunchPreparationService>();
        launchPreparation.Prepare(Arg.Any<LaunchPreparationRequest>(), Arg.Any<CancellationToken>())
            .Returns(true);
        launchPreparation.Cleanup(Arg.Any<LauncherPaths>(), Arg.Any<CancellationToken>()).Returns(true);
        launchPreparation.Recover(Arg.Any<LauncherPaths>(), Arg.Any<CancellationToken>()).Returns(true);

        return new LauncherWindowWorkflowCoordinator(
            resolvedLaunchCoordinator,
            CreateLaunchReadinessCoordinator(resolvedDialogService, executableDiscovery),
            new LauncherGameSessionCoordinator(
                runtimeContext,
                resolvedPreferencesService,
                new FakeGameInstallationService(),
                Substitute.For<ILauncherPathResolver>(),
                launchPreparation,
                Substitute.For<IRemoteConnectionProbe>(),
                resolvedCatalog,
                resolvedPackageActivityService,
                resolvedLaunchCoordinator,
                resolvedDialogService,
                stringLocalizer,
                NullLogger<LauncherGameSessionCoordinator>.Instance),
            resolvedPackageActivityService,
            Substitute.For<ILauncherShellService>(),
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
        FakeLauncherContentCatalog? catalog = null,
        IPackageDownloadService? packageDownloadService = null)
    {
        LauncherPackageActivityService resolvedPackageActivityService =
            packageActivityService ?? new LauncherPackageActivityService();
        ILauncherDialogService resolvedDialogService = dialogService ?? Substitute.For<ILauncherDialogService>();
        FakeLauncherContentCatalog resolvedCatalog = catalog ?? CreateCatalog();
        FakeStringLocalizer stringLocalizer = new();
        LaunchContentIntegrityCoordinator integrityCoordinator = TestLaunchContentIntegrityCoordinator.Create(
            catalog: resolvedCatalog,
            packageActivityService: resolvedPackageActivityService,
            dialogService: resolvedDialogService,
            stringLocalizer: stringLocalizer);
        LauncherPackageActivityAdmissionService activityAdmissionService = new(
            resolvedPackageActivityService,
            resolvedDialogService,
            stringLocalizer);

        return new LauncherContentActionCoordinator(
            resolvedCatalog,
            CreateManualImportCoordinator(resolvedCatalog),
            CreateDownloadCoordinator(
                resolvedPackageActivityService,
                resolvedDialogService,
                resolvedCatalog,
                packageDownloadService,
                activityAdmissionService),
            activityAdmissionService,
            Substitute.For<ILauncherShellService>(),
            new StubLauncherFilePicker(),
            integrityCoordinator,
            stringLocalizer,
            Substitute.For<IModificationImageFileService>(),
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
            new FakeStringLocalizer());
    }

    private static LauncherManualImportCoordinator CreateManualImportCoordinator(
        FakeLauncherContentCatalog catalog)
    {
        ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
        return new LauncherManualImportCoordinator(
            new StubLauncherFilePicker(),
            dialogService,
            catalog,
            TestLauncherPaths.CreateRuntimePathContext(TestLauncherPaths.Create()),
            Substitute.For<IManualModificationImporter>(),
            TestLaunchContentIntegrityCoordinator.Create(catalog: catalog, dialogService: dialogService),
            new FakeStringLocalizer(),
            NullLogger<LauncherManualImportCoordinator>.Instance);
    }

    private static LauncherModificationDownloadCoordinator CreateDownloadCoordinator(
        LauncherPackageActivityService packageActivityService,
        ILauncherDialogService dialogService,
        FakeLauncherContentCatalog catalog,
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
                new FakeStringLocalizer()),
            dialogService,
            new FakeStringLocalizer(),
            NullLogger<LauncherModificationDownloadCoordinator>.Instance);
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
        return TestModificationTile.Create(modification, new FakeStringLocalizer(), packageActivityService);
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
            FakeLauncherContentCatalog resolvedCatalog = catalog ?? CreateCatalog();
            LauncherPackageActivityService resolvedPackageActivityService =
                packageActivityService ?? new LauncherPackageActivityService();
            LauncherRuntimeContext runtimeContext = TestLauncherRuntimeContext.Create();

            ViewModel = TestMainWindowViewModel.Create(
                resolvedCatalog,
                preferencesService,
                runtimeContext,
                packageActivityService: resolvedPackageActivityService,
                launchCoordinator: launchCoordinator,
                stringLocalizer: new FakeStringLocalizer());

            ListBox modsList = new()
            {
                Name = "ModsList",
                SelectionMode = SelectionMode.Single,
                ItemsSource = ViewModel.ModsListSource
            };
            ListBox patchesList = new()
            {
                Name = "PatchesList",
                SelectionMode = SelectionMode.Single,
                ItemsSource = ViewModel.PatchesListSource
            };
            ListBox addonsList = new()
            {
                Name = "AddonsList",
                SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle,
                ItemsSource = ViewModel.AddonsListSource
            };

            Content = new LauncherWindowListController(
                ViewModel,
                runtimeContext,
                modsList,
                patchesList,
                addonsList);
            Context = new LauncherWindowContext(ViewModel, Content, Owner);
        }

        public Window Owner { get; } = new();

        public MainWindowViewModel ViewModel { get; }

        public LauncherWindowListController Content { get; }

        public LauncherWindowContext Context { get; }

        public void AddTile(ModificationViewModel modification)
        {
            ViewModel.ModsListSource.Add(modification);
        }
    }
}
