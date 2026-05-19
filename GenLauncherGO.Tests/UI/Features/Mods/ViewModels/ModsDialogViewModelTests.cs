using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Updating.Contracts;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Mods.ViewModels;

namespace GenLauncherGO.Tests.UI.Features.Mods.ViewModels;

public sealed class ModsDialogViewModelTests
{
    [Fact]
    public void AddModificationAcceptCommand_WithSelection_RequestsAcceptedClose()
    {
        using AddModificationViewModel viewModel = CreateAddModificationViewModel("Contra", "ShockWave");
        viewModel.SelectedModification = viewModel.VisibleModifications[0];
        bool closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        viewModel.AcceptCommand.Execute(null);

        closeRequested.Should().BeTrue();
        viewModel.DialogResult.Should().BeTrue();
        viewModel.SelectedModificationName.Should().Be("Contra");
    }

    [Fact]
    public void AddModificationCancelCommand_RequestsCanceledClose()
    {
        using AddModificationViewModel viewModel = CreateAddModificationViewModel("Contra");
        bool closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        viewModel.CancelCommand.Execute(null);

        closeRequested.Should().BeTrue();
        viewModel.DialogResult.Should().BeFalse();
    }

    [Fact]
    public void ManualAddAcceptCommand_WithMissingName_ShowsLocalizedErrorWithoutClosing()
    {
        FakeDialogService dialogService = new();
        ManualAddModificationViewModel viewModel = new(
            new[] { @"C:\Temp\mod.zip" },
            null,
            CreateStringLocalizer(),
            dialogService)
        {
            ModificationName = string.Empty,
            Version = "1.0"
        };
        bool closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        viewModel.AcceptCommand.Execute(null);

        viewModel.AcceptCommand.CanExecute(null).Should().BeFalse();
        viewModel.ModificationNameValidationMessage.Should().Be("Enter a modification name");
        closeRequested.Should().BeFalse();
        dialogService.LastErrorRequest.Should().NotBeNull();
        dialogService.LastErrorRequest!.MainMessage.Should().Be("Operation aborted");
        dialogService.LastErrorRequest.DetailMessage.Should().Be("Enter a modification name");
        viewModel.DialogResult.Should().BeNull();
    }

    [Fact]
    public void ManualAddAcceptCommand_WithMissingVersion_ShowsLocalizedErrorWithoutClosing()
    {
        FakeDialogService dialogService = new();
        ManualAddModificationViewModel viewModel = new(
            new[] { @"C:\Temp\mod.zip" },
            null,
            CreateStringLocalizer(),
            dialogService)
        {
            ModificationName = "ShockWave"
        };

        viewModel.AcceptCommand.Execute(null);

        viewModel.AcceptCommand.CanExecute(null).Should().BeFalse();
        viewModel.VersionValidationMessage.Should().Be("Enter a version");
        dialogService.LastErrorRequest.Should().NotBeNull();
        dialogService.LastErrorRequest!.DetailMessage.Should().Be("Enter a version");
        viewModel.DialogResult.Should().BeNull();
    }

    [Fact]
    public void ManualAddAcceptCommand_WithVersionWithoutDigits_ShowsLocalizedErrorWithoutClosing()
    {
        FakeDialogService dialogService = new();
        ManualAddModificationViewModel viewModel = new(
            new[] { @"C:\Temp\mod.zip" },
            null,
            CreateStringLocalizer(),
            dialogService)
        {
            ModificationName = "ShockWave",
            Version = "release"
        };

        viewModel.AcceptCommand.Execute(null);

        viewModel.AcceptCommand.CanExecute(null).Should().BeFalse();
        viewModel.VersionValidationMessage.Should().Be("Version must contain numbers");
        dialogService.LastErrorRequest.Should().NotBeNull();
        dialogService.LastErrorRequest!.DetailMessage.Should().Be("Version must contain numbers");
        viewModel.DialogResult.Should().BeNull();
    }

    [Fact]
    public void ManualAddAcceptCommand_WithUnsupportedNameCharacters_ShowsLocalizedErrorWithoutClosing()
    {
        FakeDialogService dialogService = new();
        ManualAddModificationViewModel viewModel = new(
            new[] { @"C:\Temp\mod.zip" },
            null,
            CreateStringLocalizer(),
            dialogService)
        {
            ModificationName = "!!!",
            Version = "1.0"
        };

        viewModel.AcceptCommand.Execute(null);

        viewModel.AcceptCommand.CanExecute(null).Should().BeFalse();
        viewModel.ModificationNameValidationMessage.Should().Be("Name and version must contain supported symbols");
        dialogService.LastErrorRequest.Should().NotBeNull();
        dialogService.LastErrorRequest!.DetailMessage.Should().Be("Name and version must contain supported symbols");
        viewModel.DialogResult.Should().BeNull();
    }

