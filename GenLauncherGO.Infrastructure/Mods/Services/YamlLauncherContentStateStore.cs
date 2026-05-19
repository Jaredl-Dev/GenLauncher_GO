using System;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Contracts;
using GenLauncherGO.Infrastructure.Mods.Models;
using GenLauncherGO.Infrastructure.Persistence.Services;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Mods.Services;

/// <summary>
///     Stores launcher content state in a YAML-backed document.
/// </summary>
internal sealed class YamlLauncherContentStateStore : ILauncherContentStateStore
{
    private readonly IAtomicFileWriter _atomicFileWriter;

    private readonly ILogger<YamlDocumentStore<LauncherContentState>> _logger;

    public YamlLauncherContentStateStore(
        IAtomicFileWriter atomicFileWriter,
        ILogger<YamlDocumentStore<LauncherContentState>> logger)
    {
        _atomicFileWriter = atomicFileWriter ?? throw new ArgumentNullException(nameof(atomicFileWriter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public LauncherContentState Load(LauncherPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return CreateDocumentStore(paths).Load(new LauncherContentState());
    }

    public void Save(LauncherPaths paths, LauncherContentState state)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(state);

        CreateDocumentStore(paths).Save(state);
    }

    private IYamlDocumentStore<LauncherContentState> CreateDocumentStore(LauncherPaths paths)
    {
        return new YamlDocumentStore<LauncherContentState>(
            paths.LauncherDataFilePath,
            _atomicFileWriter,
            _logger);
    }
}
