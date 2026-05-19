using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Settings.Models;

namespace GenLauncherGO.Infrastructure.Settings.Support;

/// <summary>
///     Maps the standalone preferences persistence schema once into normalized Core preferences.
/// </summary>
internal static class LauncherPreferencesDocumentMapper
{
    public static LauncherPreferences ToPreferences(LauncherPreferencesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.SchemaVersion != LauncherPreferencesDocument.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Launcher preferences schema version {document.SchemaVersion} is not supported.");
        }

        LauncherInstallationsDocument installations = document.Installations ?? new LauncherInstallationsDocument();
        LauncherGamePreferencesSetDocument games = document.Games ?? new LauncherGamePreferencesSetDocument();

        return Normalize(new LauncherPreferences
        {
            Installations = new LauncherInstallations
            {
                Generals = installations.Generals,
                ZeroHour = installations.ZeroHour
            },
            LastSelectedGame = document.LastSelectedGame,
            Shared = MapShared(document.Shared),
            Games = new LauncherGamePreferencesSet
            {
                Generals = MapGame(games.Generals),
                ZeroHour = MapGame(games.ZeroHour)
            }
        });
    }

    /// <summary>
    ///     Migrates the unversioned flat preferences format into the current normalized model.
    /// </summary>
    public static LauncherPreferences MigrateLegacyPreferences(LegacyLauncherPreferencesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Normalize(new LauncherPreferences
        {
            Shared = new LauncherSharedPreferences
            {
                AutoDeleteOldVersions = document.AutoDeleteOldVersions ?? false
            },
            Games = new LauncherGamePreferencesSet
            {
                ZeroHour = new LauncherGamePreferences
                {
                    LaunchesCount = document.LaunchesCount ?? 0,
                    SelectedGameClient = document.SelectedGameClient ?? string.Empty
                }
            }
        });
    }

    public static LauncherPreferencesDocument ToDocument(LauncherPreferences preferences)
    {
        LauncherPreferences normalized = Normalize(preferences);

        return new LauncherPreferencesDocument
        {
            SchemaVersion = LauncherPreferencesDocument.CurrentSchemaVersion,
            Installations = new LauncherInstallationsDocument
            {
                Generals = normalized.Installations.Generals,
                ZeroHour = normalized.Installations.ZeroHour
            },
            LastSelectedGame = normalized.LastSelectedGame,
            Shared = new LauncherSharedPreferencesDocument
            {
                AutoDeleteOldVersions = normalized.Shared.AutoDeleteOldVersions,
                HideLauncherAfterGameStart = normalized.Shared.HideLauncherAfterGameStart,
                UseEnglishLanguage = normalized.Shared.UseEnglishLanguage,
                HasShownRetailGenPatcherRecommendation =
                    normalized.Shared.HasShownRetailGenPatcherRecommendation
            },
            Games = new LauncherGamePreferencesSetDocument
            {
                Generals = MapGame(normalized.Games.Generals),
                ZeroHour = MapGame(normalized.Games.ZeroHour)
            }
        };
    }

    public static LauncherPreferences Normalize(LauncherPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        LauncherInstallations installations = preferences.Installations ?? new LauncherInstallations();
        LauncherSharedPreferences shared = preferences.Shared ?? new LauncherSharedPreferences();
        LauncherGamePreferencesSet games = preferences.Games ?? new LauncherGamePreferencesSet();

        return new LauncherPreferences
        {
            Installations = new LauncherInstallations
            {
                Generals = NormalizePath(installations.Generals),
                ZeroHour = NormalizePath(installations.ZeroHour)
            },
            LastSelectedGame = NormalizeGame(preferences.LastSelectedGame),
            Shared = shared,
            Games = new LauncherGamePreferencesSet
            {
                Generals = NormalizeGamePreferences(games.Generals, SupportedGame.Generals),
                ZeroHour = NormalizeGamePreferences(games.ZeroHour, SupportedGame.ZeroHour)
            }
        };
    }

    private static LauncherSharedPreferences MapShared(LauncherSharedPreferencesDocument? shared)
    {
        return shared is null
            ? new LauncherSharedPreferences()
            : new LauncherSharedPreferences
            {
                AutoDeleteOldVersions = shared.AutoDeleteOldVersions,
                HideLauncherAfterGameStart = shared.HideLauncherAfterGameStart,
                UseEnglishLanguage = shared.UseEnglishLanguage,
                HasShownRetailGenPatcherRecommendation = shared.HasShownRetailGenPatcherRecommendation
            };
    }

    private static LauncherGamePreferences MapGame(LauncherGamePreferencesDocument? game)
    {
        return game is null
            ? new LauncherGamePreferences()
            : new LauncherGamePreferences
            {
                LaunchesCount = game.LaunchesCount,
                SelectedGameClient = game.SelectedGameClient ?? string.Empty,
                SelectedWorldBuilder = game.SelectedWorldBuilder ?? string.Empty,
                GameArguments = game.GameArguments ?? string.Empty,
                WorldBuilderArguments = game.WorldBuilderArguments ?? string.Empty,
                ModsListVerticalOffset = game.ModsListVerticalOffset,
                AdvertisingPositionInList = game.AdvertisingPositionInList,
                CustomGameClients = MapCustomExecutables(game.CustomGameClients),
                CustomWorldBuilders = MapCustomExecutables(game.CustomWorldBuilders)
            };
    }

    private static LauncherGamePreferencesDocument MapGame(LauncherGamePreferences game)
    {
        return new LauncherGamePreferencesDocument
        {
            LaunchesCount = game.LaunchesCount,
            SelectedGameClient = game.SelectedGameClient,
            SelectedWorldBuilder = game.SelectedWorldBuilder,
            GameArguments = game.GameArguments,
            WorldBuilderArguments = game.WorldBuilderArguments,
            ModsListVerticalOffset = game.ModsListVerticalOffset,
            AdvertisingPositionInList = game.AdvertisingPositionInList,
            CustomGameClients = MapCustomExecutables(game.CustomGameClients),
            CustomWorldBuilders = MapCustomExecutables(game.CustomWorldBuilders)
        };
    }

    private static LauncherGamePreferences NormalizeGamePreferences(
        LauncherGamePreferences? preferences,
        SupportedGame game)
    {
        if (preferences is null)
        {
            return new LauncherGamePreferences();
        }

        return preferences with
        {
            LaunchesCount = Math.Max(0, preferences.LaunchesCount),
            SelectedGameClient = (preferences.SelectedGameClient ?? string.Empty).Trim(),
            SelectedWorldBuilder = (preferences.SelectedWorldBuilder ?? string.Empty).Trim(),
            GameArguments = preferences.GameArguments ?? string.Empty,
            WorldBuilderArguments = preferences.WorldBuilderArguments ?? string.Empty,
            ModsListVerticalOffset = double.IsFinite(preferences.ModsListVerticalOffset)
                ? Math.Max(0, preferences.ModsListVerticalOffset)
                : 0,
            AdvertisingPositionInList = Math.Max(0, preferences.AdvertisingPositionInList),
            CustomGameClients = NormalizeCustomExecutables(
                preferences.CustomGameClients,
                LauncherFileSystemLayout.GetBuiltInGameExecutableNames(game)),
            CustomWorldBuilders = NormalizeCustomExecutables(
                preferences.CustomWorldBuilders,
                LauncherFileSystemLayout.GetBuiltInWorldBuilderExecutableNames(game))
        };
    }

    private static IReadOnlyList<LauncherCustomExecutable> MapCustomExecutables(
        IReadOnlyList<LauncherCustomExecutableDocument>? documents)
    {
        if (documents == null || documents.Count == 0)
        {
            return Array.Empty<LauncherCustomExecutable>();
        }

        var executables = new List<LauncherCustomExecutable>(documents.Count);
        foreach (LauncherCustomExecutableDocument document in documents)
        {
            if (document == null)
            {
                continue;
            }

            try
            {
                executables.Add(new LauncherCustomExecutable(
                    document.DisplayName ?? string.Empty,
                    document.ExecutableName ?? string.Empty));
            }
            catch (ArgumentException)
            {
                // Invalid persisted custom entries are ignored at the settings boundary.
            }
        }

        return executables;
    }

    private static List<LauncherCustomExecutableDocument> MapCustomExecutables(
        IReadOnlyList<LauncherCustomExecutable> executables)
    {
        return executables
            .Select(executable => new LauncherCustomExecutableDocument
            {
                DisplayName = executable.DisplayName,
                ExecutableName = executable.ExecutableName
            })
            .ToList();
    }

    private static IReadOnlyList<LauncherCustomExecutable> NormalizeCustomExecutables(
        IReadOnlyList<LauncherCustomExecutable>? executables,
        IEnumerable<string> builtInNames)
    {
        if (executables == null || executables.Count == 0)
        {
            return Array.Empty<LauncherCustomExecutable>();
        }

        var displayNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var executableNames = new HashSet<string>(builtInNames, StringComparer.OrdinalIgnoreCase);
        var normalized = new List<LauncherCustomExecutable>(executables.Count);
        bool changed = false;

        foreach (LauncherCustomExecutable? executable in executables)
        {
            if (executable == null ||
                !displayNames.Add(executable.DisplayName) ||
                !executableNames.Add(executable.ExecutableName))
            {
                changed = true;
                continue;
            }

            normalized.Add(executable);
        }

        return changed ? normalized : executables;
    }

    private static SupportedGame? NormalizeGame(SupportedGame? game)
    {
        return game is SupportedGame.Generals or SupportedGame.ZeroHour ? game : null;
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path.Trim()))
        {
            return null;
        }

        try
        {
            return LexicalPath.NormalizeFullPath(path.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }
}
