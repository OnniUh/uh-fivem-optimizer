using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace UhOptimizer.Core;

public static class LocalFiles
{
    public static string Check(string path)
    {
        string full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal) || full == Path.GetPathRoot(full)) throw new IOException("Choose a local file or folder, not a drive root or network path.");
        for (string? item = full; item is not null; item = Path.GetDirectoryName(item))
            if ((File.Exists(item) || Directory.Exists(item)) && (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked files and folders are excluded: " + item);
        return full;
    }
    public static bool Inside(string file, string root) => Path.GetFullPath(file).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static byte[] Read(string path)
    {
        Check(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > 4 * 1024 * 1024) throw new IOException("File exceeds the 4 MB safety limit.");
        using var output = new MemoryStream(); input.CopyTo(output); if (output.Length > 4 * 1024 * 1024) throw new IOException("File grew beyond the safety limit."); return output.ToArray();
    }
    public static void Write(string path, byte[] bytes)
    {
        Check(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); Check(path);
        string temp = path + ".uh-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
            Check(path); File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static XDocument Xml(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }
}

public sealed record FileEdit(string Path, byte[]? Original, byte[] Replacement);
public sealed record TuningPlan(string Label, IReadOnlyList<FileEdit> Files, IReadOnlyList<string> Details);
public sealed record SavedEdit(string Path, byte[]? Original, string? OriginalHash, string ReplacementHash);
public sealed record FileJournal(int SchemaVersion, string Label, DateTimeOffset CreatedUtc, string Status, SavedEdit[] Files);

/// <summary>Prepared journal is durable before any target write. Interrupted batches remain recoverable.</summary>
public sealed class FileTuningService(string folder, Func<string, bool> allowedTarget, Func<bool>? gameRunning = null)
{
    public string JournalPath { get; } = Path.Combine(folder, "files-backup.json");
    public FileJournal? Load()
    {
        LocalFiles.Check(JournalPath);
        if (!File.Exists(JournalPath)) return null;
        if (new FileInfo(JournalPath).Length > 48 * 1024 * 1024) throw new IOException("Backup exceeds its safety limit.");
        FileJournal journal;
        try { journal = JsonSerializer.Deserialize<FileJournal>(File.ReadAllText(JournalPath)) ?? throw new IOException("Empty backup."); }
        catch (JsonException ex) { throw new IOException("Backup is damaged. Keep it for manual recovery.", ex); }
        if (journal.SchemaVersion != 1 || journal.Status is not ("Prepared" or "Applied" or "Restored") || journal.Files is null || journal.Files.Length is < 1 or > 64)
            throw new IOException("Backup format is unsupported.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edit in journal.Files)
        {
            if (string.IsNullOrEmpty(edit.Path) || !Path.IsPathFullyQualified(edit.Path) || !allowedTarget(edit.Path) || !paths.Add(edit.Path) ||
                edit.ReplacementHash is null || edit.ReplacementHash.Length != 64 ||
                (edit.Original is not null && (edit.Original.Length > 4 * 1024 * 1024 || LocalFiles.Hash(edit.Original) != edit.OriginalHash)) ||
                (edit.Original is null && edit.OriginalHash is not null)) throw new IOException("Backup validation failed.");
        }
        return journal;
    }
    private void GuardGame() { if (gameRunning?.Invoke() == true) throw new IOException("Close FiveM and GTA V before changing or restoring files."); }
    private void Save(FileJournal journal) => LocalFiles.Write(JournalPath, JsonSerializer.SerializeToUtf8Bytes(journal, new JsonSerializerOptions { WriteIndented = true }));
    private static string? CurrentHash(string path) => File.Exists(LocalFiles.Check(path)) ? LocalFiles.Hash(LocalFiles.Read(path)) : null;
    public void Apply(TuningPlan plan)
    {
        GuardGame();
        if (Load() is { Status: not "Restored" }) throw new IOException("Restore the pending backup before applying another preset.");
        if (plan.Files.Count is < 1 or > 64 || plan.Files.Sum(e => (long)e.Replacement.Length + (e.Original?.Length ?? 0)) > 24 * 1024 * 1024) throw new IOException("Invalid tuning batch size.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in plan.Files)
        {
            if (!Path.IsPathFullyQualified(file.Path) || !allowedTarget(file.Path) || !paths.Add(file.Path) || file.Replacement.Length > 4 * 1024 * 1024) throw new IOException("This target is outside the preset's allowed files.");
            if (CurrentHash(file.Path) != (file.Original is null ? null : LocalFiles.Hash(file.Original))) throw new IOException("A file changed since preview. Preview again.");
        }
        var journal = new FileJournal(1, plan.Label, DateTimeOffset.UtcNow, "Prepared", plan.Files.Select(e => new SavedEdit(e.Path, e.Original, e.Original is null ? null : LocalFiles.Hash(e.Original), LocalFiles.Hash(e.Replacement))).ToArray());
        Save(journal);
        foreach (var file in plan.Files)
        {
            GuardGame();
            if (CurrentHash(file.Path) != (file.Original is null ? null : LocalFiles.Hash(file.Original))) throw new IOException("A file changed during apply. Use Recovery.");
            LocalFiles.Write(file.Path, file.Replacement);
            if (CurrentHash(file.Path) != LocalFiles.Hash(file.Replacement)) throw new IOException("File verification failed. Use Recovery.");
        }
        Save(journal with { Status = "Applied" });
    }
    public string[] Conflicts()
    {
        var journal = Load(); if (journal is null || journal.Status == "Restored") return [];
        return journal.Files.Where(e => { string? current = CurrentHash(e.Path); return current != e.OriginalHash && current != e.ReplacementHash; }).Select(e => e.Path).ToArray();
    }
    public void Restore(bool preserveConflicts = false)
    {
        GuardGame(); var journal = Load() ?? throw new IOException("No file backup exists.");
        if (journal.Status == "Restored") return;
        var current = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase); long snapshotBytes = 0;
        foreach (var edit in journal.Files)
        {
            byte[]? bytes = File.Exists(LocalFiles.Check(edit.Path)) ? LocalFiles.Read(edit.Path) : null;
            if ((snapshotBytes += bytes?.Length ?? 0) > 24 * 1024 * 1024) throw new IOException("Current files exceed the restore snapshot limit. Keep the backup for manual recovery.");
            current.Add(edit.Path, bytes);
        }
        var hashes = current.ToDictionary(e => e.Key, e => e.Value is null ? null : LocalFiles.Hash(e.Value), StringComparer.OrdinalIgnoreCase);
        var conflicts = journal.Files.Where(e => hashes[e.Path] != e.OriginalHash && hashes[e.Path] != e.ReplacementHash).Select(e => e.Path).ToArray();
        if (conflicts.Length > 0)
        {
            if (!preserveConflicts) throw new IOException("Files have changed since the preset. Review before replacing those changes.");
            // Keep intervening user/game edits before replacing them with the original backup.
            var copy = conflicts.Select(p => new { Path = p, Content = current[p] }).ToArray();
            LocalFiles.Write(Path.Combine(folder, "before-restore-" + Guid.NewGuid().ToString("N") + ".json"), JsonSerializer.SerializeToUtf8Bytes(copy));
        }
        foreach (var edit in journal.Files)
        {
            GuardGame(); LocalFiles.Check(edit.Path);
            if (CurrentHash(edit.Path) != hashes[edit.Path]) throw new IOException("A file changed during restore. Review again.");
            if (edit.Original is null) { if (File.Exists(edit.Path)) File.Delete(edit.Path); }
            else LocalFiles.Write(edit.Path, edit.Original);
            if (CurrentHash(edit.Path) != edit.OriginalHash) throw new IOException("Restore verification failed.");
        }
        Save(journal with { Status = "Restored" });
    }
}

public static class LowEndGraphics
{
    public static bool IsTarget(string path) => string.Equals(Path.GetFileName(path), "gta5_settings.xml", StringComparison.OrdinalIgnoreCase);
    public static TuningPlan Preview(string path, bool resolution720)
    {
        path = LocalFiles.Check(path);
        if (!IsTarget(path)) throw new IOException("Select FiveM's gta5_settings.xml, not the base game's settings.xml.");
        byte[] original = LocalFiles.Read(path); var doc = LocalFiles.Xml(original);
        if (doc.Root?.Name != "Settings" || doc.Root.Elements("graphics").Count() != 1) throw new IOException("This is not a recognized FiveM graphics settings file.");
        if (doc.Root.Elements("version").Count() != 1 || doc.Root.Element("version")?.Attribute("value")?.Value != "27") throw new IOException("Only the recognized GTA V Legacy settings format (version 27) is supported. Use in-game settings for other formats.");
        var graphics = doc.Root.Element("graphics")!;
        foreach (string key in new[] { "ShadowQuality", "TextureQuality", "MSAA", "PostFX" })
            if (graphics.Elements(key).Count() != 1) throw new IOException("Missing or duplicate graphics setting: " + key);
        var details = new List<string>();
        void Set(XElement section, string key, string value, bool numeric = false)
        {
            var items = section.Elements(key).ToArray(); if (items.Length == 0) return;
            if (items.Length != 1 || items[0].Attribute("value") is not XAttribute attr) throw new IOException("Unsupported setting: " + key);
            if (numeric && (!double.TryParse(attr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double old) || !double.IsFinite(old) || old < 0)) throw new IOException("Invalid value for " + key);
            if (numeric && double.Parse(attr.Value, CultureInfo.InvariantCulture) < double.Parse(value, CultureInfo.InvariantCulture)) return;
            if (attr.Value == value) return;
            details.Add(key + ": " + attr.Value + " → " + value); attr.Value = value;
        }
        foreach (string key in new[] { "TextureQuality", "ShaderQuality", "ShadowQuality", "ReflectionQuality", "ReflectionMSAA", "SSAO", "MSAA", "MSAAFragments", "MSAAQuality", "ParticleQuality", "WaterQuality", "GrassQuality", "PostFX", "Tessellation", "MaxLodScale", "Shadow_Distance" }) Set(graphics, key, "0", true);
        Set(graphics, "LodScale", "0.250000", true);
        Set(graphics, "MotionBlurStrength", "0.000000", true);
        foreach (string key in new[] { "TXAA_Enabled", "UltraShadows_Enabled", "Shadow_LongShadows", "DoF", "HdStreamingInFlight" }) Set(graphics, key, "false");
        if (resolution720)
        {
            var video = doc.Root.Element("video") ?? throw new IOException("Video settings are missing.");
            if (video.Elements("ScreenWidth").Count() != 1 || video.Elements("ScreenHeight").Count() != 1) throw new IOException("Resolution settings are missing or duplicated.");
            // Only lower resolution, never increase either dimension of an existing smaller display mode.
            Set(video, "ScreenWidth", "1280", true); Set(video, "ScreenHeight", "720", true);
        }
        if (details.Count == 0) return new("Low-end graphics", [], ["The supported settings already meet this preset. Nothing to change."]);
        using var output = new MemoryStream();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false })) doc.Save(writer);
        return new("Low-end graphics" + (resolution720 ? " + up to 720p" : " / existing resolution"), [new(path, original, output.ToArray())], details);
    }
}

