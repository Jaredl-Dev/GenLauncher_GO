using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.IO;

namespace GenLauncherGO.Core.Integrity.Models;

/// <summary>
///     Describes one launcher-owned directory that must be verified.
/// </summary>
public sealed record ContentIntegrityTarget
{
    public ContentIntegrityTarget(
        string id,
        string displayName,
        string rootDirectory,
        ContentSourceKind sourceKind,
        IReadOnlySet<string> ignoredRelativePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(ignoredRelativePaths);

        Id = id;
        DisplayName = displayName;
        RootDirectory = rootDirectory;
        SourceKind = sourceKind;
        IgnoredRelativePaths = ignoredRelativePaths
            .Select(LexicalPath.NormalizeRelativePath)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Gets the stable identifier used for snapshot persistence.
    /// </summary>
    public string Id { get; }

    public string DisplayName { get; }

    public string RootDirectory { get; }

    public ContentSourceKind SourceKind { get; init; }

    /// <summary>
    ///     Gets known owned paths that belong to inactive content and must be preserved without verification.
    /// </summary>
    public IReadOnlySet<string> IgnoredRelativePaths { get; }
}
