using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LiveStreamSound.App.Controls;

/// <summary>
/// Simple polyline "sparkline" over a bounded min/max range.
/// Data source is <see cref="Values"/> (any IEnumerable&lt;double&gt;).
/// </summary>
public sealed class SparklineControl : Control
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(IEnumerable), typeof(SparklineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                (d, e) => ((SparklineControl)d).HookCollectionChanged(e.OldValue, e.NewValue)));

    public static readonly DependencyProperty MinValueProperty =
        DependencyProperty.Register(nameof(MinValue), typeof(double), typeof(SparklineControl),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxValueProperty =
        DependencyProperty.Register(nameof(MaxValue), typeof(double), typeof(SparklineControl),
            new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty =
        DependencyProperty.Register(nameof(LineBrush), typeof(Brush), typeof(SparklineControl),
            new FrameworkPropertyMetadata(
                new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA)),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? Values { get => (IEnumerable?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public double MinValue { get => (double)GetValue(MinValueProperty); set => SetValue(MinValueProperty, value); }
    public double MaxValue { get => (double)GetValue(MaxValueProperty); set => SetValue(MaxValueProperty, value); }
    public Brush LineBrush { get => (Brush)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }

    public SparklineControl() { MinHeight = 14; }

    private void HookCollectionChanged(object? oldVal, object? newVal)
    {
        if (oldVal is INotifyCollectionChanged oldNcc) oldNcc.CollectionChanged -= OnCollectionChanged;
        if (newVal is INotifyCollectionChanged newNcc) newNcc.CollectionChanged += OnCollectionChanged;
        InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Values is null || ActualWidth < 4 || ActualHeight < 4) return;

        var samples = Values.Cast<object?>().Select(v => v switch
        {
            double d => d,
            float f => (double)f,
            int i => (double)i,
            _ => 0d,
        }).ToArray();
        if (samples.Length < 2) return;

        var min = MinValue;
        var max = MaxValue;
        if (max - min < 1e-6) max = min + 1;

        var pen = new Pen(LineBrush, 1.5) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            for (var i = 0; i < samples.Length; i++)
            {
                var x = (samples.Length == 1) ? 0 : i * (ActualWidth / (samples.Length - 1));
                var normalized = Math.Clamp((samples[i] - min) / (max - min), 0, 1);
                var y = ActualHeight - normalized * ActualHeight;
                if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
                else ctx.LineTo(new Point(x, y), true, false);
            }
        }
        geom.Freeze();
        dc.DrawGeometry(null, pen, geom);
    }
}
