using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.UI.Features.Mods.ViewModels;

/// <summary>
/// Provides bindable manual import fields, validation, and actions for launcher content.
/// </summary>
internal sealed class ManualAddModificationViewModel : ObservableObject
{
    private const string MissingModificationNameKey = "EnterModName";

    private const string MissingVersionKey = "EnterModVersion";

    private const string UnsupportedCharactersKey = "NameAndVersionValidSymbols";

    private const string VersionMissingNumberKey = "VersionMustContainNumbers";

    private readonly IReadOnlyList<string> _files;

    private readonly string? _parentContentName;

    private readonly ILauncherStringLocalizer _stringLocalizer;

    private readonly ILauncherDialogService _dialogService;

    private string _modificationName;

    private string _version = string.Empty;

    public ManualAddModificationViewModel(
        IReadOnlyList<string> files,
        string? parentContentName,
        ILauncherStringLocalizer stringLocalizer,
        ILauncherDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(files);

        _files = files.ToList();
        _parentContentName = parentContentName;
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _modificationName = InferModificationName(_files);
        AcceptCommand = new AsyncRelayCommand(
            AcceptAsync,
            () => CanAccept,
            AsyncRelayCommandOptions.AllowConcurrentExecutions);
        CancelCommand = new RelayCommand(Cancel);
    }

    /// <summary>
    /// Occurs when the view model requests that the owning dialog close.
    /// </summary>
    public event EventHandler? CloseRequested;

    public string ModificationName
    {
        get => _modificationName;
        set
        {
            string newValue = value ?? string.Empty;
            if (String.Equals(_modificationName, newValue, StringComparison.Ordinal))
            {
                return;
            }

            _modificationName = newValue;
            NotifyInputChanged(nameof(ModificationName), nameof(ModificationNameValidationMessage));
        }
    }

    public string Version
    {
        get => _version;
        set
        {
            string newValue = value ?? string.Empty;
            if (String.Equals(_version, newValue, StringComparison.Ordinal))
            {
                return;
            }

            _version = newValue;
            NotifyInputChanged(nameof(Version), nameof(VersionValidationMessage));
        }
    }

    public string ModificationNameValidationMessage =>
        GetLocalizedValidationMessage(GetModificationNameValidationKey(ModificationName));

    public string VersionValidationMessage => GetLocalizedValidationMessage(GetVersionValidationKey(Version));

    public bool CanAccept =>
        GetModificationNameValidationKey(ModificationName) == null &&
        GetVersionValidationKey(Version) == null;

    public IAsyncRelayCommand AcceptCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public ManualModificationDialogResult? ImportResult { get; private set; }

    public bool? DialogResult { get; private set; }

    private async Task AcceptAsync()
    {
        string? validationKey = GetFirstValidationKey();
        if (validationKey != null)
        {
            await ShowValidationErrorAsync(validationKey);
            return;
        }

        ImportResult = new ManualModificationDialogResult(
            _files,
            _parentContentName,
            ModificationName,
            Version);
        DialogResult = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel()
    {
        DialogResult = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static string InferModificationName(IReadOnlyList<string> files)
    {
        string? firstFile = files.FirstOrDefault(file => !string.IsNullOrWhiteSpace(file));
        if (firstFile == null)
        {
            return string.Empty;
        }

        string fileName = Path.GetFileNameWithoutExtension(firstFile);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        return NormalizeInferredName(fileName);
    }

    private static string NormalizeInferredName(string fileName)
    {
        StringBuilder builder = new(fileName.Length);
        bool previousWasSpace = false;

        foreach (char character in fileName)
        {
            if (IsSupportedFieldCharacter(character))
            {
                builder.Append(character);
                previousWasSpace = char.IsWhiteSpace(character);
                continue;
            }

            if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private string? GetFirstValidationKey()
    {
        return GetModificationNameValidationKey(ModificationName) ?? GetVersionValidationKey(Version);
    }

    private static string? GetModificationNameValidationKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return MissingModificationNameKey;
        }

        return ContainsOnlySupportedFieldCharacters(value)
            ? null
            : UnsupportedCharactersKey;
    }

    private static string? GetVersionValidationKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return MissingVersionKey;
        }

        if (!value.Any(character => character >= '0' && character <= '9'))
        {
            return VersionMissingNumberKey;
        }

        return ContainsOnlySupportedFieldCharacters(value)
            ? null
            : UnsupportedCharactersKey;
    }

    private string GetLocalizedValidationMessage(string? validationKey)
    {
        return validationKey == null ? string.Empty : _stringLocalizer[validationKey];
    }

    private static bool ContainsOnlySupportedFieldCharacters(string value)
    {
        return value.All(IsSupportedFieldCharacter);
    }

    private static bool IsSupportedFieldCharacter(char character)
    {
        return char.IsLetterOrDigit(character) ||
               character == '_' ||
               character == '.' ||
               character == '@' ||
               character == '-' ||
               character == ' ';
    }

    private Task ShowValidationErrorAsync(string detailKey)
    {
        return _dialogService.ShowErrorAsync(new LauncherInfoDialogRequest(
            _stringLocalizer["OperationAborted"],
            _stringLocalizer[detailKey]));
    }

    private void NotifyInputChanged(string fieldPropertyName, string validationPropertyName)
    {
        OnPropertyChanged(fieldPropertyName);
        OnPropertyChanged(validationPropertyName);
        OnPropertyChanged(nameof(CanAccept));
        AcceptCommand.NotifyCanExecuteChanged();
    }

}
