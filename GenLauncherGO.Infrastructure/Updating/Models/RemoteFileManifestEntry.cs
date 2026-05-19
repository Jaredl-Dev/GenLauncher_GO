namespace GenLauncherGO.Infrastructure.Updating.Models;

internal sealed record RemoteFileManifestEntry(
    string FileName,
    string Hash,
    ulong Size);
