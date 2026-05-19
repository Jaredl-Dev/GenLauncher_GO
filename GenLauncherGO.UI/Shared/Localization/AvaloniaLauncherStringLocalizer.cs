using System.Globalization;
using GenLauncherGO.UI.Resources;

namespace GenLauncherGO.UI.Shared.Localization;

internal sealed class AvaloniaLauncherStringLocalizer : ILauncherStringLocalizer
{
    /// <summary>
    ///     Resolves a localized launcher string or a diagnostic placeholder when its key is missing.
    /// </summary>
    public string this[string key]
    {
        get
        {
            string? result = Strings.ResourceManager.GetString(
                key,
                CultureInfo.CurrentUICulture);
            return result ?? key;
        }
    }
}
