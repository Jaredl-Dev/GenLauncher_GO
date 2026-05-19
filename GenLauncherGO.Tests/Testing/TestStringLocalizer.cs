using System;
using System.Collections.Generic;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
/// Resolves test localization keys from an optional in-memory dictionary.
/// </summary>
internal sealed class TestStringLocalizer : ILauncherStringLocalizer
{
    /// <summary>
    /// The configured localized values.
    /// </summary>
    private readonly IReadOnlyDictionary<string, string> _values;

    /// <summary>
    /// Creates a fallback value for missing keys.
    /// </summary>
    private readonly Func<string, string> _fallback;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestStringLocalizer"/> class.
    /// </summary>
    public TestStringLocalizer()
        : this(new Dictionary<string, string>
        {
            ["LatestVersion"] = "Latest version: ",
        })
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestStringLocalizer"/> class.
    /// </summary>
    /// <param name="values">The explicit localized values.</param>
    public TestStringLocalizer(IReadOnlyDictionary<string, string> values)
        : this(values, key => key)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestStringLocalizer"/> class.
    /// </summary>
    /// <param name="values">The explicit localized values.</param>
    /// <param name="fallback">The value factory used when a key is missing.</param>
    public TestStringLocalizer(
        IReadOnlyDictionary<string, string> values,
        Func<string, string> fallback)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    /// <inheritdoc />
    public string this[string key] => _values.TryGetValue(key, out string? value) ? value : _fallback(key);
}
