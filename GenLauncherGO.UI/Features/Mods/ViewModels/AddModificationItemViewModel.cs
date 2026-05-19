using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenLauncherGO.UI.Features.Mods.ViewModels;

/// <summary>
///     Presents one remotely available modification and its asynchronously resolved package metadata.
/// </summary>
internal sealed class AddModificationItemViewModel : ObservableObject
{
    private string _packageSizeText;

    public AddModificationItemViewModel(string name, string calculatingPackageSizeText)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _packageSizeText = calculatingPackageSizeText ??
                           throw new ArgumentNullException(nameof(calculatingPackageSizeText));
    }

    public string Name { get; }

    public string VersionText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "\u2026";

    public string PackageSizeText
    {
        get => _packageSizeText;
        private set => SetProperty(ref _packageSizeText, value);
    }

    public void SetMetadata(string versionText, string packageSizeText)
    {
        VersionText = string.IsNullOrWhiteSpace(versionText) ? "\u2014" : versionText;
        PackageSizeText = packageSizeText;
    }

    public void SetMetadataUnavailable(string packageSizeUnavailableText)
    {
        VersionText = "\u2014";
        PackageSizeText = packageSizeUnavailableText;
    }

    public void SetPackageSize(string packageSizeText)
    {
        PackageSizeText = packageSizeText;
    }
}
