using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveStreamSound.Shared.Diagnostics;

namespace LiveStreamSound.App.Controls;

/// <summary>
/// Concentric-ring dashboard for a client's connection quality.
/// Outer ring: overall score (0.0–1.0), color tied to <see cref="Level"/>.
/// Three inner rings: Latency, PacketLoss, Buffer sub-scores (each 0.0–1.0).
/// </summary>
public sealed class QualityRingControl : Control
{
    public static readonly DependencyProperty OverallScoreProperty = Reg(nameof(OverallScore));
    public static readonly DependencyProperty LatencyScoreProperty = Reg(nameof(LatencyScore));
    public static readonly DependencyProperty LossScoreProperty = Reg(nameof(LossScore));
    public static readonly DependencyProperty BufferScoreProperty = Reg(nameof(BufferScore));

    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(nameof(Level), typeof(QualityLevel), typeof(QualityRingControl),
            new FrameworkPropertyMetadata(QualityLevel.Disconnected,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public double OverallScore { get => (double)GetValue(OverallScoreProperty); set => SetValue(OverallScoreProperty, value); }
    public double LatencyScore { get => (double)GetValue(LatencyScoreProperty); set => SetValue(LatencyScoreProperty, value); }
    public double LossScore { get => (double)GetValue(LossScoreProperty); set => SetValue(LossScoreProperty, value); }
    public double BufferScore { get => (double)GetValue(BufferScoreProperty); set => SetValue(BufferScoreProperty, value); }
    public QualityLevel Level { get => (QualityLevel)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }

    private static DependencyProperty Reg(string name) =>
        DependencyProperty.Register(name, typeof(double), typeof(QualityRingControl),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    static QualityRingControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(QualityRingControl),
            new FrameworkPropertyMetadata(typeof(QualityRingControl)));
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var side = Math.Min(ActualWidth, ActualHeight);
        if (side < 40) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var rOuter = side / 2 - 8;
        var rL = rOuter - 22;
        var rP = rOuter - 44;
        var rB = rOuter - 66;

        var bgPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 8);
        var scoreColor = LevelColor(Level);
        var scorePen = new Pen(new SolidColorBrush(scoreColor), 12) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var subPen = (Color c) => new Pen(new SolidColorBrush(c), 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        // Background tracks (full 360 at low opacity)
        DrawArc(dc, bgPen, center, rOuter, 0, 360);
        DrawArc(dc, bgPen, center, rL, 0, 360);
        DrawArc(dc, bgPen, center, rP, 0, 360);
        DrawArc(dc, bgPen, center, rB, 0, 360);

        DrawArc(dc, scorePen, center, rOuter, 0, Math.Clamp(OverallScore, 0, 1) * 360);
        DrawArc(dc, subPen(Color.FromRgb(0x60, 0xA5, 0xFA)), center, rL, 0, Math.Clamp(LatencyScore, 0, 1) * 360);
        DrawArc(dc, subPen(Color.FromRgb(0xA3, 0xE6, 0x35)), center, rP, 0, Math.Clamp(LossScore, 0, 1) * 360);
        DrawArc(dc, subPen(Color.FromRgb(0xF4, 0x72, 0xB6)), center, rB, 0, Math.Clamp(BufferScore, 0, 1) * 360);

        // Center label: big score %
        var pct = (int)Math.Round(Math.Clamp(OverallScore, 0, 1) * 100);
        var text = new FormattedText(
            $"{pct}",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            side * 0.22,
            new SolidColorBrush(scoreColor),
            1.0);
        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));

        var unitText = new FormattedText(
            "Score",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            side * 0.06,
            new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            1.0);
        dc.DrawText(unitText, new Point(center.X - unitText.Width / 2, center.Y + text.Height / 2 + 2));
    }

    private static Color LevelColor(QualityLevel level) => level switch
    {
        QualityLevel.Good => Color.FromRgb(0x16, 0xA3, 0x4A),
        QualityLevel.Degraded => Color.FromRgb(0xEA, 0xB3, 0x08),
        QualityLevel.Bad => Color.FromRgb(0xEF, 0x44, 0x44),
        QualityLevel.Disconnected => Color.FromRgb(0x6B, 0x72, 0x80),
        _ => Colors.Gray,
    };

    private static void DrawArc(DrawingContext dc, Pen pen, Point center, double radius, double startDeg, double sweepDeg)
    {
        if (sweepDeg < 0.1 || radius < 2) return;
        sweepDeg = Math.Min(sweepDeg, 359.9);

        var start = PointOnCircle(center, radius, startDeg - 90);
        var end = PointOnCircle(center, radius, startDeg + sweepDeg - 90);
        var isLarge = sweepDeg > 180;

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, isLarge, SweepDirection.Clockwise, true, false);
        }
        geom.Freeze();
        dc.DrawGeometry(null, pen, geom);
    }

    private static Point PointOnCircle(Point center, double r, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(center.X + r * Math.Cos(rad), center.Y + r * Math.Sin(rad));
    }
}
