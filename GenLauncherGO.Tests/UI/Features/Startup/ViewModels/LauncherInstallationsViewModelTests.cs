using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Startup.ViewModels;
using GenLauncherGO.UI.Features.Startup.Views;

namespace GenLauncherGO.Tests.UI.Features.Startup.ViewModels;

[Collection("Avalonia")]
public sealed class LauncherInstallationsViewModelTests
{
    private static string ExecutableDirectory => TestLauncherInstallations.StoragePaths.ExecutableDirectory;

    [Fact]
    public void OneValidInstallation_WithOtherPathEmpty_PermitsContinue()
    {
        const string CanonicalGeneralsPath = @"C:\Games\Generals";
        FakeGameInstallationService installationService = new()
        {
            ValidationRule = (_, path) => string.IsNullOrWhiteSpace(path)
                ? MissingPath()
                : GameInstallationValidationResult.Valid(CanonicalGeneralsPath)
        };
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations { Generals = @"C:\Games\GENERALS" },
            installationService);

        viewModel.CanContinue.Should().BeTrue();
        viewModel.HasGeneralsValidationError.Should().BeFalse();
        viewModel.HasZeroHourValidationError.Should().BeFalse();
        viewModel.CreateValidatedInstallations().Should().Be(
            new LauncherInstallations { Generals = CanonicalGeneralsPath });
    }

    [Fact]
    public void InvalidNonemptyPath_WithAnotherValidInstallation_BlocksContinue()
    {
        FakeGameInstallationService installationService = new()
        {
            ValidationRule = (game, path) =>
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return MissingPath();
                }

                return game == SupportedGame.Generals
                    ? GameInstallationValidationResult.Valid(@"C:\Games\Generals")
                    : GameInstallationValidationResult.Invalid(
                        GameInstallationValidationFailure.DirectoryNotFound);
            }
        };
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations
            {
                Generals = @"C:\Games\Generals",
                ZeroHour = @"C:\Not-Zero-Hour"
            },
            installationService);

        viewModel.IsGeneralsValid.Should().BeTrue();
        viewModel.HasZeroHourValidationError.Should().BeTrue();
        viewModel.CanContinue.Should().BeFalse();
        Action createInstallations = () => viewModel.CreateValidatedInstallations();
        createInstallations.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(GameInstallationValidationFailure.DirectoryNotFound)]
    [InlineData(GameInstallationValidationFailure.LauncherLocationOverlapsGame)]
    [InlineData(GameInstallationValidationFailure.UnsafeFileSystemPath)]
    [InlineData(GameInstallationValidationFailure.PathUnavailable)]
    [InlineData(GameInstallationValidationFailure.BuiltInExecutableNotFound)]
    public void InvalidInstallation_UsesRequiredFilesMessageAndBlocksContinue(
        GameInstallationValidationFailure failure)
    {
        FakeGameInstallationService installationService = new()
        {
            ValidationRule = (_, path) => string.IsNullOrWhiteSpace(path)
                ? MissingPath()
                : GameInstallationValidationResult.Invalid(failure)
        };
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations
            {
                Generals = @"C:\Games\Generals",
                ZeroHour = @"C:\Games\Zero Hour"
            },
            installationService);

        viewModel.HasGeneralsValidationError.Should().BeTrue();
        viewModel.HasZeroHourValidationError.Should().BeTrue();
        viewModel.GeneralsStatusText.Should().Be("Requires one of: generalsv.exe, generals.exe.");
        viewModel.ZeroHourStatusText.Should().Be(
            "Requires one of: generalsonlinezh.exe, generalszh.exe, generals.exe.");
        viewModel.CanContinue.Should().BeFalse();
    }

    [Fact]
    public void InstallationsResolvingToSameCanonicalPath_AreRejectedAsDuplicates()
    {
        const string SharedCanonicalPath = @"C:\Games\SharedPhysicalRoot";
        FakeGameInstallationService installationService = new()
        {
            ValidationRule = (_, path) => string.IsNullOrWhiteSpace(path)
                ? MissingPath()
                : GameInstallationValidationResult.Valid(SharedCanonicalPath)
        };
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations
            {
                Generals = @"C:\Games\GeneralsAlias",
                ZeroHour = @"C:\Games\ZeroHourAlias"
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
    public void IdenticalEnteredPaths_ShowDuplicateStatusWhenFilesystemValidationFails()
    {
        const string IdenticalPath = @"C:\Missing\Game";
        FakeGameInstallationService installationService = new()
        {
            ValidationRule = (_, _) => GameInstallationValidationResult.Invalid(
                GameInstallationValidationFailure.DirectoryNotFound)
        };
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations
            {
                Generals = IdenticalPath,
                ZeroHour = IdenticalPath
            },
            installationService);

        viewModel.HasDuplicateInstallationPath.Should().BeTrue();
        viewModel.GeneralsStatusText.Should().Be("DuplicateGameInstallation");
        viewModel.ZeroHourStatusText.Should().Be("DuplicateGameInstallation");
    }

    [Fact]
    public void SurroundingWhitespace_DoesNotHideAnIdenticalPathFromDuplicateDetection()
    {
        const string TrimmedPath = @"C:\Games\Zero Hour";
        FakeGameInstallationService installationService = new();
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations
            {
                Generals = $"  {TrimmedPath}  ",
                ZeroHour = TrimmedPath
            },
            installationService);

        viewModel.HasDuplicateInstallationPath.Should().BeTrue();
        viewModel.GeneralsStatusText.Should().Be("DuplicateGameInstallation");
        viewModel.ZeroHourStatusText.Should().Be("DuplicateGameInstallation");
        viewModel.CanContinue.Should().BeFalse();
        installationService.ValidateCalls.Select(call => call.Directory).Should().AllBe(TrimmedPath);
    }

    [Fact]
    public void TypingIdenticalPathIntoFocusedTextBox_UpdatesDuplicateStatusImmediately()
    {
        StaTestRunner.Run(() =>
        {
            const string IdenticalPath = @"C:\Games\Zero Hour";
            FakeGameInstallationService installationService = new()
            {
                ValidationRule = (game, path) =>
                    game == SupportedGame.ZeroHour &&
                    string.Equals(path, IdenticalPath, StringComparison.Ordinal)
                        ? GameInstallationValidationResult.Valid(IdenticalPath)
                        : GameInstallationValidationResult.Invalid(
                            GameInstallationValidationFailure.DirectoryNotFound)
            };
            LauncherInstallationsViewModel installations = CreateViewModel(
                new LauncherInstallations { ZeroHour = IdenticalPath },
                installationService);
            var setup = new LauncherSetupViewModel(
                new RecordingLauncherPreferencesService(new LauncherPreferences()),
                installations,
                false);
            var window = new LauncherSetupWindow(
                setup,
                new FakeStringLocalizer(),
                new RecordingStartupDialogService());

            try
            {
                window.Show();
                window.UpdateLayout();
                // The installation rows come from one template, so the field is identified by the row it edits.
                TextBox textBox = window.GetVisualDescendants()
                    .OfType<TextBox>()
                    .Single(candidate =>
                        candidate.DataContext is LauncherGameInstallationViewModel row &&
                        row.Game == SupportedGame.Generals);
                textBox.Focus();

                foreach (char character in IdenticalPath)
                {
                    window.KeyTextInput(character.ToString());
                }

                installations.GeneralsPath.Should().Be(IdenticalPath);
                installations.GeneralsStatusText.Should().Be("DuplicateGameInstallation");
                installations.ZeroHourStatusText.Should().Be("DuplicateGameInstallation");
                window.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Where(text => text.Classes.Contains("installation-status"))
                    .Select(text => text.Text)
                    .Should()
                    .Equal("DuplicateGameInstallation", "DuplicateGameInstallation");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ValidProgramFilesInstallation_ShowsModToolsWarning()
    {
        const string ProgramFilesPath = @"C:\Program Files\EA Games\Command and Conquer Generals";
        FakeGameInstallationService installationService = new()
        {
            ValidationRule = (_, path) => string.IsNullOrWhiteSpace(path)
                ? MissingPath()
                : GameInstallationValidationResult.Valid(ProgramFilesPath)
        };
        ILauncherHostEnvironmentService hostEnvironmentService =
            Substitute.For<ILauncherHostEnvironmentService>();
        hostEnvironmentService
            .IsProtectedProgramFilesDirectory(ProgramFilesPath)
            .Returns(true);
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations { Generals = ProgramFilesPath },
            installationService,
            hostEnvironmentService);

        viewModel.ShowGeneralsProgramFilesWarning.Should().BeTrue();
        viewModel.ShowZeroHourProgramFilesWarning.Should().BeFalse();
    }

    [Fact]
    public void ValidInstallationOnDifferentDrive_ShowsSameDriveRecommendation()
    {
        const string ZeroHourPath = @"D:\Games\Zero Hour";
        FakeGameInstallationService installationService = new()
        {
            ValidationRule = (_, path) => string.IsNullOrWhiteSpace(path)
                ? MissingPath()
                : GameInstallationValidationResult.Valid(ZeroHourPath)
        };
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations { ZeroHour = ZeroHourPath },
            installationService);

        viewModel.ShowZeroHourDifferentDriveRecommendation.Should().BeTrue();
        viewModel.ShowGeneralsDifferentDriveRecommendation.Should().BeFalse();
    }

    [Fact]
    public void DetectAll_WithValidManualPath_RetainsItWhileFillingMissingInstallation()
    {
        const string ManualGeneralsPath = @"C:\Manually Selected\Generals";
        const string RegistryZeroHourPath = @"C:\Registry Detected\Zero Hour";
        FakeGameInstallationService installationService = new()
        {
            DiscoveredInstallations = new LauncherInstallations
            {
                Generals = ManualGeneralsPath,
                ZeroHour = RegistryZeroHourPath
            }
        };
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations { Generals = $"  {ManualGeneralsPath}  " },
            installationService);

        viewModel.DetectAll();

        viewModel.GeneralsPath.Should().Be(ManualGeneralsPath);
        viewModel.ZeroHourPath.Should().Be(RegistryZeroHourPath);
        installationService.DiscoverValidInstallationsCalls.Should().ContainSingle()
            .Which.Should().Be(
                (new LauncherInstallations { Generals = ManualGeneralsPath }, ExecutableDirectory));
        installationService.ValidateCalls
            .Where(call => call.Game == SupportedGame.Generals)
            .Select(call => call.Directory)
            .Should()
            .AllBe(ManualGeneralsPath);
    }

    [Fact]
    public void RegistryDetection_ReplacesCurrentValidPathForRequestedGame()
    {
        const string CurrentZeroHourPath = @"C:\Current\Zero Hour";
        const string RegistryZeroHourPath = @"C:\Registry\Zero Hour";
        FakeGameInstallationService installationService = new()
        {
            DiscoveredInstallations = new LauncherInstallations { ZeroHour = RegistryZeroHourPath }
        };
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations { ZeroHour = CurrentZeroHourPath },
            installationService);

        viewModel.DetectZeroHourCommand.Execute(null);

        viewModel.ZeroHourPath.Should().Be(RegistryZeroHourPath);
        installationService.DiscoverValidInstallationsCalls.Should().ContainSingle()
            .Which.Should().Be((new LauncherInstallations(), ExecutableDirectory));
    }

    [Fact]
    public void GeneralsRegistryDetection_IgnoresDuplicateZeroHourDraft()
    {
        const string DuplicateDraftPath = @"C:\Current\Generals";
        const string RegistryGeneralsPath = @"C:\Registry\Generals";
        FakeGameInstallationService installationService = new()
        {
            DiscoveredInstallations = new LauncherInstallations { Generals = RegistryGeneralsPath }
        };
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations
            {
                Generals = DuplicateDraftPath,
                ZeroHour = DuplicateDraftPath
            },
            installationService);

        viewModel.DetectGeneralsCommand.Execute(null);

        viewModel.GeneralsPath.Should().Be(RegistryGeneralsPath);
        installationService.DiscoverValidInstallationsCalls.Should().ContainSingle()
            .Which.Should().Be((new LauncherInstallations(), ExecutableDirectory));
    }

    [Fact]
    public void RegistryDetection_ReportsOutcomeWithoutReplacingValidationStatus()
    {
        const string InvalidGeneralsPath = @"C:\Invalid\Generals";
        const string ValidZeroHourPath = @"C:\Valid\Zero Hour";
        FakeGameInstallationService installationService = new()
        {
            ValidationRule = (game, path) =>
            {
                if (game == SupportedGame.ZeroHour &&
                    string.Equals(path, ValidZeroHourPath, StringComparison.Ordinal))
                {
                    return GameInstallationValidationResult.Valid(ValidZeroHourPath);
                }

                return string.IsNullOrWhiteSpace(path)
                    ? MissingPath()
                    : GameInstallationValidationResult.Invalid(
                        GameInstallationValidationFailure.DirectoryNotFound);
            },
            DiscoveredInstallations = new LauncherInstallations { ZeroHour = ValidZeroHourPath }
        };
        LauncherInstallationsViewModel viewModel = CreateViewModel(
            new LauncherInstallations { Generals = InvalidGeneralsPath },
            installationService);
        SupportedGame? failedGame = null;
        SupportedGame? succeededGame = null;
        viewModel.RegistryDetectionFailed += game => failedGame = game;
        viewModel.RegistryDetectionSucceeded += game => succeededGame = game;

        viewModel.DetectGeneralsCommand.Execute(null);
        viewModel.DetectZeroHourCommand.Execute(null);

        failedGame.Should().Be(SupportedGame.Generals);
        succeededGame.Should().Be(SupportedGame.ZeroHour);
        viewModel.GeneralsStatusText.Should().Be("Requires one of: generalsv.exe, generals.exe.");
        viewModel.ZeroHourStatusText.Should().Be("ValidZeroHourInstallation");
        viewModel.DetectGeneralsCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void BrowseGeneralsCommand_WhenNothingIsPicked_LeavesEveryPathUnchanged()
    {
        StaTestRunner.Run(async () =>
        {
            const string TrimmedGeneralsPath = @"C:\Games\Generals";
            const string DraftGeneralsPath = $"  {TrimmedGeneralsPath}  ";
            StubLauncherFilePicker filePicker = new();
            LauncherInstallationsViewModel viewModel = CreateViewModel(
                new LauncherInstallations { Generals = DraftGeneralsPath },
                new FakeGameInstallationService(),
                filePicker: filePicker);
            Window owner = new();

            try
            {
                await viewModel.BrowseGeneralsCommand.ExecuteAsync(owner);
            }
            finally
            {
                owner.Close();
            }

            viewModel.GeneralsPath.Should().Be(DraftGeneralsPath);
            viewModel.ZeroHourPath.Should().BeEmpty();
            filePicker.RequestedInitialDirectories.Should().ContainSingle()
                .Which.Should().Be(TrimmedGeneralsPath);
        });
    }

    [Fact]
    public void BrowseZeroHourCommand_WhenAFolderIsPicked_UpdatesOnlyZeroHour()
    {
        StaTestRunner.Run(async () =>
        {
            const string PickedZeroHourPath = @"D:\Picked\Zero Hour";
            const string GeneralsPath = @"C:\Games\Generals";
            StubLauncherFilePicker filePicker = new()
            {
                GameInstallationFolderResult = PickedZeroHourPath
            };
            LauncherInstallationsViewModel viewModel = CreateViewModel(
                new LauncherInstallations { Generals = GeneralsPath },
                new FakeGameInstallationService(),
                filePicker: filePicker);
            Window owner = new();

            try
            {
                await viewModel.BrowseZeroHourCommand.ExecuteAsync(owner);
            }
            finally
            {
                owner.Close();
            }

            viewModel.ZeroHourPath.Should().Be(PickedZeroHourPath);
            viewModel.GeneralsPath.Should().Be(GeneralsPath);
            filePicker.RequestedInitialDirectories.Should().ContainSingle()
                .Which.Should().BeNull();
        });
    }

    [Fact]
    public void BrowseCommand_ForwardsPickerFailureToDispatcherExceptionBoundary()
    {
        StaTestRunner.Run(async () =>
        {
            var expectedException = new InvalidOperationException("Picker failed.");
            LauncherInstallationsViewModel viewModel = CreateViewModel(
                new LauncherInstallations(),
                new FakeGameInstallationService(),
                filePicker: new StubLauncherFilePicker
                {
                    GameInstallationFolderFailure = expectedException
                });
            TaskCompletionSource<Exception> observedException = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Window owner = new();

            Dispatcher.UIThread.UnhandledException += OnUnhandledException;
            try
            {
                viewModel.BrowseGeneralsCommand.Execute(owner);

                Exception exception = await observedException.Task.WaitAsync(TestTimeouts.Wait);

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

    private static LauncherInstallationsViewModel CreateViewModel(
        LauncherInstallations installations,
        FakeGameInstallationService installationService,
        ILauncherHostEnvironmentService? hostEnvironmentService = null,
        ILauncherFilePicker? filePicker = null)
    {
        return TestLauncherInstallations.CreateViewModel(
            installations,
            installationService,
            filePicker,
            hostEnvironmentService,
            stringLocalizer: new FakeStringLocalizer(new Dictionary<string, string>
            {
                ["GameInstallationFilesNotFound"] = "Requires one of: {0}."
            }));
    }

    private static GameInstallationValidationResult MissingPath()
    {
        return GameInstallationValidationResult.Invalid(
            GameInstallationValidationFailure.PathMissing);
    }
}
