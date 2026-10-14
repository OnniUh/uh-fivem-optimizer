using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using UhOptimizer.Core;

namespace UhOptimizer.App;

public static class AppBuild
{
    public static string Version => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.3.1";
#if SINGLE_FILE
    public static bool SupportsSelfUpdate => true;
#else
    public static bool SupportsSelfUpdate => false;
#endif
}

public record InstallRequest(string TargetPath, int ParentId, long ParentStartUtcTicks, long Size, string Sha256, string Nonce, bool Probe = false);

public static class UpdateInstaller
{
    public static async Task LaunchWorkerAsync(string candidate, UpdateCandidate update, CancellationToken token, bool probe = false)
    {
        if (!AppBuild.SupportsSelfUpdate) throw new InvalidOperationException("In-app installation is available in the standalone release EXE.");
        string target = Environment.ProcessPath ?? throw new InvalidOperationException("The current app location is unavailable.");
        string folder = Path.GetDirectoryName(candidate)!;
        string nonce = Guid.NewGuid().ToString("N");
        using var parent = Process.GetCurrentProcess();
        var request = new InstallRequest(target, Environment.ProcessId, parent.StartTime.ToUniversalTime().Ticks, update.Size, update.Sha256, nonce, probe);
        string requestFile = Path.Combine(folder, "install-request.json");
        File.WriteAllText(requestFile, JsonSerializer.Serialize(request));
        string ready = Path.Combine(folder, "ready-" + nonce);
        var start = new ProcessStartInfo(candidate) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder };
        start.ArgumentList.Add("--install-update"); start.ArgumentList.Add(requestFile);
        using var worker = Process.Start(start) ?? throw new IOException("The update helper could not start.");
        try
        {
            for (int i = 0; i < 450; i++)
            {
                token.ThrowIfCancellationRequested();
                try { if (File.Exists(ready) && File.ReadAllText(ready) == update.Sha256) return; }
                catch (IOException) { /* A transient file lock must not cancel a valid handoff. */ }
                if (worker.HasExited) throw new IOException("The update helper could not prepare the installation. The current app is unchanged.");
                await Task.Delay(100, token);
            }
            throw new IOException("The update helper did not become ready in time. The current app is unchanged.");
        }
        catch
        {
            // A failed handoff must never apply later when the player closes the app.
            File.WriteAllText(Path.Combine(folder, "cancel-" + nonce), nonce);
            try { if (!worker.HasExited) worker.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            throw;
        }
    }

    public static async Task RunWorkerAsync(string requestFile)
    {
        string? staged = null;
        string? canceled = null;
        try
        {
            string self = Path.GetFullPath(Environment.ProcessPath ?? throw new IOException("The update helper location is unavailable."));
            string folder = Path.GetDirectoryName(self)!;
            requestFile = Path.GetFullPath(requestFile);
            if (!AppBuild.SupportsSelfUpdate || Path.GetDirectoryName(requestFile) != folder || Path.GetFileName(requestFile) != "install-request.json" || new FileInfo(requestFile).Length > 8192)
                throw new InvalidDataException("Invalid update helper request.");
            var request = JsonSerializer.Deserialize<InstallRequest>(File.ReadAllText(requestFile)) ?? throw new InvalidDataException("Empty update request.");
            string hash = UpdateClient.ParseHash(request.Sha256);
            canceled = Path.Combine(folder, "cancel-" + request.Nonce);
            if (!Guid.TryParseExact(request.Nonce, "N", out _) || request.ParentId == Environment.ProcessId ||
                new FileInfo(self).Length != request.Size || await UpdateClient.FileHashAsync(self) != hash)
                throw new InvalidDataException("The update helper could not verify its executable.");
            string target = Path.GetFullPath(request.TargetPath);
            if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || target == self || !File.Exists(target))
                throw new InvalidDataException("Invalid installation target.");
            if (request.Probe) UpdateProbe.ValidateFolder(Path.GetDirectoryName(target)!, requireCurrentExecutable: false);
            using var parent = Process.GetProcessById(request.ParentId);
            if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStartUtcTicks ||
                !string.Equals(Path.GetFullPath(parent.MainModule?.FileName ?? ""), target, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update request does not match the running app.");
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("In-app updates cannot replace a linked executable.");
            staged = target + ".update-" + request.Nonce + ".tmp";
            // Stage in the destination folder before asking the parent to close.
            // Permission or disk-space failures therefore leave the current app running.
            File.Copy(self, staged, overwrite: false);
            if (await UpdateClient.FileHashAsync(staged) != hash) throw new InvalidDataException("The staged update failed verification.");
            // Publish only after the complete marker is flushed and its writer is closed.
            LocalFiles.Write(Path.Combine(folder, "ready-" + request.Nonce), System.Text.Encoding.UTF8.GetBytes(hash));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            while (!parent.HasExited)
            {
                if (File.Exists(canceled)) throw new OperationCanceledException("The update handoff was canceled.");
                await Task.Delay(100, timeout.Token);
            }
            if (File.Exists(canceled)) throw new OperationCanceledException("The update handoff was canceled.");
            UpdateFileTransaction.ReplaceAndLaunch(staged, target, target + ".previous", hash, path =>
            {
                var restart = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! };
                if (request.Probe) { restart.ArgumentList.Add("--finish-update-probe"); restart.ArgumentList.Add(Path.GetDirectoryName(path)!); }
                using var restarted = Process.Start(restart)
                    ?? throw new IOException("The updated app could not restart.");
            });
            Application.Current.Shutdown(0);
        }
        catch (Exception ex)
        {
            if (canceled is null || !File.Exists(canceled)) MessageBox.Show("The update could not finish.\n\n" + ex.Message + "\n\nYour appearance preferences and power-plan backup have been kept. You can reopen the existing app and try again.",
                "Uh FiveM Optimizer update", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown(1);
        }
        finally { try { if (staged is not null && File.Exists(staged)) File.Delete(staged); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
