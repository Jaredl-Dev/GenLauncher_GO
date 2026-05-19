using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Restores the application-scoped launcher theme resources a test replaced.
/// </summary>
/// <remarks>
///     Assigning <c>LauncherRuntimeContext.Colors</c> publishes into <c>Application.Current.Resources</c>, which the
///     headless session shares across every test in the Avalonia collection. Without this, a test that previews a
///     palette leaves it behind for whatever runs next.
/// </remarks>
internal sealed class ApplicationThemeScope : IDisposable
{
    private static readonly string[] _themedKeyPrefixes = ["GenLauncher", "ListBox", "Dialog"];

    private readonly Dictionary<object, object?> _previousValues = [];

    private readonly IResourceDictionary? _resources;

    public ApplicationThemeScope()
    {
        _resources = Application.Current?.Resources;
        if (_resources is null)
        {
            return;
        }

        foreach (object key in ThemedKeys(_resources))
        {
            _previousValues[key] = _resources[key];
        }
    }

    public void Dispose()
    {
        if (_resources is null)
        {
            return;
        }

        foreach (object key in ThemedKeys(_resources))
        {
            if (!_previousValues.ContainsKey(key))
            {
                _resources.Remove(key);
            }
        }

        foreach ((object key, object? value) in _previousValues)
        {
            _resources[key] = value;
        }
    }

    private static List<object> ThemedKeys(IResourceDictionary resources)
    {
        return resources.Keys
            .Where(key => key is string name &&
                          _themedKeyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();
    }
}
