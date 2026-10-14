using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using UhOptimizer.Core;

namespace UhOptimizer.App;

public partial class MainWindow : Window
{
    private readonly string _dataFolder;
    private readonly string? _smokeOutput;
    private readonly WindowsScanner _scanner = new();
    private readonly WindowsPowerSystem _power = new();
    private readonly PowerService _service;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly List<ResourceSample> _samples = [];
    private MachineScan? _scan;
    private string? _preferredFolder;
    private bool _busy;
    private string? _powerError;

    public MainWindow(string dataFolder, string? smokeOutput)
    {
        _dataFolder = dataFolder; _smokeOutput = smokeOutput;
        _appearanceStore = new(dataFolder);
        string? appearanceError = null;
        try { _appearance = _appearanceStore.Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { appearanceError = ex.Message; }
        ThemeManager.Apply(_appearance);
        InitializeComponent();
        _service = new(_power, new JsonBackupStore(dataFolder));
        InitializeAppearance(appearanceError);
        InitializeUpdates();
        InitializeOptimizations();
        try
        {
            string settings = System.IO.Path.Combine(dataFolder, "fivem-path.txt");
            if (File.Exists(settings)) _preferredFolder = File.ReadAllText(settings).Trim();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Loaded += async (_, _) =>
        {
            ShowPage("Overview");
            await ScanAsync();
            Tick(); _timer.Start();
            if (_smokeOutput is not null) await SmokeTestAsync(_smokeOutput);
        };
        _timer.Tick += (_, _) => Tick();
        Closed += (_, _) => { _timer.Stop(); HeroScene.MotionEnabled = AppearanceScene.MotionEnabled = false; };
        SizeChanged += (_, _) => DrawGraphs();
        Activated += (_, _) => RefreshMotion();
        Deactivated += (_, _) => RefreshMotion();
        StateChanged += (_, _) => RefreshMotion();
    }

    private void Navigate(object sender, RoutedEventArgs e) => ShowPage((string)((Button)sender).Tag);
    private void OpenTune(object sender, RoutedEventArgs e) => ShowPage("Optimize");

    private void ShowPage(string page)
    {
        var pages = new[] { OverviewPage, TunePage, OptimizePage, RestorePage, AppearancePage, UpdatesPage, AboutPage };
        var names = new[] { "Overview", "Tune", "Optimize", "Restore", "Appearance", "Updates", "About" };
        var buttons = new[] { OverviewNav, TuneNav, OptimizeNav, RestoreNav, AppearanceNav, UpdatesNav, AboutNav };
        for (int i = 0; i < names.Length; i++)
        {
            pages[i].Visibility = names[i] == page ? Visibility.Visible : Visibility.Collapsed;
            if (names[i] == page) buttons[i].SetResourceReference(BackgroundProperty, "AccentWash");
            else buttons[i].Background = Brushes.Transparent;
            buttons[i].SetResourceReference(ForegroundProperty, names[i] == page ? "Accent" : "Muted");
        }
        PageTitle.Text = page switch { "Tune" => "Tune, on your terms.", "Optimize" => "Performance, with clear tradeoffs.", "Restore" => "Keep a way back.", "Appearance" => "A setup that feels like yours.", "Updates" => "Keep your toolkit current.", "About" => "Built to be inspected.", _ => "Your setup, at a glance." };
        PageScroll.ScrollToTop();
        ScanButton.Visibility = page == "Appearance" ? Visibility.Collapsed : Visibility.Visible;
        AppearanceSaveHeader.Visibility = page == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        RefreshMotion();
        if (page == "Restore") { RefreshBackup(); RefreshFileRecovery(); }
        if (page == "Overview") Dispatcher.BeginInvoke(DrawGraphs);
    }

    private async void ScanClick(object sender, RoutedEventArgs e) => await ScanAsync();

    private async Task ScanAsync()
    {
        if (_busy) return;
        SetBusy(true); Status("Scanning hardware and FiveM. Reading only…");
        try
        {
            _scan = await Task.Run(() => _scanner.Scan(_preferredFolder));
            CpuValue.Text = _scan.Cpu; CpuDetail.Text = $"{_scan.Threads} logical processors";
            RamValue.Text = $"{_scan.TotalMemoryGb:0.#} GB";
            DiskValue.Text = _scan.FreeDiskGb is double free ? $"{free:0.#} GB" : "Unavailable";
            DiskDetail.Text = $"Free on {_scan.DiskName}";
            GpuValue.Text = "GPU  /  " + _scan.Gpu; OsValue.Text = "OS  /  " + _scan.Os;
            FiveMValue.Text = _scan.FiveMFolder is null ? "Not located yet" : "Installation found";
            FiveMPath.Text = _scan.FiveMFolder ?? "Choose your FiveM folder to inspect its storage.";
            OpenFolderButton.IsEnabled = _scan.FiveMFolder is not null;
            CacheValue.Text = "";
            RefreshPower();
            if (_scan.FiveMFolder is string folder && FiveMPaths.DataDirectory(folder) is string data)
            {
                CacheValue.Text = "Inspecting cache size…";
                var cache = await Task.Run(() => FiveMPaths.InspectCache(data));
                CacheValue.Text = $"Cache: {cache.Bytes / 1048576d:0.#} MB · {cache.Files:N0} files{(cache.IsPartial ? " (partial scan)" : "")} · read-only";
            }
            else CacheValue.Text = "Cache data folder not found. No files changed.";
            string notes = _scan.Notes.Length > 0 ? " " + string.Join(" ", _scan.Notes) : "";
            Status($"Scan complete at {DateTime.Now:t}. No changes applied by scanning.{notes}" + (_powerError is null ? "" : " Power controls unavailable: " + _powerError));
        }
        catch (Exception ex) { Status("Scan could not finish: " + ex.Message, true); }
        finally { SetBusy(false); }
    }

    private void RefreshPower()
    {
        _powerError = null;
        try
        {
            var plans = _power.GetPlans(); var active = _power.GetActive();
            PlanPicker.ItemsSource = plans;
            PlanPicker.SelectedItem = plans.FirstOrDefault(x => x.Id == Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")) ?? plans.FirstOrDefault(x => x.Id != active) ?? plans.FirstOrDefault();
            ActivePlanValue.Text = "Current plan: " + (plans.FirstOrDefault(x => x.Id == active)?.Name ?? active.ToString());
            PowerHint.Text = plans.Count <= 1
                ? "Only one plan is exposed by Windows. Some PCs manage performance through Power mode in Windows Settings. This app does not create or unlock hidden plans."
                : "Your current plan is saved to disk before Windows is changed. Closing this app keeps the chosen plan active until you restore it.";
        }
        catch (Exception ex)
        {
            _powerError = ex.Message; PlanPicker.ItemsSource = null;
            ActivePlanValue.Text = "Power plan unavailable: " + ex.Message;
        }
        RefreshBackup();
    }

    private void RefreshBackup()
    {
        try
        {
            var backup = _service.GetBackup();
            bool pending = backup is not null && backup.Status != "Restored";
            BackupTitle.Text = backup is null ? "No changes yet" : backup.Status == "Restored" ? "Original plan restored" : "Your original plan is saved";
            BackupDetails.Text = backup is null ? "When you apply a power-plan change, its backup will appear here." :
                $"Original: {backup.OriginalName}\nChosen: {backup.TargetName}\nSaved: {backup.CreatedUtc.ToLocalTime():g}\nStatus: {backup.Status}" +
                (backup.RestoredUtc is DateTimeOffset time ? $"\nRestored: {time.ToLocalTime():g}" : "\nRestore is available even if the app was closed before the change finished.");
            RestoreButton.IsEnabled = pending && !_busy && _powerError is null;
            ApplyButton.IsEnabled = !pending && !_busy && _powerError is null && PlanPicker.Items.Count > 0;
            if (pending) PowerHint.Text = "An existing backup is pending. Restore the original plan before applying another change.";
        }
        catch (Exception ex)
        {
            BackupTitle.Text = "Backup needs attention"; BackupDetails.Text = ex.Message;
            RestoreButton.IsEnabled = false; ApplyButton.IsEnabled = false;
            PowerHint.Text = "The saved backup could not be read. New changes are blocked to protect your recovery information.";
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy; ScanButton.IsEnabled = !busy; PlanPicker.IsEnabled = !busy;
        RefreshBackup();
        RefreshOptimizationControls();
    }

    private void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (_busy || PlanPicker.SelectedItem is not PowerPlan plan) return;
        try
        {
            var active = _power.GetActive();
            var original = _power.GetPlans().FirstOrDefault(x => x.Id == active)?.Name ?? active.ToString();
            if (active == plan.Id) { Status("This plan is already active. Nothing changed."); return; }
            var answer = MessageBox.Show(this,
                $"Current: {original}\nChange to: {plan.Name}\n\nOnly the active Windows power plan will change. Your original plan will be saved before applying. Higher-performance plans may increase heat, fan noise, and battery use. No FPS gain is guaranteed.\n\nThis change remains active after closing the app. Use Restore to undo it.\n\nApply this change?",
                "Review power-plan change", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            SetBusy(true);
            _service.Apply(plan.Id);
            Status("Power plan changed and verified. Your original plan is saved in Restore.");
        }
        catch (Exception ex) { Status("Power change did not complete: " + ex.Message + " Check Restore before retrying.", true); }
        finally { _busy = false; RefreshPower(); SetBusy(false); }
    }

    private void RestoreClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            var backup = _service.GetBackup(); if (backup is null) return;
            var current = _power.GetActive();
            string extra = current != backup.TargetId && current != backup.OriginalId
                ? "\nWindows is currently using a different plan than the one applied by this app. Restoring will replace that current selection.\n" : "";
            if (MessageBox.Show(this, $"Restore your original plan: {backup.OriginalName}?\n{extra}\nOnly the active power plan will change.",
                "Review restore", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            SetBusy(true); _service.Restore(); Status("Original power plan restored and verified.");
        }
        catch (Exception ex) { Status("Restore did not complete: " + ex.Message, true); }
        finally { _busy = false; RefreshPower(); SetBusy(false); }
    }

    private async void LocateClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFolderDialog { Title = "Choose the folder containing FiveM.exe or FiveM.app", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var resolved = FiveMPaths.Resolve(dialog.FolderName);
            if (resolved is null) { Status("This folder does not look like a FiveM installation. Choose its launcher folder or FiveM.app.", true); return; }
            Directory.CreateDirectory(_dataFolder);
            File.WriteAllText(System.IO.Path.Combine(_dataFolder, "fivem-path.txt"), resolved);
            _preferredFolder = resolved; await ScanAsync();
        }
        catch (Exception ex) { Status("Could not save the FiveM location: " + ex.Message, true); }
    }

    private void SettingsClick(object sender, RoutedEventArgs e) => OpenTarget((string)((Button)sender).Tag);
    private void OpenFiveMFolder(object sender, RoutedEventArgs e) { if (_scan?.FiveMFolder is string folder) OpenTarget(folder); }
    private void BackupFolderClick(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(_dataFolder); OpenTarget(_dataFolder); }
        catch (Exception ex) { Status("Could not open backup folder: " + ex.Message, true); }
    }
    private void OpenTarget(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); Status("Opened " + target); }
        catch (Exception ex) { Status("Could not open this location: " + ex.Message, true); }
    }

