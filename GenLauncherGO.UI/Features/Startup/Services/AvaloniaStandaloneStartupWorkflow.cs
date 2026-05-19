using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Startup.Contracts;
using GenLauncherGO.UI.Features.Startup.Models;
using GenLauncherGO.UI.Features.Startup.ViewModels;
using GenLauncherGO.UI.Features.Startup.Views;
using GenLauncherGO.UI.Shared.Localization;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.UI.Features.Startup.Services;

/// <summary>
/// Implements standalone startup setup, location blocking, and initial game selection with Avalonia windows.
/// </summary>
internal sealed class AvaloniaStandaloneStartupWorkflow : IStandaloneStartupWorkflow
{
    private readonly IGameInstallationService _installationService;
    private readonly ILauncherHostEnvironmentService _hostEnvironmentService;
    private readonly ILauncherFilePicker _filePicker;
    private readonly ILauncherStringLocalizer _stringLocalizer;
    private readonly IStartupDialogService _startupDialogService;

    public AvaloniaStandaloneStartupWorkflow(
        IGameInstallationService installationService,
        ILauncherHostEnvironmentService hostEnvironmentService,
        ILauncherFilePicker filePicker,
        ILauncherStringLocalizer stringLocalizer,
        IStartupDialogService startupDialogService)
    {
        _installationService = installationService ?? throw new ArgumentNullException(nameof(installationService));
        _hostEnvironmentService = hostEnvironmentService ??
                                  throw new ArgumentNullException(nameof(hostEnvironmentService));
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _startupDialogService = startupDialogService ?? throw new ArgumentNullException(nameof(startupDialogService));
    }

    /// <inheritdoc />
    public async Task<bool> ShowBlockingLauncherLocationAsync(LauncherStoragePaths storagePaths)
    {
        ArgumentNullException.ThrowIfNull(storagePaths);

        GameInstallationLocation? containingInstallation =
            _installationService.FindContainingInstallation(storagePaths.ExecutableDirectory);
        if (containingInstallation is null)
        {
            return false;
        }

        LauncherLocationWarningWindow warningWindow = new(
            storagePaths.ExecutableDirectory,
            containingInstallation.Directory,
            containingInstallation.Game);
        await ShowWindowAsync(warningWindow);
        return true;
    }

    /// <inheritdoc />
    public async Task<StandaloneStartupResult> RunAsync(
        LauncherStoragePaths storagePaths,
        ILauncherPreferencesService preferencesService)
    {
        ArgumentNullException.ThrowIfNull(storagePaths);
        ArgumentNullException.ThrowIfNull(preferencesService);

        while (true)
        {
            if (!TryValidateCompleteConfiguration(
                    storagePaths,
                    preferencesService.Current.Installations,
                    out LauncherInstallations validatedInstallations))
            {
                if (!await ShowSetupAsync(storagePaths, preferencesService))
                {
                    return StandaloneStartupResult.Canceled;
                }

                continue;
            }

            SupportedGame? selectedGame = validatedInstallations.ResolvePreferredGame(
                preferencesService.Current.LastSelectedGame);
            if (selectedGame == null)
            {
                LauncherGameSelectionWindow selectionWindow = new(
                    new LauncherGameSelectionViewModel(preferencesService),
                    _stringLocalizer,
                    _startupDialogService);
                await ShowWindowAsync(selectionWindow);
                if (!selectionWindow.Accepted)
                {
                    return StandaloneStartupResult.Canceled;
                }

                selectedGame = preferencesService.Current.LastSelectedGame;
            }

            if (selectedGame is not { } activeGame)
            {
                continue;
            }

            string? activePath = validatedInstallations.GetPath(activeGame);
            if (string.IsNullOrWhiteSpace(activePath))
            {
                continue;
            }

            if (preferencesService.Current.LastSelectedGame != activeGame)
            {
                preferencesService.Update(preferencesService.Current with { LastSelectedGame = activeGame });
            }

            return StandaloneStartupResult.Ready(activeGame, activePath);
        }
    }

    private async Task<bool> ShowSetupAsync(
        LauncherStoragePaths storagePaths,
        ILauncherPreferencesService preferencesService)
    {
        var installations = new LauncherInstallationsViewModel(
            preferencesService.Current.Installations,
            storagePaths,
            _installationService,
            _hostEnvironmentService,
            _filePicker,
            _stringLocalizer);
        LauncherSetupWindow setupWindow = new(
            new LauncherSetupViewModel(preferencesService, installations),
            _stringLocalizer,
            LauncherThemePresets.Create(SupportedGame.ZeroHour),
            _startupDialogService);
        await ShowWindowAsync(setupWindow);
        return setupWindow.Accepted;
    }

    private static Task ShowWindowAsync(Window window)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += Window_Closed;
        window.Show();
        return completion.Task;

        void Window_Closed(object? sender, EventArgs e)
        {
            window.Closed -= Window_Closed;
            completion.TrySetResult();
        }
    }

    private bool TryValidateCompleteConfiguration(
        LauncherStoragePaths storagePaths,
        LauncherInstallations installations,
        out LauncherInstallations validatedInstallations)
    {
        LauncherInstallationsValidationResult validation = _installationService.ValidateInstallations(
            installations,
            storagePaths.ExecutableDirectory);
        validatedInstallations = validation.CanonicalInstallations;
        return validation.IsValid;
    }
}
