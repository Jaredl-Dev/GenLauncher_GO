using System;
using CommunityToolkit.Mvvm.Input;
using GenLauncherGO.UI.Features.Dialogs.Models;

namespace GenLauncherGO.UI.Features.Mods.ViewModels;

internal sealed class InfoDialogViewModel
{
    public InfoDialogViewModel(
        LauncherInfoDialogRequest request,
        InfoDialogKind kind,
        string? continueText = null,
        string? cancelText = null,
        string? actionText = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        MainMessage = request.MainMessage;
        DetailMessage = request.DetailMessage;
        DetailFontSize = request.DetailFontSize ?? 15D;
        ContinueText = string.IsNullOrWhiteSpace(continueText) ? null : continueText;
        CancelText = string.IsNullOrWhiteSpace(cancelText) ? "Cancel" : cancelText;
        ActionText = string.IsNullOrWhiteSpace(actionText) ? null : actionText;
        IsInfoAction = kind == InfoDialogKind.InfoAction;
        OkCommand = new RelayCommand(Accept);
        CancelCommand = new RelayCommand(Cancel);
        ContinueCommand = OkCommand;
        ActionCommand = new RelayCommand(CompleteAction);
        CloseCommand = new RelayCommand(Close);

        IsWarningConfirmation = kind == InfoDialogKind.WarningConfirmation;
        IsOkVisible = kind is InfoDialogKind.Info or InfoDialogKind.Error;
        IsActionVisible = IsInfoAction && ActionText != null;
        IsContinueVisible = kind == InfoDialogKind.WarningConfirmation;
        IsCancelVisible = kind == InfoDialogKind.WarningConfirmation;
        IsInfoIconVisible = kind is InfoDialogKind.Info or InfoDialogKind.InfoAction;
        IsWarningIconVisible = kind == InfoDialogKind.WarningConfirmation;
        IsErrorIconVisible = kind == InfoDialogKind.Error;
    }

    public string MainMessage { get; }

    public string DetailMessage { get; }

    public double DetailFontSize { get; }

    public string? ContinueText { get; }

    public string CancelText { get; }

    public string? ActionText { get; }

    public bool IsOkVisible { get; }

    public bool IsContinueVisible { get; }

    public bool IsActionVisible { get; }

    public bool IsCancelVisible { get; }

    public bool IsInfoIconVisible { get; }

    public bool IsWarningIconVisible { get; }

    public bool IsErrorIconVisible { get; }

    public IRelayCommand OkCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand ContinueCommand { get; }

    public IRelayCommand ActionCommand { get; }

    public IRelayCommand CloseCommand { get; }

    /// <summary>
    ///     Gets the result requested by the dialog command.
    /// </summary>
    public bool? DialogResult { get; private set; }

    private bool IsWarningConfirmation { get; }

    private bool IsInfoAction { get; }

    /// <summary>
    ///     Occurs when the view model requests that the owning dialog close.
    /// </summary>
    public event EventHandler? CloseRequested;

    private void Accept()
    {
        CompleteDialog(!IsInfoAction);
    }

    private void CompleteAction()
    {
        CompleteDialog(true);
    }

    private void Cancel()
    {
        CompleteDialog(false);
    }

    private void CompleteDialog(bool result)
    {
        DialogResult = result;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Close()
    {
        if (IsWarningConfirmation || IsInfoAction)
        {
            Cancel();
            return;
        }

        Accept();
    }
}
