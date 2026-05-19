using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.UI.Features.Launcher.Contracts;

namespace GenLauncherGO.Tests.Testing;

internal sealed class StubLauncherFilePicker : ILauncherFilePicker
{
    public string? GameInstallationFolderResult { get; init; }

    public IReadOnlyList<string> ManualPackageFilesResult { get; init; } = [];

    public string? ModificationImageFileResult { get; init; }

    public string? GameExecutableFileResult { get; init; }

    public Exception? GameInstallationFolderFailure { get; init; }

    /// <summary>
    ///     The folder each browse started from, which is how a test sees that the picker opened where the user was
    ///     already working rather than at an unrelated default.
    /// </summary>
    public List<string?> RequestedInitialDirectories { get; } = [];

    public Task<string?> PickGameInstallationFolderAsync(
        Window owner,
        string? initialDirectory)
    {
        RequestedInitialDirectories.Add(initialDirectory);
        return GameInstallationFolderFailure is null
            ? Task.FromResult(GameInstallationFolderResult)
            : Task.FromException<string?>(GameInstallationFolderFailure);
    }

    public Task<IReadOnlyList<string>> PickManualPackageFilesAsync(Window owner)
    {
        return Task.FromResult(ManualPackageFilesResult);
    }

    public Task<string?> PickModificationImageFileAsync(
        Window owner,
        string imageFilterLabel)
    {
        return Task.FromResult(ModificationImageFileResult);
    }

    public Task<string?> PickGameExecutableFileAsync(Window owner, string gameDirectory)
    {
        return Task.FromResult(GameExecutableFileResult);
    }
}
