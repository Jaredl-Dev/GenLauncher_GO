using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace GenLauncherGO.UI.Shared.Controls;

/// <summary>
///     Provides the shared transient path-field feedback used by installation discovery views.
/// </summary>
internal static class LauncherTextBoxFeedback
{
    private static readonly ConditionalWeakTable<TextBox, FeedbackState> _feedbackStates = [];

    public static void Flash(TextBox textBox, IBrush? accentBrush)
    {
        ArgumentNullException.ThrowIfNull(textBox);

        FeedbackState state = _feedbackStates.GetOrCreateValue(textBox);
        long generation = ++state.Generation;
        Color accentColor = accentBrush is ISolidColorBrush solidColorBrush
            ? solidColorBrush.Color
            : Colors.DeepSkyBlue;
        DropShadowEffect glow = new()
        {
            BlurRadius = 36,
            Color = accentColor,
            OffsetX = 0,
            OffsetY = 0,
            Opacity = 0
        };
        textBox.Effect = glow;

        Animation flash = new()
        {
            Duration = TimeSpan.FromMilliseconds(480),
            IterationCount = new IterationCount(2),
            PlaybackDirection = PlaybackDirection.Alternate,
            FillMode = FillMode.None,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter
                        {
                            Property = DropShadowEffect.OpacityProperty,
                            Value = 0d
                        }
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter
                        {
                            Property = DropShadowEffect.OpacityProperty,
                            Value = 1d
                        }
                    }
                }
            }
        };
        _ = RunFlashAsync(textBox, glow, flash, state, generation);
    }

    private static async Task RunFlashAsync(
        TextBox textBox,
        DropShadowEffect glow,
        Animation flash,
        FeedbackState state,
        long generation)
    {
        try
        {
            await flash.RunAsync(glow, CancellationToken.None);
        }
        finally
        {
            if (state.Generation == generation &&
                ReferenceEquals(textBox.Effect, glow))
            {
                textBox.ClearValue(Visual.EffectProperty);
            }
        }
    }

    private sealed class FeedbackState
    {
        public long Generation { get; set; }
    }
}
