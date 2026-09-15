using System.Collections.Generic;
using GenLauncherGO.Features.Mods;

namespace GenLauncherGO.Features.Integrity;

/// <summary>
///     Owns the stable identifiers that bind launcher content identity to its persisted integrity snapshots.
/// </summary>
/// <remarks>
///     Snapshots are stored under a hash of these identifiers, so target construction and snapshot retention must
///     derive them the same way or retention silently stops matching the snapshots it is meant to keep.
/// </remarks>
internal static class ContentIntegrityTargetId
{
    private const string PackagePrefix = "package";

    private const string CachePrefix = "cache";

    /// <summary>
    ///     Builds the identifier for a version's installed package directory.
    /// </summary>
    public static string ForPackage(LauncherContentKey contentKey)
    {
        return Create(PackagePrefix, contentKey);
    }

    /// <summary>
    ///     Builds the identifier for a version's cached artwork directory.
    /// </summary>
    public static string ForCache(LauncherContentKey contentKey)
    {
        return Create(CachePrefix, contentKey);
    }

    /// <summary>
    ///     Enumerates every identifier a content version can own.
    /// </summary>
    /// <remarks>
    ///     Only modifications ever receive a cache target. Naming the cache identifier for every content type keeps
    ///     retention free of that rule, because an identifier no target ever produced simply matches no snapshot.
    /// </remarks>
    public static IEnumerable<string> ForContent(LauncherContentKey contentKey)
    {
        yield return ForPackage(contentKey);
        yield return ForCache(contentKey);
    }

    private static string Create(string prefix, LauncherContentKey contentKey)
    {
        return string.Concat(prefix, ":", contentKey.ToStableString());
    }
}
