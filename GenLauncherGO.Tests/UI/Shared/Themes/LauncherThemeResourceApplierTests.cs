using System;
using Avalonia.Controls;
using Avalonia.Media;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.Tests.UI.Shared.Themes;

public sealed class LauncherThemeResourceApplierTests
{
    [Theory]
    [InlineData(SupportedGame.Generals)]
    [InlineData(SupportedGame.ZeroHour)]
    public void PresetLoadsBackgroundThroughInitializedAvaloniaApplication(SupportedGame game)
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

    [Theory]
    [InlineData(SupportedGame.ZeroHour, "#F21D2057", "#F21D2057")]
    [InlineData(SupportedGame.Generals, "#FF8A2E0D", "#FF5A210D")]
    public void PresetUsesCanonicalSelectionGradient(
        SupportedGame game,
        string expectedStart,
        string expectedMiddle)
    {
        StaTestRunner.Run(() =>
        {
            ColorsInfo colors = LauncherThemePresets.Create(game);

            colors.ListSelectionStartColor.Should().Be(Color.Parse(expectedStart));
            colors.ListSelectionMiddleColor.Should().Be(Color.Parse(expectedMiddle));
        });
    }

    [Fact]
    public void ApplyPopulatesThemeResourcesAndBrushes()
    {
        StaTestRunner.Run(() =>
        {
            ResourceDictionary resources = new();
            ColorsInfo colors = LauncherThemePresets.Create(SupportedGame.Generals);

            LauncherThemeResourceApplier.Apply(resources, colors);

            resources["GenLauncherAccentBorderColor"].Should()
                .BeAssignableTo<IImmutableSolidColorBrush>();
            resources["GenLauncherSubtleAccentBorderColor"].Should()
                .BeAssignableTo<IImmutableSolidColorBrush>();
            resources["GenLauncherAccentTextColor"].Should()
                .BeAssignableTo<IImmutableSolidColorBrush>();
            IGradientBrush buttonPointerOverBackground = resources["ButtonPointerOverBackground"]
                .Should().BeAssignableTo<IGradientBrush>().Which;
            resources["GenLauncherActiveColor"].Should().BeSameAs(colors.GenLauncherActiveColor);
            resources["GenLauncherDarkFillColor"].Should().BeSameAs(colors.GenLauncherDarkFillColor);
            resources["GenLauncherInactiveBorder"].Should().BeSameAs(colors.GenLauncherInactiveBorder);
            resources["GenLauncherInactiveBorder2"].Should().BeSameAs(colors.GenLauncherInactiveBorder2);
            resources["GenLauncherDefaultTextColor"].Should().BeSameAs(colors.GenLauncherDefaultTextColor);
            resources["GenLauncherDownloadTextColor"].Should().BeSameAs(colors.GenLauncherDownloadTextColor);
            resources["GenLauncherLightBackGround"].Should().BeSameAs(colors.GenLauncherLightBackGround);
            resources["GenLauncherListBoxSelectionColor1"].Should().Be(colors.ListSelectionStartColor);
            resources["GenLauncherListBoxSelectionColor2"].Should().Be(colors.ListSelectionMiddleColor);
            resources["GenLauncherButtonSelectionColor"].Should().Be(colors.GenLauncherButtonSelectionColor);
            resources["GenLauncherBackGroundImage"].Should().BeSameAs(colors.GenLauncherBackgroundImage);

            IGradientBrush buttonPressedBackground = resources["ButtonPressedBackground"]
                .Should().BeAssignableTo<IGradientBrush>().Which;
            buttonPointerOverBackground.GradientStops.Should().HaveCount(4);
            buttonPressedBackground.GradientStops.Should().HaveCount(4);
            for (int index = 0; index < buttonPressedBackground.GradientStops.Count; index++)
            {
                buttonPointerOverBackground.GradientStops[index].Offset.Should()
                    .Be(buttonPressedBackground.GradientStops[index].Offset);
            }

            buttonPointerOverBackground.GradientStops[0].Color.Should()
                .Be(buttonPressedBackground.GradientStops[0].Color);
            buttonPointerOverBackground.GradientStops[3].Color.Should()
                .Be(buttonPressedBackground.GradientStops[3].Color);
            GetRelativeLuminance(buttonPointerOverBackground.GradientStops[1].Color).Should()
                .BeLessThan(GetRelativeLuminance(buttonPressedBackground.GradientStops[1].Color));
            IGradientBrush selectedBackground = resources["ListBoxSelectedItemBackground"]
                .Should().BeAssignableTo<IGradientBrush>().Which;
            selectedBackground.GradientStops.Should().HaveCount(3);
            selectedBackground.GradientStops[0].Color.Should().Be(colors.ListSelectionStartColor);
            selectedBackground.GradientStops[1].Color.Should().Be(colors.ListSelectionMiddleColor);
            selectedBackground.GradientStops[2].Color.Should().Be(Color.FromArgb(
                0,
                colors.ListSelectionMiddleColor.R,
                colors.ListSelectionMiddleColor.G,
                colors.ListSelectionMiddleColor.B));
            resources["DialogListBoxSelectedItemBackground"].Should().BeAssignableTo<IGradientBrush>()
                .Which.GradientStops.Should().HaveCount(2);
            IGradientBrush hoverBackground = resources["ListBoxMouseOverItemBackground"]
                .Should().BeAssignableTo<IGradientBrush>().Which;
            hoverBackground.GradientStops.Should().HaveCount(3);
            hoverBackground.GradientStops[2].Color.Should().Be(Color.FromArgb(
                0,
                colors.ListSelectionMiddleColor.R,
                colors.ListSelectionMiddleColor.G,
                colors.ListSelectionMiddleColor.B));
        });
    }

