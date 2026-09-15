using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GenLauncherGO.Resources;
using GenLauncherGO.Shared.Localization;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Resolves neutral production resources while allowing a test to override the values it distinguishes.
/// </summary>
/// <remarks>
///     Reading the neutral resource keeps production localization as the single source of truth and makes test output
///     independent of the machine's UI culture.
/// </remarks>
internal sealed class FakeStringLocalizer : ILauncherStringLocalizer
{
    /// <summary>
    ///     The configured localized values.
    /// </summary>
    private readonly IReadOnlyDictionary<string, string> _values;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeStringLocalizer" /> class.
    /// </summary>
    public FakeStringLocalizer()
        : this(new Dictionary<string, string>(StringComparer.Ordinal))
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeStringLocalizer" /> class.
    /// </summary>
    /// <param name="values">The explicit localized values.</param>
    public FakeStringLocalizer(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        ValidateKeys(values);

        _values = values;
    }

    /// <inheritdoc />
    public string this[string key] => _values.TryGetValue(key, out string? value)
        ? value
        : Strings.ResourceManager.GetString(key, CultureInfo.InvariantCulture) ?? key;

    private static void ValidateKeys(IReadOnlyDictionary<string, string> values)
    {
        var unknownKeys = values.Keys
            .Where(key => Strings.ResourceManager.GetString(key, CultureInfo.InvariantCulture) == null)
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
