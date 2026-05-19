using System;
using GenLauncherGO.Core.IO;

namespace GenLauncherGO.Core.Mods.Models;

/// <summary>
/// Identifies one normalized launcher-owned content path together with the root that owns it.
/// </summary>
public sealed record OwnedContentPath
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OwnedContentPath"/> record.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when either path is missing or <paramref name="fullPath"/> is not below
    /// <paramref name="ownerRoot"/>.
    /// </exception>
    public OwnedContentPath(string ownerRoot, string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        string normalizedOwnerRoot = LexicalPath.NormalizeFullPath(ownerRoot);
        string normalizedFullPath = LexicalPath.NormalizeFullPath(fullPath);
        if (!LexicalPath.IsPathBelowDirectory(normalizedFullPath, normalizedOwnerRoot))
        {
            throw new ArgumentException("An owned content path must be below its owning root.", nameof(fullPath));
        }

        OwnerRoot = normalizedOwnerRoot;
        FullPath = normalizedFullPath;
    }

    public string OwnerRoot { get; }

    public string FullPath { get; }

    public string RelativePath => LexicalPath.GetRelativePath(OwnerRoot, FullPath);
}
