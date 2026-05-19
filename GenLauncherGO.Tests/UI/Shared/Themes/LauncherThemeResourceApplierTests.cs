using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Media;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.Tests.UI.Shared.Themes;

[Collection("Avalonia")]
public sealed partial class LauncherThemeResourceApplierTests
{
    [Theory]
    [InlineData(SupportedGame.Generals)]
    [InlineData(SupportedGame.ZeroHour)]
    public void Preset_LoadsBackgroundThroughInitializedAvaloniaApplication(SupportedGame game)
    {
        StaTestRunner.Run(() =>
        {
            ColorsInfo colors = LauncherThemePresets.Create(game);

            colors.GenLauncherBackgroundImage.Should().NotBeNull();
            colors.GenLauncherBackgroundImage.Should().BeAssignableTo<IImmutableBrush>();
            colors.GenLauncherBackgroundImage!.Stretch.Should().Be(Stretch.Fill);
            colors.GenLauncherBackgroundImage.Source.Should().NotBeNull();
        });
    }

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

    /// <summary>
    ///     Selection and hover deliberately share one brush, so a hovered row previews what selecting it looks like.
    /// </summary>
    [Fact]
    public void Apply_GivesListSelectionAndHoverTheSameBrush()
    {
        StaTestRunner.Run(() =>
        {
            ResourceDictionary resources = [];
            ColorsInfo colors = LauncherThemePresets.Create(SupportedGame.Generals);

            LauncherThemeResourceApplier.Apply(resources, colors);

            resources["ListBoxMouseOverItemBackground"].Should()
                .BeSameAs(resources["ListBoxSelectedItemBackground"]);
        });
    }

    /// <summary>
    ///     The hover wash carries the palette's button accent across the middle of the control and fades into the
    ///     palette's own panel scrim at both edges, so it sits on the surface behind it instead of on a fixed black.
    /// </summary>
    [Fact]
    public void Apply_FadesTheHoverWashIntoThePaletteScrim()
    {
        StaTestRunner.Run(() =>
        {
            ResourceDictionary resources = [];
            ColorsInfo colors = LauncherThemePresets.Create(SupportedGame.ZeroHour);

            LauncherThemeResourceApplier.Apply(resources, colors);

            resources["GenLauncherHoverBackground"].Should().BeAssignableTo<IGradientBrush>()
                .Which.GradientStops.Select(stop => stop.Color).Should().Equal(
                    colors.GenLauncherLightBackGround.Color,
                    colors.GenLauncherButtonSelectionColor,
                    colors.GenLauncherButtonSelectionColor,
                    colors.GenLauncherLightBackGround.Color);
        });
    }

    /// <summary>
    ///     The hover accent is the only derived colour in the theme, so it is the only one worth pinning: it has to
    ///     read as the palette's own accent lit up, not as an unrelated colour, whatever accent a theme supplies.
    /// </summary>
    [Theory]
    [InlineData("#FF000000", "#FFA6A6A6")]
    [InlineData("#FFFFFFFF", "#FFFFFFFF")]
    [InlineData("#8000E3FF", "#80A6F5FF")]
    public void Apply_DerivesTheHoverAccentByLighteningTheBorderColour(string border, string expectedHighlight)
    {
        StaTestRunner.Run(() =>
        {
            ResourceDictionary resources = [];

            LauncherThemeResourceApplier.Apply(resources, TestLauncherTheme.Create(border: border));

            resources["GenLauncherBorderHighlightColor"].Should().BeAssignableTo<ISolidColorBrush>()
                .Which.Color.Should().Be(Color.Parse(expectedHighlight));
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
        foreach (FileInfo markupFile in UiProjectDirectory().EnumerateFiles("*.axaml", SearchOption.AllDirectories))
        {
            foreach (Match match in ThemedDynamicResourcePattern().Matches(File.ReadAllText(markupFile.FullName)))
            {
                keys.Add(match.Groups["key"].Value);
            }
        }

        return keys;
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

    [GeneratedRegex(
        @"\{DynamicResource\s+(?<key>(?:GenLauncher|ListBox|Dialog)[A-Za-z0-9]*)\s*\}",
        RegexOptions.CultureInvariant)]
    private static partial Regex ThemedDynamicResourcePattern();
}
