using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GenLauncherGO.Tests.UI.Shared.Localization;

public sealed partial class LocalizationResourceParityTests
{
    private static readonly Regex _compositeFormatItemPattern = CompositeFormatItemPattern();

    [Fact]
    public void EveryLocalizationResource_HasNeutralKeysAndNonBlankValues()
    {
        string[] requiredResourceFileNames =
        [
            "Strings.resx",
            "Strings.ar.resx",
            "Strings.de.resx",
            "Strings.es.resx",
            "Strings.fr.resx",
            "Strings.hr.resx",
            "Strings.pt.resx",
            "Strings.ru.resx",
            "Strings.tr.resx",
            "Strings.uk.resx",
            "Strings.zh.resx"
        ];
        string resourcesDirectory = Path.Combine(AppContext.BaseDirectory, "LocalizationResources");
        FileInfo[] resourceFiles = new DirectoryInfo(resourcesDirectory)
            .GetFiles("Strings*.resx")
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();

        resourceFiles.Select(file => file.Name).Should().BeEquivalentTo(
            requiredResourceFileNames,
            "every supported localization must remain part of the complete resource set");

        Dictionary<string, string> neutralValues = ReadValues(
            resourceFiles.Single(file => file.Name == "Strings.resx"));
        var neutralPlaceholderIndexes = neutralValues.ToDictionary(
            entry => entry.Key,
            entry => _compositeFormatItemPattern.IsMatch(entry.Value)
                ? ReadCompositeFormatArgumentIndexes("Strings.resx", entry.Key, entry.Value)
                : [],
            StringComparer.Ordinal);

        foreach (FileInfo resourceFile in resourceFiles)
        {
            Dictionary<string, string> localizedValues = ReadValues(resourceFile);

            localizedValues.Keys.Should().BeEquivalentTo(
                LocalizationResourceKeys.All,
                $"{resourceFile.Name} must contain the same keys as Strings.resx");
            localizedValues.Where(entry => string.IsNullOrWhiteSpace(entry.Value)).Should().BeEmpty(
                $"{resourceFile.Name} must not contain blank localized values");
            foreach ((string key, string value) in localizedValues)
            {
                int[] localizedPlaceholderIndexes = FindCompositeFormatArgumentIndexes(value);
                if (neutralPlaceholderIndexes[key].Length == 0 && localizedPlaceholderIndexes.Length == 0)
                {
                    continue;
                }

                ReadCompositeFormatArgumentIndexes(resourceFile.Name, key, value).Should().Equal(
                    neutralPlaceholderIndexes[key],
                    $"{resourceFile.Name}:{key} must preserve the neutral resource placeholders");
            }
        }
    }

    /// <summary>
    ///     Holds every key the launcher actually asks for to the shipped neutral resource.
    /// </summary>
    /// <remarks>
    ///     Both request paths fall back to the key itself when it is missing, so a typo or a renamed resource ships
    ///     as a raw identifier in the UI and nothing fails. Markup resolves at load time and never throws, and the
    ///     indexer returns the key as its diagnostic placeholder, which leaves this scan as the only gate.
    /// </remarks>
    [Fact]
    public void EveryLocalizationKeyTheUiRequests_IsShippedByTheNeutralResource()
    {
        List<(string Key, string SourceFile)> requests = ReadRequestedLocalizationKeys();

        requests.Should().NotBeEmpty("the scan must find the keys the launcher asks for");

        var unknownRequests = requests
            .Where(request => !LocalizationResourceKeys.Contains(request.Key))
            .Select(request => $"{request.SourceFile}: {request.Key}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        unknownRequests.Should().BeEmpty(
            "Strings.resx must ship every key the launcher requests, but it is missing:{0}{1}",
            Environment.NewLine,
            string.Join(Environment.NewLine, unknownRequests));
    }

    private static Dictionary<string, string> ReadValues(FileInfo resourceFile)
    {
        // Loaded through a stream: the path overload routes through System.Uri, which rejects a path past
        // MAX_PATH with an unrelated "hostname could not be parsed" error. Checkouts sit at arbitrary depths.
        using FileStream resourceStream = resourceFile.OpenRead();
        var document = XDocument.Load(resourceStream);
        XElement root = document.Root ??
                        throw new InvalidDataException($"{resourceFile.Name} has no root element.");

        return root.Elements("data").ToDictionary(
            element => element.Attribute("name")?.Value ??
                       throw new InvalidDataException($"{resourceFile.Name} contains an unnamed resource."),
            element => element.Element("value")?.Value ??
                       throw new InvalidDataException(
                           $"{resourceFile.Name} contains a resource without a value."),
            StringComparer.Ordinal);
    }

