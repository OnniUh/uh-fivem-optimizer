using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace UhOptimizer.App.Controls;

/// <summary>Event-driven hover/focus/press effects; no per-button render loop.</summary>
public static class ButtonMotion
{
    public static bool MotionAllowed { get; set; } = true;
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(ButtonMotion), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Button button) return;
        if ((bool)e.NewValue)
        {
            button.MouseEnter += Enter; button.MouseLeave += Leave; button.PreviewMouseLeftButtonDown += Down;
            button.PreviewMouseLeftButtonUp += Up; button.MouseMove += Move;
            button.GotKeyboardFocus += Focus; button.LostKeyboardFocus += Blur;
        }
        else
        {
            button.MouseEnter -= Enter; button.MouseLeave -= Leave; button.PreviewMouseLeftButtonDown -= Down;
            button.PreviewMouseLeftButtonUp -= Up; button.MouseMove -= Move;
            button.GotKeyboardFocus -= Focus; button.LostKeyboardFocus -= Blur;
        }
    }
    private static void Enter(object sender, MouseEventArgs _) => Animate((Button)sender, true, false);
    private static void Leave(object sender, MouseEventArgs _) => Animate((Button)sender, false, false);
    private static void Down(object sender, MouseButtonEventArgs _) => Animate((Button)sender, true, true);
    private static void Up(object sender, MouseButtonEventArgs _) { var b = (Button)sender; Animate(b, b.IsMouseOver, false); }
    private static void Focus(object sender, KeyboardFocusChangedEventArgs _) => Animate((Button)sender, true, false);
    private static void Blur(object sender, KeyboardFocusChangedEventArgs _) { var b = (Button)sender; Animate(b, b.IsMouseOver, false); }
    private static void Move(object sender, MouseEventArgs args)
    {
        if (!MotionAllowed) return;
        var b = (Button)sender;
        if (b.Template.FindName("PointerLight", b) is System.Windows.Controls.Border light)
        {
            if (light.Background is not RadialGradientBrush brush || brush.IsFrozen)
            {
                brush = new RadialGradientBrush(System.Windows.Media.Color.FromArgb(55, 255, 255, 255), Colors.Transparent) { RadiusX = .7, RadiusY = 1.6 };
                light.Background = brush;
            }
            var p = args.GetPosition(b); var center = new Point(p.X / Math.Max(1, b.ActualWidth), p.Y / Math.Max(1, b.ActualHeight));
            brush.Center = center; brush.GradientOrigin = center;
        }
    }
    private static void Animate(Button button, bool hover, bool pressed)
    {
        double scale = pressed ? .985 : hover ? 1.018 : 1;
        if (button.RenderTransform is not ScaleTransform transform || transform.IsFrozen)
        { transform = new ScaleTransform(1, 1); button.RenderTransform = transform; button.RenderTransformOrigin = new(.5, .5); }
        if (!MotionAllowed) { transform.BeginAnimation(ScaleTransform.ScaleXProperty, null); transform.BeginAnimation(ScaleTransform.ScaleYProperty, null); transform.ScaleX = transform.ScaleY = 1; }
        else
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(pressed ? 90 : 220)) { EasingFunction = ease });
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(pressed ? 90 : 220)) { EasingFunction = ease });
        }
        if (button.Template.FindName("PointerLight", button) is System.Windows.Controls.Border light)
        {
            double opacity = hover ? 1 : 0;
            if (MotionAllowed) light.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(180)));
            else { light.BeginAnimation(UIElement.OpacityProperty, null); light.Opacity = opacity; }
        }
    }
}
