using System.IO;
using System.Text.Json;
using System.Windows;
using UhOptimizer.Core;

namespace UhOptimizer.App;

/// <summary>Opt-in installer integration test, restricted to a named disposable copy of the app.</summary>
public static class UpdateProbe
{
    public static void ValidateFolder(string folder, bool requireCurrentExecutable = true)
    {
        string full = Path.GetFullPath(folder);
        if (!Path.GetFileName(full).StartsWith("uh-update-probe-", StringComparison.Ordinal) ||
            (requireCurrentExecutable && !string.Equals(full, Path.GetDirectoryName(Environment.ProcessPath), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Installer probes must run from a disposable uh-update-probe-* folder containing a copy of the EXE.");
    }
    public static async Task BeginAsync(string folder)
    {
        try
        {
            ValidateFolder(folder);
            string self = Environment.ProcessPath!;
            string hash = await UpdateClient.FileHashAsync(self);
            File.WriteAllText(Path.Combine(folder, "probe-expected.txt"), hash);
            File.WriteAllText(Path.Combine(folder, "probe-appearance.json"), "keep-appearance");
            File.WriteAllText(Path.Combine(folder, "probe-power-backup.json"), "keep-recovery");
            string cache = Path.Combine(folder, "candidate"); Directory.CreateDirectory(cache);
            string candidate = Path.Combine(cache, "UhFiveMOptimizer.exe"); File.Copy(self, candidate);
            var update = new UpdateCandidate(AppBuild.Version, "probe", new string('a', 40), UpdateClient.ReleasesUrl,
                new Uri(UpdateClient.ReleasesUrl), new FileInfo(self).Length, hash);
            await UpdateInstaller.LaunchWorkerAsync(candidate, update, CancellationToken.None, probe: true);
            Application.Current.Shutdown(0);
        }
        catch (Exception ex) { Report(folder, false, ex.ToString()); Application.Current.Shutdown(1); }
    }
    public static async Task FinishAsync(string folder)
    {
        try
        {
            ValidateFolder(folder);
            string self = Environment.ProcessPath!;
            string expected = File.ReadAllText(Path.Combine(folder, "probe-expected.txt"));
            bool success = await UpdateClient.FileHashAsync(self) == expected && await UpdateClient.FileHashAsync(self + ".previous") == expected &&
                File.ReadAllText(Path.Combine(folder, "probe-appearance.json")) == "keep-appearance" &&
                File.ReadAllText(Path.Combine(folder, "probe-power-backup.json")) == "keep-recovery";
            Report(folder, success, success ? "Helper handshake, parent exit, verified replacement, previous EXE, and relaunch passed." : "Probe verification failed.");
            Application.Current.Shutdown(success ? 0 : 1);
        }
        catch (Exception ex) { Report(folder, false, ex.ToString()); Application.Current.Shutdown(1); }
    }
    private static void Report(string folder, bool success, string message) => LocalFiles.Write(Path.Combine(folder, "probe-result.json"),
        JsonSerializer.SerializeToUtf8Bytes(new { Success = success, Version = AppBuild.Version, Message = message }, new JsonSerializerOptions { WriteIndented = true }));
}
