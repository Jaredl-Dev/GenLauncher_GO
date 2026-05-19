using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Tests.UI.Shared.Localization;

public sealed class LocalizationResourceParityTests
{
    private static readonly Regex _compositeFormatItemPattern = new(
        @"(?<!\{)\{(?<index>\d+)(?:,[^}:]+)?(?::[^}]*)?\}(?!\})",
        RegexOptions.CultureInvariant);

    [Fact]
    public void EveryLocalizationResourceHasNeutralKeysAndNonBlankValues()
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
            "Strings.zh.resx",
        ];
        string resourcesDirectory = Path.Combine(AppContext.BaseDirectory, "LocalizationResources");
        FileInfo[] resourceFiles = new DirectoryInfo(resourcesDirectory)
            .GetFiles("Strings*.resx")
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();

        resourceFiles.Select(file => file.Name).Should().BeEquivalentTo(
            requiredResourceFileNames,
            because: "every supported localization must remain part of the complete resource set");

        Dictionary<string, string> neutralValues = ReadValues(
            resourceFiles.Single(file => file.Name == "Strings.resx"));
        var neutralPlaceholderIndexes = neutralValues.ToDictionary(
            entry => entry.Key,
            entry => _compositeFormatItemPattern.IsMatch(entry.Value)
                ? ReadCompositeFormatArgumentIndexes("Strings.resx", entry.Key, entry.Value)
                : Array.Empty<int>(),
            StringComparer.Ordinal);

        foreach (FileInfo resourceFile in resourceFiles)
        {
            Dictionary<string, string> localizedValues = ReadValues(resourceFile);

            localizedValues.Keys.Should().BeEquivalentTo(
                neutralValues.Keys,
                because: $"{resourceFile.Name} must contain the same keys as Strings.resx");
            localizedValues.Where(entry => string.IsNullOrWhiteSpace(entry.Value)).Should().BeEmpty(
                because: $"{resourceFile.Name} must not contain blank localized values");
            foreach ((string key, string value) in localizedValues)
            {
                int[] localizedPlaceholderIndexes = FindCompositeFormatArgumentIndexes(value);
                if (neutralPlaceholderIndexes[key].Length == 0 && localizedPlaceholderIndexes.Length == 0)
                {
                    continue;
                }

                ReadCompositeFormatArgumentIndexes(resourceFile.Name, key, value).Should().Equal(
                    neutralPlaceholderIndexes[key],
                    because: $"{resourceFile.Name}:{key} must preserve the neutral resource placeholders");
            }

            string generalsExecutableRequirement =
                localizedValues["GeneralsExecutableRequirement"];
            generalsExecutableRequirement.Should().Contain(
                LauncherFileSystemLayout.GeneralsCommunityExecutableFileName);
            generalsExecutableRequirement.Should().Contain("TheSuperHackers");

            string zeroHourExecutableRequirement =
                localizedValues["ZeroHourExecutableRequirement"];
            zeroHourExecutableRequirement.Should().Contain(
                LauncherFileSystemLayout.ZeroHourCommunityExecutableFileName);
            zeroHourExecutableRequirement.Should().Contain(
                LauncherFileSystemLayout.GeneralsOnlineExecutableFileName);
            zeroHourExecutableRequirement.Should().Contain("TheSuperHackers");
            zeroHourExecutableRequirement.Should().Contain("GeneralsOnline");
        }
    }

    private static Dictionary<string, string> ReadValues(FileInfo resourceFile)
    {
        var document = XDocument.Load(resourceFile.FullName);
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
            .Select(match => Int32.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .Order()
            .ToArray();
    }
}
