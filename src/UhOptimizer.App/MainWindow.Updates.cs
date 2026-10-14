using System.IO;
using System.Net.Http;
using System.Windows;
using UhOptimizer.Core;

namespace UhOptimizer.App;

public partial class MainWindow
{
    private readonly HttpClient _updateHttp = UpdateClient.CreateHttpClient();
    private CancellationTokenSource? _updateCancellation;
    private UpdateCandidate? _updateCandidate;
    private bool _updateBusy;
    private UpdateClient Updates => new(_updateHttp);

    private void InitializeUpdates()
    {
        InstalledVersionText.Text = "v" + AppBuild.Version;
        SidebarVersionText.Text = "v" + AppBuild.Version + "  /  preview";
        AboutVersionText.Text = "Uh FiveM Optimizer / v" + AppBuild.Version;
        Closed += (_, _) => { _updateCancellation?.Cancel(); _updateHttp.Dispose(); };
    }
    private async void CheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (_updateBusy) return;
        _updateCancellation = new(); SetUpdateBusy(true); InstallUpdateButton.IsEnabled = false;
        UpdateTitle.Text = "Checking GitHub…"; UpdateDetails.Text = "Only release metadata is being checked. No EXE is downloaded by this check.";
        UpdateProgressBar.Value = 0; _updateCandidate = null;
        try
        {
            var check = await Updates.CheckAsync(AppBuild.Version, _updateCancellation.Token);
            _updateCandidate = check.Available;
            if (check.Available is { } candidate)
            {
                UpdateTitle.Text = "v" + candidate.Version + " is available.";
                UpdateDetails.Text = $"Windows x64 · {candidate.Size / 1048576d:0.#} MB · source {candidate.SourceCommit[..7]}\nDownload once, verify it, then restart in this same location.";
                UpdateNotice.Text = AppBuild.SupportsSelfUpdate ? "Ready when you are. Your saved appearance and recovery settings will be kept." : "This is a source build. In-app installation is available in the standalone release EXE; you can still view releases here.";
            }
            else
            {
                UpdateTitle.Text = "You're up to date.";
                UpdateDetails.Text = "Installed: v" + AppBuild.Version + " · latest compatible release: v" + check.LatestVersion;
                UpdateNotice.Text = "No download is needed. Check again whenever you choose.";
            }
            Status("Update check complete. No PC optimization settings changed.");
        }
        catch (OperationCanceledException) { UpdateTitle.Text = "Check stopped."; UpdateDetails.Text = "The check was canceled or timed out. Your installed app is unchanged."; }
        catch (Exception ex) { UpdateTitle.Text = "Couldn't check right now."; UpdateDetails.Text = ex.Message; UpdateNotice.Text = "Check your connection and try again. The installed app is unchanged."; }
        finally { SetUpdateBusy(false); _updateCancellation.Dispose(); _updateCancellation = null; }
    }

    private async void InstallUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_updateBusy || _updateCandidate is not { } candidate || !AppBuild.SupportsSelfUpdate) return;
        if (MessageBox.Show(this, $"Download v{candidate.Version} ({candidate.Size / 1048576d:0.#} MB), verify it, and restart the app?\n\nThe EXE will be replaced in its current folder. Your saved appearance and power-plan backup will be kept. The previous EXE will be retained beside it as a .previous file.\n\nContinue?", "Install update", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _updateCancellation = new(); SetUpdateBusy(true); UpdateTitle.Text = "Downloading your update…";
        try
        {
            var progress = new Progress<UpdateProgress>(value =>
            {
                UpdateProgressBar.Value = value.Total == 0 ? 0 : value.Received * 100d / value.Total;
                UpdateDetails.Text = value.UsingCachedFile ? "A verified copy is already downloaded. No repeat download is needed." :
                    $"{value.Received / 1048576d:0.#} / {value.Total / 1048576d:0.#} MB · SHA-256 is checked before installation.";
            });
            string file = await Updates.DownloadAsync(candidate, Path.Combine(_dataFolder, "updates"), progress, _updateCancellation.Token);
            UpdateTitle.Text = "Verified. Preparing to restart…"; CancelUpdateButton.IsEnabled = false;
            // Once handoff begins, finish it or report failure; cancellation is disabled.
            await UpdateInstaller.LaunchWorkerAsync(file, candidate, CancellationToken.None);
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException) { UpdateTitle.Text = "Download stopped."; UpdateDetails.Text = "The installed EXE is unchanged. You can try again when ready."; }
        catch (Exception ex) { UpdateTitle.Text = "Update was not installed."; UpdateDetails.Text = ex.Message; UpdateNotice.Text = "Your existing app, appearance, and power-plan backup are kept. You can retry."; }
        finally { SetUpdateBusy(false); _updateCancellation.Dispose(); _updateCancellation = null; }
    }
    private void SetUpdateBusy(bool busy)
    {
        _updateBusy = busy; CheckUpdateButton.IsEnabled = !busy; CancelUpdateButton.IsEnabled = busy;
        InstallUpdateButton.IsEnabled = !busy && _updateCandidate is not null && AppBuild.SupportsSelfUpdate;
    }
    private void CancelUpdateClick(object sender, RoutedEventArgs e) => _updateCancellation?.Cancel();
    private void ReleasesClick(object sender, RoutedEventArgs e) => OpenTarget(_updateCandidate?.ReleaseUrl ?? UpdateClient.ReleasesUrl);
}
