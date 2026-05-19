using System;
using System.Globalization;
using GenLauncherGO.UI.Resources;

namespace GenLauncherGO.UI.Shared.Localization;

/// <summary>
///     Resolves an embedded launcher string for Avalonia markup.
/// </summary>
public sealed class LocExtension
{
    private readonly string _key;

    public LocExtension(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _key = key;
    }

    /// <summary>
    ///     Resolves the localized value when Avalonia loads the markup.
    /// </summary>
    public string ProvideValue()
    {
        return Strings.ResourceManager.GetString(
                   _key,
                   CultureInfo.CurrentUICulture) ??
               _key;
    }
}
