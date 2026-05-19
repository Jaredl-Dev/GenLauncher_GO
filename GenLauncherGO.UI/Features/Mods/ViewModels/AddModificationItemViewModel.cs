using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenLauncherGO.UI.Features.Mods.ViewModels;

/// <summary>
/// Presents one remotely available modification and its asynchronously resolved package metadata.
/// </summary>
internal sealed class AddModificationItemViewModel : ObservableObject
{
    private string _versionText = "\u2026";

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
        get => _versionText;
        private set
        {
            SetProperty(ref _versionText, value);
        }
    }

    public string PackageSizeText
    {
        get => _packageSizeText;
        private set
        {
            SetProperty(ref _packageSizeText, value);
        }
    }

    public void SetMetadata(string versionText, string packageSizeText)
    {
        VersionText = String.IsNullOrWhiteSpace(versionText) ? "\u2014" : versionText;
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
