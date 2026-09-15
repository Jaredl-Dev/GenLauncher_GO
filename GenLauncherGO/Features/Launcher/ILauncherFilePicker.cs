using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace GenLauncherGO.Features.Launcher;

/// <summary>
///     Opens launcher-owned file selection dialogs.
/// </summary>
internal interface ILauncherFilePicker
{
    /// <summary>
    ///     Opens a folder picker for a Generals or Zero Hour installation root.
    /// </summary>
    /// <param name="owner">The owner window for the picker dialog.</param>
    /// <param name="initialDirectory">The directory initially displayed when it exists.</param>
    /// <returns>The selected folder path, or <see langword="null" /> when the user cancels the dialog.</returns>
    Task<string?> PickGameInstallationFolderAsync(Window owner, string? initialDirectory);

    /// <summary>
    ///     Opens a multi-select file picker for manual content imports.
    /// </summary>
    /// <remarks>
    ///     Launcher package formats are offered first, but any file may be selected: a modification is free to ship
    ///     loose <c>.ini</c>, <c>.csf</c>, or artwork files the launcher stores without interpreting them.
    /// </remarks>
    /// <param name="owner">The owner window for the picker dialog.</param>
    /// <returns>The selected file paths, or an empty list when the user cancels the dialog.</returns>
    Task<IReadOnlyList<string>> PickManualPackageFilesAsync(Window owner);

    /// <summary>
    ///     Opens a folder picker for manual content imports.
    /// </summary>
    /// <param name="owner">The owner window for the picker dialog.</param>
    /// <returns>The selected folder path, or <see langword="null" /> when the user cancels the dialog.</returns>
    Task<string?> PickManualContentFolderAsync(Window owner);

    /// <summary>
    ///     Opens an image picker for manual modification image replacement.
    /// </summary>
    /// <param name="owner">The owner window for the picker dialog.</param>
    /// <param name="imageFilterLabel">The localized label used in the image picker file filter.</param>
    /// <returns>The selected image path, or <see langword="null" /> when the user cancels the dialog.</returns>
    Task<string?> PickModificationImageFileAsync(Window owner, string imageFilterLabel);

    /// <summary>
    ///     Opens a single-select executable picker rooted initially at the active game directory.
    /// </summary>
    /// <param name="owner">The owner window for the picker dialog.</param>
    /// <param name="gameDirectory">The active game directory initially displayed.</param>
    /// <returns>The selected local executable path, or <see langword="null" /> when canceled.</returns>
    Task<string?> PickGameExecutableFileAsync(Window owner, string gameDirectory);
}
