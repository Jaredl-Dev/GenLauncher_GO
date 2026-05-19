using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Mods.Services;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Launcher.Models;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.UI.Features.Launcher.Services;

/// <summary>
/// Coordinates user-selected manual content imports with dialogs, catalog refresh, and integrity snapshots.
/// </summary>
internal sealed class LauncherManualImportCoordinator
{
    private readonly ILauncherFilePicker _filePicker;

    private readonly ILauncherDialogService _dialogService;

    private readonly ILauncherContentCatalog _catalog;

    private readonly LauncherRuntimePathContext _runtimePaths;

    private readonly IManualModificationImporter _manualModificationImporter;

    private readonly LaunchContentIntegrityCoordinator _integrityCoordinator;

    private readonly ILogger<LauncherManualImportCoordinator> _logger;

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
    /// Runs the manual import workflow and returns the catalog entry that should be shown in the UI.
    /// </summary>
    /// <param name="request">The import request.</param>
    /// <param name="cancellationToken">The token used to cancel import work.</param>
    /// <returns>The imported catalog entry, or <see langword="null"/> when the user cancels.</returns>
    public async Task<LauncherManualImportResult?> ImportAsync(
        LauncherManualImportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LauncherPaths launcherPaths = _runtimePaths.ActivePaths;

        IReadOnlyList<string> selectedFiles = await _filePicker.PickManualPackageFilesAsync(request.Owner);
        if (selectedFiles.Count == 0)
        {
            return null;
        }

        string? parentContentName = GetParentContentName(request);
        ManualModificationDialogResult? importResult = await ShowImportDialogAsync(
            selectedFiles,
            parentContentName,
            request);

        if (importResult == null)
        {
            return null;
        }

        string resolvedParentContentName = importResult.ParentContentName
                                           ?? parentContentName
                                           ?? LauncherContentKey.OriginalGame.Name;

        OwnedContentPath destinationPath = ResolveDestinationPath(
            request.Kind,
            resolvedParentContentName,
            importResult.ModificationName,
            importResult.Version,
            launcherPaths);

        await Task.Run(
            () => _manualModificationImporter.Import(
                importResult.Files,
                destinationPath,
                cancellationToken),
            cancellationToken);

        _catalog.UpdateLocalModificationsData();

        LauncherContentKey importedKey = CreateContentKey(
            request.Kind,
            resolvedParentContentName,
            importResult.ModificationName,
            importResult.Version);
        LauncherContent savedModification = _catalog.Data.FindContent(importedKey)
                                             ?? throw CreateMissingImportedModificationException(request.Kind,
                                                 importResult.ModificationName);

        LauncherContentVersion savedVersion = savedModification.Versions
                                               .FirstOrDefault(version => version.ContentKey == importedKey)
                                           ?? throw CreateMissingImportedVersionException(
                                               request.Kind,
                                               importResult.ModificationName,
                                               importResult.Version);

        await _integrityCoordinator.RegisterManualImportAsync(savedVersion);

        return new LauncherManualImportResult(request.Kind, savedModification);
    }

    private Task<ManualModificationDialogResult?> ShowImportDialogAsync(
        IReadOnlyList<string> selectedFiles,
        string? parentContentName,
        LauncherManualImportRequest request)
    {
        ManualModificationDialogRequest dialogRequest = request.Kind == ModificationType.Mod
            ? new ManualModificationDialogRequest(selectedFiles)
            : new ManualModificationDialogRequest(
                selectedFiles,
                parentContentName ?? LauncherContentKey.OriginalGame.Name);

        return _dialogService.ShowManualModificationImportAsync(dialogRequest, request.Owner);
    }

    private string? GetParentContentName(LauncherManualImportRequest request)
    {
        if (request.Kind == ModificationType.Mod)
        {
            return null;
        }

        return String.IsNullOrWhiteSpace(request.ParentContentName)
            ? LauncherContentKey.OriginalGame.Name
            : request.ParentContentName;
    }

    /// <summary>
    /// Resolves the canonical owned destination for imported package files.
    /// </summary>
    /// <param name="kind">The import kind.</param>
    /// <param name="parentContentName">The parent content name.</param>
    /// <param name="modificationName">The imported content name.</param>
    /// <param name="version">The imported content version.</param>
    /// <param name="launcherPaths">The immutable active path snapshot for the import.</param>
    /// <returns>The validated launcher-owned destination path.</returns>
    private OwnedContentPath ResolveDestinationPath(
        ModificationType kind,
        string parentContentName,
        string modificationName,
        string version,
        LauncherPaths launcherPaths)
    {
        LauncherContentKey contentKey = CreateContentKey(
            kind,
            parentContentName,
            modificationName,
            version);

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

    private static string GetImportKindName(ModificationType kind) =>
        kind == ModificationType.Mod ? "Modification" : kind.ToString();
}
