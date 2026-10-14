using System.Globalization;
using System.Text.Json;

namespace UhOptimizer.Core;

public record AppearanceSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string AccentHex { get; init; } = "#B5F36B";
    public bool ReduceMotion { get; init; }
    public bool PauseWhileGaming { get; init; } = true;
}

public sealed class AppearanceStore(string folder)
{
    public string SettingsPath => Path.Combine(folder, "appearance.json");
    public AppearanceSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new();
        try
        {
            var value = JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(SettingsPath));
            if (value is null || value.SchemaVersion != 1 || !UiColor.TryParse(value.AccentHex, out var color))
                throw new InvalidDataException("Invalid appearance settings.");
            return value with { AccentHex = color.Hex };
        }
        catch (JsonException ex) { throw new InvalidDataException("Appearance settings could not be read.", ex); }
    }

    public void Save(AppearanceSettings value)
    {
        if (value.SchemaVersion != 1 || !UiColor.TryParse(value.AccentHex, out var color))
            throw new InvalidDataException("Choose a six-digit hex color before saving.");
        Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, $"appearance-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value with { AccentHex = color.Hex }, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, SettingsPath, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public readonly record struct UiColor(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public static bool TryParse(string? text, out UiColor color)
    {
        color = default;
        if (text is null) return false;
        string value = text.Trim();
        if (value.StartsWith('#')) value = value[1..];
        if (value.Length != 6 || !uint.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb)) return false;
        color = new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); return true;
    }
    public static UiColor Parse(string hex) => TryParse(hex, out var c) ? c : throw new FormatException("Use a hex color such as #B5F36B.");
    public UiColor Mix(UiColor other, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return new((byte)Math.Round(R + (other.R - R) * amount), (byte)Math.Round(G + (other.G - G) * amount), (byte)Math.Round(B + (other.B - B) * amount));
    }
    private static double Linear(byte channel) { double x = channel / 255d; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
    public double Luminance => .2126 * Linear(R) + .7152 * Linear(G) + .0722 * Linear(B);
    public double Contrast(UiColor other) => (Math.Max(Luminance, other.Luminance) + .05) / (Math.Min(Luminance, other.Luminance) + .05);
    public UiColor ReadableOn(UiColor background, double required = 4.5)
    {
        if (Contrast(background) >= required) return this;
        var destination = new UiColor(255, 255, 255).Contrast(background) >= new UiColor(0, 0, 0).Contrast(background)
            ? new UiColor(255, 255, 255) : new UiColor(0, 0, 0);
        for (int i = 1; i <= 100; i++) { var c = Mix(destination, i / 100d); if (c.Contrast(background) >= required) return c; }
        return destination;
    }
}

public sealed record ThemePalette(UiColor Accent, UiColor Background, UiColor Panel, UiColor TextAccent, UiColor AccentWash,
    UiColor AccentLine, UiColor ButtonTop, UiColor ButtonBase, UiColor ButtonBottom, UiColor ButtonInk)
{
    public static ThemePalette Create(string hex)
    {
        var raw = UiColor.Parse(hex); var black = new UiColor(0, 0, 0); var white = new UiColor(255, 255, 255);
        var bg = UiColor.Parse("#0D1110").Mix(raw, .015);
        var panel = UiColor.Parse("#161C1B").Mix(raw, .025);
        var wash = panel.Mix(raw, .12);
        var ink = raw.Contrast(black) >= raw.Contrast(white) ? black : white;
        // Every primary-button gradient stop maintains readable text, even for dark custom colors.
        return new(raw, bg, panel, raw.ReadableOn(panel).ReadableOn(wash), wash, panel.Mix(raw, .38),
            raw.Mix(white, .14).ReadableOn(ink), raw.ReadableOn(ink), raw.Mix(black, .10).ReadableOn(ink), ink);
    }
}