    [Fact]
    public void ManualAddAcceptCommand_WithValidInput_CreatesImportResultAndCloses()
    {
        ManualAddModificationViewModel viewModel = new(
            new[] { @"C:\Temp\mod.zip" },
            "Parent Mod",
            CreateStringLocalizer(),
            new FakeDialogService())
        {
            ModificationName = "Patch Pack",
            Version = "1.2"
        };
        bool closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        viewModel.AcceptCommand.Execute(null);

        viewModel.AcceptCommand.CanExecute(null).Should().BeTrue();
        viewModel.ModificationNameValidationMessage.Should().BeEmpty();
        viewModel.VersionValidationMessage.Should().BeEmpty();
        closeRequested.Should().BeTrue();
        viewModel.DialogResult.Should().BeTrue();
        viewModel.ImportResult.Should().NotBeNull();
        viewModel.ImportResult!.ParentContentName.Should().Be("Parent Mod");
        viewModel.ImportResult.ModificationName.Should().Be("Patch Pack");
        viewModel.ImportResult.Version.Should().Be("1.2");
        viewModel.ImportResult.Files.Should().ContainSingle().Which.Should().Be(@"C:\Temp\mod.zip");
    }

    [Fact]
    public void ManualAddCancelCommand_RequestsCanceledClose()
    {
        ManualAddModificationViewModel viewModel = new(
            new[] { @"C:\Temp\mod.zip" },
            null,
            CreateStringLocalizer(),
            new FakeDialogService());
        bool closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        viewModel.CancelCommand.Execute(null);

        closeRequested.Should().BeTrue();
        viewModel.DialogResult.Should().BeFalse();
        viewModel.ImportResult.Should().BeNull();
    }

