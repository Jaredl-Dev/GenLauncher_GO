namespace GenLauncherGO.Features.Launching;

internal sealed class ExecutableOption(
    string displayName,
    string executablePath,
    bool isAvailable,
    bool isBuiltIn,
    bool isGeneralsOnline = false,
    bool isRetail = false,
    string groupDisplayName = "",
    bool showGroupHeader = false)
{
    public string DisplayName { get; } = displayName;

    public string ExecutablePath { get; } = executablePath;

    public bool IsAvailable { get; } = isAvailable;

    public bool IsUnavailable => !IsAvailable;

    public bool IsBuiltIn { get; } = isBuiltIn;

    public bool CanRemove => !IsBuiltIn;

    public bool IsGeneralsOnline { get; } = isGeneralsOnline;

    public bool IsRetail { get; } = isRetail;

    public string GroupDisplayName { get; } = groupDisplayName;

    public bool ShowGroupHeader { get; } = showGroupHeader;

    public override string ToString()
    {
        return DisplayName;
    }
}
