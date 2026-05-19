using System.Resources;

namespace GenLauncherGO.UI.Resources;

/// <summary>
///     Provides resource-manager access for launcher UI string resources.
/// </summary>
public static class Strings
{
    /// <summary>
    ///     The manifest resource base name for launcher UI strings.
    /// </summary>
    private const string ResourceBaseName = "GenLauncherGO.UI.Resources.Strings";

    /// <summary>
    ///     Gets the launcher UI string resource manager.
    /// </summary>
    public static ResourceManager ResourceManager { get; } = new(ResourceBaseName, typeof(Strings).Assembly);
}
