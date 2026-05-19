using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Startup.ViewModels;

namespace GenLauncherGO.Tests.UI.Features.Startup.ViewModels;

public sealed class LauncherInstallationsViewModelTests
{
    [Fact]
    public void OneValidInstallation_WithOtherPathEmpty_PermitsContinue()
    {
        const string canonicalGeneralsPath = @"C:\Games\Generals";
        IGameInstallationService installationService = CreateInstallationService(
            (_, path) => string.IsNullOrWhiteSpace(path)
                ? MissingPath()
                : GameInstallationValidationResult.Valid(canonicalGeneralsPath));
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations { Generals = @"C:\Games\GENERALS" },
            installationService);

        viewModel.CanContinue.Should().BeTrue();
        viewModel.HasGeneralsValidationError.Should().BeFalse();
        viewModel.HasZeroHourValidationError.Should().BeFalse();
        viewModel.CreateValidatedInstallations().Should().Be(
            new LauncherInstallations { Generals = canonicalGeneralsPath });
    }

    [Fact]
    public void InvalidNonemptyPath_WithAnotherValidInstallation_BlocksContinue()
    {
        IGameInstallationService installationService = CreateInstallationService(
            (game, path) =>
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return MissingPath();
                }

                return game == SupportedGame.Generals
                    ? GameInstallationValidationResult.Valid(@"C:\Games\Generals")
                    : GameInstallationValidationResult.Invalid(
                        GameInstallationValidationFailure.RequiredFilesMissing);
            });
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations
            {
                Generals = @"C:\Games\Generals",
                ZeroHour = @"C:\Not-Zero-Hour",
            },
            installationService);

        viewModel.IsGeneralsValid.Should().BeTrue();
        viewModel.HasZeroHourValidationError.Should().BeTrue();
        viewModel.CanContinue.Should().BeFalse();
        Action createInstallations = () => viewModel.CreateValidatedInstallations();
        createInstallations.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void InstallationsResolvingToSameCanonicalPath_AreRejectedAsDuplicates()
    {
        const string sharedCanonicalPath = @"C:\Games\SharedPhysicalRoot";
        IGameInstallationService installationService = CreateInstallationService(
            (_, path) => string.IsNullOrWhiteSpace(path)
                ? MissingPath()
                : GameInstallationValidationResult.Valid(sharedCanonicalPath));
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations
            {
                Generals = @"C:\Games\GeneralsAlias",
                ZeroHour = @"C:\Games\ZeroHourAlias",
            },
            installationService);

        viewModel.HasDuplicateInstallationPath.Should().BeTrue();
        viewModel.HasGeneralsValidationError.Should().BeTrue();
        viewModel.HasZeroHourValidationError.Should().BeTrue();
        viewModel.GeneralsStatusText.Should().Be("DuplicateGameInstallation");
        viewModel.ZeroHourStatusText.Should().Be("DuplicateGameInstallation");
        viewModel.CanContinue.Should().BeFalse();
    }

    [Fact]
    public void ValidProgramFilesInstallation_ShowsModToolsWarning()
    {
        const string programFilesPath = @"C:\Program Files\EA Games\Command and Conquer Generals";
        IGameInstallationService installationService = CreateInstallationService(
            (_, path) => string.IsNullOrWhiteSpace(path)
                ? MissingPath()
                : GameInstallationValidationResult.Valid(programFilesPath));
        ILauncherHostEnvironmentService hostEnvironmentService =
            Substitute.For<ILauncherHostEnvironmentService>();
        hostEnvironmentService
            .IsProtectedProgramFilesDirectory(programFilesPath)
            .Returns(true);
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations { Generals = programFilesPath },
            installationService,
            hostEnvironmentService);

        viewModel.ShowGeneralsProgramFilesWarning.Should().BeTrue();
        viewModel.ShowZeroHourProgramFilesWarning.Should().BeFalse();
    }

    [Fact]
    public void ValidInstallationOnDifferentDrive_ShowsSameDriveRecommendation()
    {
        const string zeroHourPath = @"D:\Games\Zero Hour";
        IGameInstallationService installationService = CreateInstallationService(
            (_, path) => string.IsNullOrWhiteSpace(path)
                ? MissingPath()
                : GameInstallationValidationResult.Valid(zeroHourPath));
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations { ZeroHour = zeroHourPath },
            installationService);

        viewModel.ShowZeroHourDifferentDriveRecommendation.Should().BeTrue();
        viewModel.ShowGeneralsDifferentDriveRecommendation.Should().BeFalse();
    }

    [Fact]
    public void DetectAll_WithValidManualPath_RetainsItWhileFillingMissingInstallation()
    {
        const string manualGeneralsPath = @"C:\Manually Selected\Generals";
        const string registryZeroHourPath = @"C:\Registry Detected\Zero Hour";
        IGameInstallationService installationService = CreateInstallationService(
            (_, path) => string.IsNullOrWhiteSpace(path)
                ? MissingPath()
                : GameInstallationValidationResult.Valid(path));
        installationService
            .DiscoverValidInstallations(
                Arg.Any<LauncherInstallations>(),
                Arg.Any<string>())
            .Returns(call =>
            {
                LauncherInstallations current = call.ArgAt<LauncherInstallations>(0);
                return current with { ZeroHour = registryZeroHourPath };
            });
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations { Generals = manualGeneralsPath },
            installationService);

        viewModel.DetectAll();

        viewModel.GeneralsPath.Should().Be(manualGeneralsPath);
        viewModel.ZeroHourPath.Should().Be(registryZeroHourPath);
        installationService.Received(1).DiscoverValidInstallations(
            new LauncherInstallations { Generals = manualGeneralsPath },
            @"C:\Launcher");
    }

    [Fact]
    public void RegistryDetectionCommands_AreEnabledOnlyForInvalidInstallations()
    {
        IGameInstallationService installationService = CreateInstallationService(
            (_, path) => path is not null && path.Contains("Valid", StringComparison.Ordinal)
                ? GameInstallationValidationResult.Valid(path)
                : MissingPath());
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations
            {
                Generals = @"C:\Valid\Generals",
                ZeroHour = @"C:\Missing\Zero Hour",
            },
            installationService);

        viewModel.DetectGeneralsCommand.CanExecute(null).Should().BeFalse();
        viewModel.DetectZeroHourCommand.CanExecute(null).Should().BeTrue();

        viewModel.GeneralsPath = @"C:\Missing\Generals";
        viewModel.ZeroHourPath = @"C:\Valid\Zero Hour";

        viewModel.DetectGeneralsCommand.CanExecute(null).Should().BeTrue();
        viewModel.DetectZeroHourCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void RegistryDetection_ReportsOutcomeWithoutReplacingValidationStatus()
    {
        const string invalidGeneralsPath = @"C:\Invalid\Generals";
        const string validZeroHourPath = @"C:\Valid\Zero Hour";
        IGameInstallationService installationService = CreateInstallationService(
            (game, path) =>
            {
                if (game == SupportedGame.ZeroHour &&
                    string.Equals(path, validZeroHourPath, StringComparison.Ordinal))
                {
                    return GameInstallationValidationResult.Valid(validZeroHourPath);
                }

                return string.IsNullOrWhiteSpace(path)
                    ? MissingPath()
                    : GameInstallationValidationResult.Invalid(
                        GameInstallationValidationFailure.RequiredFilesMissing);
            });
        installationService
            .DiscoverValidInstallations(
                Arg.Any<LauncherInstallations>(),
                Arg.Any<string>())
            .Returns(new LauncherInstallations { ZeroHour = validZeroHourPath });
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations { Generals = invalidGeneralsPath },
            installationService);
        SupportedGame? failedGame = null;
        SupportedGame? succeededGame = null;
        viewModel.RegistryDetectionFailed += game => failedGame = game;
        viewModel.RegistryDetectionSucceeded += game => succeededGame = game;

        viewModel.DetectGeneralsCommand.Execute(null);
        viewModel.DetectZeroHourCommand.Execute(null);

        failedGame.Should().Be(SupportedGame.Generals);
        succeededGame.Should().Be(SupportedGame.ZeroHour);
        viewModel.GeneralsStatusText.Should().Be("InvalidGeneralsInstallation");
        viewModel.ZeroHourStatusText.Should().Be("ValidZeroHourInstallation");
    }

    [Fact]
    public void BrowseCommandForwardsPickerFailureToDispatcherExceptionBoundary()
    {
        StaTestRunner.Run(async () =>
        {
            var expectedException = new InvalidOperationException("Picker failed.");
            LauncherInstallationsViewModel viewModel = CreateViewModel(
                new LauncherInstallations(),
                CreateInstallationService((_, _) => MissingPath()),
                filePicker: new FailingGameInstallationFilePicker(expectedException));
            TaskCompletionSource<Exception> observedException = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Window owner = new();

            Dispatcher.UIThread.UnhandledException += OnUnhandledException;
            try
            {
                viewModel.BrowseGeneralsCommand.Execute(owner);

                Exception exception = await observedException.Task.WaitAsync(TimeSpan.FromSeconds(5));

                exception.Should().BeSameAs(expectedException);
            }
            finally
            {
                Dispatcher.UIThread.UnhandledException -= OnUnhandledException;
                owner.Close();
            }

            void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs eventArgs)
            {
                eventArgs.Handled = true;
                observedException.TrySetResult(eventArgs.Exception);
            }
        });
    }

    private sealed class FailingGameInstallationFilePicker : ILauncherFilePicker
    {
        private readonly Exception _exception;

        public FailingGameInstallationFilePicker(Exception exception)
        {
            _exception = exception;
        }

        public Task<string?> PickGameInstallationFolderAsync(Window owner, string? initialDirectory)
        {
            return Task.FromException<string?>(_exception);
        }

        public Task<IReadOnlyList<string>> PickManualPackageFilesAsync(Window owner)
        {
            throw new NotSupportedException();
        }

        public Task<string?> PickModificationImageFileAsync(Window owner, string imageFilterLabel)
        {
            throw new NotSupportedException();
        }

        public Task<string?> PickGameExecutableFileAsync(Window owner, string gameDirectory)
        {
            throw new NotSupportedException();
        }
    }

    private static LauncherInstallationsViewModel CreateViewModel(
        LauncherInstallations installations,
        IGameInstallationService installationService,
        ILauncherHostEnvironmentService? hostEnvironmentService = null,
        ILauncherFilePicker? filePicker = null)
    {
        return new LauncherInstallationsViewModel(
            installations,
            new LauncherStoragePaths(@"C:\Launcher"),
            installationService,
            hostEnvironmentService ?? Substitute.For<ILauncherHostEnvironmentService>(),
            filePicker ?? new NullLauncherFilePicker(),
            new TestStringLocalizer());
    }

    private static IGameInstallationService CreateInstallationService(
        Func<SupportedGame, string?, GameInstallationValidationResult> validation)
    {
        IGameInstallationService installationService =
            Substitute.For<IGameInstallationService>();
        installationService
            .Validate(
                Arg.Any<SupportedGame>(),
                Arg.Any<string?>(),
                Arg.Any<string>())
            .Returns(call => validation(
                call.ArgAt<SupportedGame>(0),
                call.ArgAt<string?>(1)));
        installationService
            .DiscoverValidInstallations(
                Arg.Any<LauncherInstallations>(),
                Arg.Any<string>())
            .Returns(call => call.ArgAt<LauncherInstallations>(0));
        return installationService;
    }

    private static GameInstallationValidationResult MissingPath()
    {
        return GameInstallationValidationResult.Invalid(
            GameInstallationValidationFailure.PathMissing);
    }
}

