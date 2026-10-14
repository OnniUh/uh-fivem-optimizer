using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using UhOptimizer.Core;

namespace UhOptimizer.App;

public sealed class WindowsPowerSystem : IPowerSystem
{
    public IReadOnlyList<PowerPlan> GetPlans()
    {
        var plans = new List<PowerPlan>();
        for (uint index = 0; index < 256; index++)
        {
            uint size = 16;
            var buffer = new byte[16];
            uint result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, index, buffer, ref size);
            if (result == 259) break; // ERROR_NO_MORE_ITEMS
            Check(result);
            var id = new Guid(buffer);
            uint nameSize = 0;
            uint nameResult = PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, null, ref nameSize);
            if (nameResult != 0 && nameResult != 234) Check(nameResult);
            if (nameSize > 65536) throw new InvalidDataException("Windows returned an invalid plan name.");
            var name = new byte[nameSize];
            Check(PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, name, ref nameSize));
            plans.Add(new(id, Encoding.Unicode.GetString(name).TrimEnd('\0')));
        }
        return plans;
    }

    public Guid GetActive()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out var ptr));
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { LocalFree(ptr); }
    }

    public void SetActive(Guid id) => Check(PowerSetActiveScheme(IntPtr.Zero, ref id));
    private static void Check(uint code) { if (code != 0) throw new Win32Exception((int)code); }

    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subgroup, uint access, uint index, byte[] buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting, byte[]? buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr active);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr ptr);
}

public record MachineScan(string Cpu, string Gpu, string Os, int Threads, double TotalMemoryGb, string? FiveMFolder, double? FreeDiskGb, string DiskName, string[] Notes);
public record ResourceSample(DateTimeOffset Time, double? CpuPercent, double MemoryPercent, double UsedMemoryGb, double TotalMemoryGb);

public sealed class WindowsScanner
{
    private ulong _idle, _kernel, _user;
    private bool _hasPrevious;

    public MachineScan Scan(string? preferredFolder)
    {
        var notes = new List<string>();
        string cpu = "CPU information unavailable", gpu = "GPU information unavailable";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            cpu = key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? cpu;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { notes.Add("Windows blocked CPU details."); }
        try
        {
            using var video = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
            var names = new HashSet<string>();
            foreach (var id in video?.GetSubKeyNames() ?? [])
            {
                using var device = video?.OpenSubKey(id + @"\0000");
                if (device?.GetValue("DriverDesc") is string name && !string.IsNullOrWhiteSpace(name)) names.Add(name);
            }
            if (names.Count > 0) gpu = string.Join(" / ", names);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { notes.Add("Windows blocked GPU details."); }
        var memory = ReadMemory();
        string? fiveM = preferredFolder is null ? null : FiveMPaths.Resolve(preferredFolder);
        fiveM ??= FiveMPaths.Resolve(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM"));
        double? diskFree = null; string diskName = "Unavailable";
        try
        {
            string root = Path.GetPathRoot(fiveM ?? Environment.SystemDirectory)!;
            var drive = new DriveInfo(root);
            diskFree = drive.AvailableFreeSpace / 1073741824d; diskName = root;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { notes.Add("Disk information is unavailable."); }
        return new(cpu, gpu, RuntimeInformation.OSDescription, Environment.ProcessorCount,
            memory.TotalPhysical / 1073741824d, fiveM, diskFree, diskName, notes.ToArray());
    }

    public ResourceSample Sample()
    {
        var memory = ReadMemory(); double? cpu = null;
        if (GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
        {
            if (_hasPrevious && kernel >= _kernel && user >= _user && idle >= _idle)
            {
                ulong total = kernel - _kernel + user - _user, idleDelta = idle - _idle;
                if (total > 0) cpu = Math.Clamp(100d * (total - Math.Min(total, idleDelta)) / total, 0, 100);
            }
            _idle = idle; _kernel = kernel; _user = user; _hasPrevious = true;
        }
        double used = memory.TotalPhysical - memory.AvailablePhysical;
        return new(DateTimeOffset.UtcNow, cpu, used / memory.TotalPhysical * 100, used / 1073741824d, memory.TotalPhysical / 1073741824d);
    }

    public static string FiveMState()
    {
        bool found = false;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { if (process.ProcessName.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase)) found = true; }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            }
        }
        return found ? "Running" : "Not running";
    }

    private static MemoryStatus ReadMemory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (status.TotalPhysical == 0) throw new InvalidOperationException("Windows returned no memory information.");
        return status;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
}
