using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UhOptimizer.Core;

namespace UhOptimizer.App;

public partial class MainWindow
{
    private FileTuningService _graphicsService = null!, _packService = null!;
    private string? _graphicsPath;
    private TempCleanupService? _cleanupService;
    private CleanupPreview? _cleanupPreview;
    private CancellationTokenSource? _cleanupCancellation;
    private DateTime _lastGameCheck;
    private bool _gameFilesInUse;

    private bool GamesRunning()
    {
        if (DateTime.UtcNow - _lastGameCheck < TimeSpan.FromMilliseconds(750)) return _gameFilesInUse;
        _lastGameCheck = DateTime.UtcNow; _gameFilesInUse = false;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    string name = process.ProcessName;
                    if (name.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("GTA5", StringComparison.OrdinalIgnoreCase)) _gameFilesInUse = true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        return _gameFilesInUse;
    }
    private void InitializeOptimizations()
    {
        _graphicsService = new(Path.Combine(_dataFolder, "graphics"), LowEndGraphics.IsTarget, GamesRunning);
        _packService = new(Path.Combine(_dataFolder, "visual-pack"), LegacyVisualPack.IsTarget, GamesRunning);
        // Smoke mode uses only its own fixture, never the player's real settings file.
        string candidate = Path.Combine(_smokeOutput is null ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) : _dataFolder, "CitizenFX", "gta5_settings.xml");
        if (File.Exists(candidate)) _graphicsPath = candidate;
        GraphicsPathText.Text = _graphicsPath ?? "No gta5_settings.xml found. Launch FiveM once, close it, then choose its settings file in Roaming\\CitizenFX.";
        foreach (var check in new[] { UserTempCheck, WindowsTempCheck, PrefetchCheck })
        {
            check.Checked += (_, _) => InvalidateCleanup(); check.Unchecked += (_, _) => InvalidateCleanup();
        }
        Closed += (_, _) => _cleanupCancellation?.Cancel();
        RefreshFileRecovery(); RefreshOptimizationControls();
    }
    private void RefreshOptimizationControls()
    {
        if (_graphicsService is null) return;
        bool enabled = !_busy && !_updateBusy;
        foreach (var control in new Control[] { GraphicsReviewButton, ChooseGraphicsButton, LegacyEditionCheck, Resolution720Check, ImportPackButton, CleanupScanButton, UserTempCheck, WindowsTempCheck, PrefetchCheck, LaunchFiveMButton }) control.IsEnabled = enabled;
        CleanupDeleteButton.IsEnabled = enabled && _cleanupPreview?.Files.Count > 0;
        CleanupCancelButton.IsEnabled = _cleanupCancellation is not null;
        RefreshFileRecovery();
    }
    private void RefreshFileRecovery()
    {
        if (_graphicsService is null) return;
        void Show(FileTuningService service, TextBlock text, Button button)
        {
            try
            {
                var backup = service.Load();
                text.Text = backup is null ? "No changes yet." : $"{backup.Label} · {backup.Status}\n{backup.Files.Length} file(s) · saved {backup.CreatedUtc.ToLocalTime():g}";
                button.IsEnabled = !_busy && !_updateBusy && backup is { Status: not "Restored" };
            }
            catch (Exception ex) { text.Text = "Backup needs attention: " + ex.Message; button.IsEnabled = false; }
        }
        Show(_graphicsService, GraphicsBackupText, GraphicsRestoreButton); Show(_packService, PackBackupText, PackRestoreButton);
    }
    private bool ReviewAction(string title, string description, IEnumerable<string> details, string accept)
    {
        var dialog = new Window { Owner = this, Title = title, Width = 720, Height = 610, MinWidth = 540, MinHeight = 430, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (System.Windows.Media.Brush)FindResource("Bg"), Foreground = Foreground, FontFamily = FontFamily };
        var panel = new DockPanel { Margin = new Thickness(24) };
        var heading = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18), FontSize = 14 };
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 12, 0) };
        var confirm = new Button { Content = accept, Style = (Style)FindResource("PrimaryButton") };
        cancel.Click += (_, _) => dialog.DialogResult = false; confirm.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        panel.Children.Add(new TextBox { Text = string.Join(Environment.NewLine, details), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        dialog.Content = panel; return dialog.ShowDialog() == true;
    }
    private void ChooseGraphicsClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _updateBusy) return;
        var dialog = new OpenFileDialog { Title = "Choose FiveM's gta5_settings.xml (Roaming\\CitizenFX)", FileName = "gta5_settings.xml", Filter = "FiveM graphics settings|gta5_settings.xml", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) { _graphicsPath = dialog.FileName; GraphicsPathText.Text = _graphicsPath; }
    }
    private void GraphicsPreviewClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _updateBusy) return;
        try
        {
            if (LegacyEditionCheck.IsChecked != true) throw new IOException("Confirm GTA V Legacy above first. Use in-game graphics settings for Enhanced.");
            if (GamesRunning()) throw new IOException("Close FiveM and GTA V first.");
            if (_graphicsPath is null) { ChooseGraphicsClick(sender, e); if (_graphicsPath is null) return; }
            var plan = LowEndGraphics.Preview(_graphicsPath, Resolution720Check.IsChecked == true);
            if (plan.Files.Count == 0) { GraphicsResultText.Text = plan.Details[0]; return; }
            if (!ReviewAction("Review low-end preset", "This reduces visual quality. A complete original-file backup will be saved before changes. No FPS gain is guaranteed. Resolution changes only if selected. Use Recovery to undo it.", new[] { _graphicsPath, "", "Changes:" }.Concat(plan.Details), "Back up & apply")) return;
            SetBusy(true); _graphicsService.Apply(plan);
            GraphicsResultText.Text = $"Applied and verified {plan.Details.Count} setting changes. Backup saved in Recovery. Launch FiveM to test.";
            Status("Low-end graphics applied. Your original file is saved in Recovery.");
        }
        catch (Exception ex) { GraphicsResultText.Text = ex.Message; Status("Graphics preset did not complete. Check Recovery. " + ex.Message, true); }
        finally { SetBusy(false); }
    }
    private void VisualPackThreadClick(object sender, RoutedEventArgs e) => OpenTarget(LegacyVisualPack.Forum);
    private string ApplicationFolder()
    {
        string? folder = _scan?.FiveMFolder;
        if (folder is null) throw new IOException("Locate FiveM in Overview first.");
        if (Directory.Exists(Path.Combine(folder, "citizen"))) return folder;
        return Path.Combine(folder, "FiveM.app");
    }
    private void VisualPackImportClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _updateBusy) return;
        try
        {
            if (LegacyEditionCheck.IsChecked != true) throw new IOException("This pack is Legacy only. Confirm GTA V Legacy in the first card. Enhanced currently blocks graphics mods.");
            if (GamesRunning()) throw new IOException("Close FiveM and GTA V first.");
            var dialog = new OpenFileDialog { Title = "Choose the author's downloaded low-end visual pack ZIP", Filter = "ZIP archive|*.zip", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            var plan = LegacyVisualPack.Preview(dialog.FileName, ApplicationFolder());
            if (!ReviewAction("Review experimental visual pack", "This old community pack has unverified compatibility with modern FiveM. It can cause loading or visual problems and may be blocked by your server. Import only if your server allows it. Existing originals are backed up; new files are tracked for removal during restore. No FPS gain is guaranteed.", plan.Files.Select(f => f.Path + (f.Original is null ? " [new file]" : " [replace, original saved]")), "Back up & import")) return;
            SetBusy(true); _packService.Apply(plan);
            VisualPackResultText.Text = $"Imported and verified {plan.Files.Count} visual files. Restore from Recovery if loading or visuals break.";
            Status("Visual pack imported. Original files are saved in Recovery.");
        }
        catch (Exception ex) { VisualPackResultText.Text = ex.Message; Status("Visual pack import did not complete. Check Recovery. " + ex.Message, true); }
        finally { SetBusy(false); }
    }
    private void RestoreFiles(FileTuningService service)
    {
        if (_busy || _updateBusy) return;
        try
        {
            if (GamesRunning()) throw new IOException("Close FiveM and GTA V first.");
            var backup = service.Load(); if (backup is null || backup.Status == "Restored") return;
            var conflicts = service.Conflicts();
            string extra = conflicts.Length == 0 ? "" : $"\n{conflicts.Length} file(s) changed after the preset. Their current contents will be saved separately before restoring the original backup. This replaces those newer edits.";
            if (!ReviewAction("Review file recovery", "Restore the original files saved before " + backup.Label + "? New files introduced by that preset will be removed." + extra, backup.Files.Select(f => f.Path + (f.Original is null ? " [remove introduced file]" : " [restore original]")), "Restore originals")) return;
            SetBusy(true); service.Restore(conflicts.Length > 0); Status("Original files restored and verified.");
        }
        catch (Exception ex) { Status("File recovery did not complete: " + ex.Message, true); }
        finally { SetBusy(false); }
    }
    private void GraphicsRestoreClick(object sender, RoutedEventArgs e) => RestoreFiles(_graphicsService);
    private void PackRestoreClick(object sender, RoutedEventArgs e) => RestoreFiles(_packService);
    private void InvalidateCleanup()
    {
        _cleanupPreview = null; _cleanupService = null; CleanupDeleteButton.IsEnabled = false;
        CleanupFilesText.Text = ""; CleanupFilesText.Visibility = Visibility.Collapsed;
        CleanupSummaryText.Text = "Selection changed. Preview again before deleting.";
    }
    private async void CleanupPreviewClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _updateBusy) return;
        InvalidateCleanup();
        var roots = new List<CleanupRoot>();
        var selectionNotices = new List<string>();
        if (UserTempCheck.IsChecked == true)
        {
            string standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
            roots.Add(new("User Temp", standard));
            string actual = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (!actual.Equals(standard, StringComparison.OrdinalIgnoreCase))
            {
                if (Path.GetFileName(actual).Equals("Temp", StringComparison.OrdinalIgnoreCase)) roots.Add(new("Redirected %TEMP%", actual));
                else selectionNotices.Add("Redirected %TEMP% is excluded because its folder is not named Temp: " + actual);
            }
        }
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (WindowsTempCheck.IsChecked == true) roots.Add(new("Windows Temp", Path.Combine(windows, "Temp")));
        if (PrefetchCheck.IsChecked == true) roots.Add(new("Prefetch", Path.Combine(windows, "Prefetch"), true));
        if (roots.Count == 0) { CleanupSummaryText.Text = "Select at least one folder."; return; }
        _cleanupCancellation = new(); SetBusy(true); CleanupSummaryText.Text = "Scanning old files. Reading only…";
        try
        {
            if (_smokeOutput is not null) throw new IOException("Real cleanup is disabled in UI smoke mode.");
            if (GamesRunning()) throw new IOException("Close FiveM and GTA V first.");
            _cleanupService = new(roots, GamesRunning);
            _cleanupPreview = await Task.Run(() => _cleanupService.Preview(_cleanupCancellation.Token));
            _cleanupPreview = _cleanupPreview with { Notices = _cleanupPreview.Notices.Concat(selectionNotices).ToArray() };
            var result = _cleanupPreview;
            CleanupSummaryText.Text = $"{result.Files.Count:N0} candidates · {result.Bytes / 1048576d:0.##} MB · {result.Skipped:N0} excluded{(result.Partial ? " · partial scan" : "")}\n" + string.Join("\n", result.Notices) + "\nPreview expires in 10 minutes. Deletion is permanent.";
            CleanupFilesText.Text = string.Join(Environment.NewLine, result.Files.Select(f => $"{f.Bytes:N0} bytes  {f.Path}"));
            CleanupFilesText.Visibility = Visibility.Visible;
            Status("Cleanup preview ready. No files have been deleted.");
        }
        catch (OperationCanceledException) { InvalidateCleanup(); CleanupSummaryText.Text = "Scan canceled. No files deleted."; }
        catch (Exception ex) { InvalidateCleanup(); CleanupSummaryText.Text = ex.Message; }
        finally { _cleanupCancellation.Dispose(); _cleanupCancellation = null; SetBusy(false); }
    }
    private async void CleanupDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _updateBusy || _cleanupPreview is not { } preview || _cleanupService is not { } service) return;
        if (!ReviewAction("Review permanent deletion", $"Permanently remove up to {preview.Files.Count:N0} old files ({preview.Bytes / 1048576d:0.##} MB)? There is no backup or recycle-bin recovery. Locked, changed, protected and linked files will be skipped. Prefetch removal can slow later app launches. Close games and other apps first.", preview.Files.Select(f => f.Path), "Delete listed files")) return;
        _cleanupCancellation = new(); SetBusy(true);
        try
        {
            var result = await Task.Run(() => service.Clean(preview, _cleanupCancellation.Token));
            InvalidateCleanup(); CleanupSummaryText.Text = $"{(result.Interrupted ? "Stopped. " : "Finished. ")}{result.Deleted:N0} files removed · {result.Bytes / 1048576d:0.##} MB · {result.Skipped:N0} skipped. Preview again for an updated list.";
            Status("Cleanup finished. " + CleanupSummaryText.Text);
        }
        catch (Exception ex) { InvalidateCleanup(); CleanupSummaryText.Text = "Cleanup stopped: " + ex.Message + " Preview again to see what remains."; }
        finally { _cleanupCancellation.Dispose(); _cleanupCancellation = null; SetBusy(false); }
    }
    private void CleanupCancelClick(object sender, RoutedEventArgs e) => _cleanupCancellation?.Cancel();
    private void LaunchFiveMClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _updateBusy) return;
        try
        {
            if (GamesRunning()) { Status("FiveM or GTA V is already running."); return; }
            string folder = _scan?.FiveMFolder ?? throw new IOException("Locate FiveM in Overview first.");
            string launcher = Path.Combine(folder, "FiveM.exe");
            if (!File.Exists(launcher) && Path.GetFileName(folder).Equals("FiveM.app", StringComparison.OrdinalIgnoreCase)) launcher = Path.Combine(Path.GetDirectoryName(folder)!, "FiveM.exe");
            LocalFiles.Check(launcher);
            if (!File.Exists(launcher)) throw new IOException("FiveM.exe was not found. Choose the launcher folder in Overview.");
            Process.Start(new ProcessStartInfo(launcher) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(launcher)! });
            Status("FiveM launched. No automatic cleanup or tuning applied."); WindowState = WindowState.Minimized;
        }
        catch (Exception ex) { Status("Could not launch FiveM: " + ex.Message, true); }
    }
}
