using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Updating.Contracts;
using GenLauncherGO.UI.Shared.Formatting;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.UI.Features.Mods.ViewModels;

/// <summary>
///     Provides searchable selection state and cancellable remote metadata loading for repository modifications.
/// </summary>
internal sealed class AddModificationViewModel : ObservableObject, IDisposable
{
    private const int MaxConcurrentVersionRequests = 6;

    private const int MaxConcurrentPackageSizeRequests = 6;

    private readonly IReadOnlyList<AddModificationItemViewModel> _allModifications;

    private readonly ILauncherContentCatalog _catalog;

    private readonly ILogger<AddModificationViewModel> _logger;

    private readonly CancellationTokenSource _metadataCancellation = new();

    private readonly IRemotePackageSizeResolver _packageSizeResolver;

    private readonly string _packageSizeUnavailableText;

    private bool _metadataLoadingStarted;
    private AddModificationItemViewModel? _selectedModification;

    public AddModificationViewModel(
        IReadOnlyList<string> modificationNames,
        ILauncherContentCatalog catalog,
        IRemotePackageSizeResolver packageSizeResolver,
        ILauncherStringLocalizer stringLocalizer,
        ILogger<AddModificationViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(modificationNames);
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _packageSizeResolver = packageSizeResolver ?? throw new ArgumentNullException(nameof(packageSizeResolver));
        ArgumentNullException.ThrowIfNull(stringLocalizer);
        _logger = logger ?? NullLogger<AddModificationViewModel>.Instance;
        _packageSizeUnavailableText = stringLocalizer["PackageSizeUnavailable"];

        _allModifications = modificationNames
            .Select(name => new AddModificationItemViewModel(
                name,
                stringLocalizer["CalculatingPackageSize"]))
            .ToList();
        VisibleModifications = new ObservableCollection<AddModificationItemViewModel>(_allModifications);
        _selectedModification = VisibleModifications.FirstOrDefault();
        AcceptCommand = new RelayCommand(AcceptSelection, () => CanAccept);
        CancelCommand = new RelayCommand(Cancel);
    }

    public ObservableCollection<AddModificationItemViewModel> VisibleModifications { get; }

    public string SearchText
    {
        get;
        set
        {
            string newValue = value ?? string.Empty;
            if (string.Equals(field, newValue, StringComparison.Ordinal))
            {
                return;
            }

            field = newValue;
            OnPropertyChanged();
            ApplyFilter();
        }
    } = string.Empty;

    public AddModificationItemViewModel? SelectedModification
    {
        get => _selectedModification;
        set
        {
            if (ReferenceEquals(_selectedModification, value))
            {
                return;
            }

            _selectedModification = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedModificationName));
            OnPropertyChanged(nameof(CanAccept));
            AcceptCommand.NotifyCanExecuteChanged();
        }
    }

    public string? SelectedModificationName => SelectedModification?.Name;

    public bool HasNoVisibleModifications => VisibleModifications.Count == 0;

    public bool CanAccept =>
        SelectedModification != null && VisibleModifications.Contains(SelectedModification);

    public IRelayCommand AcceptCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public bool? DialogResult { get; private set; }

    public void Dispose()
    {
        _metadataCancellation.Dispose();
    }

    /// <summary>
    ///     Occurs when the view model requests that the owning dialog close.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    ///     Loads version and package-size metadata with bounded concurrency until complete or canceled by dialog closure.
    /// </summary>
    public async Task LoadMetadataAsync()
    {
        if (_metadataLoadingStarted)
        {
            return;
        }

        _metadataLoadingStarted = true;
        using var versionGate = new SemaphoreSlim(MaxConcurrentVersionRequests);
        using var packageSizeGate = new SemaphoreSlim(MaxConcurrentPackageSizeRequests);
        try
        {
            await Task.WhenAll(_allModifications.Select(item => LoadMetadataAsync(
                item,
                versionGate,
                packageSizeGate,
                _metadataCancellation.Token)));
        }
        catch (OperationCanceledException) when (_metadataCancellation.IsCancellationRequested)
        {
            _logger.LogDebug("Canceled add-modification dialog metadata loading.");
        }
    }

    /// <summary>
    ///     Cancels remote metadata work when the dialog lifetime ends.
    /// </summary>
    public void CancelMetadataLoading()
    {
        if (!_metadataCancellation.IsCancellationRequested)
        {
            _metadataCancellation.Cancel();
        }
    }

    private async Task LoadMetadataAsync(
        AddModificationItemViewModel item,
        SemaphoreSlim versionGate,
        SemaphoreSlim packageSizeGate,
        CancellationToken cancellationToken)
    {
        LauncherContentVersion version;
        await versionGate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                version = await _catalog.GetRepositoryModificationMetadataAsync(
                    item.Name,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "Failed to load repository metadata for modification {ModificationName}; failure type: {FailureType}.",
                    item.Name,
                    exception.GetType().Name);
                item.SetMetadataUnavailable(_packageSizeUnavailableText);
                return;
            }
        }
        finally
        {
            versionGate.Release();
        }

        item.SetMetadata(version.Version, item.PackageSizeText);
        await packageSizeGate.WaitAsync(cancellationToken);
        try
        {
            long? totalBytes = await _packageSizeResolver.GetTotalBytesAsync(version, cancellationToken);
            item.SetPackageSize(totalBytes.HasValue
                ? ByteSizeFormatter.Format(totalBytes.Value)
                : _packageSizeUnavailableText);
        }
        finally
        {
            packageSizeGate.Release();
        }
    }

    private void ApplyFilter()
    {
        AddModificationItemViewModel? previousSelection = SelectedModification;
        CompareInfo comparer = CultureInfo.CurrentCulture.CompareInfo;
        var matchingItems = _allModifications
            .Where(item => string.IsNullOrWhiteSpace(SearchText) ||
                           comparer.IndexOf(item.Name, SearchText, CompareOptions.IgnoreCase) >= 0)
            .ToList();

        VisibleModifications.Clear();
        foreach (AddModificationItemViewModel item in matchingItems)
        {
            VisibleModifications.Add(item);
        }

        SelectedModification = previousSelection != null && VisibleModifications.Contains(previousSelection)
            ? previousSelection
            : VisibleModifications.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoVisibleModifications));
        OnPropertyChanged(nameof(CanAccept));
        AcceptCommand.NotifyCanExecuteChanged();
    }

    private void AcceptSelection()
    {
        if (!CanAccept)
        {
            return;
        }

        CompleteDialog(true);
    }

    private void Cancel()
    {
        CompleteDialog(false);
    }

    private void CompleteDialog(bool accepted)
    {
        DialogResult = accepted;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
