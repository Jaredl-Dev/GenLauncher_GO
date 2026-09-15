using GenLauncherGO.Features.Launcher;
using GenLauncherGO.Features.Settings;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Shared.Localization;

namespace GenLauncherGO.Tests.Features.Settings;

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
