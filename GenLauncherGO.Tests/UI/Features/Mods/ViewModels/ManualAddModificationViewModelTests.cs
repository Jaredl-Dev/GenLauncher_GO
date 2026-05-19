using System;
using System.Collections.Generic;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Mods.ViewModels;

namespace GenLauncherGO.Tests.UI.Features.Mods.ViewModels;

public sealed class ManualAddModificationViewModelTests
{
    [Theory]
    [InlineData("", "1.0", "Enter a modification name", "")]
    [InlineData("ShockWave", "", "", "Enter a version")]
    [InlineData("ShockWave", "release", "", "Version must contain numbers")]
    [InlineData("!!!", "1.0", "Name and version must contain supported symbols", "")]
    public void AcceptCommand_WithInvalidInput_ShowsLocalizedErrorWithoutClosing(
        string modificationName,
        string version,
        string expectedNameValidationMessage,
        string expectedVersionValidationMessage)
    {
        RecordingLauncherDialogService dialogService = new();
        ManualAddModificationViewModel viewModel = new(
            new[] { @"C:\Temp\mod.zip" },
            CreateStringLocalizer(),
            dialogService)
        {
            ModificationName = modificationName,
            Version = version
        };
        bool closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        viewModel.AcceptCommand.Execute(null);

        string expectedErrorMessage = string.IsNullOrEmpty(expectedNameValidationMessage)
            ? expectedVersionValidationMessage
            : expectedNameValidationMessage;
        viewModel.AcceptCommand.CanExecute(null).Should().BeFalse();
        viewModel.ModificationNameValidationMessage.Should().Be(expectedNameValidationMessage);
        viewModel.VersionValidationMessage.Should().Be(expectedVersionValidationMessage);
        closeRequested.Should().BeFalse();
        LauncherInfoDialogRequest errorRequest = dialogService.ErrorRequests.Should().ContainSingle().Which;
        errorRequest.MainMessage.Should().Be("Operation aborted");
        errorRequest.DetailMessage.Should().Be(expectedErrorMessage);
        viewModel.DialogResult.Should().BeNull();
    }

    [Fact]
    public void AcceptCommand_WithValidInput_CreatesImportResultAndCloses()
    {
        ManualAddModificationViewModel viewModel = new(
            new[] { @"C:\Temp\mod.zip" },
            CreateStringLocalizer(),
            new RecordingLauncherDialogService())
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
        viewModel.ImportResult!.ModificationName.Should().Be("Patch Pack");
        viewModel.ImportResult.Version.Should().Be("1.2");
    }

    [Fact]
    public void CancelCommand_RequestsCanceledClose()
    {
        ManualAddModificationViewModel viewModel = new(
            new[] { @"C:\Temp\mod.zip" },
            CreateStringLocalizer(),
            new RecordingLauncherDialogService());
        bool closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        viewModel.CancelCommand.Execute(null);

        closeRequested.Should().BeTrue();
        viewModel.DialogResult.Should().BeFalse();
        viewModel.ImportResult.Should().BeNull();
    }

