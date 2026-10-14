namespace UhOptimizer.Core;

public static class FiveMPaths
{
    public static string? Resolve(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        var full = Path.GetFullPath(folder);
        // Accept the launcher directory, FiveM.app, or its parent.
        if (File.Exists(Path.Combine(full, "FiveM.exe"))) return full;
        if (Directory.Exists(Path.Combine(full, "citizen")) && File.Exists(Path.Combine(full, "CitizenFX.ini"))) return full;
        var app = Path.Combine(full, "FiveM.app");
        if (Directory.Exists(Path.Combine(app, "citizen"))) return full;
        return null;
    }

    public static string? DataDirectory(string folder)
    {
        var direct = Path.Combine(folder, "data");
        if (Directory.Exists(Path.Combine(folder, "citizen")) && Directory.Exists(direct)) return direct;
        var nested = Path.Combine(folder, "FiveM.app", "data");
        return Directory.Exists(nested) ? nested : null;
    }

    /// <summary>Read-only, bounded inspection. Does not follow directory junctions/symlinks.</summary>
    public static CacheSize InspectCache(string dataFolder, CancellationToken cancel = default)
    {
        long bytes = 0; int files = 0; bool partial = false;
        if (!Directory.Exists(dataFolder)) return new(0, 0, false);
        if ((File.GetAttributes(dataFolder) & FileAttributes.ReparsePoint) != 0) return new(0, 0, true);
        var roots = new[] { "cache", "server-cache", "server-cache-priv" };
        var stopAt = DateTime.UtcNow.AddSeconds(5);
        foreach (var name in roots)
        {
            var root = Path.Combine(dataFolder, name);
            if (!Directory.Exists(root)) continue;
            var pending = new Stack<string>(); pending.Push(root);
            while (pending.Count > 0)
            {
                cancel.ThrowIfCancellationRequested();
                if (DateTime.UtcNow > stopAt || files >= 100_000) return new(bytes, files, true);
                var current = pending.Pop();
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) { partial = true; continue; }
                    foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                    {
                        cancel.ThrowIfCancellationRequested();
                        if (DateTime.UtcNow > stopAt || files >= 100_000) return new(bytes, files, true);
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) { partial = true; continue; }
                        if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                        else { bytes += new FileInfo(entry).Length; files++; }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { partial = true; }
            }
        }
        return new(bytes, files, partial);
    }
}

public record CacheSize(long Bytes, int Files, bool IsPartial);
