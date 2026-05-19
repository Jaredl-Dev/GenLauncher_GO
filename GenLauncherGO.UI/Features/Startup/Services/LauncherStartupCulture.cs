using System.Globalization;

namespace GenLauncherGO.UI.Features.Startup.Services;

/// <summary>
///     Applies the persisted launcher language before Avalonia views are composed.
/// </summary>
internal static class LauncherStartupCulture
{
    /// <summary>
    ///     Applies the persisted language preference to the current process.
    /// </summary>
    /// <param name="useEnglishLanguage">
    ///     <see langword="true" /> to force English; otherwise, to use the installed Windows UI culture.
    /// </param>
    public static void Apply(bool useEnglishLanguage)
    {
        CultureInfo culture = useEnglishLanguage
            ? CultureInfo.GetCultureInfo("en-US")
            : CultureInfo.InstalledUICulture;

        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
