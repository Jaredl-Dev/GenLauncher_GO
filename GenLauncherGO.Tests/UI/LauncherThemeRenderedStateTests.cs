using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Mods.ViewModels;
using GenLauncherGO.UI.Features.Mods.Views;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.Tests.UI;

public sealed class LauncherThemeRenderedStateTests
{
    [Theory]
    [InlineData(SupportedGame.Generals)]
    [InlineData(SupportedGame.ZeroHour)]
    public void AddModificationWindow_RendersTheActiveThemeAndItemMetadata(SupportedGame game)
    {
        StaTestRunner.Run(() =>
        {
            ColorsInfo colors = LauncherThemePresets.Create(game);
            AddModificationWindow window = new();
            LauncherThemeResourceApplier.Apply(window, colors, includeBackgroundImage: false);
            AddModificationItemViewModel modification = new("Contra", "Calculating...");
            modification.SetMetadata("10.0.2 Beta 2 Patch 1", "2.1 GB");
            ListBox modifications = window.FindControl<ListBox>("ModificationsList")!;
            modifications.ItemsSource = new[] { modification };

            try
            {
                window.Show();
                window.UpdateLayout();

                window.Background.Should().BeSameAs(colors.GenLauncherDarkBackGround);
                window.FindControl<TextBox>("SearchBox")!.BorderBrush.Should()
                    .BeSameAs(colors.GenLauncherBorderColor);
                modifications.BorderBrush.Should().BeSameAs(colors.GenLauncherBorderColor);

                TextBlock version = window.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(text => text.Text == modification.VersionText);
                TextBlock packageSize = window.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(text => text.Text == modification.PackageSizeText);
                version.Foreground.Should().BeSameAs(colors.GenLauncherBorderColor);
                packageSize.Foreground.Should().BeSameAs(colors.GenLauncherBorderColor);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
