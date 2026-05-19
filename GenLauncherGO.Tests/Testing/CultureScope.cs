using System;
using System.Globalization;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Sets the culture for one test and restores what was there before.
/// </summary>
/// <remarks>
///     The thread defaults are process-wide, so a test that changes them without restoring them changes the result of
///     every test that runs afterwards in the same process.
/// </remarks>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo? _previousDefaultThreadCulture = CultureInfo.DefaultThreadCurrentCulture;
    private readonly CultureInfo? _previousDefaultThreadUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
    private readonly CultureInfo _previousUiCulture = CultureInfo.CurrentUICulture;

    public CultureScope(string? cultureName = null, string? uiCultureName = null)
    {
        if (cultureName is not null)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        }

        if (uiCultureName is not null)
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(uiCultureName);
        }
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _previousCulture;
        CultureInfo.CurrentUICulture = _previousUiCulture;
        CultureInfo.DefaultThreadCurrentCulture = _previousDefaultThreadCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _previousDefaultThreadUiCulture;
    }
}