    [Theory]
    [InlineData(@"C:\Temp\ShockWave-1.2.zip", "ShockWave-1.2", "")]
    [InlineData(@"C:\Temp\[RotR] v1.87!!.zip", "RotR  v1.87", "")]
    [InlineData(@"C:\Temp\###.zip", "", "Enter a modification name")]
    public void Constructor_InfersModificationNameFromFirstSelectedFile(
        string firstFile,
        string expectedModificationName,
        string expectedNameValidationMessage)
    {
        ManualAddModificationViewModel viewModel = new(
            new[] { firstFile, @"C:\Temp\ignored.big" },
            CreateStringLocalizer(),
            new RecordingLauncherDialogService());

        viewModel.ModificationName.Should().Be(expectedModificationName);
        viewModel.ModificationNameValidationMessage.Should().Be(expectedNameValidationMessage);
        viewModel.AcceptCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Fields_UpdateValidationStateImmediately()
    {
        ManualAddModificationViewModel viewModel = new(
            new[] { @"C:\Temp\mod.zip" },
            CreateStringLocalizer(),
            new RecordingLauncherDialogService());

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

    [Theory]
    [InlineData((int)InfoDialogKind.Info, DialogElement.Ok | DialogElement.InfoIcon)]
    [InlineData((int)InfoDialogKind.Error, DialogElement.Ok | DialogElement.ErrorIcon)]
    [InlineData((int)InfoDialogKind.InfoAction, DialogElement.Action | DialogElement.InfoIcon)]
    [InlineData(
        (int)InfoDialogKind.WarningConfirmation,
        DialogElement.Continue | DialogElement.Cancel | DialogElement.WarningIcon)]
    public void InfoDialogConstructor_ConfiguresExpectedState(
        int kindValue,
        DialogElement visibleElements)
    {
        var kind = (InfoDialogKind)kindValue;
        double? requestedFontSize = kind == InfoDialogKind.Error ? 12D : null;

        InfoDialogViewModel viewModel = new(
            new LauncherInfoDialogRequest(kind.ToString(), "Details", requestedFontSize),
            kind,
            continueText: "Continue anyway",
            actionText: "Visit download page");

        viewModel.MainMessage.Should().Be(kind.ToString());
        viewModel.DetailMessage.Should().Be("Details");
        viewModel.DetailFontSize.Should().Be(requestedFontSize ?? 15D);
        viewModel.ContinueText.Should().Be("Continue anyway");
        viewModel.ActionText.Should().Be("Visit download page");
        viewModel.IsOkVisible.Should().Be(visibleElements.HasFlag(DialogElement.Ok));
        viewModel.IsContinueVisible.Should().Be(visibleElements.HasFlag(DialogElement.Continue));
        viewModel.IsCancelVisible.Should().Be(visibleElements.HasFlag(DialogElement.Cancel));
        viewModel.IsActionVisible.Should().Be(visibleElements.HasFlag(DialogElement.Action));
        viewModel.IsInfoIconVisible.Should().Be(visibleElements.HasFlag(DialogElement.InfoIcon));
        viewModel.IsWarningIconVisible.Should().Be(visibleElements.HasFlag(DialogElement.WarningIcon));
        viewModel.IsErrorIconVisible.Should().Be(visibleElements.HasFlag(DialogElement.ErrorIcon));
    }

    [Theory]
    [InlineData((int)InfoDialogKind.InfoAction, DialogCommand.Action, null, null, true)]
    [InlineData((int)InfoDialogKind.InfoAction, DialogCommand.Ok, null, null, false)]
    [InlineData((int)InfoDialogKind.Info, DialogCommand.Ok, null, null, true)]
    [InlineData((int)InfoDialogKind.Info, DialogCommand.Close, null, null, true)]
    [InlineData(
        (int)InfoDialogKind.WarningConfirmation,
        DialogCommand.Cancel,
        "Continue anyway",
        "Continue anyway",
        false)]
    [InlineData(
        (int)InfoDialogKind.WarningConfirmation,
        DialogCommand.Continue,
        " ",
        null,
        true)]
    [InlineData(
        (int)InfoDialogKind.WarningConfirmation,
        DialogCommand.Close,
        null,
        null,
        false)]
    public void InfoDialogCommands_ReturnExpectedResultAndRequestClose(
        int kindValue,
        DialogCommand command,
        string? continueText,
        string? expectedContinueText,
        bool expectedResult)
    {
        var kind = (InfoDialogKind)kindValue;

        InfoDialogViewModel viewModel = new(
            new LauncherInfoDialogRequest(kind.ToString(), "Details"),
            kind,
            continueText,
            actionText: "Visit download page");
        bool closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        ExecuteDialogCommand(viewModel, command);

        closeRequested.Should().BeTrue();
        viewModel.DialogResult.Should().Be(expectedResult);
        viewModel.ContinueText.Should().Be(expectedContinueText);
    }

    private static FakeStringLocalizer CreateStringLocalizer()
    {
        return new FakeStringLocalizer(new Dictionary<string, string>
        {
            ["EnterModName"] = "Enter a modification name",
            ["EnterModVersion"] = "Enter a version",
            ["NameAndVersionValidSymbols"] = "Name and version must contain supported symbols",
            ["OperationAborted"] = "Operation aborted",
            ["VersionMustContainNumbers"] = "Version must contain numbers"
        });
    }

    private static void ExecuteDialogCommand(InfoDialogViewModel viewModel, DialogCommand command)
    {
        switch (command)
        {
            case DialogCommand.Ok:
                viewModel.OkCommand.Execute(null);
                break;
            case DialogCommand.Cancel:
                viewModel.CancelCommand.Execute(null);
                break;
            case DialogCommand.Continue:
                viewModel.ContinueCommand.Execute(null);
                break;
            case DialogCommand.Action:
                viewModel.ActionCommand.Execute(null);
                break;
            case DialogCommand.Close:
                viewModel.CloseCommand.Execute(null);
                break;
        }
    }
}

/// <summary>
///     The dialog parts a single <see cref="InfoDialogKind" /> is expected to show.
/// </summary>
[Flags]
public enum DialogElement
{
    Ok = 1,
    Continue = 2,
    Cancel = 4,
    Action = 8,
    InfoIcon = 16,
    WarningIcon = 32,
    ErrorIcon = 64
}

/// <summary>
///     The command a test drives on an <see cref="InfoDialogViewModel" />.
/// </summary>
public enum DialogCommand
{
    Ok,
    Cancel,
    Continue,
    Action,
    Close
}
