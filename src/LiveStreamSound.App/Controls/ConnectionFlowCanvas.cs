using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LiveStreamSound.App.Controls;

/// <summary>
/// Overlay Canvas that draws animated Bezier "connection flows" from a host anchor
/// element to each client anchor element, with particles flowing along each path.
/// Tempo and opacity of the particles modulate with <see cref="AudioLevel"/>.
///
/// Anchor discovery: the canvas walks the visual tree of its owning Window on each
/// <see cref="FrameworkElement.LayoutUpdated"/> tick and picks up FrameworkElements
/// with Tag="host-anchor" or Tag="client-anchor". This keeps the XAML dead-simple
/// — no binding plumbing, just set Tag on the tile templates.
/// </summary>
public sealed class ConnectionFlowCanvas : Canvas
{
    private const int ParticlesPerFlow = 5;
    private const double ParticleRadius = 3.5;

    public static readonly DependencyProperty AudioLevelProperty =
        DependencyProperty.Register(
            nameof(AudioLevel), typeof(double), typeof(ConnectionFlowCanvas),
            new PropertyMetadata(0d));

    public double AudioLevel
    {
        get => (double)GetValue(AudioLevelProperty);
        set => SetValue(AudioLevelProperty, value);
    }

    public static readonly DependencyProperty FlowColorProperty =
        DependencyProperty.Register(
            nameof(FlowColor), typeof(Color), typeof(ConnectionFlowCanvas),
            new PropertyMetadata(Color.FromRgb(0x60, 0xA5, 0xFA)));

    public Color FlowColor
    {
        get => (Color)GetValue(FlowColorProperty);
        set => SetValue(FlowColorProperty, value);
    }

    private readonly List<Flow> _flows = new();
    private FrameworkElement? _host;
    private readonly List<FrameworkElement> _clients = new();
    private double _timeSeconds;

    public ConnectionFlowCanvas()
    {
        IsHitTestVisible = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering += OnRender;
        LayoutUpdated += OnLayoutUpdated;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering -= OnRender;
        LayoutUpdated -= OnLayoutUpdated;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => RebuildIfChanged();

    private void RebuildIfChanged()
    {
        var window = Window.GetWindow(this);
        if (window is null) return;

        FrameworkElement? host = null;
        var clients = new List<FrameworkElement>();
        Walk(window, host, clients);

        if (host is null && _host is null && _clients.Count == clients.Count && _clients.SequenceEqual(clients))
            return;

        _host = FindByTag(window, "host-anchor");
        _clients.Clear();
        CollectByTag(window, "client-anchor", _clients);

        Rebuild();
    }

    private static void Walk(DependencyObject parent, FrameworkElement? host, List<FrameworkElement> clients)
    {
        // kept for signature compat; actual collection done in FindByTag/CollectByTag below
    }

    private static FrameworkElement? FindByTag(DependencyObject root, string tag)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && (fe.Tag as string) == tag)
                return fe;
            var recurse = FindByTag(child, tag);
            if (recurse is not null) return recurse;
        }
        return null;
    }

    private static void CollectByTag(DependencyObject root, string tag, List<FrameworkElement> acc)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && (fe.Tag as string) == tag)
                acc.Add(fe);
            CollectByTag(child, tag, acc);
        }
    }

    private void Rebuild()
    {
        Children.Clear();
        _flows.Clear();

        if (_host is null || _clients.Count == 0 || ActualWidth < 2 || ActualHeight < 2) return;

        Point hostCenter = CenterInCanvas(_host);
        var color = FlowColor;

        foreach (var client in _clients)
        {
            var p3 = CenterInCanvas(client);
            if (double.IsNaN(p3.X) || double.IsInfinity(p3.X)) continue;

            // Gentle S-curve: pull control points vertically so lines fan out
            var dy = p3.Y - hostCenter.Y;
            var p1 = new Point(hostCenter.X, hostCenter.Y + dy * 0.4);
            var p2 = new Point(p3.X, p3.Y - dy * 0.4);

            var flow = new Flow(hostCenter, p1, p2, p3);
            for (var i = 0; i < ParticlesPerFlow; i++)
            {
                var e = new Ellipse
                {
                    Width = ParticleRadius * 2,
                    Height = ParticleRadius * 2,
                    Fill = new SolidColorBrush(color),
                    Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 2 },
                };
                Children.Add(e);
                flow.Particles.Add(e);
                flow.Offsets.Add(i / (double)ParticlesPerFlow);
            }
            _flows.Add(flow);
        }
    }

    private Point CenterInCanvas(FrameworkElement fe)
    {
        try
        {
            var transform = fe.TransformToVisual(this);
            var bounds = transform.TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
            return new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        }
        catch
        {
            return new Point(double.NaN, double.NaN);
        }
    }

    private void OnRender(object? sender, EventArgs e)
    {
        _timeSeconds += 1 / 60.0;

        // Audio modulates: silence → slow (0.25 laps/sec), loud → fast (0.9 laps/sec).
        // Opacity 0.15 at silence → 0.85 at peak. Capped so it never totally disappears.
        var a = Math.Clamp(AudioLevel, 0, 1);
        var speed = 0.25 + a * 0.65;
        var opacity = 0.15 + a * 0.70;

        foreach (var flow in _flows)
            flow.Advance(_timeSeconds, speed, opacity, ParticleRadius);
    }

    private sealed class Flow
    {
        public Point P0, P1, P2, P3;
        public readonly List<Ellipse> Particles = new();
        public readonly List<double> Offsets = new();

        public Flow(Point p0, Point p1, Point p2, Point p3) { P0 = p0; P1 = p1; P2 = p2; P3 = p3; }

        public void Advance(double time, double speed, double opacity, double radius)
        {
            for (var i = 0; i < Particles.Count; i++)
            {
                var u = ((time * speed) + Offsets[i]) % 1.0;
                var pt = CubicBezier(P0, P1, P2, P3, u);
                SetLeft(Particles[i], pt.X - radius);
                SetTop(Particles[i], pt.Y - radius);
                // Fade in/out at the ends so particles don't pop at anchors
                var endFade = Math.Min(u, 1 - u) * 6;
                Particles[i].Opacity = opacity * Math.Clamp(endFade, 0, 1);
            }
        }

        public static Point CubicBezier(Point p0, Point p1, Point p2, Point p3, double t)
        {
            var u = 1 - t;
            return new Point(
                u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X,
                u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y);
        }
    }
}
