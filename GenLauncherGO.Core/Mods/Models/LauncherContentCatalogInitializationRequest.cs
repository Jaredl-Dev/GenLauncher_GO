using System;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Core.Mods.Models;

/// <summary>
///     Initializes one game catalog; a missing remote manifest URI selects local-only mode.
/// </summary>
public sealed record LauncherContentCatalogInitializationRequest(
    Uri? RemoteManifestUri,
    LauncherPaths Paths);
