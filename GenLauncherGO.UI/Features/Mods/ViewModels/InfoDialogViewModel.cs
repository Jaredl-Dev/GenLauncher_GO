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
        string? cancelText = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        MainMessage = request.MainMessage;
        DetailMessage = request.DetailMessage;
        DetailFontSize = request.DetailFontSize ?? 15D;
        ContinueText = string.IsNullOrWhiteSpace(continueText) ? null : continueText;
        CancelText = string.IsNullOrWhiteSpace(cancelText) ? "Cancel" : cancelText;
        OkCommand = new RelayCommand(Accept);
        CancelCommand = new RelayCommand(Cancel);
        ContinueCommand = OkCommand;
        CloseCommand = new RelayCommand(Close);

        IsWarningConfirmation = kind == InfoDialogKind.WarningConfirmation;
        IsOkVisible = kind != InfoDialogKind.WarningConfirmation;
        IsContinueVisible = kind == InfoDialogKind.WarningConfirmation;
        IsCancelVisible = kind == InfoDialogKind.WarningConfirmation;
        IsInfoIconVisible = kind == InfoDialogKind.Info;
        IsWarningIconVisible = kind == InfoDialogKind.WarningConfirmation;
        IsErrorIconVisible = kind == InfoDialogKind.Error;
    }

    /// <summary>
    /// Occurs when the view model requests that the owning dialog close.
    /// </summary>
    public event EventHandler? CloseRequested;

    public string MainMessage { get; }

    public string DetailMessage { get; }

    public double DetailFontSize { get; }

    public string? ContinueText { get; }

    public string CancelText { get; }

    public bool IsOkVisible { get; }

    public bool IsContinueVisible { get; }

    public bool IsCancelVisible { get; }

    public bool IsInfoIconVisible { get; }

    public bool IsWarningIconVisible { get; }

    public bool IsErrorIconVisible { get; }

    public IRelayCommand OkCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand ContinueCommand { get; }

    public IRelayCommand CloseCommand { get; }

    /// <summary>
    /// Gets the result requested by the dialog command.
    /// </summary>
    public bool? DialogResult { get; private set; }

    private bool IsWarningConfirmation { get; }

    private void Accept()
    {
        DialogResult = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel()
    {
        DialogResult = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Close()
    {
        if (IsWarningConfirmation)
        {
            Cancel();
            return;
        }

        Accept();
    }
}