public static class LegacyVisualPack
{
    public const string Forum = "https://forum.cfx.re/t/release-fivem-for-low-end-pc-s-2-0/46352";
    public static bool AllowedRelative(string name)
    {
        if (name.Contains('\\') || name.Contains(':') || name.Split('/').Any(p => p is ".." or "." or "")) return false;
        return name.Equals("citizen/common/data/visualsettings.dat", StringComparison.OrdinalIgnoreCase) ||
            (name.StartsWith("citizen/common/data/timecycle/", StringComparison.OrdinalIgnoreCase) && name.Split('/').Length == 5 && name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
    }
    public static bool IsTarget(string path) => FindCitizen(path) is string citizen && AllowedRelative("citizen/" + Path.GetRelativePath(citizen, path).Replace('\\', '/'));
    private static string? FindCitizen(string path)
    {
        for (string? dir = Path.GetDirectoryName(Path.GetFullPath(path)); dir is not null; dir = Path.GetDirectoryName(dir))
            if (Path.GetFileName(dir).Equals("citizen", StringComparison.OrdinalIgnoreCase)) return dir;
        return null;
    }
    public static TuningPlan Preview(string zip, string applicationFolder)
    {
        string root = LocalFiles.Check(applicationFolder);
        if (!Directory.Exists(Path.Combine(root, "citizen")) || !File.Exists(Path.Combine(root, "CitizenFX.ini"))) throw new IOException("Choose the FiveM application folder containing citizen and CitizenFX.ini.");
        LocalFiles.Check(zip); if (new FileInfo(zip).Length > 8 * 1024 * 1024) throw new IOException("ZIP exceeds the 8 MB limit.");
        using var archive = ZipFile.OpenRead(zip);
        if (archive.Entries.Count > 128) throw new IOException("Too many ZIP entries.");
        var edits = new List<FileEdit>(); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in archive.Entries)
        {
            string name = entry.FullName;
            if (name.EndsWith('/') && !name.Contains("..") && !name.Contains('\\') && !name.Contains(':')) continue;
            if (!AllowedRelative(name)) throw new IOException("Unsupported ZIP entry: " + name + ". Only visualsettings.dat and timecycle XML files inside citizen/common/data are accepted.");
            if (entry.Length <= 0 || entry.Length > 4 * 1024 * 1024 || (total += entry.Length) > 12 * 1024 * 1024 || edits.Count >= 64) throw new IOException("Unpacked files exceed safety limits.");
            string target = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!LocalFiles.Inside(target, root) || !paths.Add(target)) throw new IOException("Duplicate or invalid ZIP path.");
            LocalFiles.Check(target);
            using var input = entry.Open(); using var buffer = new MemoryStream();
            var chunk = new byte[8192]; int read;
            while ((read = input.Read(chunk)) > 0) { if (buffer.Length + read > entry.Length) throw new IOException("ZIP length mismatch."); buffer.Write(chunk, 0, read); }
            byte[] replacement = buffer.ToArray(); if (replacement.LongLength != entry.Length) throw new IOException("ZIP length mismatch.");
            if (name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) _ = LocalFiles.Xml(replacement);
            else if (replacement.Any(b => b == 0) || !Encoding.UTF8.GetString(replacement).Contains("#")) throw new IOException("Unrecognized visualsettings.dat text.");
            edits.Add(new(target, File.Exists(target) ? LocalFiles.Read(target) : null, replacement));
        }
        if (edits.Count == 0) throw new IOException("The ZIP contains no supported visual files.");
        return new("Legacy visual pack (experimental)", edits, edits.Select(e => Path.GetRelativePath(root, e.Path)).ToArray());
    }
}
