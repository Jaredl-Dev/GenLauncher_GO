using System;
using System.Collections.Generic;
using System.IO;
using GenLauncherGO.Core.IO;

namespace GenLauncherGO.Core.Startup;

/// <summary>
///     Defines the canonical launcher-owned folder layout and supported game file names.
/// </summary>
public static class LauncherFileSystemLayout
{
    public const string LauncherDataFolderName = "GenLauncherGO Data";

    internal const string GeneralsDataFolderName = "C&C Generals Data";

    internal const string ZeroHourDataFolderName = "C&C Zero Hour Data";

    internal const string RuntimeFolderName = "Runtime";

    internal const string CacheFolderName = "Cache";

    internal const string ImagesFolderName = "Images";

    internal const string ModsFolderName = "Mods";

    internal const string LogsFolderName = "Logs";

    internal const string TempFolderName = "Temp";

    internal const string DeploymentFolderName = "Deployment";

    internal const string IntegrityFolderName = "Integrity";

    internal const string StateFolderName = "State";

    internal const string PackageBackupsFolderName = "PackageBackups";

    public const string PackagesFolderName = "Packages";

    public const string AddonsFolderName = "Addons";

    public const string PatchesFolderName = "Patches";

    public const string ZeroHourCommunityExecutableFileName = "generalszh.exe";

    public const string GeneralsCommunityExecutableFileName = "generalsv.exe";

    public const string GeneralsOnlineExecutableFileName = "generalsonlinezh.exe";

    public const string RetailGameExecutableFileName = "generals.exe";

    public const string ZeroHourCommunityWorldBuilderExecutableFileName = "worldbuilderzh.exe";

    public const string GeneralsCommunityWorldBuilderExecutableFileName = "worldbuilderv.exe";

    public const string RetailWorldBuilderExecutableFileName = "WorldBuilder.exe";

    private static readonly IReadOnlyList<string> _generalsGameExecutableNames = Array.AsReadOnly<string>(
    [
        GeneralsCommunityExecutableFileName,
        RetailGameExecutableFileName
    ]);

    private static readonly IReadOnlyList<string> _zeroHourGameExecutableNames = Array.AsReadOnly<string>(
    [
        GeneralsOnlineExecutableFileName,
        ZeroHourCommunityExecutableFileName,
        RetailGameExecutableFileName
    ]);

    private static readonly IReadOnlyList<string> _generalsWorldBuilderExecutableNames = Array.AsReadOnly<string>(
    [
        RetailWorldBuilderExecutableFileName,
        GeneralsCommunityWorldBuilderExecutableFileName
    ]);

    private static readonly IReadOnlyList<string> _zeroHourWorldBuilderExecutableNames = Array.AsReadOnly<string>(
    [
        RetailWorldBuilderExecutableFileName,
        ZeroHourCommunityWorldBuilderExecutableFileName
    ]);

    /// <summary>
    ///     Gets the built-in game executables accepted for a managed game, in launcher display order.
    /// </summary>
    public static IReadOnlyList<string> GetBuiltInGameExecutableNames(SupportedGame managedGame)
    {
        return PerGame.Select(
            managedGame,
            _generalsGameExecutableNames,
            _zeroHourGameExecutableNames,
            nameof(managedGame));
    }

    /// <summary>
    ///     Gets the built-in World Builder executables accepted for a managed game, in launcher display order.
    /// </summary>
    public static IReadOnlyList<string> GetBuiltInWorldBuilderExecutableNames(SupportedGame managedGame)
    {
        return PerGame.Select(
            managedGame,
            _generalsWorldBuilderExecutableNames,
            _zeroHourWorldBuilderExecutableNames,
            nameof(managedGame));
    }

    /// <summary>
    ///     Normalizes a root-level Windows executable file name.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     Thrown when the value is not a safe root-level <c>.exe</c> file name.
    /// </exception>
    public static string NormalizeExecutableFileName(string? executableName)
    {
        string normalizedName = LexicalPath.NormalizePathSegment(
            executableName,
            nameof(executableName));
        if (!string.Equals(Path.GetExtension(normalizedName), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Executable file names must use the .exe extension.", nameof(executableName));
        }

        return normalizedName;
    }

    /// <summary>
    ///     Gets the launcher-owned per-title directory name for a supported game.
    /// </summary>
    internal static string GetGameDataFolderName(SupportedGame managedGame)
    {
        return PerGame.Select(
            managedGame,
            GeneralsDataFolderName,
            ZeroHourDataFolderName,
            nameof(managedGame));
    }
}
