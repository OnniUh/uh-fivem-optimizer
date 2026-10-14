using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UhOptimizer.App.Controls;
using UhOptimizer.Core;

namespace UhOptimizer.App;

public partial class MainWindow
{
    private readonly AppearanceStore _appearanceStore;
    private AppearanceSettings _appearance = new();
    private bool _appearanceReady, _fiveMRunning;
    private int _sampleTicks;
    private readonly List<(Button Button, string Hex)> _presetButtons = [];

    private void InitializeAppearance(string? error)
    {
        foreach (var preset in ThemeManager.Presets)
        {
            var color = ThemeManager.Color(UiColor.Parse(preset.Hex));
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new System.Windows.Shapes.Ellipse { Width = 13, Height = 13, Fill = new SolidColorBrush(color), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = preset.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = content, Margin = new Thickness(0, 0, 10, 10), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(17, 13, 17, 13) };
            System.Windows.Automation.AutomationProperties.SetName(button, preset.Name + " accent " + preset.Hex);
            button.Click += (_, _) => PreviewAppearance(_appearance with { AccentHex = preset.Hex });
            _presetButtons.Add((button, preset.Hex)); PresetPanel.Children.Add(button);
        }
        SyncAppearanceControls(); _appearanceReady = true;
        if (error is not null) AppearanceNotice.Text = "Saved appearance could not be read. Defaults are shown; Save appearance will replace those preferences. " + error;
    }

    private void SyncAppearanceControls()
    {
        _appearanceReady = false;
        CustomColorInput.Text = _appearance.AccentHex;
        ReduceMotionCheck.IsChecked = _appearance.ReduceMotion;
        PauseGamingCheck.IsChecked = _appearance.PauseWhileGaming;
        string? name = ThemeManager.Presets.Where(p => p.Hex == _appearance.AccentHex).Select(p => p.Name).FirstOrDefault();
        ColorFeedback.Text = (name ?? "Custom color") + " / " + _appearance.AccentHex;
        ColorFeedback.SetResourceReference(ForegroundProperty, "Accent");
        foreach (var (button, hex) in _presetButtons)
        {
            bool selected = hex == _appearance.AccentHex;
            button.SetResourceReference(Button.BorderBrushProperty, selected ? "Accent" : "Line");
            button.SetResourceReference(Button.BackgroundProperty, selected ? "AccentWash" : "Panel");
            button.SetResourceReference(Button.ForegroundProperty, selected ? "Accent" : "Muted");
        }
        _appearanceReady = true;
    }

    private void PreviewAppearance(AppearanceSettings settings)
    {
        _appearance = settings;
        ThemeManager.Apply(_appearance); SyncAppearanceControls(); RefreshMotion(); DrawGraphs();
        AppearanceNotice.Text = "Live preview · Save appearance to keep these choices after restarting.";
        Status("Appearance preview updated. Your PC optimization settings have not changed.");
    }

    private bool TryCustomColor(out string hex)
    {
        if (UiColor.TryParse(CustomColorInput.Text, out var color)) { hex = color.Hex; return true; }
        hex = _appearance.AccentHex;
        ColorFeedback.Text = "Use six hex digits, for example #B5F36B.";
        ColorFeedback.Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 140));
        Status("That color could not be applied. Enter a six-digit hex color.", true); return false;
    }

    private void PreviewColorClick(object sender, RoutedEventArgs e)
    {
        if (TryCustomColor(out var hex)) PreviewAppearance(_appearance with { AccentHex = hex });
    }
    private void MotionOptionChanged(object sender, RoutedEventArgs e)
    {
        if (!_appearanceReady) return;
        _appearance = _appearance with { ReduceMotion = ReduceMotionCheck.IsChecked == true, PauseWhileGaming = PauseGamingCheck.IsChecked == true };
        RefreshMotion(); AppearanceNotice.Text = "Motion preference previewed · Save appearance to keep it.";
    }
    private void SaveAppearanceClick(object sender, RoutedEventArgs e)
    {
        if (!TryCustomColor(out var hex)) return;
        try
        {
            var value = _appearance with { AccentHex = hex };
            _appearanceStore.Save(value); PreviewAppearance(value);
            AppearanceNotice.Text = "Saved on this PC. Your color and motion choices will return next time.";
            Status("Appearance saved. Your PC optimization settings have not changed.");
        }
        catch (Exception ex) { AppearanceNotice.Text = "Could not save appearance: " + ex.Message; Status(AppearanceNotice.Text, true); }
    }
    private void ResetAppearanceClick(object sender, RoutedEventArgs e) => PreviewAppearance(new());

    private void RefreshMotion()
    {
        if (!_appearanceReady) return;
        bool systemMotion = SystemParameters.ClientAreaAnimation;
        bool gamingPause = _appearance.PauseWhileGaming && _fiveMRunning;
        bool active = IsLoaded && IsActive && WindowState != WindowState.Minimized;
        bool allowed = systemMotion && !_appearance.ReduceMotion && !gamingPause && active;
        ButtonMotion.MotionAllowed = allowed;
        HeroScene.MotionEnabled = allowed; AppearanceScene.MotionEnabled = allowed;
        MotionStatusText.Text = !systemMotion ? "Still artwork · Windows animation preferences are respected." :
            _appearance.ReduceMotion ? "Still artwork · Reduced motion is on." :
            gamingPause ? "Motion paused · FiveM is running." :
            !active ? "Motion paused · This window is inactive or minimized." :
            "Subtle motion · Native vector artwork, capped at 20 frames per second.";
    }
}
