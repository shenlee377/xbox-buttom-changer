using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GuideDeck.Core;
using Microsoft.Win32;

namespace GuideDeck.Desktop;

public partial class MainWindow
{
    private sealed record GuideSource(string ConfigPath, string DisplayName);
    private GuideSettingsPlan? guidePlan;
    private string? guideObservedHash;
    private bool guideMonitoring;

    private void DetectGuideSteam(object? sender, RoutedEventArgs? args) => Run(() =>
    {
        GuideSources.SelectedIndex = -1; GuideConfigPath.Clear();
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var root = key?.GetValue("SteamPath") as string;
        if (root == null) { GuideFeedback.Text = "找不到 Steam。可手動選擇帳號 localconfig.vdf 或安裝目錄 config.vdf。"; return; }
        var sources = new List<GuideSource>();
        var userdata = Path.Combine(root, "userdata");
        if (Directory.Exists(userdata))
            foreach (var account in Directory.EnumerateDirectories(userdata).OrderBy(p => p))
            {
                if (!Path.GetFileName(account).All(char.IsAsciiDigit)) continue;
                var path = Path.Combine(account, "config", "localconfig.vdf");
                if (File.Exists(path)) sources.Add(new(path, "帳號 " + Path.GetFileName(account) + " · localconfig.vdf"));
            }
        var global = Path.Combine(root, "config", "config.vdf");
        if (File.Exists(global)) sources.Add(new(global, "Steam 安裝設定 · config.vdf"));
        GuideSources.ItemsSource = sources;
        // Never silently choose one account out of several saved accounts.
        if (sources.Count(s => Path.GetFileName(s.ConfigPath) == "localconfig.vdf") == 1) GuideSources.SelectedIndex = 0;
        else if (sources.Count == 1) GuideSources.SelectedIndex = 0;
        else GuideFeedback.Text = "請選擇目前使用的帳號；也可檢查 Steam 安裝設定。每次只修改畫面上這個檔案。";
    });
    private void GuideSourceChanged(object sender, SelectionChangedEventArgs args)
    {
        if (GuideSources.SelectedItem is not GuideSource source) return;
        GuideConfigPath.Text = source.ConfigPath; Run(InspectGuideSettings);
    }
    private void GuidePathChanged(object sender, TextChangedEventArgs args)
    {
        guidePlan = null; guideObservedHash = null;
        if (GuideApply == null) return;
        GuideApply.IsEnabled = false; GuideConsent.IsChecked = false;
        GuideSwitches.Text = "設定來源已改變，請重新檢查。"; GuideChanges.Text = "尚未建立預覽。";
    }
    private void ChooseGuideFile(object sender, RoutedEventArgs args) => Run(() =>
    {
        var picker = new OpenFileDialog {Filter = "Steam 設定|localconfig.vdf;config.vdf", Title = "選擇目前帳號 localconfig.vdf 或 Steam config.vdf"};
        if (picker.ShowDialog() != true) return;
        GuideSources.SelectedIndex = -1; GuideConfigPath.Text = picker.FileName; InspectGuideSettings();
    });
    private void InspectGuideClick(object sender, RoutedEventArgs args) => Run(InspectGuideSettings);
    private void InspectGuideSettings()
    {
        guidePlan = null; guideObservedHash = null; GuideApply.IsEnabled = false; GuideConsent.IsChecked = false;
        GuideSwitches.Text = "正在讀取…"; GuideChanges.Text = "尚未建立預覽。";
        try
        {
            var bytes = ConfigTransaction.ReadBounded(GuideConfigPath.Text);
            var settings = SteamGuideSettings.Inspect(bytes);
            var plan = GuideSettingsTransaction.Preview(GuideConfigPath.Text);
            if (plan.SourceHash != ConfigTransaction.Hash(bytes)) throw new IOException("檢查期間設定已改變，請重試。");
            guidePlan = plan; guideObservedHash = plan.SourceHash;
            GuideSwitches.Text = string.Join("\n\n", settings.Select(s => s.Label + "\n" + s.State + "\n" + (s.Location ?? s.Key)));
            GuideChanges.Text = plan.Changes.Length == 0 ? "這個檔案沒有可關閉的已知開關。缺少欄位仍是未知，請使用 Steam 原生設定。" :
                string.Join("\n", plan.Changes.Select(c => c.Key + "：開啟 → 關閉"));
            GuideFeedback.Text = settings.Any(s => s.Value == null) ? "尚有未知項目。請到 Steam 控制器／進階設定關閉兩個 Guide 選項，完整離開 Steam 後再檢查。必要時切換帳號設定與安裝設定檔。" :
                settings.All(s => s.Value == "0") ? "這個檔案的兩個開關已儲存為關閉；請重啟 Steam 後做實測，尚未宣告阻擋成功。" : "預覽已產生。請完整離開 Steam，再確認下方同意框並套用。";
            UpdateGuideApply();
            PublishGuideSummary(bytes);
        }
        catch { GuideSwitches.Text = "無法判讀這個檔案。請使用 Steam 原生控制器設定。"; throw; }
    }
    private void GuideConsentChanged(object sender, RoutedEventArgs args) => UpdateGuideApply();
    private void UpdateGuideApply()
    {
        if (GuideApply != null) GuideApply.IsEnabled = !busy && guidePlan?.Changes.Length > 0 && GuideConsent.IsChecked == true;
    }
    private async void ApplyGuideSettings(object sender, RoutedEventArgs args) => await RunAsync(async () =>
    {
        var plan = guidePlan ?? throw new InvalidOperationException("請先重新檢查 Guide 設定。");
        if (GuideConsent.IsChecked != true) throw new InvalidOperationException("請確認停用 Steam Guide 組合鍵的影響。");
        var receipt = await Task.Run(() => GuideSettingsTransaction.Apply(plan, BackupDirectory));
        InvalidateObservations(); InspectGuideSettings();
        Status.Text = "已備份並關閉此檔案中的已知 Guide 開關。請重啟 Steam 並測試；備份：" + receipt;
    });
    private async void RestoreGuideSettings(object sender, RoutedEventArgs args) => await RunAsync(async () =>
    {
        var picker = new OpenFileDialog {Filter = "Steam Guide 開關備份|*.guide-settings.json", InitialDirectory = Directory.Exists(BackupDirectory) ? BackupDirectory : DataDirectory};
        if (picker.ShowDialog() != true) return;
        var plan = await Task.Run(() => GuideSettingsTransaction.PreviewRestore(picker.FileName));
        var dialog = new Window {Title = "還原 Steam Guide 開關", Owner = this, Width = 720, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner};
        ((App)Application.Current).Themes.Attach(dialog);
        var panel = new StackPanel {Margin = new Thickness(20)};
        panel.Children.Add(new TextBlock {Text = plan.ConfigPath + "\n\n" + (plan.AlreadyRestored ? "已還原，無需再次寫入。" : string.Join("\n", plan.Changes.Select(c => c.Key + "：關閉 → 開啟"))) + "\n\n只還原這些開關，保留其他設定。請完整離開 Steam。"});
        var accept = new Button {Content = plan.AlreadyRestored ? "關閉" : "還原列出的開關", HorizontalAlignment = HorizontalAlignment.Left};
        accept.Click += (_, _) => dialog.DialogResult = true;
        panel.Children.Add(accept); panel.Children.Add(new Button {Content = "取消", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Left});
        dialog.Content = new ScrollViewer {Content = panel};
        if (dialog.ShowDialog() != true) return;
        await Task.Run(() => GuideSettingsTransaction.Restore(plan));
        InvalidateObservations();
        if (File.Exists(GuideConfigPath.Text)) InspectGuideSettings();
        Status.Text = "Guide 開關還原完成。請重啟 Steam；控制器 mapping 與其他設定保持原樣。";
    });
    private void OpenSteamControllerSettings(object sender, RoutedEventArgs args) => Run(() =>
        Process.Start(new ProcessStartInfo("steam://open/settings/controller") {UseShellExecute = true}));
    private async Task MonitorGuideSettingsAsync()
    {
        if (busy || closed || guideMonitoring || string.IsNullOrWhiteSpace(GuideConfigPath.Text) || guideObservedHash == null) return;
        guideMonitoring = true;
        try
        {
            var path = GuideConfigPath.Text;
            var bytes = await Task.Run(() => ConfigTransaction.ReadBounded(path));
            if (closed || busy || path != GuideConfigPath.Text) return;
            if (ConfigTransaction.Hash(bytes) != guideObservedHash)
            {
                InspectGuideSettings(); InvalidateObservations();
                Status.Text = "Steam Guide 設定檔已改變，已重新讀取並撤銷舊預覽。請核對開關是否被 Steam 寫回。";
            }
            else PublishGuideSummary(bytes);
        }
        catch (Exception error)
        {
            guidePlan = null; guideObservedHash = null; GuideConsent.IsChecked = false; UpdateGuideApply();
            GuideFeedback.Text = "無法追蹤 Guide 設定，請重新檢查：" + error.Message;
        }
        finally { guideMonitoring = false; }
    }
    private void PublishGuideSummary(byte[] bytes)
    {
        var settings = SteamGuideSettings.Inspect(bytes);
        AppFiles.Save(Path.Combine(DataDirectory, "status-summary.json"), new {
            SchemaVersion = 1, UpdatedAt = DateTimeOffset.UtcNow, State = "steam-guide-settings",
            Detail = "目前選取檔案的 Guide 開關（效果未驗證）：\n" + string.Join("\n", settings.Select(s => s.Label + "：" + s.State)),
            ConfigHash = ConfigTransaction.Hash(bytes), MappingIdentity = (string?)null
        });
    }
}
