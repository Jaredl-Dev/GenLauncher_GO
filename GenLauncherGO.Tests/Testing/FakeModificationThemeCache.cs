using System.Collections.Generic;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Keeps cached palettes in memory so tests can assert what was cached without touching disk.
/// </summary>
internal sealed class FakeModificationThemeCache : IModificationThemeCache
{
    private readonly Dictionary<(string Name, string Version), LauncherContentTheme> _entries = [];

    public IReadOnlyDictionary<(string Name, string Version), LauncherContentTheme> Entries => _entries;

    public void Save(string modificationName, string version, LauncherContentTheme theme)
    {
        _entries[(modificationName, version)] = theme;
    }

    public LauncherContentTheme? Load(string modificationName, string version)
    {
        return _entries.TryGetValue((modificationName, version), out LauncherContentTheme? theme) ? theme : null;
    }
}
