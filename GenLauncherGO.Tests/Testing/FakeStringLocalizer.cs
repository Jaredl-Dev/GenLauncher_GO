using System;
using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Resolves test localization keys from an in-memory dictionary whose keys must exist in the shipped neutral
///     resource.
/// </summary>
/// <remarks>
///     Validating the keys is what stops a fixture from asserting text the product could never produce: a test that
///     seeds a key nobody ships still passes while the feature it claims to cover is broken.
/// </remarks>
internal sealed class FakeStringLocalizer : ILauncherStringLocalizer
{
    /// <summary>
    ///     Creates a fallback value for missing keys.
    /// </summary>
    private readonly Func<string, string> _fallback;

    /// <summary>
    ///     The configured localized values.
    /// </summary>
    private readonly IReadOnlyDictionary<string, string> _values;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeStringLocalizer" /> class.
    /// </summary>
    public FakeStringLocalizer()
        : this(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LatestVersion"] = "Latest version: "
        })
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeStringLocalizer" /> class.
    /// </summary>
    /// <param name="values">The explicit localized values.</param>
    public FakeStringLocalizer(IReadOnlyDictionary<string, string> values)
        : this(values, key => key)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeStringLocalizer" /> class.
    /// </summary>
    /// <param name="values">The explicit localized values.</param>
    /// <param name="fallback">The value factory used when a key is missing.</param>
    public FakeStringLocalizer(
        IReadOnlyDictionary<string, string> values,
        Func<string, string> fallback)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(fallback);

        ValidateKeys(values);

        _values = values;
        _fallback = fallback;
    }

    /// <inheritdoc />
    public string this[string key] => _values.TryGetValue(key, out string? value) ? value : _fallback(key);

    /// <summary>
    ///     Builds a localizer from a shared set, replacing only the values this test is asserting on.
    /// </summary>
    public static FakeStringLocalizer Create(
        IReadOnlyDictionary<string, string> baseSet,
        params (string Key, string Value)[] overrides)
    {
        ArgumentNullException.ThrowIfNull(baseSet);
        ArgumentNullException.ThrowIfNull(overrides);

        var values = new Dictionary<string, string>(baseSet, StringComparer.Ordinal);
        foreach ((string key, string value) in overrides)
        {
            values[key] = value;
        }

        return new FakeStringLocalizer(values);
    }

    private static void ValidateKeys(IReadOnlyDictionary<string, string> values)
    {
        var unknownKeys = values.Keys
            .Where(key => !LocalizationResourceKeys.Contains(key))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (unknownKeys.Count == 0)
        {
            return;
        }

        throw new ArgumentException(
            "These localization keys are not in Strings.resx, so no production caller can ask for them: " +
            $"{string.Join(", ", unknownKeys)}. Assert on a key the launcher actually ships.",
            nameof(values));
    }
}
