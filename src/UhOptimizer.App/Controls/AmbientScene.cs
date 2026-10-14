using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace UhOptimizer.App.Controls;

/// <summary>Native vector ribbon: 20 Hz only while visible and allowed. No web runtime or shader dependency.</summary>
public sealed class AmbientScene : FrameworkElement
{
    public static readonly DependencyProperty AccentColorProperty = DependencyProperty.Register(nameof(AccentColor), typeof(Color), typeof(AmbientScene),
        new FrameworkPropertyMetadata(Color.FromRgb(181, 243, 107), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MotionEnabledProperty = DependencyProperty.Register(nameof(MotionEnabled), typeof(bool), typeof(AmbientScene), new PropertyMetadata(false, PolicyChanged));
    public Color AccentColor { get => (Color)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
    public bool MotionEnabled { get => (bool)GetValue(MotionEnabledProperty); set => SetValue(MotionEnabledProperty, value); }
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
    private double _time, _pointerX, _pointerY, _targetX, _targetY;
    public int FrameCount { get; private set; }
    public bool IsAnimating => _timer.IsEnabled;

    public AmbientScene()
    {
        ClipToBounds = true;
        _timer.Tick += (_, _) => { _time += .05; _pointerX += (_targetX - _pointerX) * .12; _pointerY += (_targetY - _pointerY) * .12; FrameCount++; InvalidateVisual(); };
        Loaded += (_, _) => UpdateTimer(); Unloaded += (_, _) => _timer.Stop();
        IsVisibleChanged += (_, _) => UpdateTimer();
        MouseMove += (_, e) => { var p = e.GetPosition(this); _targetX = p.X / Math.Max(1, ActualWidth) - .5; _targetY = p.Y / Math.Max(1, ActualHeight) - .5; };
        MouseLeave += (_, _) => { _targetX = _targetY = 0; };
    }
    private static void PolicyChanged(DependencyObject target, DependencyPropertyChangedEventArgs _) => ((AmbientScene)target).UpdateTimer();
    private void UpdateTimer() { if (IsLoaded && IsVisible && MotionEnabled) _timer.Start(); else _timer.Stop(); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth, h = ActualHeight; if (w < 2 || h < 2) return;
        var accent = AccentColor;
        var glow = new RadialGradientBrush(Color.FromArgb(32, accent.R, accent.G, accent.B), Colors.Transparent) { RadiusX = .54, RadiusY = .6 };
        dc.DrawRectangle(glow, null, new Rect(0, 0, w, h));
        double size = Math.Min(w * .40, h * .62), cx = w * .52 + _pointerX * 10, cy = h * .53 + _pointerY * 8;
        // The original page's flowing bands inform this independent native vector sculpture.
        // Drawing order and depth tint create a metallic ribbon without textures, blur passes, or WebGL.
        for (int band = 0; band < 29; band++)
        {
            double v = (band / 28d - .5) * 1.75, phase = _time * .11;
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                for (int step = 0; step <= 90; step++)
                {
                    double angle = step / 90d * Math.PI * 2;
                    double r = size * (1 + .19 * Math.Cos(angle * 3 + v * 2 + phase));
                    double x = Math.Cos(angle) * r;
                    double y = Math.Sin(angle) * r * .63;
                    double twist = .48 * Math.Sin(angle + phase) + v * .45;
                    double rx = x * Math.Cos(twist) - y * Math.Sin(twist);
                    double ry = x * Math.Sin(twist) + y * Math.Cos(twist);
                    var p = new Point(cx + rx * (1 + v * .23), cy + ry + v * size * .19);
                    if (step == 0) path.BeginFigure(p, false, false); else path.LineTo(p, true, false);
                }
            }
            geometry.Freeze();
            byte alpha = (byte)(70 + 140 * (1 - Math.Abs(v) / 1.2));
            var brush = new LinearGradientBrush
            {
                StartPoint = new(.1, .05), EndPoint = new(.85, 1),
                GradientStops = new()
                {
                    new(Color.FromArgb((byte)(alpha / 2), accent.R, accent.G, accent.B), 0),
                    new(Color.FromArgb(alpha, 234, 245, 227), .34),
                    new(Color.FromArgb(alpha, accent.R, accent.G, accent.B), .52),
                    new(Color.FromArgb(25, accent.R, accent.G, accent.B), .8),
                    new(Color.FromArgb(125, accent.R, accent.G, accent.B), 1)
                }
            };
            brush.Freeze(); var pen = new Pen(brush, band % 5 == 0 ? 1.9 : 1.05); pen.Freeze(); dc.DrawGeometry(null, pen, geometry);
        }
        var orbit = new Pen(new SolidColorBrush(Color.FromArgb(35, accent.R, accent.G, accent.B)), .7) { DashStyle = new DashStyle([2, 7], 0) };
        dc.DrawEllipse(null, orbit, new Point(cx, cy), size * 1.29, size * 1.08);
        for (int i = 0; i < 10; i++)
        {
            double a = i * 2.39996 + _time * .025;
            double radius = size * (1.13 + .12 * Math.Sin(i * 4.1));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(65 + (i % 3) * 35), accent.R, accent.G, accent.B)), null,
                new Point(cx + Math.Cos(a) * radius * 1.2, cy + Math.Sin(a) * radius), i % 4 == 0 ? 2 : 1, i % 4 == 0 ? 2 : 1);
        }
    }
}
