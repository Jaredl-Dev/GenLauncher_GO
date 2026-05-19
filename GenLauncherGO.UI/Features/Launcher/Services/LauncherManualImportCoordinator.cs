using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Mods.Services;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.UI.Features.Launcher.Services;

/// <summary>
///     Coordinates user-selected manual content imports with dialogs, catalog refresh, and integrity snapshots.
/// </summary>
internal sealed class LauncherManualImportCoordinator
{
    private readonly ILauncherContentCatalog _catalog;

    private readonly ILauncherDialogService _dialogService;
    private readonly ILauncherFilePicker _filePicker;

    private readonly LaunchContentIntegrityCoordinator _integrityCoordinator;

    private readonly ILogger<LauncherManualImportCoordinator> _logger;

    private readonly IManualModificationImporter _manualModificationImporter;

    private readonly LauncherRuntimePathContext _runtimePaths;

    public LauncherManualImportCoordinator(
        ILauncherFilePicker filePicker,
        ILauncherDialogService dialogService,
        ILauncherContentCatalog catalog,
        LauncherRuntimePathContext runtimePaths,
        IManualModificationImporter manualModificationImporter,
        LaunchContentIntegrityCoordinator integrityCoordinator,
        ILogger<LauncherManualImportCoordinator> logger)
    {
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _runtimePaths = runtimePaths ?? throw new ArgumentNullException(nameof(runtimePaths));
        _manualModificationImporter = manualModificationImporter ??
                                      throw new ArgumentNullException(nameof(manualModificationImporter));
        _integrityCoordinator = integrityCoordinator ?? throw new ArgumentNullException(nameof(integrityCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Runs the manual import workflow and returns the catalog entry that should be shown in the UI.
    /// </summary>
    /// <param name="kind">The kind of content to import.</param>
    /// <param name="owner">The window that owns the import dialogs.</param>
    /// <param name="parentContentName">The parent modification name, or <see langword="null" /> for original-game content.</param>
    /// <param name="cancellationToken">The token used to cancel import work.</param>
    /// <returns>The imported catalog entry, or <see langword="null" /> when the user cancels.</returns>
    public async Task<LauncherContent?> ImportAsync(
        ModificationType kind,
        Window owner,
        string? parentContentName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        LauncherPaths launcherPaths = _runtimePaths.ActivePaths;

        IReadOnlyList<string> selectedFiles = await _filePicker.PickManualPackageFilesAsync(owner);
        if (selectedFiles.Count == 0)
        {
            return null;
        }

        string resolvedParentContentName = GetParentContentName(kind, parentContentName);
        ManualModificationDialogResult? importResult = await _dialogService.ShowManualModificationImportAsync(
            selectedFiles,
            owner);

        if (importResult == null)
        {
            return null;
        }

        LauncherContentKey importedKey = CreateContentKey(
            kind,
            resolvedParentContentName,
            importResult.ModificationName,
            importResult.Version);
        OwnedContentPath destinationPath = ResolveDestinationPath(launcherPaths, importedKey);

        await Task.Run(
            () => _manualModificationImporter.Import(
                selectedFiles,
                destinationPath,
                cancellationToken),
            cancellationToken);

        _catalog.UpdateLocalModificationsData();

        LauncherContent savedModification = _catalog.Data.FindContent(importedKey)
                                            ?? throw CreateMissingImportedModificationException(kind,
                                                importResult.ModificationName);

        LauncherContentVersion savedVersion = savedModification.Versions
                                                  .FirstOrDefault(version => version.ContentKey == importedKey)
                                              ?? throw CreateMissingImportedVersionException(
                                                  kind,
                                                  importResult.ModificationName,
                                                  importResult.Version);

        await _integrityCoordinator.RegisterManualImportAsync(savedVersion);

        return savedModification;
    }

    private static string GetParentContentName(
        ModificationType kind,
        string? parentContentName)
    {
        if (kind == ModificationType.Mod)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(parentContentName)
            ? LauncherContentKey.OriginalGame.Name
            : parentContentName;
    }

    /// <summary>
    ///     Resolves the canonical owned destination for imported package files.
    /// </summary>
    /// <param name="launcherPaths">The immutable active path snapshot for the import.</param>
    /// <param name="contentKey">The validated identity shared by path resolution and catalog lookup.</param>
    /// <returns>The validated launcher-owned destination path.</returns>
    private static OwnedContentPath ResolveDestinationPath(
        LauncherPaths launcherPaths,
        LauncherContentKey contentKey)
    {
        return LauncherContentPathResolver.ResolveVersionPath(
                   launcherPaths,
                   contentKey)
               ?? throw new InvalidOperationException("Manual content identity did not resolve to an owned path.");
    }

    private static LauncherContentKey CreateContentKey(
        ModificationType kind,
        string parentContentName,
        string modificationName,
        string version)
    {
        ModificationType modificationType = kind switch
        {
            ModificationType.Mod or ModificationType.Patch or ModificationType.Addon => kind,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown manual import kind.")
        };

        return new LauncherContentKey(
            modificationType,
            modificationType == ModificationType.Mod ? string.Empty : parentContentName,
            modificationName,
            version);
    }

    private InvalidOperationException CreateMissingImportedModificationException(
        ModificationType kind,
        string modificationName)
    {
        string importKindName = GetImportKindName(kind);
        _logger.LogError(
            "Manual import for {ImportKind} completed, but catalog entry {ModificationName} was not found after refresh.",
            importKindName,
            modificationName);

        return new InvalidOperationException(
            $"Imported {importKindName} '{modificationName}' was not found in the refreshed launcher catalog.");
    }

    private InvalidOperationException CreateMissingImportedVersionException(
        ModificationType kind,
        string modificationName,
        string version)
    {
        string importKindName = GetImportKindName(kind);
        _logger.LogError(
            "Manual import for {ImportKind} completed, but catalog entry {ModificationName} version {Version} was not found after refresh.",
            importKindName,
            modificationName,
            version);

        return new InvalidOperationException(
            $"Imported {importKindName} '{modificationName}' version '{version}' was not found in the refreshed launcher catalog.");
    }

    private static string GetImportKindName(ModificationType kind)
    {
        return kind == ModificationType.Mod ? "Modification" : kind.ToString();
    }
}
