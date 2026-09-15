namespace GenLauncherGO.Features.Updating;

internal sealed record RemoteFileManifestEntry(
    string FileName,
    string Hash,
    ulong Size);