public sealed class LauncherGameSelectionViewModelTests
{
    [Fact]
    public void InitialState_SelectsZeroHourByDefault()
    {
        var preferencesService = new RecordingPreferencesService(new LauncherPreferences
        {
            LastSelectedGame = SupportedGame.Generals,
        });
        var viewModel = new LauncherGameSelectionViewModel(preferencesService);

        viewModel.SelectedGame.Should().Be(SupportedGame.ZeroHour);
        viewModel.IsGeneralsSelected.Should().BeFalse();
        viewModel.IsZeroHourSelected.Should().BeTrue();
        viewModel.ContinueCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void SelectingAndContinuing_PersistsChoiceAndCompletes()
    {
        var preferencesService = new RecordingPreferencesService(new LauncherPreferences());
        var viewModel = new LauncherGameSelectionViewModel(preferencesService);
        int selectionChangedCount = 0;
        int completedCount = 0;
        viewModel.SelectionChanged += (_, _) => selectionChangedCount++;
        viewModel.Completed += (_, _) => completedCount++;

        viewModel.SelectGameCommand.Execute(SupportedGame.Generals);

        viewModel.SelectedGame.Should().Be(SupportedGame.Generals);
        viewModel.IsGeneralsSelected.Should().BeTrue();
        viewModel.ContinueCommand.CanExecute(null).Should().BeTrue();
        selectionChangedCount.Should().Be(1);

        viewModel.ContinueCommand.Execute(null);

        preferencesService.Updates.Should().ContainSingle();
        preferencesService.Current.LastSelectedGame.Should().Be(SupportedGame.Generals);
        completedCount.Should().Be(1);
    }

    [Fact]
    public void Canceling_RequestsStartupExit()
    {
        var preferencesService = new RecordingPreferencesService(new LauncherPreferences());
        var viewModel = new LauncherGameSelectionViewModel(preferencesService);
        int cancelRequestedCount = 0;
        viewModel.CancelRequested += (_, _) => cancelRequestedCount++;

        viewModel.CancelCommand.Execute(null);

        cancelRequestedCount.Should().Be(1);
        preferencesService.Updates.Should().BeEmpty();
    }
}

public sealed class LauncherSetupViewModelTests
{
    [Fact]
    public void InstallationBecomesValid_NotifiesContinueCommandAvailability()
    {
        const string validPath = @"C:\Games\Generals";
        IGameInstallationService installationService =
            Substitute.For<IGameInstallationService>();
        installationService
            .Validate(
                Arg.Any<SupportedGame>(),
                Arg.Any<string?>(),
                Arg.Any<string>())
            .Returns(call => string.Equals(
                    call.ArgAt<string?>(1),
                    validPath,
                    StringComparison.Ordinal)
                ? GameInstallationValidationResult.Valid(validPath)
                : GameInstallationValidationResult.Invalid(
                    GameInstallationValidationFailure.PathMissing));
        var installations = new LauncherInstallationsViewModel(
            new LauncherInstallations(),
            new LauncherStoragePaths(@"C:\Launcher"),
            installationService,
            Substitute.For<ILauncherHostEnvironmentService>(),
            new NullLauncherFilePicker(),
            new TestStringLocalizer());
        var viewModel = new LauncherSetupViewModel(
            new RecordingPreferencesService(new LauncherPreferences()),
            installations,
            detectInstallationsOnOpen: false);
        int availabilityChangedCount = 0;
        viewModel.ContinueCommand.CanExecuteChanged += (_, _) => availabilityChangedCount++;

        viewModel.ContinueCommand.CanExecute(null).Should().BeFalse();

        installations.GeneralsPath = validPath;

        viewModel.ContinueCommand.CanExecute(null).Should().BeTrue();
        availabilityChangedCount.Should().Be(1);
    }

