using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using LiveStreamSound.Shared.Diagnostics;

namespace LiveStreamSound.App.Controls;

/// <summary>
/// A pulsing concentric-ring "heartbeat" indicator for the simple-mode
/// client status card. The pulse rhythm + colour reflects connection health:
/// <list type="bullet">
///   <item>Good — calm steady pulse, green</item>
///   <item>Degraded — slightly faster, amber</item>
///   <item>Bad — fast urgent, red</item>
///   <item>Disconnected — flat (no pulse), grey</item>
/// </list>
/// Summarises everything for the teacher without exposing any numbers.
/// </summary>
public sealed class HeartbeatRing : Grid
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(QualityLevel), typeof(HeartbeatRing),
        new PropertyMetadata(QualityLevel.Disconnected, OnLevelChanged));

    public QualityLevel Level
    {
        get => (QualityLevel)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    private readonly Ellipse _core;
    private readonly Ellipse _ring1;
    private readonly Ellipse _ring2;
    private Storyboard? _activeStoryboard;

    public HeartbeatRing()
    {
        Width = 200;
        Height = 200;

        _ring2 = NewRing(180, fill: false);
        _ring1 = NewRing(140, fill: false);
        _core = NewRing(96, fill: true);

        Children.Add(_ring2);
        Children.Add(_ring1);
        Children.Add(_core);

        Loaded += (_, _) => ApplyAnimationFor(Level);
        Unloaded += (_, _) => _activeStoryboard?.Stop();
    }

    private static Ellipse NewRing(double size, bool fill)
    {
        var e = new Ellipse
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            StrokeThickness = fill ? 0 : 2,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
        };
        return e;
    }

    private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeartbeatRing hr) hr.ApplyAnimationFor((QualityLevel)e.NewValue);
    }

    private void ApplyAnimationFor(QualityLevel level)
    {
        _activeStoryboard?.Stop();

        var (color, periodSec, pulseScale) = level switch
        {
            QualityLevel.Good => (Color.FromRgb(46, 204, 113), 1.6, 1.18),     // calm green
            QualityLevel.Degraded => (Color.FromRgb(241, 196, 15), 1.0, 1.22), // brisker amber
            QualityLevel.Bad => (Color.FromRgb(231, 76, 60), 0.6, 1.30),       // urgent red
            QualityLevel.Disconnected => (Color.FromRgb(127, 140, 141), 0, 1.0),
            _ => (Color.FromRgb(127, 140, 141), 0, 1.0),
        };

        var brush = new SolidColorBrush(color);
        _core.Fill = brush;
        _ring1.Stroke = brush;
        _ring2.Stroke = brush;

        // Reset transforms + opacity to a calm baseline before animating.
        ((ScaleTransform)_core.RenderTransform).ScaleX = 1;
        ((ScaleTransform)_core.RenderTransform).ScaleY = 1;
        _ring1.Opacity = 0;
        _ring2.Opacity = 0;

        if (periodSec <= 0) return;

        var sb = new Storyboard();

        // Core: scale pulse, in time with periodSec.
        AddPulse(sb, _core, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 1.0, pulseScale, periodSec);
        AddPulse(sb, _core, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 1.0, pulseScale, periodSec);

        // Outer rings: fade out as they expand → "ripple from core" feel.
        AddPulse(sb, _ring1, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 0.6, 1.05, periodSec);
        AddPulse(sb, _ring1, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 0.6, 1.05, periodSec);
        AddOpacityPulse(sb, _ring1, 0.7, 0.0, periodSec);

        AddPulse(sb, _ring2, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 0.4, 1.1, periodSec, beginOffset: periodSec / 3);
        AddPulse(sb, _ring2, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 0.4, 1.1, periodSec, beginOffset: periodSec / 3);
        AddOpacityPulse(sb, _ring2, 0.4, 0.0, periodSec, beginOffset: periodSec / 3);

        _activeStoryboard = sb;
        sb.Begin();
    }

    private static void AddPulse(Storyboard sb, DependencyObject target, string path,
        double from, double to, double periodSec, double beginOffset = 0)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(periodSec / 2),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromSeconds(beginOffset),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, new PropertyPath(path));
        sb.Children.Add(anim);
    }

    private static void AddOpacityPulse(Storyboard sb, UIElement target,
        double from, double to, double periodSec, double beginOffset = 0)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(periodSec),
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromSeconds(beginOffset),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(anim);
    }
}
