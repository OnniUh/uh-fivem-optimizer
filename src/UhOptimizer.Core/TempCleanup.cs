using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UhOptimizer.Core;

public sealed record CleanupRoot(string Name, string Path, bool Prefetch = false);
public sealed record CleanupFile(string Root, string Path, long Bytes, DateTime CreatedUtc, DateTime WrittenUtc, uint Volume, ulong FileId);
public sealed record CleanupPreview(DateTimeOffset CreatedUtc, IReadOnlyList<CleanupFile> Files, int Skipped, bool Partial, IReadOnlyList<string> Notices)
{
    public long Bytes => Files.Sum(f => f.Bytes);
}
public sealed record CleanupResult(int Deleted, long Bytes, int Skipped, bool Interrupted);

/// <summary>Explicit preview then permanent removal of old files only. No recursive directory deletion.</summary>
public sealed class TempCleanupService
{
    private readonly CleanupRoot[] _roots;
    private readonly Func<bool> _gameRunning;
    public TempCleanupService(IEnumerable<CleanupRoot> roots, Func<bool>? gameRunning = null)
    {
        _roots = roots.Select(r => r with { Path = LocalFiles.Check(r.Path) }).DistinctBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (_roots.Any(r => !Path.GetFileName(r.Path).Equals(r.Prefetch ? "Prefetch" : "Temp", StringComparison.OrdinalIgnoreCase))) throw new IOException("Cleanup is restricted to Temp and Prefetch folders.");
        // Overlapping roots would make a file appear twice in the preview.
        if (_roots.Any(a => _roots.Any(b => a != b && LocalFiles.Inside(a.Path, b.Path)))) throw new IOException("Cleanup roots overlap.");
        _gameRunning = gameRunning ?? (() => false);
    }
    public CleanupPreview Preview(CancellationToken cancel = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Cleanup uses Windows file handles.");
        var files = new List<CleanupFile>(); var notes = new List<string>(); int skipped = 0, visited = 0;
        var clock = Stopwatch.StartNew(); DateTime cutoff = DateTime.UtcNow.AddDays(-7);
        foreach (var root in _roots)
        {
            var pending = new Stack<string>(); pending.Push(root.Path);
            while (pending.Count > 0)
            {
                cancel.ThrowIfCancellationRequested();
                if (clock.Elapsed > TimeSpan.FromSeconds(20) || visited >= 50_000) return new(DateTimeOffset.UtcNow, files, skipped, true, notes.Append("Scan limit reached. Only listed files can be removed.").ToArray());
                string dir = pending.Pop();
                try
                {
                    LocalFiles.Check(dir);
                    if (!Directory.Exists(dir)) continue;
                    foreach (string path in Directory.EnumerateFileSystemEntries(dir))
                    {
                        cancel.ThrowIfCancellationRequested();
                        if (++visited >= 50_000 || clock.Elapsed > TimeSpan.FromSeconds(20)) return new(DateTimeOffset.UtcNow, files, skipped, true, notes.Append("Scan limit reached. Only listed files can be removed.").ToArray());
                        try
                        {
                            var attr = File.GetAttributes(path);
                            if ((attr & (FileAttributes.ReparsePoint | FileAttributes.ReadOnly | FileAttributes.System)) != 0) { skipped++; continue; }
                            if ((attr & FileAttributes.Directory) != 0) { if (!root.Prefetch) pending.Push(path); continue; }
                            if (root.Prefetch && !Path.GetExtension(path).Equals(".pf", StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                            using var handle = CreateFileW(path, 0x80, 7, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
                            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info) || (info.Attributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.ReadOnly | FileAttributes.System)) != 0) { skipped++; continue; }
                            DateTime created = DateTime.FromFileTimeUtc(((long)info.CreatedHigh << 32) | info.CreatedLow);
                            DateTime written = DateTime.FromFileTimeUtc(((long)info.WrittenHigh << 32) | info.WrittenLow);
                            if (written >= cutoff || created >= cutoff) { skipped++; continue; }
                            files.Add(new(root.Path, path, ((long)info.SizeHigh << 32) | info.SizeLow, created, written, info.Volume, ((ulong)info.IndexHigh << 32) | info.IndexLow));
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; notes.Add(root.Name + ": inaccessible or linked folder skipped."); }
            }
        }
        return new(DateTimeOffset.UtcNow, files, skipped, false, notes.Distinct().ToArray());
    }
    public CleanupResult Clean(CleanupPreview preview, CancellationToken cancel = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Cleanup uses Windows file handles.");
        if (DateTimeOffset.UtcNow - preview.CreatedUtc > TimeSpan.FromMinutes(10) || preview.CreatedUtc > DateTimeOffset.UtcNow) throw new IOException("Cleanup preview expired. Scan again.");
        if (_gameRunning()) throw new IOException("Close FiveM and GTA V before cleanup.");
        int deleted = 0, skipped = 0; long bytes = 0; var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in preview.Files)
        {
            if (cancel.IsCancellationRequested || _gameRunning()) return new(deleted, bytes, skipped, true);
            var root = _roots.FirstOrDefault(r => r.Path.Equals(file.Root, StringComparison.OrdinalIgnoreCase));
            if (root is null || !LocalFiles.Inside(file.Path, root.Path) || !seen.Add(file.Path) ||
                (root.Prefetch && (Path.GetDirectoryName(file.Path) != root.Path || !Path.GetExtension(file.Path).Equals(".pf", StringComparison.OrdinalIgnoreCase)))) { skipped++; continue; }
            try
            {
                LocalFiles.Check(file.Path);
                if (DeleteUnchanged(file)) { deleted++; bytes += file.Bytes; } else skipped++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
        }
        return new(deleted, bytes, skipped, false);
    }

    // Hold an exclusive non-following handle, verify identity/age and resolved path, then mark that
    // same handle for deletion. A replaced path, active file, hard link or reparse point is skipped.
    private static bool DeleteUnchanged(CleanupFile file)
    {
        using var handle = CreateFileW(file.Path, 0x00010000 | 0x80, 0, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info)) return false;
        if (info.Volume != file.Volume || (((ulong)info.IndexHigh << 32) | info.IndexLow) != file.FileId) return false;
        if ((info.Attributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.ReadOnly | FileAttributes.System)) != 0 || info.Links != 1) return false;
        long length = ((long)info.SizeHigh << 32) | info.SizeLow;
        DateTime created = DateTime.FromFileTimeUtc(((long)info.CreatedHigh << 32) | info.CreatedLow);
        DateTime written = DateTime.FromFileTimeUtc(((long)info.WrittenHigh << 32) | info.WrittenLow);
        if (length != file.Bytes || created != file.CreatedUtc || written != file.WrittenUtc || created >= DateTime.UtcNow.AddDays(-7) || written >= DateTime.UtcNow.AddDays(-7)) return false;
        var path = new System.Text.StringBuilder(32768);
        uint count = GetFinalPathNameByHandleW(handle, path, (uint)path.Capacity, 0);
        if (count == 0 || count >= path.Capacity || !path.ToString().Equals(@"\\?\" + Path.GetFullPath(file.Path), StringComparison.OrdinalIgnoreCase)) return false;
        LocalFiles.Check(file.Path);
        int disposition = 1;
        return SetFileInformationByHandle(handle, 4, ref disposition, sizeof(int));
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes, CreatedLow, CreatedHigh, AccessedLow, AccessedHigh, WrittenLow, WrittenHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string file, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, System.Text.StringBuilder path, uint size, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int infoClass, ref int value, int size);
}