    [Fact]
    public void OpeningFromSettings_DoesNotAutomaticallyDiscoverInstallations()
    {
        IGameInstallationService installationService =
            Substitute.For<IGameInstallationService>();
        installationService
            .Validate(
                Arg.Any<SupportedGame>(),
                Arg.Any<string?>(),
                Arg.Any<string>())
            .Returns(GameInstallationValidationResult.Invalid(
                GameInstallationValidationFailure.PathMissing));
        var installations = new LauncherInstallationsViewModel(
            new LauncherInstallations(),
            new LauncherStoragePaths(@"C:\Launcher"),
            installationService,
            Substitute.For<ILauncherHostEnvironmentService>(),
            new NullLauncherFilePicker(),
            new TestStringLocalizer());

        _ = new LauncherSetupViewModel(
            new RecordingPreferencesService(new LauncherPreferences()),
            installations,
            detectInstallationsOnOpen: false);

        installationService.DidNotReceive().DiscoverValidInstallations(
            Arg.Any<LauncherInstallations>(),
            Arg.Any<string>());
    }

    [Fact]
    public void ContinuingWithSoleValidInstallation_PersistsCanonicalPathAndSelectsGame()
    {
        const string configuredPath = @"C:\Games\GENERALS";
        const string canonicalPath = @"C:\Games\Generals";
        var preferencesService = new RecordingPreferencesService(new LauncherPreferences
        {
            LastSelectedGame = SupportedGame.ZeroHour,
        });
        IGameInstallationService installationService =
            Substitute.For<IGameInstallationService>();
        installationService
            .Validate(
                Arg.Any<SupportedGame>(),
                Arg.Any<string?>(),
                Arg.Any<string>())
            .Returns(call => string.IsNullOrWhiteSpace(call.ArgAt<string?>(1))
                ? GameInstallationValidationResult.Invalid(
                    GameInstallationValidationFailure.PathMissing)
                : GameInstallationValidationResult.Valid(canonicalPath));
        installationService
            .DiscoverValidInstallations(
                Arg.Any<LauncherInstallations>(),
                Arg.Any<string>())
            .Returns(call => call.ArgAt<LauncherInstallations>(0));
        var installations = new LauncherInstallationsViewModel(
            new LauncherInstallations { Generals = configuredPath },
            new LauncherStoragePaths(@"C:\Launcher"),
            installationService,
            Substitute.For<ILauncherHostEnvironmentService>(),
            new NullLauncherFilePicker(),
            new TestStringLocalizer());
        var viewModel = new LauncherSetupViewModel(preferencesService, installations);
        int completedCount = 0;
        viewModel.Completed += (_, _) => completedCount++;

        viewModel.ContinueCommand.CanExecute(null).Should().BeTrue();
        viewModel.ContinueCommand.Execute(null);

        preferencesService.Updates.Should().ContainSingle();
        preferencesService.Current.Installations.Should().Be(
            new LauncherInstallations { Generals = canonicalPath });
        preferencesService.Current.LastSelectedGame.Should().Be(SupportedGame.Generals);
        completedCount.Should().Be(1);
    }
}

internal sealed class RecordingPreferencesService : ILauncherPreferencesService
{
    public RecordingPreferencesService(LauncherPreferences preferences)
    {
        Current = preferences;
    }

    public event EventHandler<LauncherPreferences>? PreferencesChanged;

    public LauncherPreferences Current { get; private set; }

    public List<LauncherPreferences> Updates { get; } = new();

    public void Update(LauncherPreferences preferences)
    {
        Current = preferences;
        Updates.Add(preferences);
        PreferencesChanged?.Invoke(this, preferences);
    }
}

internal sealed class NullLauncherFilePicker : ILauncherFilePicker
{
    public Task<string?> PickGameInstallationFolderAsync(
        Window owner,
        string? initialDirectory)
    {
        return Task.FromResult<string?>(null);
    }

    public Task<IReadOnlyList<string>> PickManualPackageFilesAsync(Window owner)
    {
        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    public Task<string?> PickModificationImageFileAsync(
        Window owner,
        string imageFilterLabel)
    {
        return Task.FromResult<string?>(null);
    }

    public Task<string?> PickGameExecutableFileAsync(Window owner, string gameDirectory)
    {
        return Task.FromResult<string?>(null);
    }
}
