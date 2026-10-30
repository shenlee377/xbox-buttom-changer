using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GuideDeck.Core;
using Microsoft.Win32;

namespace GuideDeck.Desktop;

public partial class MainWindow : Window
{
    private sealed record Preferences(string ConfigPath, string? Identity, string? LastReceipt);
    private sealed record ClipboardBackup(string Original, string Modified, DateTimeOffset CreatedAt);
    private sealed record ReportEntry(ValidationReport Report, string Display) { public override string ToString() => Display; }
    private PatchPreview? preview;
    private MappingEdit? clipboardEdit;
    private readonly List<ComboBox> checks = [];
    private readonly DispatcherTimer monitor = new() {Interval = TimeSpan.FromSeconds(5)};
    private Preferences preferences = new("", null, null);
    private string? observedHash;
    private bool busy, monitoring, closed;
    private bool updatingAppearance, capturing;
    private static string DataDirectory => DesktopStorage.Root;
    private static string BackupDirectory => Path.Combine(DataDirectory, "Backups");
    private static string ReportDirectory => Path.Combine(DataDirectory, "Reports");
    private static string PreferencesPath => Path.Combine(DataDirectory, "preferences.json");

    public MainWindow()
    {
        InitializeComponent();
        var themes = ((App)Application.Current).Themes;
        themes.Attach(this);
        themes.Changed += UpdateAppearance;
        UpdateAppearance();
        foreach (var criterion in ValidationRules.Labels)
        {
            ChecksPanel.Children.Add(new TextBlock {Text = criterion, Margin = new Thickness(0, 6, 0, 3)});
            var combo = new ComboBox {ItemsSource = new[] {"未測試", "通過", "失敗", "不適用"}, SelectedIndex = 0, Padding = new Thickness(6)};
            System.Windows.Automation.AutomationProperties.SetName(combo, criterion);
            checks.Add(combo); ChecksPanel.Children.Add(combo);
        }
        GameName.TextChanged += (_, _) => InvalidateObservations();
        EnvironmentNotes.TextChanged += (_, _) => InvalidateObservations();
        TestScope.SelectionChanged += (_, _) => InvalidateObservations();
        Loaded += (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            MinWidth = Math.Min(MinWidth, workArea.Width); MinHeight = Math.Min(MinHeight, workArea.Height);
            Width = Math.Min(Width, workArea.Width); Height = Math.Min(Height, workArea.Height);
            Run(() => {
                if (File.Exists(PreferencesPath)) preferences = AppFiles.Load<Preferences>(PreferencesPath) ?? preferences;
                if (File.Exists(preferences.ConfigPath)) { ConfigPath.Text = preferences.ConfigPath; Inspect(); }
                ReloadHistory();
            });
            monitor.Start();
        };
        monitor.Tick += async (_, _) => await MonitorAsync();
        Closed += (_, _) => { closed = true; monitor.Stop(); themes.Changed -= UpdateAppearance; };
        ContentRendered += async (_, _) => {
            var directory = ((App)Application.Current).CaptureDirectory;
            if (directory == null || capturing) return;
            capturing = true;
            try { await UiCapture.SaveAsync(this, directory); Application.Current.Shutdown(0); }
            catch (Exception error) { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "capture-error.txt"), error.ToString()); Application.Current.Shutdown(1); }
        };
    }
    private void UpdateAppearance()
    {
        if (AppearanceChoice == null) return;
        var themes = ((App)Application.Current).Themes;
        updatingAppearance = true;
        try {
            AppearanceChoice.SelectedIndex = (int)themes.Mode;
            ThemeStatus.Text = themes.Variant == ThemeVariant.HighContrast ? "高對比模式優先" :
                (themes.Mode == ThemeMode.System ? "跟隨系統 · " : "已記住外觀 · ") + (themes.Variant == ThemeVariant.Dark ? "深色" : "淺色");
        } finally { updatingAppearance = false; }
    }
    private void AppearanceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingAppearance || AppearanceChoice.SelectedIndex < 0) return;
        Run(() => ((App)Application.Current).Themes.SetMode((ThemeMode)AppearanceChoice.SelectedIndex));
        UpdateAppearance();
    }
    private void Run(Action action)
    {
        try { action(); } catch (Exception e) { Status.Text = "未完成：" + e.Message; }
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true; MainTabs.IsEnabled = false;
        try { await action(); }
        catch (Exception e) { Status.Text = "未完成：" + e.Message; }
        finally { busy = false; MainTabs.IsEnabled = true; UpdateApply(); }
    }
    private void ClearPreview()
    {
        preview = null;
        if (ApplyButton == null) return;
        ApplyButton.IsEnabled = false; Before.Clear(); After.Clear(); ExperimentalConsent.IsChecked = false;
    }
    private void ConfigPathChanged(object sender, TextChangedEventArgs e)
    { ClearPreview(); InvalidateObservations(); observedHash = null; if (Mappings != null) Mappings.ItemsSource = null; }
    private void InvalidateObservations() { foreach (var check in checks) check.SelectedIndex = 0; }
    private void DetectSteam(object sender, RoutedEventArgs e) => Run(() =>
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var root = key?.GetValue("SteamPath") as string;
        if (root == null) throw new InvalidOperationException("找不到目前使用者的 Steam 安裝。請手動選擇 config.vdf。");
        ConfigPath.Text = Path.Combine(root, "config", "config.vdf"); Inspect();
    });
    private void ChooseConfig(object sender, RoutedEventArgs e) => Run(() =>
    {
        var picker = new OpenFileDialog {Filter = "Steam 設定 (config.vdf)|config.vdf", Title = "選擇 Steam/config/config.vdf"};
        if (picker.ShowDialog() == true) { ConfigPath.Text = picker.FileName; preferences = preferences with {Identity = null}; Inspect(); }
    });
    private void InspectConfig(object sender, RoutedEventArgs e) => Run(Inspect);
    private void Inspect()
    {
        ClearPreview(); Mappings.ItemsSource = null;
        var bytes = ConfigTransaction.ReadBounded(ConfigPath.Text);
        var inspection = SteamConfig.Inspect(bytes);
        var newHash = ConfigTransaction.Hash(bytes);
        if (observedHash != newHash) InvalidateObservations();
        observedHash = newHash;
        preferences = preferences with {ConfigPath = ConfigPath.Text};
        if (inspection.Problems.Count != 0)
        {
            InspectionStatus.Text = string.Join("\n", inspection.Problems);
            Status.Text = "格式未支援；未修改設定。可使用 mapping 工作台。";
            PublishSummary("unsupported", "設定格式未支援；尚未建立有效修改。"); return;
        }
        Mappings.ItemsSource = inspection.Mappings;
        InspectionStatus.Text = $"找到 {inspection.Mappings.Count} 筆 mapping。請核對 GUID／CRC；不會批次修改。";
        var selected = inspection.Mappings.FirstOrDefault(m => m.Identity == preferences.Identity);
        if (selected != null) Mappings.SelectedItem = selected;
        Status.Text = "已檢查設定；Guide 實際效果仍需遊戲驗收。";
        AppFiles.Save(PreferencesPath, preferences);
        PublishSummary(selected?.Binding.Length == 0 ? "mapping-without-guide" : "needs-setup",
            selected?.Binding.Length == 0 ? "所選 mapping 沒有 Guide；不代表所有 Steam 路徑已阻止。" : "請在桌面管理器選擇並預覽控制器 mapping。");
        ReloadHistory();
    }
    private void MappingChanged(object sender, SelectionChangedEventArgs e) => Run(() =>
    {
        ClearPreview();
        if (Mappings.SelectedItem is not MappingCandidate candidate) return;
        if (preferences.Identity != candidate.Identity) InvalidateObservations();
        Before.Text = candidate.Before; After.Text = candidate.After;
        preferences = preferences with {ConfigPath = ConfigPath.Text, Identity = candidate.Identity};
        AppFiles.Save(PreferencesPath, preferences);
        if (candidate.Binding.Length == 0) { Status.Text = "此 mapping 沒有 Guide；仍需要實測其他輸入路徑。"; return; }
        var plan = ConfigTransaction.Preview(ConfigPath.Text, candidate.Index);
        if (plan.Identity != candidate.Identity || plan.Before != candidate.Before)
            throw new InvalidOperationException("清單產生後設定已改變，請重新檢查，避免選錯控制器。");
        preview = plan;
        Status.Text = $"將移除 {candidate.Binding}。請完整離開 Steam，再確認套用。";
        UpdateApply();
    });
    private void ConsentChanged(object sender, RoutedEventArgs e) => UpdateApply();
    private void UpdateApply() { if (ApplyButton != null) ApplyButton.IsEnabled = !busy && preview != null && ExperimentalConsent.IsChecked == true; }
    private async void ApplyPatch(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var plan = preview ?? throw new InvalidOperationException("請重新選擇並預覽 mapping。");
        Status.Text = "正在備份與寫入設定…";
        var receipt = await Task.Run(() => ConfigTransaction.Apply(plan, BackupDirectory));
        preferences = preferences with {LastReceipt = receipt};
        ClearPreview(); Status.Text = "修改已寫入；效果待驗收。備份：" + receipt;
        AppFiles.Save(PreferencesPath, preferences); Inspect();
        Status.Text = "Guide mapping 已修改，請重啟 Steam 並驗收。只備份了該筆 mapping。";
    });
    private async void RestorePatch(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var picker = new OpenFileDialog {Filter = "GuideDeck 備份|*.backup.json", InitialDirectory = Directory.Exists(BackupDirectory) ? BackupDirectory : DataDirectory};
        if (picker.ShowDialog() != true) return;
        var plan = await Task.Run(() => ConfigTransaction.PreviewRestore(picker.FileName));
        if (!ShowRestorePreview(plan)) return;
        await Task.Run(() => ConfigTransaction.Restore(plan));
        ClearPreview(); Mappings.ItemsSource = null;
        if (File.Exists(ConfigPath.Text)) Inspect();
        Status.Text = plan.AlreadyRestored ? "此 mapping 已是原始狀態；沒有重複寫入。" : "已還原這筆 mapping，保留其他設定的後續變更。請重啟 Steam。";
    });
    private bool ShowRestorePreview(RestorePreview plan)
    {
        var dialog = new Window {Title = "還原預覽 · " + plan.Identity, Owner = this, Width = 760, Height = 470, WindowStartupLocation = WindowStartupLocation.CenterOwner};
        ((App)Application.Current).Themes.Attach(dialog);
        var stack = new StackPanel {Margin = new Thickness(20)};
        stack.Children.Add(new TextBlock {Text = plan.AlreadyRestored ? "已還原，無需再次寫入。" : "只還原以下 mapping；其他設定不變。請完整離開 Steam。"});
        foreach (var value in new[] {plan.CurrentMapping, plan.RestoredMapping})
            stack.Children.Add(new TextBox {Text = value, IsReadOnly = true, Height = 115, Margin = new Thickness(0, 10, 0, 0)});
        var apply = new Button {Content = plan.AlreadyRestored ? "關閉" : "還原這筆 mapping", HorizontalAlignment = HorizontalAlignment.Left};
        apply.Click += (_, _) => dialog.DialogResult = true;
        stack.Children.Add(apply); stack.Children.Add(new Button {Content = "取消", HorizontalAlignment = HorizontalAlignment.Left, IsCancel = true});
        dialog.Content = new ScrollViewer {Content = stack}; return dialog.ShowDialog() == true;
    }
    private void OpenBackups(object sender, RoutedEventArgs e) => OpenFolder(BackupDirectory);
    private void OpenReports(object sender, RoutedEventArgs e) => OpenFolder(ReportDirectory);
    private void OpenFolder(string path) => Run(() => { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) {UseShellExecute = true}); });
    private void MappingInputChanged(object sender, TextChangedEventArgs e)
    { clipboardEdit = null; if (CopyMappingButton != null) { CopyMappingButton.IsEnabled = false; MappingOutput.Clear(); } }
    private void PasteMapping(object sender, RoutedEventArgs e) => Run(() => MappingInput.Text = Clipboard.GetText());
    private void PreviewMapping(object sender, RoutedEventArgs e) => Run(() =>
    {
        clipboardEdit = MappingEditor.RemoveGuide(MappingInput.Text.TrimEnd('\r', '\n'));
        MappingOutput.Text = clipboardEdit.Modified; CopyMappingButton.IsEnabled = clipboardEdit.ChangesGuide;
        Status.Text = clipboardEdit.ChangesGuide ? "預覽已產生。複製時會先備份原始 mapping；尚未修改 Steam。" : "沒有 Guide 欄位；不代表衝突已排除。";
    });
    private void CopyMapping(object sender, RoutedEventArgs e) => Run(() =>
    {
        var edit = clipboardEdit ?? throw new InvalidOperationException("請先預覽。");
        if (!edit.ChangesGuide) throw new InvalidOperationException("沒有可移除的 Guide。");
        AppFiles.Save(Path.Combine(BackupDirectory, Guid.NewGuid() + ".mapping.json"), new ClipboardBackup(edit.Original, edit.Modified, DateTimeOffset.UtcNow));
        Clipboard.SetText(edit.Modified); Status.Text = "已備份原始 mapping 並複製結果；尚未套用 Steam。";
    });
    private void RestoreClipboard(object sender, RoutedEventArgs e) => Run(() =>
    {
        var picker = new OpenFileDialog {Filter = "mapping 備份|*.mapping.json", InitialDirectory = BackupDirectory};
        if (picker.ShowDialog() != true) return;
        var backup = AppFiles.Load<ClipboardBackup>(picker.FileName) ?? throw new FormatException("備份格式不明。");
        if (MappingEditor.RemoveGuide(backup.Original).Modified != backup.Modified) throw new FormatException("不是有效的 Guide 修改備份。");
        Clipboard.SetText(backup.Original); Status.Text = "已複製原始 mapping，需貼回 Steam 才完成復原。";
    });
    private void SaveValidation(object sender, RoutedEventArgs e) => Run(() =>
    {
        byte[]? bytes = File.Exists(ConfigPath.Text) ? ConfigTransaction.ReadBounded(ConfigPath.Text) : null;
        string? hash = bytes == null ? null : ConfigTransaction.Hash(bytes);
        if (observedHash != null && hash != observedHash) throw new InvalidOperationException("測試期間設定已變更，請重新檢查並重新測試。");
        var mapping = bytes == null ? null : SteamConfig.Inspect(bytes).Mappings.FirstOrDefault(m => m.Identity == preferences.Identity);
        var record = new ValidationReport(2, Guid.NewGuid(), DateTimeOffset.UtcNow, GameName.Text, EnvironmentNotes.Text,
            (TestScope)TestScope.SelectedIndex, Environment.OSVersion.VersionString, hash, mapping?.Identity,
            mapping == null ? null : ConfigTransaction.Hash(Encoding.UTF8.GetBytes(mapping.Before)),
            ValidationRules.Keys.Select((k, i) => (k, result: (Observation)checks[i].SelectedIndex)).ToDictionary(v => v.k, v => v.result), TestNotes.Text);
        AppFiles.SaveReport(ReportDirectory, record);
        ReloadHistory(); Status.Text = ValidationRules.Evaluate(record, hash) + "。紀錄只適用於本次填寫的情境。";
        foreach (var combo in checks) combo.SelectedIndex = 0;
    });
    private void RefreshHistory(object sender, RoutedEventArgs e) => Run(ReloadHistory);
    private void ReloadHistory()
    {
        var reports = new List<ReportEntry>(); int invalid = 0;
        if (Directory.Exists(ReportDirectory))
            foreach (var path in Directory.EnumerateFiles(ReportDirectory, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(100))
            {
                try
                {
                    var report = AppFiles.Load<ValidationReport>(path) ?? throw new FormatException();
                    reports.Add(new(report, $"{report.ObservedAt.ToLocalTime():MM-dd HH:mm} · {report.Game} · {report.Scope} · {ValidationRules.Evaluate(report, observedHash)}"));
                }
                catch { invalid++; }
            }
        ReportHistory.ItemsSource = reports;
        HistoryStatus.Text = $"最近 {reports.Count} 份紀錄" + (invalid == 0 ? "" : $"；{invalid} 份舊版／無效紀錄未載入（原檔保留）");
    }
    private void ExportReport(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (ReportHistory.SelectedItem is not ReportEntry entry) throw new InvalidOperationException("請選擇一份觀察紀錄。");
        var picker = new SaveFileDialog {Filter = "觀察紀錄|*.json", FileName = "GuideDeck-observation.json"};
        if (picker.ShowDialog() == true) { AppFiles.Save(picker.FileName, entry.Report); Status.Text = "已匯出人工觀察；包含填寫的遊戲與環境，不含 Steam 設定檔。"; }
    });
    private async Task MonitorAsync()
    {
        if (busy || monitoring || closed || string.IsNullOrWhiteSpace(ConfigPath.Text)) return;
        monitoring = true;
        try
        {
            string path = ConfigPath.Text;
            var bytes = await Task.Run(() => ConfigTransaction.ReadBounded(path));
            if (closed || busy || ConfigPath.Text != path) return;
            var hash = ConfigTransaction.Hash(bytes);
            if (observedHash != null && hash != observedHash)
            { ClearPreview(); Inspect(); Status.Text = "偵測到設定變動：已撤銷舊預覽與未儲存觀察，請重測。"; return; }
            var inspection = SteamConfig.Inspect(bytes);
            if (inspection.Problems.Count != 0) { PublishSummary("unsupported", "設定格式未支援，請重新檢查。"); return; }
            var mapping = inspection.Mappings.FirstOrDefault(m => m.Identity == preferences.Identity);
            PublishSummary(mapping?.Binding.Length == 0 ? "mapping-without-guide" : "needs-setup",
                mapping == null ? "尚未選擇有效 mapping。" : mapping.Binding.Length == 0 ? "所選 mapping 沒有 Guide；請確認實際效果。" : "所選 mapping 仍有 Guide 綁定。");
        }
        catch (Exception e) { if (!closed) { ClearPreview(); Status.Text = "無法更新設定狀態：" + e.Message; } }
        finally { monitoring = false; }
    }
    private void PublishSummary(string state, string detail)
    {
        AppFiles.Save(Path.Combine(DataDirectory, "status-summary.json"), new {SchemaVersion = 1, UpdatedAt = DateTimeOffset.UtcNow,
            State = state, Detail = detail, MappingIdentity = preferences.Identity, ConfigHash = observedHash});
    }
    private void OpenGameBarSettings(object sender, RoutedEventArgs e) => Run(() => Process.Start(new ProcessStartInfo("ms-settings:gaming-gamebar") {UseShellExecute = true}));
}
