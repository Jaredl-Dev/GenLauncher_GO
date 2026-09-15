using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GenLauncherGO.Shared.IO;
using GenLauncherGO.Shared.Localization;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Launcher;

/// <summary>
///     Opens Avalonia storage-provider pickers used by launcher workflows.
/// </summary>
internal sealed class AvaloniaLauncherFilePicker : ILauncherFilePicker
{
    /// <summary>
    ///     The filters offered to the user, derived from the formats the launcher actually accepts so a picker
    ///     can never offer a file the rest of the launcher would refuse.
    /// </summary>
    private static readonly IReadOnlyList<string> _packagePatterns = ToPatterns(
        [.. LauncherContentFileTypes.GamePackageExtensions, .. LauncherContentFileTypes.ArchiveExtensions]);

    private static readonly IReadOnlyList<string> _imagePatterns =
        ToPatterns(LauncherContentFileTypes.ImageExtensions);

    private static readonly IReadOnlyList<string> _executablePatterns = ["*.exe"];

    /// <summary>
    ///     The logger used for file-picker workflow diagnostics.
    /// </summary>
    private readonly ILogger<AvaloniaLauncherFilePicker> _logger;

    private readonly ILauncherStringLocalizer _stringLocalizer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AvaloniaLauncherFilePicker" /> class.
    /// </summary>
    /// <param name="logger">The logger used for file-picker workflow diagnostics.</param>
    /// <param name="stringLocalizer">The localized launcher text provider.</param>
    public AvaloniaLauncherFilePicker(
        ILogger<AvaloniaLauncherFilePicker> logger,
        ILauncherStringLocalizer stringLocalizer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
    }

    /// <inheritdoc />
    public async Task<string?> PickGameInstallationFolderAsync(
        Window owner,
        string? initialDirectory)
    {
        ArgumentNullException.ThrowIfNull(owner);

        IStorageFolder? startLocation = null;
        if (!string.IsNullOrWhiteSpace(initialDirectory) &&
            Directory.Exists(initialDirectory))
        {
            startLocation = await owner.StorageProvider.TryGetFolderFromPathAsync(initialDirectory);
        }

        IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                SuggestedStartLocation = startLocation,
                Title = _stringLocalizer["InstallationFolder"]
            });
        string? selectedPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (selectedPath == null)
        {
            _logger.LogDebug("Game installation folder picker was canceled.");
            return null;
        }

        _logger.LogDebug("Game installation folder picker selected a folder.");
        return selectedPath;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> PickManualPackageFilesAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType(_stringLocalizer["LauncherPackagesFilter"])
                    {
                        Patterns = _packagePatterns
                    },
                    // A modification is free to ship loose .ini, .csf, or artwork files. The launcher stores them
                    // without interpreting them, so the picker must not be the thing that refuses them.
                    new FilePickerFileType(_stringLocalizer["AllFilesFilter"])
                    {
                        Patterns = ["*"]
                    }
                ]
            });
        IReadOnlyList<string> selectedFiles = files
            .Select(file => file.TryGetLocalPath())
            .Where(path => path != null)
            .Cast<string>()
            .ToList();
        if (selectedFiles.Count == 0)
        {
            _logger.LogDebug("Manual package file picker was canceled.");
            return Array.Empty<string>();
        }

        _logger.LogDebug(
            "Manual package file picker selected {SelectedFileCount} file(s).",
            selectedFiles.Count);
        return selectedFiles;
    }

    /// <inheritdoc />
    public async Task<string?> PickManualContentFolderAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = _stringLocalizer["ManualImportFolderTitle"]
            });
        string? selectedPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (selectedPath == null)
        {
            _logger.LogDebug("Manual content folder picker was canceled.");
            return null;
        }

        _logger.LogDebug("Manual content folder picker selected a folder.");
        return selectedPath;
    }

    /// <inheritdoc />
    public async Task<string?> PickModificationImageFileAsync(
        Window owner,
        string imageFilterLabel)
    {
        ArgumentNullException.ThrowIfNull(owner);

        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType($"{imageFilterLabel} 500x100")
                    {
                        Patterns = _imagePatterns
                    }
                ]
            });
        string? selectedPath = files.FirstOrDefault()?.TryGetLocalPath();
        if (selectedPath == null)
        {
            _logger.LogDebug("Modification image file picker was canceled.");
            return null;
        }

        _logger.LogDebug("Modification image file picker selected an image.");
        return selectedPath;
    }

    /// <inheritdoc />
    public async Task<string?> PickGameExecutableFileAsync(Window owner, string gameDirectory)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);

        IStorageFolder? startLocation = Directory.Exists(gameDirectory)
            ? await owner.StorageProvider.TryGetFolderFromPathAsync(gameDirectory)
            : null;
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                AllowMultiple = false,
                SuggestedStartLocation = startLocation,
                Title = _stringLocalizer["ChooseExecutable"],
                FileTypeFilter =
                [
                    new FilePickerFileType(_stringLocalizer["Executables"])
                    {
                        Patterns = _executablePatterns
                    }
                ]
            });
        string? selectedPath = files.FirstOrDefault()?.TryGetLocalPath();
        if (selectedPath == null)
        {
            _logger.LogDebug("Game executable file picker was canceled.");
            return null;
        }

        _logger.LogDebug("Game executable file picker selected a file.");
        return selectedPath;
    }
    /// <summary>
    ///     Turns accepted extensions into the glob patterns a storage-provider filter expects.
    /// </summary>
    private static IReadOnlyList<string> ToPatterns(IReadOnlyList<string> extensions)
    {
        return extensions.Select(extension => "*" + extension).ToList();
    }

}
