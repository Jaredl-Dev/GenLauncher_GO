using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Startup.ViewModels;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Builds the shared installation-path view model that first-run setup and launcher settings both edit.
/// </summary>
internal static class TestLauncherInstallations
{
    /// <summary>
    ///     The launcher root every installation test validates against.
    /// </summary>
    public static LauncherStoragePaths StoragePaths { get; } = new(@"C:\Launcher");

    public static LauncherInstallationsViewModel CreateViewModel(
        LauncherInstallations? installations = null,
        IGameInstallationService? installationService = null,
        ILauncherFilePicker? filePicker = null,
        ILauncherHostEnvironmentService? hostEnvironmentService = null,
        LauncherStoragePaths? storagePaths = null,
        ILauncherStringLocalizer? stringLocalizer = null)
    {
        return new LauncherInstallationsViewModel(
            installations ?? new LauncherInstallations(),
            storagePaths ?? StoragePaths,
            installationService ?? new FakeGameInstallationService(),
            hostEnvironmentService ?? Substitute.For<ILauncherHostEnvironmentService>(),
            filePicker ?? new StubLauncherFilePicker(),
            stringLocalizer ?? new FakeStringLocalizer());
    }
}
