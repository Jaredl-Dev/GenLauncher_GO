using System;
using System.IO;
using GenLauncherGO.Core.IO;

namespace GenLauncherGO.Core.Startup;

/// <summary>
/// Defines the canonical launcher-owned folder layout and supported game file names.
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

    public const string BinkLibraryFileName = "BINKW32.DLL";

    public const string ZeroHourWindowArchiveFileName = "WindowZH.big";

    public const string GeneralsWindowArchiveFileName = "Window.big";

    public const string ZeroHourCommunityExecutableFileName = "generalszh.exe";

    public const string GeneralsCommunityExecutableFileName = "generalsv.exe";

    public const string GeneralsOnlineExecutableFileName = "generalsonlinezh.exe";

    public const string ZeroHourCommunityWorldBuilderExecutableFileName = "worldbuilderzh.exe";

    public const string GeneralsCommunityWorldBuilderExecutableFileName = "worldbuilderv.exe";

    public const string VanillaWorldBuilderExecutableFileName = "WorldBuilder.exe";

    /// <summary>
    /// Normalizes a root-level Windows executable file name.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the value is not a safe root-level <c>.exe</c> file name.
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
    /// Gets the community game executable for a managed game.
    /// </summary>
    public static string GetCommunityGameExecutableName(SupportedGame managedGame)
    {
        return managedGame switch
        {
            SupportedGame.Generals => GeneralsCommunityExecutableFileName,
            SupportedGame.ZeroHour => ZeroHourCommunityExecutableFileName,
            _ => throw new ArgumentOutOfRangeException(
                nameof(managedGame),
                managedGame,
                "A supported game is required."),
        };
    }

    /// <summary>
    /// Gets the community World Builder executable for a managed game.
    /// </summary>
    public static string GetCommunityWorldBuilderExecutableName(SupportedGame managedGame)
    {
        return managedGame switch
        {
            SupportedGame.Generals => GeneralsCommunityWorldBuilderExecutableFileName,
            SupportedGame.ZeroHour => ZeroHourCommunityWorldBuilderExecutableFileName,
            _ => throw new ArgumentOutOfRangeException(
                nameof(managedGame),
                managedGame,
                "A supported game is required."),
        };
    }

    /// <summary>
    /// Gets the launcher-owned per-title directory name for a supported game.
    /// </summary>
    internal static string GetGameDataFolderName(SupportedGame managedGame)
    {
        return managedGame switch
        {
            SupportedGame.Generals => GeneralsDataFolderName,
            SupportedGame.ZeroHour => ZeroHourDataFolderName,
            _ => throw new ArgumentOutOfRangeException(
                nameof(managedGame),
                managedGame,
                "A supported game is required."),
        };
    }
}
