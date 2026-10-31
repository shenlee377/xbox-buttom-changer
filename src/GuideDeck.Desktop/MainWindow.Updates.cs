using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Velopack;

namespace GuideDeck.Desktop;

public partial class MainWindow
{
    private UpdateService? updater;
    private UpdateInfo? availableUpdate;
    private CancellationTokenSource? updateDownload;
    private bool updating;
    private DateTimeOffset lastUpdateCheck;

    private void InitializeUpdates()
    {
        if (DesktopStorage.IsPackaged)
            UpdateStatus.Text = "這是 Game Bar 套件內的管理器，必須更新整個 MSIX 套件。開發套件尚未提供自動更新。";
        else
        {
            try
            {
                updater = new UpdateService();
                UpdateStatus.Text = updater.Installed
                    ? $"安裝版本 {updater.Version}。手動檢查 GitHub Releases（包含實驗版）；不會自動重啟。"
                    : "這是免安裝版或舊腳本安裝版。請先安裝新的 GuideDeck-Setup.exe，之後即可在此更新。";
                if (updater.Installed && updater.Pending is { } pending)
                    UpdateStatus.Text = $"版本 {pending.Version} 已下載。可以稍後確認重新啟動並套用。";
            }
            catch (Exception error) { UpdateStatus.Text = "無法初始化更新器：" + error.Message; }
        }
        RefreshUpdateControls();
        Closing += (_, e) =>
        {
            if (!updating) return;
            updateDownload?.Cancel();
            e.Cancel = true;
            UpdateStatus.Text = "正在結束更新工作；完成後可關閉程式。";
        };
    }
    private void RefreshUpdateControls()
    {
        if (CheckUpdateButton == null) return;
        var installed = updater?.Installed == true;
        CheckUpdateButton.IsEnabled = installed && !busy;
        DownloadUpdateButton.IsEnabled = installed && !busy && availableUpdate != null && updater!.Pending == null;
        RestartUpdateButton.IsEnabled = installed && !busy && updater!.Pending != null;
        CancelUpdateButton.IsEnabled = updating && updateDownload != null;
    }
    private async Task RunUpdateAsync(Func<Task> action)
    {
        if (busy) return;
        busy = updating = true;
        foreach (TabItem item in MainTabs.Items) if (item != UpdatesTab) item.IsEnabled = false;
        UpdateApply(); UpdateGuideApply(); RefreshUpdateControls();
        try { await action(); }
        catch (OperationCanceledException) { UpdateStatus.Text = "已取消下載。現有版本保持可用。"; }
        catch (Exception error) { UpdateStatus.Text = "更新未完成，現有版本保持可用。\n" + error.Message; }
        finally
        {
            updateDownload?.Dispose(); updateDownload = null;
            busy = updating = false;
            foreach (TabItem item in MainTabs.Items) item.IsEnabled = true;
            UpdateApply(); UpdateGuideApply(); RefreshUpdateControls();
        }
    }
    private async void CheckUpdates(object sender, RoutedEventArgs e) => await RunUpdateAsync(async () =>
    {
        if (updater?.Installed != true) return;
        if (DateTimeOffset.UtcNow - lastUpdateCheck < TimeSpan.FromMinutes(1))
        { UpdateStatus.Text = "請稍候一分鐘再檢查，避免共用網路的 GitHub API 額度耗盡。"; return; }
        lastUpdateCheck = DateTimeOffset.UtcNow;
        availableUpdate = null;
        UpdateStatus.Text = "正在檢查 GitHub Releases…";
        availableUpdate = await updater.CheckAsync();
        UpdateStatus.Text = availableUpdate == null ? "目前沒有較新的相容版本。" :
            $"可更新至 {availableUpdate.TargetFullRelease.Version}，下載約 {availableUpdate.TargetFullRelease.Size / 1048576d:F1} MB。先下載，稍後再決定何時重啟。";
    });
    private async void DownloadUpdate(object sender, RoutedEventArgs e) => await RunUpdateAsync(async () =>
    {
        if (updater?.Installed != true || availableUpdate == null) return;
        updateDownload = new CancellationTokenSource();
        RefreshUpdateControls();
        UpdateProgress.Value = 0;
        var progress = new Progress<int>(value => { UpdateProgress.Value = value; UpdateStatus.Text = $"下載並校驗更新：{value}%"; });
        await updater.DownloadAsync(availableUpdate, value => ((IProgress<int>)progress).Report(value), updateDownload.Token);
        UpdateStatus.Text = $"版本 {availableUpdate.TargetFullRelease.Version} 已下載並校驗。結束遊戲並儲存觀察紀錄後，再按「重啟並更新」。";
    });
    private void CancelUpdate(object sender, RoutedEventArgs e) => updateDownload?.Cancel();
    private void RestartUpdate(object sender, RoutedEventArgs e)
    {
        if (busy || updater?.Installed != true || updater.Pending is not { } pending) return;
        if (MessageBox.Show(this, "請先儲存尚未儲存的觀察紀錄。現在關閉 GuideDeck、套用更新並重新開啟嗎？", "重新啟動並更新", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        Run(() => updater.Restart(pending));
    }
    private void OpenReleaseDownloads(object sender, RoutedEventArgs e) => Run(() =>
        Process.Start(new ProcessStartInfo(UpdateService.Repository + "/releases") { UseShellExecute = true }));
    private void OpenInstalledApps(object sender, RoutedEventArgs e) => Run(() =>
        Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true }));
}