    [Fact]
    public void ApplyCreatesTheSameReadableNeutralPaletteForBothGames()
    {
        StaTestRunner.Run(() =>
        {
            ResourceDictionary zeroHourResources = new();
            ResourceDictionary generalsResources = new();

            LauncherThemeResourceApplier.Apply(
                zeroHourResources,
                LauncherThemePresets.Create(SupportedGame.ZeroHour));
            LauncherThemeResourceApplier.Apply(
                generalsResources,
                LauncherThemePresets.Create(SupportedGame.Generals));

            string[] neutralResourceNames =
            [
                "GenLauncherNeutralSurfaceColor",
                "GenLauncherNeutralBorderColor",
                "GenLauncherNeutralPressedColor",
                "GenLauncherSecondaryTextColor",
            ];

            foreach (string resourceName in neutralResourceNames)
            {
                GetBrushColor(zeroHourResources, resourceName)
                    .Should().Be(GetBrushColor(generalsResources, resourceName));
            }

            GetBrushColor(zeroHourResources, "GenLauncherNeutralSurfaceColor")
                .Should().Be(Color.FromArgb(0xE6, 0x0A, 0x0D, 0x10));
            GetBrushColor(zeroHourResources, "GenLauncherNeutralBorderColor")
                .Should().Be(Color.FromRgb(0x5A, 0x62, 0x6B));
            GetBrushColor(zeroHourResources, "GenLauncherNeutralPressedColor")
                .Should().Be(Color.FromRgb(0x30, 0x36, 0x3D));
            GetBrushColor(zeroHourResources, "GenLauncherSecondaryTextColor")
                .Should().Be(Color.FromRgb(0xD2, 0xD8, 0xE0));
        });
    }

