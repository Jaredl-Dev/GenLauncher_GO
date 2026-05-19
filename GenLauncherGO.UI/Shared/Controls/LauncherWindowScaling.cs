using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace GenLauncherGO.UI.Shared.Controls;

/// <summary>
///     Sizes a fixed launcher window to the display it opens on.
/// </summary>
/// <remarks>
///     Every window keeps the fixed design proportions it declares in AXAML; this scales those proportions by one
///     shared factor so a dialog is the same apparent size as the main window behind it, and so the launcher is
///     neither oversized on 1080p nor postage-stamp sized on 4K. Attach this from a window constructor.
/// </remarks>
internal static class LauncherWindowScaling
{
    /// <summary>
    ///     Main window design width the shared factor is expressed against, so it reads as 1.0 at the size the
    ///     artwork was drawn for.
    /// </summary>
    private const double ReferenceWidth = 970;

    /// <summary>
    ///     Footprint of the largest window in the launcher, currently the settings window. The shared factor is
    ///     capped against this rather than against each window, so one window never scales past its display while
    ///     the others still fit. Raise these if a larger window is added.
    /// </summary>
    private const double WidestDesignWidth = 986;

    private const double TallestDesignHeight = 762;

    private const double MinimumScale = 0.75;

    private const double MaximumScale = 1.5;

    /// <summary>
    ///     Factor the previously opened window resolved. Only ever touched from the UI thread.
    /// </summary>
    /// <remarks>
    ///     The display is not known until a window has a platform handle, so the first window can only scale once
    ///     it is already visible. Reusing its result lets every later window size itself before it is shown, which
    ///     is what stops dialogs opening at their declared size and then jumping.
    /// </remarks>
    private static double? _lastResolvedScale;

    /// <summary>
    ///     Applies the shared display scale to <paramref name="window" />.
    /// </summary>
    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        InstallTransformHost(window);

        // Captured before anything is scaled so re-applying a new factor never compounds the previous one.
        double declaredWidth = window.Width;
        double declaredHeight = window.Height;
        double declaredMinWidth = window.MinWidth;
        double declaredMinHeight = window.MinHeight;
        double declaredMaxWidth = window.MaxWidth;
        double declaredMaxHeight = window.MaxHeight;

        void ApplyScale(double scale)
        {
            if (window.Content is LayoutTransformControl host)
            {
                host.LayoutTransform = new ScaleTransform(scale, scale);
            }

            window.MinWidth = Scale(declaredMinWidth, scale);
            window.MinHeight = Scale(declaredMinHeight, scale);
            window.MaxWidth = Scale(declaredMaxWidth, scale);
            window.MaxHeight = Scale(declaredMaxHeight, scale);
            window.Width = Scale(declaredWidth, scale);
            window.Height = Scale(declaredHeight, scale);
        }

        if (_lastResolvedScale is { } cached)
        {
            ApplyScale(cached);
        }

        window.Opened += (_, _) =>
        {
            double scale = GetScale(window);
            _lastResolvedScale = scale;
            ApplyScale(scale);
            Recentre(window);
        };
    }

    /// <summary>
    ///     Gets the shared scale for the display <paramref name="window" /> is on.
    /// </summary>
    private static double GetScale(Window window)
    {
        Screen? screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen == null)
        {
            return _lastResolvedScale ?? 1;
        }

        double availableWidth = screen.WorkingArea.Width / screen.Scaling;
        double availableHeight = screen.WorkingArea.Height / screen.Scaling;

        // Half the display width reads comfortably on 1080p and upwards; the fit cap wins on displays too
        // small to honour it, which is why it is applied after the preferred range rather than inside it.
        double preferred = Math.Clamp(
            availableWidth / 2 / ReferenceWidth,
            MinimumScale,
            MaximumScale);
        double fits = Math.Min(
            availableWidth / WidestDesignWidth,
            availableHeight / TallestDesignHeight);
        return Math.Min(preferred, fits);
    }

    /// <summary>
    ///     Moves the window content under a transform host, so scaling the layout later never re-parents it.
    /// </summary>
    /// <remarks>
    ///     Re-parenting once the window is visible tears down and rebuilds the visual tree, which shows the empty
    ///     window background for a frame. Doing it before the first frame avoids that flash.
    /// </remarks>
    private static void InstallTransformHost(Window window)
    {
        if (window.Content is not Control content || content is LayoutTransformControl)
        {
            return;
        }

        // The content has to leave the window before it can be re-parented into the transform.
        window.Content = null;
        window.Content = new LayoutTransformControl
        {
            LayoutTransform = new ScaleTransform(1, 1),
            Child = content
        };
    }

    /// <summary>
    ///     Leaves lengths the window does not declare untouched, so a <see cref="Window.SizeToContent" /> axis keeps
    ///     measuring itself and an unset bound stays unset.
    /// </summary>
    private static double Scale(double length, double scale)
    {
        return double.IsNaN(length) || double.IsInfinity(length) || length <= 0
            ? length
            : length * scale;
    }

    /// <summary>
    ///     Restores the requested startup placement, because the window was already centred at its declared size
    ///     before it was rescaled.
    /// </summary>
    private static void Recentre(Window window)
    {
        PixelRect target;
        if (window.WindowStartupLocation == WindowStartupLocation.CenterOwner && window.Owner is Window owner)
        {
            target = new PixelRect(
                owner.Position,
                PixelSize.FromSize(owner.FrameSize ?? owner.ClientSize, owner.RenderScaling));
        }
        else if (window.WindowStartupLocation == WindowStartupLocation.CenterScreen)
        {
            Screen? screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
            if (screen == null)
            {
                return;
            }

            target = screen.WorkingArea;
        }
        else
        {
            return;
        }

        var size = PixelSize.FromSize(window.FrameSize ?? window.ClientSize, window.RenderScaling);
        window.Position = new PixelPoint(
            target.X + (target.Width - size.Width) / 2,
            target.Y + (target.Height - size.Height) / 2);
    }
}
