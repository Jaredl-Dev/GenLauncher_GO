using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Reads the neutral localization resource once and caches its key set.
/// </summary>
/// <remarks>
///     This is the authority a test localizer validates against, so a fixture cannot invent a key the product does
///     not ship and then assert a value nothing would ever produce.
/// </remarks>
internal static class LocalizationResourceKeys
{
    private const string NeutralResourceFileName = "Strings.resx";

    private const string ResourceFolderName = "LocalizationResources";

    private static readonly Lazy<IReadOnlySet<string>> _keys = new(ReadNeutralKeys);

    public static IReadOnlySet<string> All => _keys.Value;

    public static bool Contains(string key)
    {
        return All.Contains(key);
    }

    private static IReadOnlySet<string> ReadNeutralKeys()
    {
        string resourceFilePath = Path.Combine(
            AppContext.BaseDirectory,
            ResourceFolderName,
            NeutralResourceFileName);
        // Loaded through a stream rather than the path overload: that overload routes the path through
        // System.Uri, which rejects a path long enough to exceed MAX_PATH with an unrelated
        // "hostname could not be parsed" error. Test checkouts sit at arbitrary depths.
        using FileStream resourceStream = File.OpenRead(resourceFilePath);
        var document = XDocument.Load(resourceStream);
        XElement root = document.Root ??
                        throw new InvalidDataException($"{NeutralResourceFileName} has no root element.");

        return root.Elements("data")
            .Select(element => element.Attribute("name")?.Value ??
                               throw new InvalidDataException(
                                   $"{NeutralResourceFileName} contains an unnamed resource."))
            .ToHashSet(StringComparer.Ordinal);
    }
}