    [Theory]
    [InlineData(SupportedGame.Generals)]
    [InlineData(SupportedGame.ZeroHour)]
    public void StandardPaletteMaintainsReadableTextAndAccentContrast(SupportedGame game)
    {
        StaTestRunner.Run(() =>
        {
            ResourceDictionary resources = new();
            ColorsInfo colors = LauncherThemePresets.Create(game);
            LauncherThemeResourceApplier.Apply(resources, colors);

            Color surface = GetBrushColor(resources, "GenLauncherNeutralSurfaceColor");
            Color pressedSurface = GetBrushColor(resources, "GenLauncherNeutralPressedColor");
            Color secondaryText = GetBrushColor(resources, "GenLauncherSecondaryTextColor");
            Color buttonPointerOver = GetGradientStopColor(
                resources,
                "ButtonPointerOverBackground",
                1);

            GetContrastRatio(colors.GenLauncherDefaultTextColor.Color, surface)
                .Should().BeGreaterThanOrEqualTo(4.5);
            GetContrastRatio(secondaryText, surface)
                .Should().BeGreaterThanOrEqualTo(4.5);
            GetContrastRatio(colors.GenLauncherBorderColor.Color, pressedSurface)
                .Should().BeGreaterThanOrEqualTo(4.5);
            GetContrastRatio(colors.GenLauncherBorderColor.Color, buttonPointerOver)
                .Should().BeGreaterThanOrEqualTo(4.5);
        });
    }

    [Theory]
    [InlineData(
        SupportedGame.ZeroHour,
        "#141D8C",
        "#173352",
        "#070B0F",
        "#49A1FF",
        "#36495E",
        "#C7E2FF")]
    [InlineData(
        SupportedGame.Generals,
        "#7C2A0D",
        "#523417",
        "#0F0B07",
        "#FFA349",
        "#5E4A36",
        "#FFE3C7")]
    public void PresetUsesExactInteractionPalette(
        SupportedGame game,
        string pointerOver,
        string accentFill,
        string subtleAccentFill,
        string accentBorder,
        string subtleAccentBorder,
        string accentText)
    {
        StaTestRunner.Run(() =>
        {
            ColorsInfo colors = LauncherThemePresets.Create(game);

            colors.GenLauncherButtonPointerOverColor.Should().Be(Color.Parse(pointerOver));
            colors.GenLauncherAccentFillColor.Color.Should().Be(Color.Parse(accentFill));
            colors.GenLauncherSubtleAccentFillColor.Color.Should().Be(Color.Parse(subtleAccentFill));
            colors.GenLauncherAccentBorderColor.Color.Should().Be(Color.Parse(accentBorder));
            colors.GenLauncherSubtleAccentBorderColor.Color.Should().Be(Color.Parse(subtleAccentBorder));
            colors.GenLauncherAccentTextColor.Color.Should().Be(Color.Parse(accentText));
        });
    }

    [Fact]
    public void ApplyWhenBackgroundImageIsExcludedKeepsExistingBackgroundResource()
    {
        StaTestRunner.Run(() =>
        {
            ResourceDictionary resources = new()
            {
                ["GenLauncherBackGroundImage"] = "existing"
            };
            ColorsInfo colors = LauncherThemePresets.Create(SupportedGame.ZeroHour);

            LauncherThemeResourceApplier.Apply(resources, colors, includeBackgroundImage: false);

            resources["GenLauncherBackGroundImage"].Should().Be("existing");
        });
    }

    private static Color GetBrushColor(ResourceDictionary resources, string resourceName)
    {
        return resources[resourceName].Should().BeAssignableTo<ISolidColorBrush>().Which.Color;
    }

    private static Color GetGradientStopColor(
        ResourceDictionary resources,
        string resourceName,
        int stopIndex)
    {
        return resources[resourceName].Should().BeAssignableTo<IGradientBrush>()
            .Which.GradientStops[stopIndex].Color;
    }

    private static double GetContrastRatio(Color first, Color second)
    {
        double firstLuminance = GetRelativeLuminance(first);
        double secondLuminance = GetRelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
               (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double GetRelativeLuminance(Color color)
    {
        return (0.2126 * ToLinear(color.R)) +
               (0.7152 * ToLinear(color.G)) +
               (0.0722 * ToLinear(color.B));
    }

    private static double ToLinear(byte channel)
    {
        double value = channel / 255d;
        return value <= 0.03928
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
