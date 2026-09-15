using System;
using System.Globalization;
using GenLauncherGO.Resources;

namespace GenLauncherGO.Shared.Localization;

/// <summary>
///     Resolves an embedded launcher string for Avalonia markup.
/// </summary>
internal sealed class LocExtension
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