    private void ExportClick(object sender, RoutedEventArgs e)
    {
        if (_scan is null) { Status("Complete a PC scan first."); return; }
        var save = new SaveFileDialog { FileName = $"UhOptimizer-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json", Filter = "JSON report|*.json", DefaultExt = ".json", AddExtension = true };
        if (save.ShowDialog(this) != true) return;
        try
        {
            var report = new { App = "Uh FiveM Optimizer", Version = AppBuild.Version, ExportedUtc = DateTimeOffset.UtcNow, Privacy = "Local export; installation paths may contain your Windows account name. Review before sharing.", Scan = _scan, Samples = _samples.ToArray(), Metrics = "Whole-PC CPU and RAM; not game FPS or a benchmark." };
            File.WriteAllText(save.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Status("Report exported. Review installation paths before sharing it.");
        }
        catch (Exception ex) { Status("Report could not be exported: " + ex.Message, true); }
    }

    private void Tick()
    {
        try
        {
            var sample = _scanner.Sample(); _samples.Add(sample); if (_samples.Count > 60) _samples.RemoveAt(0);
            LiveCpu.Text = sample.CpuPercent is double cpu ? $"CPU  /  {cpu:0}%" : "CPU  /  collecting sample";
            LiveRam.Text = $"RAM  /  {sample.MemoryPercent:0}%";
            if (++_sampleTicks % 5 == 0 || _sampleTicks == 1)
            {
                string state = WindowsScanner.FiveMState(); _fiveMRunning = state == "Running";
                FiveMValue.Text = _scan?.FiveMFolder is null ? "Not located yet" : "Installation found · " + state;
            }
            RefreshMotion();
            DrawGraphs();
        }
        catch (Exception ex) { _timer.Stop(); Status("Live readings paused: " + ex.Message, true); }
    }

    private void DrawGraphs()
    {
        if (CpuGraph is null || RamGraph is null) return;
        DrawGraph(CpuGraph, _samples.Select(s => s.CpuPercent).ToList(), ((SolidColorBrush)FindResource("Accent")).Color);
        DrawGraph(RamGraph, _samples.Select(s => (double?)s.MemoryPercent).ToList(), ((SolidColorBrush)FindResource("ChartSecondary")).Color);
    }

    private static void DrawGraph(Canvas canvas, IReadOnlyList<double?> values, Color color)
    {
        canvas.Children.Clear(); double w = canvas.ActualWidth, h = canvas.ActualHeight; if (w < 1 || h < 1) return;
        foreach (double y in new[] { 0.25, 0.5, 0.75 })
            canvas.Children.Add(new Line { X1 = 0, X2 = w, Y1 = h * y, Y2 = h * y, Stroke = new SolidColorBrush(Color.FromRgb(34, 46, 55)), StrokeThickness = 1 });
        var points = new PointCollection();
        for (int i = 0; i < values.Count; i++)
            if (values[i] is double v) points.Add(new Point(w * (60 - values.Count + i) / 59d, 3 + (h - 6) * (1 - v / 100)));
        if (points.Count > 1) canvas.Children.Add(new System.Windows.Shapes.Polyline { Points = points, Stroke = new SolidColorBrush(color), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round });
        if (points.Count > 0)
        {
            var dot = new Ellipse { Width = 5, Height = 5, Fill = new SolidColorBrush(color) };
            Canvas.SetLeft(dot, points[^1].X - 3); Canvas.SetTop(dot, points[^1].Y - 3); canvas.Children.Add(dot);
        }
    }

    private void Status(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = error ? new SolidColorBrush(Color.FromRgb(255, 180, 140)) : (Brush)FindResource("Muted");
    }

    private async Task SmokeTestAsync(string output)
    {
        try
        {
            Directory.CreateDirectory(output);
            string loadedAccent = _appearance.AccentHex;
            async Task Capture(string name)
            {
                UpdateLayout(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                var visual = (FrameworkElement)Content;
                var image = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var file = File.Create(System.IO.Path.Combine(output, name + ".png")); encoder.Save(file);
            }
            await Task.Delay(2200); // A real second CPU sample; no mock readings.
            Tick();
            // New feature integration uses only disposable files beneath the smoke directory.
            string fixture = System.IO.Path.Combine(output, "fixture", "gta5_settings.xml");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fixture)!);
            string originalFixture = "<Settings><version value=\"27\"/><graphics><ShadowQuality value=\"3\"/><TextureQuality value=\"2\"/><MSAA value=\"4\"/><PostFX value=\"3\"/><LodScale value=\"1.0\"/></graphics><video><ScreenWidth value=\"1920\"/><ScreenHeight value=\"1080\"/></video></Settings>";
            File.WriteAllText(fixture, originalFixture);
            var fixtureService = new FileTuningService(System.IO.Path.Combine(output, "fixture-backup"), p => LocalFiles.Inside(p, System.IO.Path.Combine(output, "fixture")));
            if (fixtureService.Load() is { Status: not "Restored" }) fixtureService.Restore(true);
            fixtureService.Apply(LowEndGraphics.Preview(fixture, true)); fixtureService.Restore();
            bool graphicsFixtureRestored = File.ReadAllText(fixture) == originalFixture;
            foreach (bool compact in new[] { false, true })
            {
                if (compact) { Width = MinWidth; Height = MinHeight; }
                foreach (string page in new[] { "Overview", "Tune", "Optimize", "Restore", "Appearance", "Updates", "About" })
                {
                    ShowPage(page); await Capture(page.ToLowerInvariant() + (compact ? "-compact" : ""));
                    if (page == "Optimize")
                    {
                        PageScroll.ScrollToVerticalOffset(470); await Capture("cleanup" + (compact ? "-compact" : ""));
                        PageScroll.ScrollToEnd(); await Capture("visual-pack" + (compact ? "-compact" : ""));
                    }
                }
            }
            Width = 1280; Height = 900; ShowPage("Appearance");
            PreviewAppearance(new() { AccentHex = "#80C7FF", ReduceMotion = true });
            SaveAppearanceClick(this, new RoutedEventArgs());
            bool themeSaved = _appearanceStore.Load().AccentHex == "#80C7FF" && _appearanceStore.Load().ReduceMotion;
            CustomColorInput.Text = "#BAD!!"; PreviewColorClick(this, new RoutedEventArgs());
            bool invalidColorBlocked = _appearance.AccentHex == "#80C7FF" && _appearanceStore.Load().AccentHex == "#80C7FF";
            SyncAppearanceControls(); SaveAppearanceClick(this, new RoutedEventArgs()); await Capture("appearance-blue");
            PageScroll.ScrollToEnd(); await Capture("appearance-motion");
            bool reducedStops = !HeroScene.IsAnimating && !AppearanceScene.IsAnimating;
            _presetButtons.First(p => p.Hex == "#FF9FB4").Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ShowPage("Overview"); await Capture("overview-rose");
            PreviewAppearance(new()); ShowPage("Overview");
            int initialFrames = HeroScene.FrameCount; HeroScene.MotionEnabled = true;
            await Task.Delay(350);
            bool motionTicks = HeroScene.FrameCount > initialFrames;
            ShowPage("Tune"); bool hiddenStops = !HeroScene.IsAnimating && !AppearanceScene.IsAnimating;
            PlanPicker.IsDropDownOpen = true; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            bool dropdownOpens = PlanPicker.IsDropDownOpen && PlanPicker.Items.Count > 0; PlanPicker.IsDropDownOpen = false;
            _fiveMRunning = true; RefreshMotion(); bool gamingStops = !HeroScene.MotionEnabled && !AppearanceScene.MotionEnabled;
            _fiveMRunning = false;
            bool success = _scan is not null && graphicsFixtureRestored && !PrefetchCheck.IsChecked.GetValueOrDefault() && themeSaved && invalidColorBlocked && reducedStops && motionTicks && hiddenStops && gamingStops;
            File.WriteAllText(System.IO.Path.Combine(output, "smoke-result.json"), JsonSerializer.Serialize(new { Success = success, _scan, PowerError = _powerError, SampleCount = _samples.Count, LoadedAccent = loadedAccent, ThemeSaved = themeSaved, InvalidColorBlocked = invalidColorBlocked, ReducedMotionStopsTimers = reducedStops, NativeMotionTicks = motionTicks, HiddenPagesStopTimers = hiddenStops, GamingPausesMotion = gamingStops, PlanDropdownOpens = dropdownOpens, GraphicsFixtureRestored = graphicsFixtureRestored, PrefetchOffByDefault = !PrefetchCheck.IsChecked.GetValueOrDefault(), SystemChangesApplied = false, UpdateNetworkRequests = 0 }, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(success ? 0 : 1);
        }
        catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(output, "smoke-error.txt"), ex.ToString()); Application.Current.Shutdown(1); }
    }
}
