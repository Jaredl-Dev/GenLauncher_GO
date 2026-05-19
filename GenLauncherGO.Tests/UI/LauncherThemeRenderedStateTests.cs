using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Mods.ViewModels;
using GenLauncherGO.UI.Features.Mods.Views;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.Tests.UI;

[Collection("Avalonia")]
public sealed class LauncherThemeRenderedStateTests
{
    /// <summary>
    ///     Windows carry no palette of their own, so this proves a window nobody themed still resolves the active
    ///     theme through application-scoped resources.
    /// </summary>
    [Theory]
    [InlineData(SupportedGame.Generals)]
    [InlineData(SupportedGame.ZeroHour)]
    public void Window_ResolvesTheApplicationThemeWithoutBeingThemedItself(SupportedGame game)
    {
        StaTestRunner.Run(() =>
        {
            using ApplicationThemeScope applicationTheme = new();
            ColorsInfo colors = LauncherThemePresets.Create(game);
            LauncherThemeResourceApplier.Apply(Application.Current!.Resources, colors);

            AddModificationWindow window = new();
            AddModificationItemViewModel modification = new("Contra", "Calculating...");
            modification.SetMetadata("10.0.2 Beta 2 Patch 1", "2.1 GB");
            ListBox modifications = window.FindControl<ListBox>("ModificationsList")!;
            modifications.ItemsSource = new[] { modification };

            try
            {
                window.Show();
                window.UpdateLayout();

                window.Background.Should().BeSameAs(colors.GenLauncherDarkBackGround);
                // The search box takes focus on open, so assert a colour that focus does not change.
                window.FindControl<TextBox>("SearchBox")!.Background.Should()
                    .BeSameAs(colors.GenLauncherDarkBackGround);
                modifications.BorderBrush.Should().BeSameAs(colors.GenLauncherBorderColor);

                TextBlock version = window.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(text => text.Text == modification.VersionText);
                TextBlock packageSize = window.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(text => text.Text == modification.PackageSizeText);
                version.Foreground.Should().BeSameAs(colors.GenLauncherInactiveBorder);
                packageSize.Foreground.Should().BeSameAs(colors.GenLauncherInactiveBorder);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    ///     The game-selection window previews a palette that is not the active one, which relies on a window's own
    ///     resources shadowing application scope.
    /// </summary>
    [Fact]
    public void WindowResources_ShadowTheApplicationTheme()
    {
        StaTestRunner.Run(() =>
        {
            using ApplicationThemeScope applicationTheme = new();
            ColorsInfo applicationColors = LauncherThemePresets.Create(SupportedGame.ZeroHour);
            ColorsInfo previewColors = LauncherThemePresets.Create(SupportedGame.Generals);
            LauncherThemeResourceApplier.Apply(Application.Current!.Resources, applicationColors);

            AddModificationWindow window = new();
            LauncherThemeResourceApplier.Apply(
                window.Resources,
                previewColors,
                false);

            try
            {
                window.Show();
                window.UpdateLayout();

                window.Background.Should().BeSameAs(previewColors.GenLauncherDarkBackGround);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
