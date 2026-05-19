using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Exceptions;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Core.Mods.Contracts;

/// <summary>
///     Owns the active launcher content aggregate and coordinates its loading, mutation, and persistence.
/// </summary>
public interface ILauncherContentCatalog
{
    /// <summary>
    ///     Gets the active in-memory launcher content aggregate.
    /// </summary>
    LauncherData Data { get; }

    /// <summary>
    ///     Gets the active advertising content, or <see langword="null" /> when none is available.
    /// </summary>
    LauncherContentVersion? Advertising { get; }

    /// <summary>
    ///     Gets modification names advertised by the remote repository, or <see langword="null" /> when unavailable.
    /// </summary>
    IReadOnlyList<string>? RepositoryModificationNames { get; }

    /// <summary>
    ///     Initializes the catalog from local state and, when available, the remote repository.
    /// </summary>
    Task InitDataAsync(
        LauncherContentCatalogInitializationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Reads add-ons and patches that belong to the original game.
    /// </summary>
    Task ReadOriginalGameAddonsAndPatchesAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Reads one repository modification's normalized metadata without adding it to the active catalog.
    /// </summary>
    Task<LauncherContentVersion> GetRepositoryModificationMetadataAsync(
        string name,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Downloads one repository modification, caches its images, and adds it to the active catalog.
    /// </summary>
    Task<LauncherContentVersion> AddRepositoryModificationAsync(
        string name,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Reads remote patches and add-ons for a modification.
    /// </summary>
    Task ReadPatchesAndAddonsForModAsync(
        LauncherContentKey modificationKey,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Deletes one installed version and reconciles local state while retaining available catalog metadata.
    /// </summary>
    void UninstallVersion(LauncherContentKey contentKey);

    /// <summary>
    ///     Deletes one installed version, discards that version's catalog metadata, and reconciles local state.
    /// </summary>
    void DiscardVersion(LauncherContentKey contentKey);

    /// <summary>
    ///     Deletes all installed files for a content card, discards the whole card, and reconciles local state.
    /// </summary>
    void DiscardContent(LauncherContentKey contentKey);

    /// <summary>
    ///     Refreshes the catalog from locally installed content and removes stale local-only cards.
    /// </summary>
    void UpdateLocalModificationsData();

    /// <summary>
    ///     Saves the current catalog selection and installed state.
    /// </summary>
    /// <exception cref="LauncherContentPersistenceException">
    ///     Thrown when the current state cannot be persisted. The in-memory catalog remains authoritative so callers can
    ///     retry without rolling back completed file-system work.
    /// </exception>
    void SaveLauncherData();
}
