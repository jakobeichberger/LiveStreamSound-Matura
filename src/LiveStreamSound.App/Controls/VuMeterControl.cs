using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LiveStreamSound.App.Controls;

/// <summary>
/// Horizontal VU meter made of 7 bars. Bars light up left-to-right according to
/// the <see cref="Level"/> property (0.0 silence … 1.0 peak). Top two bars turn
/// amber then red to hint clipping.
/// </summary>
public sealed class VuMeterControl : Control
{
    private const int BarCount = 7;
    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private StackPanel? _panel;

    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(
            nameof(Level), typeof(double), typeof(VuMeterControl),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender,
                (d, e) => ((VuMeterControl)d).UpdateBars()));

    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, Math.Clamp(value, 0d, 1d));
    }

    public VuMeterControl()
    {
        _panel = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < BarCount; i++)
        {
            var rect = new Rectangle
            {
                Width = 6,
                Margin = new Thickness(0, 0, 2, 0),
                RadiusX = 1,
                RadiusY = 1,
                VerticalAlignment = VerticalAlignment.Stretch,
                Fill = GetBarBrush(i, active: false),
            };
            _bars[i] = rect;
            _panel.Children.Add(rect);
        }
        AddVisualChild(_panel);
        AddLogicalChild(_panel);
        MinHeight = 14;
        Height = 14;
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _panel!;

    protected override Size MeasureOverride(Size constraint)
    {
        _panel!.Measure(constraint);
        return _panel.DesiredSize;
    }

    protected override Size ArrangeOverride(Size arrangeBounds)
    {
        _panel!.Arrange(new Rect(arrangeBounds));
        return arrangeBounds;
    }

    private void UpdateBars()
    {
        var lit = (int)Math.Round(Level * BarCount);
        for (var i = 0; i < BarCount; i++)
            _bars[i].Fill = GetBarBrush(i, active: i < lit);
    }

    private static Brush GetBarBrush(int index, bool active)
    {
        if (!active)
            return new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        // Green (0..4), amber (5), red (6)
        return index switch
        {
            < 5 => new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)),
            5 => new SolidColorBrush(Color.FromRgb(0xEA, 0xB3, 0x08)),
            _ => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
        };
    }
}
