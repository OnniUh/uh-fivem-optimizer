using System.Windows;
using System.Windows.Media;
using UhOptimizer.Core;

namespace UhOptimizer.App;

public static class ThemeManager
{
    public static readonly (string Name, string Hex)[] Presets =
    [
        ("Living green", "#B5F36B"), ("Glacier blue", "#80C7FF"), ("Violet dusk", "#B3A0FF"),
        ("Rose quartz", "#FF9FB4"), ("Amber glow", "#FFC66F"), ("Ice mint", "#79E2C4")
    ];

    public static Color Color(UiColor c) => System.Windows.Media.Color.FromRgb(c.R, c.G, c.B);
    private static SolidColorBrush Brush(UiColor c) { var brush = new SolidColorBrush(Color(c)); brush.Freeze(); return brush; }

    public static void Apply(AppearanceSettings settings)
    {
        var palette = ThemePalette.Create(settings.AccentHex); var resources = Application.Current.Resources;
        resources["AccentColor"] = Color(palette.Accent);
        resources["Accent"] = Brush(palette.TextAccent);
        resources["AccentRaw"] = Brush(palette.Accent);
        resources["Bg"] = Brush(palette.Background);
        resources["Panel"] = Brush(palette.Panel);
        resources["Sidebar"] = Brush(palette.Background.Mix(palette.Accent, .025));
        resources["Line"] = Brush(palette.Panel.Mix(UiColor.Parse("#A0ADA4"), .15));
        resources["AccentWash"] = Brush(palette.AccentWash);
        resources["AccentLine"] = Brush(palette.AccentLine);
        resources["AccentInk"] = Brush(palette.ButtonInk);
        resources["ChartSecondary"] = Brush(palette.TextAccent.Mix(UiColor.Parse("#F1F6F3"), .55));
        var gradient = new LinearGradientBrush { StartPoint = new(.2, 0), EndPoint = new(.85, 1) };
        gradient.GradientStops.Add(new(Color(palette.ButtonTop), 0));
        gradient.GradientStops.Add(new(Color(palette.ButtonBase), .42));
        gradient.GradientStops.Add(new(Color(palette.ButtonBottom), 1)); gradient.Freeze();
        resources["PrimaryFill"] = gradient;
    }
}
