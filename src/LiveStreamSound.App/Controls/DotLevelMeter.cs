using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LiveStreamSound.Shared.Diagnostics;

namespace LiveStreamSound.App.Controls;

/// <summary>
/// A 5-dot quality meter (●●●●○ style). Redundantly encodes the quality level
/// as color *and* count-of-filled-dots — colour-blind safe.
/// </summary>
public sealed class DotLevelMeter : Control
{
    private const int DotCount = 5;
    private readonly Ellipse[] _dots = new Ellipse[DotCount];
    private StackPanel? _panel;

    public static readonly DependencyProperty QualityProperty =
        DependencyProperty.Register(
            nameof(Quality), typeof(QualityLevel), typeof(DotLevelMeter),
            new FrameworkPropertyMetadata(QualityLevel.Disconnected,
                FrameworkPropertyMetadataOptions.AffectsRender,
                (d, e) => ((DotLevelMeter)d).UpdateDots()));

    public QualityLevel Quality
    {
        get => (QualityLevel)GetValue(QualityProperty);
        set => SetValue(QualityProperty, value);
    }

    public DotLevelMeter()
    {
        _panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < DotCount; i++)
        {
            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Margin = new Thickness(0, 0, 3, 0),
                Fill = InactiveBrush,
            };
            _dots[i] = dot;
            _panel.Children.Add(dot);
        }
        AddVisualChild(_panel);
        AddLogicalChild(_panel);
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _panel!;
    protected override Size MeasureOverride(Size constraint) { _panel!.Measure(constraint); return _panel.DesiredSize; }
    protected override Size ArrangeOverride(Size arrangeBounds) { _panel!.Arrange(new Rect(arrangeBounds)); return arrangeBounds; }

    private void UpdateDots()
    {
        var (filled, brush) = Quality switch
        {
            QualityLevel.Good => (5, GreenBrush),
            QualityLevel.Degraded => (3, AmberBrush),
            QualityLevel.Bad => (2, RedBrush),
            QualityLevel.Disconnected => (0, GrayBrush),
            _ => (0, GrayBrush),
        };
        for (var i = 0; i < DotCount; i++)
            _dots[i].Fill = i < filled ? brush : InactiveBrush;
    }

    private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
    private static readonly Brush AmberBrush = new SolidColorBrush(Color.FromRgb(0xEA, 0xB3, 0x08));
    private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly Brush GrayBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
    private static readonly Brush InactiveBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
}
