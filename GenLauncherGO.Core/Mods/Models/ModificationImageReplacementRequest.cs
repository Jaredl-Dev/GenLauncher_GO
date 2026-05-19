using System;

namespace GenLauncherGO.Core.Mods.Models;

public sealed class ModificationImageReplacementRequest
{
    public ModificationImageReplacementRequest(
        string modificationName,
        string imageBaseName,
        string sourceImagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modificationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageBaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceImagePath);

        ModificationName = modificationName;
        ImageBaseName = imageBaseName;
        SourceImagePath = sourceImagePath;
    }

    public string ModificationName { get; }

    public string ImageBaseName { get; }

    public string SourceImagePath { get; }
}
