using System;

namespace GenLauncherGO.Core.Mods.Models;

/// <summary>
/// Identifies launcher content by type, parent identity, name, and version.
/// </summary>
/// <remarks>
/// Identity text retains its supplied representation and is compared using ordinal, case-insensitive semantics.
/// Missing text is equivalent to an empty string. The original-game key is a stable nonlocalized relationship
/// identity and must not be used as user-visible display text.
/// </remarks>
public readonly struct LauncherContentKey : IEquatable<LauncherContentKey>
{
    private readonly string? _parentIdentity;

    private readonly string? _name;

    private readonly string? _version;

    public LauncherContentKey(
        ModificationType contentType,
        string? parentIdentity,
        string? name,
        string? version)
    {
        ContentType = contentType;
        _parentIdentity = parentIdentity;
        _name = name;
        _version = version;
    }

    public static LauncherContentKey OriginalGame { get; } =
        new(ModificationType.Mod, string.Empty, "Original Game", string.Empty);

    /// <summary>
    /// Creates the name-only identity used by the top-level modification catalog.
    /// </summary>
    public static LauncherContentKey ForModificationName(string? name)
    {
        return new LauncherContentKey(ModificationType.Mod, string.Empty, name, string.Empty);
    }

    public ModificationType ContentType { get; }

    public string ParentIdentity => _parentIdentity ?? string.Empty;

    public string Name => _name ?? string.Empty;

    public string Version => _version ?? string.Empty;

    /// <summary>
    /// Gets the card identity for this content, omitting its version.
    /// </summary>
    internal LauncherContentKey WithoutVersion()
    {
        return new LauncherContentKey(ContentType, ParentIdentity, Name, string.Empty);
    }

    /// <summary>
    /// Determines whether this content belongs to the supplied parent.
    /// </summary>
    public bool IsChildOf(LauncherContentKey parent)
    {
        return IdentityTextEquals(ParentIdentity, parent.Name);
    }

    /// <summary>
    /// Determines whether this key has the supplied content name.
    /// </summary>
    public bool HasName(string? name)
    {
        return IdentityTextEquals(Name, name);
    }

    /// <summary>
    /// Determines whether this key has the supplied version identity.
    /// </summary>
    public bool HasVersion(string? version)
    {
        return IdentityTextEquals(Version, version);
    }

    /// <summary>
    /// Formats the legacy-compatible lowercase identity used by launcher-owned integrity records.
    /// </summary>
    public string ToStableString()
    {
        return string.Join(
            ":",
            ContentType,
            ParentIdentity,
            Name,
            Version).ToLowerInvariant();
    }

    public bool Equals(LauncherContentKey other)
    {
        return ContentType == other.ContentType &&
               IdentityTextEquals(ParentIdentity, other.ParentIdentity) &&
               IdentityTextEquals(Name, other.Name) &&
               IdentityTextEquals(Version, other.Version);
    }

    public override bool Equals(object? obj)
    {
        return obj is LauncherContentKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(ContentType);
        hashCode.Add(ParentIdentity, StringComparer.OrdinalIgnoreCase);
        hashCode.Add(Name, StringComparer.OrdinalIgnoreCase);
        hashCode.Add(Version, StringComparer.OrdinalIgnoreCase);
        return hashCode.ToHashCode();
    }

    public static bool operator ==(LauncherContentKey left, LauncherContentKey right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LauncherContentKey left, LauncherContentKey right)
    {
        return !left.Equals(right);
    }

    private static bool IdentityTextEquals(string? left, string? right)
    {
        return StringComparer.OrdinalIgnoreCase.Equals(left ?? string.Empty, right ?? string.Empty);
    }
}
