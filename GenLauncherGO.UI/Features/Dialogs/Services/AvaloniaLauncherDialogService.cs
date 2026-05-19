using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Updating.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Integrity.ViewModels;
using GenLauncherGO.UI.Features.Mods.ViewModels;
using GenLauncherGO.UI.Features.Mods.Views;
using GenLauncherGO.UI.Shared.Dialogs;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.UI.Features.Dialogs.Services;

internal sealed class AvaloniaLauncherDialogService : ILauncherDialogService
{
    private readonly ILogger<AddModificationViewModel> _addModificationLogger;

    private readonly ILauncherContentCatalog _catalog;

    private readonly ILogger<AvaloniaLauncherDialogService> _logger;

    private readonly IRemotePackageSizeResolver _packageSizeResolver;

    private readonly ILauncherStringLocalizer _stringLocalizer;

    public AvaloniaLauncherDialogService(
        ILauncherStringLocalizer stringLocalizer,
        ILauncherContentCatalog catalog,
        IRemotePackageSizeResolver packageSizeResolver,
        ILogger<AvaloniaLauncherDialogService>? logger = null,
        ILogger<AddModificationViewModel>? addModificationLogger = null)
    {
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _packageSizeResolver = packageSizeResolver ?? throw new ArgumentNullException(nameof(packageSizeResolver));
        _logger = logger ?? NullLogger<AvaloniaLauncherDialogService>.Instance;
        _addModificationLogger = addModificationLogger ?? NullLogger<AddModificationViewModel>.Instance;
    }

    public Task ShowInfoAsync(LauncherInfoDialogRequest request, Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug("Showing launcher info dialog {DialogTitle}.", request.MainMessage);
        return ShowInfoDialogAsync(request, InfoDialogKind.Info, owner);
    }

    public Task<bool> ShowInfoActionAsync(
        LauncherInfoDialogRequest request,
        string actionText,
        Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionText);

        _logger.LogDebug("Showing launcher info action dialog {DialogTitle}.", request.MainMessage);
        return ShowInfoDialogAsync(
            request,
            InfoDialogKind.InfoAction,
            owner,
            actionText: actionText);
    }

    public Task ShowErrorAsync(LauncherInfoDialogRequest request, Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogWarning("Showing launcher error dialog {DialogTitle}.", request.MainMessage);
        return ShowInfoDialogAsync(request, InfoDialogKind.Error, owner);
    }

    public async Task<bool> ShowWarningConfirmationAsync(
        LauncherInfoDialogRequest request,
        string? continueText = null,
        Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool confirmed = await ShowInfoDialogAsync(
            request,
            InfoDialogKind.WarningConfirmation,
            owner,
            continueText ?? _stringLocalizer["Continue"]);
        _logger.LogInformation(
            "Launcher warning confirmation {DialogTitle} completed with confirmed: {Confirmed}.",
            request.MainMessage,
            confirmed);
        return confirmed;
    }

    public async Task<string?> ShowModificationSelectionAsync(
        IReadOnlyList<string> modificationNames,
        Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(modificationNames);

        using AddModificationViewModel viewModel = new(
            modificationNames,
            _catalog,
            _packageSizeResolver,
            _stringLocalizer,
            _addModificationLogger);
        AddModificationWindow dialog = new(viewModel);

        Task metadataLoadingTask = viewModel.LoadMetadataAsync();
        bool accepted;
        try
        {
            accepted = await AvaloniaDialog.ShowAsync(
                dialog,
                owner,
                () => dialog.ViewModel.DialogResult == true);
        }
        finally
        {
            viewModel.CancelMetadataLoading();
            await metadataLoadingTask;
        }

        if (!accepted)
        {
            _logger.LogDebug(
                "Repository modification selection dialog was canceled. Available options: {ModificationCount}.",
                modificationNames.Count);
            return null;
        }

        _logger.LogInformation(
            "Repository modification selection dialog selected {ModificationName}.",
            dialog.SelectedModificationName);
        return dialog.SelectedModificationName;
    }

    public async Task<ManualModificationDialogResult?> ShowManualModificationImportAsync(
        IReadOnlyList<string> files,
        Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(files);

        ManualAddModificationWindow dialog = new(
            new ManualAddModificationViewModel(
                new List<string>(files),
                _stringLocalizer,
                this));

        if (!await AvaloniaDialog.ShowAsync(
                dialog,
                owner,
                () => dialog.ViewModel.DialogResult == true))
        {
            _logger.LogDebug("Manual modification import dialog was canceled.");
            return null;
        }

        _logger.LogInformation("Manual modification import dialog produced an import request.");
        return dialog.ImportResult;
    }

    public async Task<bool> ShowIntegrityReviewAsync(
        ContentIntegrityReport report,
        Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        IntegrityReviewDialog dialog = new(new IntegrityReviewViewModel(report, _stringLocalizer));
        bool confirmed = await AvaloniaDialog.ShowAsync(
            dialog,
            owner,
            () => dialog.ResolutionConfirmed);
        _logger.LogInformation(
            "Integrity review dialog completed with confirmed: {Confirmed}; issues: {IssueCount}.",
            confirmed,
            report.Issues.Count);
        return confirmed;
    }

    private Task<bool> ShowInfoDialogAsync(
        LauncherInfoDialogRequest request,
        InfoDialogKind kind,
        Window? owner,
        string? continueText = null,
        string? actionText = null)
    {
        InfoWindow dialog = new(
            request,
            kind,
            continueText,
            request.CancelText ?? _stringLocalizer["Cancel"],
            actionText);
        return AvaloniaDialog.ShowAsync(dialog, owner, () => dialog.Accepted);
    }
}
