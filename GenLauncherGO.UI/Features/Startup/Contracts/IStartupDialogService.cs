using System.Threading.Tasks;

namespace GenLauncherGO.UI.Features.Startup.Contracts;

/// <summary>
///     Shows startup messages before the main launcher dialog service is available.
/// </summary>
/// <remarks>
///     These dialogs carry no palette of their own. They read the application-scoped launcher theme, which is seeded
///     at startup and replaced whenever the active game changes.
/// </remarks>
internal interface IStartupDialogService
{
    Task ShowMessageAsync(string message);

    Task ShowMessageAsync(string title, string message);

    /// <summary>
    ///     Shows a warning with retry and cancel options.
    /// </summary>
    /// <returns><see langword="true" /> when the user chooses retry.</returns>
    Task<bool> ShowRetryCancelWarningAsync(string title, string message);
}
