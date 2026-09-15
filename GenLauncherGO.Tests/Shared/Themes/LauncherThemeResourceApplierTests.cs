using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Media;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Shared.Themes;

namespace GenLauncherGO.Tests.Shared.Themes;

[Collection("Avalonia")]
public sealed partial class LauncherThemeResourceApplierTests
{
    /// <summary>
    ///     The expected keys come from the shipped markup rather than from a list kept here, so a control added with
    ///     a new themed brush fails this test instead of silently rendering with no brush at all.
    /// </summary>
    [Fact]
    public void Apply_PublishesEveryColourTheMarkupBindsTo()
    {
        StaTestRunner.Run(() =>
        {
            ResourceDictionary resources = [];
            ColorsInfo colors = LauncherThemePresets.Create(SupportedGame.Generals);
            IReadOnlyCollection<string> boundKeys = ReadThemedKeysBoundByMarkup();

            LauncherThemeResourceApplier.Apply(resources, colors);

            boundKeys.Should().NotBeEmpty("the shipped markup must name the theme resources it binds to");
            boundKeys.Where(key => !resources.ContainsKey(key) || resources[key] is null).Should().BeEmpty(
                "a DynamicResource the theme never publishes leaves the control it paints unstyled");

            resources["GenLauncherBorderColor"].Should().BeSameAs(colors.GenLauncherBorderColor);
            resources["GenLauncherActiveColor"].Should().BeSameAs(colors.GenLauncherActiveColor);
            resources["GenLauncherActionTextColor"].Should().BeSameAs(colors.GenLauncherActionTextColor);
            resources["GenLauncherHeadingTextColor"].Should().BeSameAs(colors.GenLauncherHeadingTextColor);
            resources["GenLauncherDarkFillColor"].Should().BeSameAs(colors.GenLauncherDarkFillColor);
            resources["GenLauncherInactiveBorder"].Should().BeSameAs(colors.GenLauncherInactiveBorder);
            resources["GenLauncherInactiveBorder2"].Should().BeSameAs(colors.GenLauncherInactiveBorder2);
            resources["GenLauncherDefaultTextColor"].Should().BeSameAs(colors.GenLauncherDefaultTextColor);
            resources["GenLauncherErrorColor"].Should().BeSameAs(colors.GenLauncherErrorColor);
            resources["GenLauncherDisabledTextColor"].Should().BeSameAs(colors.GenLauncherDisabledTextColor);
            resources["GenLauncherChromeBackground"].Should().BeSameAs(colors.GenLauncherChromeBackground);
            resources["GenLauncherScrimColor"].Should().BeSameAs(colors.GenLauncherScrimColor);
            resources["GenLauncherLightBackGround"].Should().BeSameAs(colors.GenLauncherLightBackGround);
            resources["GenLauncherDarkBackGround"].Should().BeSameAs(colors.GenLauncherDarkBackGround);
            resources["GenLauncherListBoxSelectionColor2"].Should().Be(colors.ListSelectionMiddleColor);
            resources["GenLauncherBackGroundImage"].Should().BeSameAs(colors.GenLauncherBackgroundImage);

            // A dialog row is filled with the row-selection colour, not with the full-strength button accent that
            // would overpower the label sitting on top of it.
            resources["DialogListBoxSelectedItemBackground"].Should().BeAssignableTo<ISolidColorBrush>()
                .Which.Color.Should().Be(colors.ListSelectionMiddleColor);
        });
    }

    [Fact]
    public void ApplyWhenBackgroundImage_IsExcludedKeepsExistingBackgroundResource()
    {
        StaTestRunner.Run(() =>
        {
            ResourceDictionary resources = new()
            {
                ["GenLauncherBackGroundImage"] = "existing"
            };
            ColorsInfo colors = LauncherThemePresets.Create(SupportedGame.ZeroHour);

            LauncherThemeResourceApplier.Apply(resources, colors, false);

            resources["GenLauncherBackGroundImage"].Should().Be("existing");
        });
    }

    /// <summary>
    ///     Collects the theme resource keys the shipped AXAML resolves through <c>DynamicResource</c>.
    /// </summary>
    private static IReadOnlyCollection<string> ReadThemedKeysBoundByMarkup()
    {
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (FileInfo markupFile in TestAppProjectSources.Enumerate("*.axaml"))
        {
            foreach (Match match in ThemedDynamicResourcePattern().Matches(File.ReadAllText(markupFile.FullName)))
            {
                keys.Add(match.Groups["key"].Value);
            }
        }

        return keys;
    }
    [GeneratedRegex(
        @"\{DynamicResource\s+(?<key>(?:GenLauncher|ListBox|Dialog)[A-Za-z0-9]*)\s*\}",
        RegexOptions.CultureInvariant)]
    private static partial Regex ThemedDynamicResourcePattern();
}
