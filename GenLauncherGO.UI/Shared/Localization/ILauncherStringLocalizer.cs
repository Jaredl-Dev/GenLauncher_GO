namespace GenLauncherGO.UI.Shared.Localization;

/// <summary>
/// Resolves localized launcher text by resource key.
/// </summary>
internal interface ILauncherStringLocalizer
{
    string this[string key] { get; }
}
