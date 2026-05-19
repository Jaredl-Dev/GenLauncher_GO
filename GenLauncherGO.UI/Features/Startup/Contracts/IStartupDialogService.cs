using GenLauncherGO.UI.Shared.Themes;
using System.Threading.Tasks;

namespace GenLauncherGO.UI.Features.Startup.Contracts;

/// <summary>
/// Shows startup messages before the main launcher dialog service is available.
/// </summary>
internal interface IStartupDialogService
{
    Task ShowMessageAsync(string message);

    Task ShowThemedMessageAsync(string title, string message, ColorsInfo colors);

    /// <summary>
    /// Shows a warning with retry and cancel options.
    /// </summary>
    /// <returns><see langword="true"/> when the user chooses retry.</returns>
    Task<bool> ShowRetryCancelWarningAsync(string title, string message);
}
