using Avalonia.Controls;
using Avalonia.Media;
using GenLauncherGO.UI.Shared.Controls;

namespace GenLauncherGO.Tests.UI.Shared.Controls;

[Collection("Avalonia")]
public sealed class LauncherTextBoxFeedbackTests
{
    [Fact]
    public void Flash_WithASolidAccent_GlowsInThatAccent()
    {
        StaTestRunner.Run(() =>
        {
            TextBox textBox = new();

            LauncherTextBoxFeedback.Flash(textBox, Brushes.Orange);

            textBox.Effect.Should().BeOfType<DropShadowEffect>()
                .Which.Color.Should().Be(Colors.Orange);
        });
    }

    /// <summary>
    ///     The accent comes from the active theme, which supplies gradients as well as flat colours, and a glow is
    ///     drawn in exactly one colour. Falling back keeps the field readable instead of leaving it unlit.
    /// </summary>
    [Fact]
    public void Flash_WithoutASolidAccent_GlowsInTheFallbackColour()
    {
        StaTestRunner.Run(() =>
        {
            TextBox textBox = new();
            LinearGradientBrush accent = new()
            {
                GradientStops =
                {
                    new GradientStop(Colors.Orange, 0),
                    new GradientStop(Colors.Red, 1)
                }
            };

            LauncherTextBoxFeedback.Flash(textBox, accent);

            textBox.Effect.Should().BeOfType<DropShadowEffect>()
                .Which.Color.Should().Be(Colors.DeepSkyBlue);
        });
    }

    /// <summary>
    ///     Installation discovery can announce a second path while the first glow is still running, and only the
    ///     newest flash may own the field: the superseded one must not keep painting over it.
    /// </summary>
    [Fact]
    public void Flash_CalledAgainOnTheSameTextBox_LeavesTheNewestGlowOwningTheField()
    {
        StaTestRunner.Run(() =>
        {
            TextBox textBox = new();
            LauncherTextBoxFeedback.Flash(textBox, Brushes.Orange);
            IEffect? supersededGlow = textBox.Effect;

            LauncherTextBoxFeedback.Flash(textBox, Brushes.Lime);

            textBox.Effect.Should().NotBeSameAs(supersededGlow);
            textBox.Effect.Should().BeOfType<DropShadowEffect>()
                .Which.Color.Should().Be(Colors.Lime);
        });
    }
}
