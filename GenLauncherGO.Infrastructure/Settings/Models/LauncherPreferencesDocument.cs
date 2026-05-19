using System.Collections.Generic;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Infrastructure.Settings.Models;

/// <summary>
/// Reads only the schema marker so unsupported documents can be rejected before binding their version-specific shape.
/// </summary>
internal sealed class LauncherPreferencesSchemaDocument
{
    private int? _schemaVersion;

    /// <summary>
    /// Tracks whether YAML binding encountered the schema key. The load fallback explicitly assigns <see langword="null"/>,
    /// so <see cref="HasSchemaVersion"/> distinguishes an unreadable document from valid legacy YAML that omits the key.
    /// </summary>
    public int? SchemaVersion
    {
        get => _schemaVersion;
        set
        {
            _schemaVersion = value;
            HasSchemaVersion = true;
        }
    }

    internal bool HasSchemaVersion { get; private set; }
}

/// <summary>
/// Defines the exact standalone preferences YAML schema at the persistence boundary.
/// </summary>
internal sealed class LauncherPreferencesDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; }

    public LauncherInstallationsDocument? Installations { get; set; }

    public SupportedGame? LastSelectedGame { get; set; }

    public LauncherSharedPreferencesDocument? Shared { get; set; }

    public LauncherGamePreferencesSetDocument? Games { get; set; }
}

/// <summary>
/// Defines the unversioned flat preferences format written before the standalone schema was introduced.
/// </summary>
internal sealed class LegacyLauncherPreferencesDocument
{
    public int? LaunchesCount { get; set; }

    public bool? AutoDeleteOldVersions { get; set; }

    public string? SelectedGameClient { get; set; }

    public bool HasKnownValues =>
        LaunchesCount.HasValue ||
        AutoDeleteOldVersions.HasValue ||
        SelectedGameClient is not null;
}

internal sealed class LauncherInstallationsDocument
{
    public string? Generals { get; set; }

    public string? ZeroHour { get; set; }
}

internal sealed class LauncherSharedPreferencesDocument
{
    public bool AutoDeleteOldVersions { get; set; }

    public bool HideLauncherAfterGameStart { get; set; }

    public bool UseEnglishLanguage { get; set; }
}

internal sealed class LauncherGamePreferencesSetDocument
{
    public LauncherGamePreferencesDocument? Generals { get; set; }

    public LauncherGamePreferencesDocument? ZeroHour { get; set; }
}

internal sealed class LauncherGamePreferencesDocument
{
    public int LaunchesCount { get; set; }

    public string? SelectedGameClient { get; set; }

    public string? SelectedWorldBuilder { get; set; }

    public string? GameArguments { get; set; }

    public string? WorldBuilderArguments { get; set; }

    public List<LauncherCustomExecutableDocument>? CustomGameClients { get; set; }

    public List<LauncherCustomExecutableDocument>? CustomWorldBuilders { get; set; }
}

internal sealed class LauncherCustomExecutableDocument
{
    public string? DisplayName { get; set; }

    public string? ExecutableName { get; set; }
}
