using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using GenLauncherGO.UI.Shared.Controls;

namespace GenLauncherGO.Tests.UI.Shared.Controls;

[Collection("Avalonia")]
public sealed class LauncherWindowScalingTests
{
    [Fact]
    public void AttachAndOpen_AppliesOneDisplayScaleToWindowAndContent()
    {
        StaTestRunner.Run(() =>
        {
            var content = new Border();
            var window = new Window
            {
                Width = 970,
                Height = 500,
                MinWidth = 400,
                MinHeight = 300,
                Content = content
            };
            LauncherWindowScaling.Attach(window);

            try
            {
                window.Show();
                window.UpdateLayout();

                window.Content.Should().BeOfType<LayoutTransformControl>();
                var transformHost = (LayoutTransformControl)window.Content!;
                transformHost.Child.Should().BeSameAs(content);
                transformHost.LayoutTransform.Should().BeOfType<ScaleTransform>();
                var transform = (ScaleTransform)transformHost.LayoutTransform!;
                double scale = transform.ScaleX;

                scale.Should().BeGreaterThan(0);
                scale.Should().BeLessThanOrEqualTo(1.5);
                transform.ScaleY.Should().Be(scale);
                window.Width.Should().BeApproximately(970 * scale, 0.001);
                window.Height.Should().BeApproximately(500 * scale, 0.001);
                window.MinWidth.Should().BeApproximately(400 * scale, 0.001);
                window.MinHeight.Should().BeApproximately(300 * scale, 0.001);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    ///     A dialog opening at its declared size and then jumping is what reusing the previously resolved factor
    ///     prevents, so the sizing has to be in place before the window is ever shown.
    /// </summary>
    [Fact]
    public void Attach_AfterAnEarlierWindowResolvedTheScale_SizesTheNextWindowBeforeItOpens()
    {
        StaTestRunner.Run(() =>
        {
            var openedWindow = new Window { Width = 970, Height = 500, Content = new Border() };
            LauncherWindowScaling.Attach(openedWindow);
            openedWindow.Show();
            openedWindow.UpdateLayout();
            double scale = ResolvedScale(openedWindow);

            // The info dialog's shape: a declared width and a capped height it measures itself into.
            var dialog = new Window
            {
                Width = 640,
                MaxHeight = 520,
                SizeToContent = SizeToContent.Height,
                Content = new Border()
            };

            try
            {
                LauncherWindowScaling.Attach(dialog);

                dialog.Width.Should().BeApproximately(640 * scale, 0.001);
                dialog.MaxHeight.Should().BeApproximately(520 * scale, 0.001);
                double.IsNaN(dialog.Height).Should().BeTrue(
                    "a height the window measures for itself must stay unset, not become a fixed scaled length");
            }
            finally
            {
                openedWindow.Close();
            }
        });
    }

    /// <summary>
    ///     The window was already centred at its declared size before it was rescaled, so the placement it asked
    ///     for has to be restored against the size it actually ended up.
    /// </summary>
    [Fact]
    public void Open_WhenTheWindowCentresOnTheScreen_CentresItsScaledFrame()
    {
        StaTestRunner.Run(() =>
        {
            var openedWindow = new Window { Width = 970, Height = 500, Content = new Border() };
            LauncherWindowScaling.Attach(openedWindow);
            openedWindow.Show();
            openedWindow.UpdateLayout();

            var window = new Window
            {
                Width = 640,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new Border()
            };
            LauncherWindowScaling.Attach(window);

            try
            {
                window.Show();
                window.UpdateLayout();

                Screen screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary!;
                var frame = PixelSize.FromSize(
                    window.FrameSize ?? window.ClientSize,
                    window.RenderScaling);
                window.Position.Should().Be(new PixelPoint(
                    screen.WorkingArea.X + ((screen.WorkingArea.Width - frame.Width) / 2),
                    screen.WorkingArea.Y + ((screen.WorkingArea.Height - frame.Height) / 2)));
            }
            finally
            {
                window.Close();
                openedWindow.Close();
            }
        });
    }

    private static double ResolvedScale(Window window)
    {
        var transformHost = (LayoutTransformControl)window.Content!;
        return ((ScaleTransform)transformHost.LayoutTransform!).ScaleX;
    }
}