    [Fact]
    public void ManualAddConstructor_InfersModificationNameFromFirstSelectedFile()
    {
        ManualAddModificationViewModel viewModel = new(
            new[] { @"C:\Temp\ShockWave-1.2.zip", @"C:\Temp\ignored.big" },
            null,
            CreateStringLocalizer(),
            new FakeDialogService());

        viewModel.ModificationName.Should().Be("ShockWave-1.2");
        viewModel.ModificationNameValidationMessage.Should().BeEmpty();
        viewModel.AcceptCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ManualAddFields_UpdateValidationStateImmediately()
    {
        ManualAddModificationViewModel viewModel = new(
            new[] { @"C:\Temp\mod.zip" },
            null,
            CreateStringLocalizer(),
            new FakeDialogService());

        viewModel.AcceptCommand.CanExecute(null).Should().BeFalse();
        viewModel.VersionValidationMessage.Should().Be("Enter a version");

        viewModel.ModificationName = "ShockWave";
        viewModel.Version = "release";

        viewModel.AcceptCommand.CanExecute(null).Should().BeFalse();
        viewModel.VersionValidationMessage.Should().Be("Version must contain numbers");

        viewModel.Version = "1.2";

        viewModel.AcceptCommand.CanExecute(null).Should().BeTrue();
        viewModel.ModificationNameValidationMessage.Should().BeEmpty();
        viewModel.VersionValidationMessage.Should().BeEmpty();
    }

    [Fact]
    public void InfoDialogConstructor_ForInfo_ConfiguresOkOnlyNeutralState()
    {
        InfoDialogViewModel viewModel = new(
            new LauncherInfoDialogRequest("Information", "Details"),
            InfoDialogKind.Info);

        viewModel.IsOkVisible.Should().BeTrue();
        viewModel.IsContinueVisible.Should().BeFalse();
        viewModel.IsCancelVisible.Should().BeFalse();
        viewModel.IsInfoIconVisible.Should().BeTrue();
        viewModel.IsErrorIconVisible.Should().BeFalse();
        viewModel.IsWarningIconVisible.Should().BeFalse();
    }

    [Fact]
    public void InfoDialogConstructor_ForError_ConfiguresOkOnlyErrorState()
    {
        InfoDialogViewModel viewModel = new(
            new LauncherInfoDialogRequest("Error", "Details", 12D),
            InfoDialogKind.Error);

        viewModel.MainMessage.Should().Be("Error");
        viewModel.DetailMessage.Should().Be("Details");
        viewModel.DetailFontSize.Should().Be(12D);
        viewModel.IsOkVisible.Should().BeTrue();
        viewModel.IsContinueVisible.Should().BeFalse();
        viewModel.IsCancelVisible.Should().BeFalse();
        viewModel.IsInfoIconVisible.Should().BeFalse();
        viewModel.IsErrorIconVisible.Should().BeTrue();
        viewModel.IsWarningIconVisible.Should().BeFalse();
    }

    [Fact]
    public void InfoDialogOkCommand_ForInfo_AcceptsAndRequestsClose()
    {
        InfoDialogViewModel viewModel = new(
            new LauncherInfoDialogRequest("Information", "Details"),
            InfoDialogKind.Info);
        bool closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        viewModel.OkCommand.Execute(null);

        closeRequested.Should().BeTrue();
        viewModel.DialogResult.Should().BeTrue();
    }

    [Fact]
    public void InfoDialogCloseCommand_ForInfo_AcceptsAndRequestsClose()
    {
        InfoDialogViewModel viewModel = new(
            new LauncherInfoDialogRequest("Information", "Details"),
            InfoDialogKind.Info);

        viewModel.CloseCommand.Execute(null);

        viewModel.DialogResult.Should().BeTrue();
    }

    [Fact]
    public void InfoDialogCancelCommand_ForWarning_SetsNegativeResultAndRequestsHide()
    {
        InfoDialogViewModel viewModel = new(
            new LauncherInfoDialogRequest("Warning", "Details"),
            InfoDialogKind.WarningConfirmation,
            "Continue anyway");
        bool closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        viewModel.CancelCommand.Execute(null);

        closeRequested.Should().BeTrue();
        viewModel.DialogResult.Should().BeFalse();
        viewModel.ContinueText.Should().Be("Continue anyway");
    }

    [Fact]
    public void InfoDialogContinueCommand_ForWarning_SetsPositiveResultAndRequestsHide()
    {
        InfoDialogViewModel viewModel = new(
            new LauncherInfoDialogRequest("Warning", "Details"),
            InfoDialogKind.WarningConfirmation,
            " ");
        bool closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        viewModel.ContinueCommand.Execute(null);

        closeRequested.Should().BeTrue();
        viewModel.DialogResult.Should().BeTrue();
        viewModel.ContinueText.Should().BeNull();
    }

    [Fact]
    public void InfoDialogCloseCommand_ForWarning_CancelsAndRequestsHide()
    {
        InfoDialogViewModel viewModel = new(
            new LauncherInfoDialogRequest("Warning", "Details"),
            InfoDialogKind.WarningConfirmation);

        viewModel.CloseCommand.Execute(null);

        viewModel.DialogResult.Should().BeFalse();
    }

    private static TestStringLocalizer CreateStringLocalizer()
    {
        return new TestStringLocalizer(new Dictionary<string, string>
        {
            ["EnterModName"] = "Enter a modification name",
            ["EnterModVersion"] = "Enter a version",
            ["NameAndVersionValidSymbols"] = "Name and version must contain supported symbols",
            ["OperationAborted"] = "Operation aborted",
            ["VersionMustContainNumbers"] = "Version must contain numbers",
        });
    }

    private static AddModificationViewModel CreateAddModificationViewModel(params string[] names)
    {
        return new AddModificationViewModel(
            names,
            new FakeLauncherContentCatalog(),
            Substitute.For<IRemotePackageSizeResolver>(),
            new TestStringLocalizer(new Dictionary<string, string>
            {
                ["CalculatingPackageSize"] = "Calculating...",
                ["PackageSizeUnavailable"] = "Unavailable",
            }));
    }

    private sealed class FakeDialogService : ILauncherDialogService
    {
        public LauncherInfoDialogRequest? LastErrorRequest { get; private set; }

        public LauncherInfoDialogRequest? LastInfoRequest { get; private set; }

        public Task ShowInfoAsync(LauncherInfoDialogRequest request, Window? owner = null)
        {
            LastInfoRequest = request;
            return Task.CompletedTask;
        }

        public Task ShowErrorAsync(LauncherInfoDialogRequest request, Window? owner = null)
        {
            LastErrorRequest = request;
            return Task.CompletedTask;
        }

        public Task<bool> ShowWarningConfirmationAsync(
            LauncherInfoDialogRequest request,
            string? continueText = null,
            Window? owner = null)
        {
            return Task.FromResult(true);
        }

        public Task<string?> ShowModificationSelectionAsync(
            IReadOnlyList<string> modificationNames,
            Window? owner = null)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<ManualModificationDialogResult?> ShowManualModificationImportAsync(
            ManualModificationDialogRequest request,
            Window? owner = null)
        {
            return Task.FromResult<ManualModificationDialogResult?>(null);
        }

        public Task<bool> ShowIntegrityReviewAsync(
            ContentIntegrityReport report,
            Window? owner = null)
        {
            return Task.FromResult(false);
        }
    }
}