    /// <summary>
    ///     Collects the localization keys the UI project requests from markup and from code, with the file each one
    ///     came from so a failure names the site to fix.
    /// </summary>
    private static List<(string Key, string SourceFile)> ReadRequestedLocalizationKeys()
    {
        DirectoryInfo uiProject = UiProjectDirectory();
        List<(string Key, string SourceFile)> requests = [];

        foreach (FileInfo sourceFile in EnumerateAuthoredFiles(uiProject, "*.axaml"))
        {
            AddRequests(requests, uiProject, sourceFile, MarkupLocalizationKeyPattern());
        }

        foreach (FileInfo sourceFile in EnumerateAuthoredFiles(uiProject, "*.cs"))
        {
            AddRequests(requests, uiProject, sourceFile, CodeLocalizationKeyPattern());
        }

        return requests;
    }

    private static void AddRequests(
        List<(string Key, string SourceFile)> requests,
        DirectoryInfo uiProject,
        FileInfo sourceFile,
        Regex pattern)
    {
        string relativePath = Path.GetRelativePath(uiProject.FullName, sourceFile.FullName);
        foreach (Match match in pattern.Matches(File.ReadAllText(sourceFile.FullName)))
        {
            requests.Add((match.Groups["key"].Value, relativePath));
        }
    }

    /// <summary>
    ///     Enumerates the files a person wrote, skipping build output so a stale copy never joins the scan.
    /// </summary>
    private static IEnumerable<FileInfo> EnumerateAuthoredFiles(DirectoryInfo uiProject, string searchPattern)
    {
        return uiProject
            .EnumerateFiles(searchPattern, SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(uiProject, file));
    }

    private static bool IsBuildOutput(DirectoryInfo uiProject, FileInfo file)
    {
        string relativePath = Path.GetRelativePath(uiProject.FullName, file.FullName);
        string topLevelSegment = relativePath.Split(Path.DirectorySeparatorChar)[0];
        return string.Equals(topLevelSegment, "bin", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(topLevelSegment, "obj", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Locates the UI project's sources, which are what the shipped markup is authored in.
    /// </summary>
    private static DirectoryInfo UiProjectDirectory()
    {
        for (DirectoryInfo? candidate = new(AppContext.BaseDirectory);
             candidate != null;
             candidate = candidate.Parent)
        {
            DirectoryInfo uiProject = new(Path.Combine(candidate.FullName, "GenLauncherGO.UI"));
            if (uiProject.Exists)
            {
                return uiProject;
            }
        }

        throw new InvalidOperationException(
            $"No GenLauncherGO.UI directory above '{AppContext.BaseDirectory}', so the markup cannot be scanned.");
    }

    private static int[] ReadCompositeFormatArgumentIndexes(
        string resourceFileName,
        string key,
        string value)
    {
        try
        {
            _ = CompositeFormat.Parse(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException(
                $"{resourceFileName}:{key} is not a valid composite format string.",
                exception);
        }

        return FindCompositeFormatArgumentIndexes(value);
    }

    private static int[] FindCompositeFormatArgumentIndexes(string value)
    {
        return _compositeFormatItemPattern.Matches(value)
            .Select(match => int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .Order()
            .ToArray();
    }

    [GeneratedRegex(@"(?<!\{)\{(?<index>\d+)(?:,[^}:]+)?(?::[^}]*)?\}(?!\})", RegexOptions.CultureInvariant)]
    private static partial Regex CompositeFormatItemPattern();

    [GeneratedRegex(@"\{localization:Loc\s+(?<key>[A-Za-z0-9_]+)\s*\}", RegexOptions.CultureInvariant)]
    private static partial Regex MarkupLocalizationKeyPattern();

    [GeneratedRegex(@"stringLocalizer\[""(?<key>[A-Za-z0-9_]+)""\]", RegexOptions.CultureInvariant)]
    private static partial Regex CodeLocalizationKeyPattern();
}
