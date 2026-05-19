using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;
using GenLauncherGO.UI.Features.Startup.ViewModels;

namespace GenLauncherGO.Tests.UI.Features.Startup.ViewModels;

public sealed class LauncherGameInstallationViewModelTests
{
    private const string GeneralsPath = @"C:\Missing\Generals";

    private const string ZeroHourPath = @"D:\Program Files\Zero Hour";

    private const string PickedPath = @"E:\Picked\Game";

    private const string GeneralsDisplayName = "Command and Conquer Generals";

    private const string ZeroHourDisplayName = "Command and Conquer Generals Zero Hour";

    private const string GeneralsStatusText = "Game files not found";

    private const string ZeroHourStatusText = "Zero Hour installation found";

    [Theory]
    [InlineData(
        SupportedGame.Generals,
        GeneralsPath,
        GeneralsStatusText,
        false,
        true,
        false,
        false,
        GeneralsDisplayName)]
    [InlineData(
        SupportedGame.ZeroHour,
        ZeroHourPath,
        ZeroHourStatusText,
        true,
        false,
        true,
        true,
        ZeroHourDisplayName)]
    public void Row_ExposesOnlyItsOwnGameState(
        SupportedGame game,
        string expectedPath,
        string expectedStatusText,
        bool expectedIsValid,
        bool expectedHasValidationError,
        bool expectedProgramFilesWarning,
        bool expectedDifferentDriveRecommendation,
        string expectedDisplayName)
    {
        LauncherInstallationsViewModel installations = CreateInstallations();

        LauncherGameInstallationViewModel row = GetRow(installations, game);

        row.Path.Should().Be(expectedPath);
        row.StatusText.Should().Be(expectedStatusText);
        row.IsValid.Should().Be(expectedIsValid);
        row.HasValidationError.Should().Be(expectedHasValidationError);
        row.ShowProgramFilesWarning.Should().Be(expectedProgramFilesWarning);
        row.ShowDifferentDriveRecommendation.Should().Be(expectedDifferentDriveRecommendation);
        row.DisplayName.Should().Be(expectedDisplayName);
    }

    [Theory]
    [InlineData(SupportedGame.Generals, PickedPath, ZeroHourPath)]
    [InlineData(SupportedGame.ZeroHour, GeneralsPath, PickedPath)]
    public void Path_WhenAssigned_UpdatesOnlyTheMatchingGame(
        SupportedGame game,
        string expectedGeneralsPath,
        string expectedZeroHourPath)
    {
        LauncherInstallationsViewModel installations = CreateInstallations();
        LauncherGameInstallationViewModel row = GetRow(installations, game);

        row.Path = PickedPath;

        installations.GeneralsPath.Should().Be(expectedGeneralsPath);
        installations.ZeroHourPath.Should().Be(expectedZeroHourPath);
    }

    [Fact]
    public void Commands_AreTheSharedCommandsOfTheMatchingGame()
    {
        LauncherInstallationsViewModel installations = CreateInstallations();

        LauncherGameInstallationViewModel generalsRow = GetRow(installations, SupportedGame.Generals);
        LauncherGameInstallationViewModel zeroHourRow = GetRow(installations, SupportedGame.ZeroHour);

        generalsRow.BrowseCommand.Should().BeSameAs(installations.BrowseGeneralsCommand);
        generalsRow.DetectCommand.Should().BeSameAs(installations.DetectGeneralsCommand);
        zeroHourRow.BrowseCommand.Should().BeSameAs(installations.BrowseZeroHourCommand);
        zeroHourRow.DetectCommand.Should().BeSameAs(installations.DetectZeroHourCommand);
    }

    [Fact]
    public void StatusText_WhenTheOtherGameBecomesADuplicate_ChangesAndNotifies()
    {
        LauncherInstallationsViewModel installations = CreateInstallations();
        LauncherGameInstallationViewModel generalsRow = GetRow(installations, SupportedGame.Generals);
        var changedProperties = new List<string?>();
        generalsRow.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        installations.ZeroHourPath = GeneralsPath;

        generalsRow.StatusText.Should().Be("Both games share one folder");
        changedProperties.Should().Contain(nameof(LauncherGameInstallationViewModel.StatusText));
    }

    private static LauncherGameInstallationViewModel GetRow(
        LauncherInstallationsViewModel installations,
        SupportedGame game)
    {
        return installations.Games.Single(candidate => candidate.Game == game);
    }

    private static LauncherInstallationsViewModel CreateInstallations()
    {
        var installationService = new FakeGameInstallationService
        {
            ValidationRule = (game, _) => game == SupportedGame.ZeroHour
                ? GameInstallationValidationResult.Valid(ZeroHourPath)
                : GameInstallationValidationResult.Invalid(
                    GameInstallationValidationFailure.DirectoryNotFound)
        };
        ILauncherHostEnvironmentService hostEnvironmentService =
            Substitute.For<ILauncherHostEnvironmentService>();
        hostEnvironmentService.IsProtectedProgramFilesDirectory(ZeroHourPath).Returns(true);

        return TestLauncherInstallations.CreateViewModel(
            new LauncherInstallations
            {
                Generals = GeneralsPath,
                ZeroHour = ZeroHourPath
            },
            installationService,
            hostEnvironmentService: hostEnvironmentService,
            stringLocalizer: new FakeStringLocalizer(new Dictionary<string, string>
            {
                ["DuplicateGameInstallation"] = "Both games share one folder",
                ["GameInstallationFilesNotFound"] = GeneralsStatusText,
                ["GeneralsFullName"] = GeneralsDisplayName,
                ["ValidZeroHourInstallation"] = ZeroHourStatusText,
                ["ZeroHourFullName"] = ZeroHourDisplayName
            }));
    }
}
