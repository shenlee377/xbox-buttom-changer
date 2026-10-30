using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using GuideDeck.Core;
using Microsoft.Win32;

namespace GuideDeck.Desktop;

public partial class MainWindow : Window
{
    private PatchPreview? preview;
    private readonly List<ComboBox> checks = [];
    private static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GuideDeck");
    private static readonly string BackupDirectory = Path.Combine(DataDirectory, "Backups");
    private readonly string[] criteria = ["Guide 只開啟 Game Bar", "Guide 沒有聚焦 Steam／進 Big Picture", "Guide 沒有開啟 Steam 遊戲 Overlay", "Shift+Tab 仍能開啟 Steam Overlay", "普通按鍵、搖桿與扳機正常", "震動與需要的額外按鍵正常"];

    public MainWindow()
    {
        InitializeComponent();
        foreach (var criterion in criteria)
        {
            ChecksPanel.Children.Add(new TextBlock {Text = criterion, Margin = new Thickness(0, 6, 0, 3)});
            var combo = new ComboBox {ItemsSource = new[] {"未測試", "通過", "失敗"}, SelectedIndex = 0, Padding = new Thickness(6)};
            System.Windows.Automation.AutomationProperties.SetName(combo, criterion);
            checks.Add(combo); ChecksPanel.Children.Add(combo);
        }
    }
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception e) { Status.Text = "未完成：" + e.Message; }
    }
    private void ClearPreview()
    {
        preview = null;
        if (ApplyButton == null) return;
        ApplyButton.IsEnabled = false; Before.Clear(); After.Clear();
    }
    private void ConfigPathChanged(object sender, TextChangedEventArgs e) { ClearPreview(); if (Mappings != null) Mappings.ItemsSource = null; }
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
        if (picker.ShowDialog() == true) { ConfigPath.Text = picker.FileName; Inspect(); }
    });
    private void InspectConfig(object sender, RoutedEventArgs e) => Run(Inspect);
    private void Inspect()
    {
        ClearPreview(); Mappings.ItemsSource = null;
        var inspection = SteamConfig.Inspect(ConfigTransaction.ReadBounded(ConfigPath.Text));
        if (inspection.Problems.Count != 0)
        { InspectionStatus.Text = string.Join("\n", inspection.Problems); Status.Text = "格式未支援；未修改設定。可使用 mapping 工作台。"; return; }
        Mappings.ItemsSource = inspection.Mappings;
        InspectionStatus.Text = $"找到 {inspection.Mappings.Count} 筆明確 mapping。請選擇要測試的控制器；不會批次移除所有裝置的 Guide。";
        Status.Text = "已讀取設定，尚未修改。";
    }
    private void MappingChanged(object sender, SelectionChangedEventArgs e) => Run(() =>
    {
        ClearPreview();
        if (Mappings.SelectedItem is not MappingCandidate candidate) return;
        Before.Text = candidate.Before; After.Text = candidate.After;
        if (candidate.Binding.Length == 0) { Status.Text = "此 mapping 沒有 Guide；仍需要實測其他輸入路徑。"; return; }
        preview = ConfigTransaction.Preview(ConfigPath.Text, candidate.Index);
        Status.Text = $"將移除 {candidate.Binding}，保留其他欄位。需要完整離開 Steam 才能套用。";
        UpdateApply();
    });
    private void ConsentChanged(object sender, RoutedEventArgs e) => UpdateApply();
    private void UpdateApply() { if (ApplyButton != null) ApplyButton.IsEnabled = preview != null && ExperimentalConsent.IsChecked == true; }
    private void ApplyPatch(object sender, RoutedEventArgs e) => Run(() =>
    {
        var plan = preview ?? throw new InvalidOperationException("請重新選擇並預覽 mapping。");
        var backup = ConfigTransaction.Apply(plan, BackupDirectory);
        Inspect();
        Status.Text = "設定已修改，效果尚未驗證。請重新啟動 Steam 並完成遊戲驗收。備份：" + backup;
    });
    private void RestorePatch(object sender, RoutedEventArgs e) => Run(() =>
    {
        var picker = new OpenFileDialog {Filter = "GuideDeck 備份|*.backup.json", InitialDirectory = Directory.Exists(BackupDirectory) ? BackupDirectory : DataDirectory};
        if (picker.ShowDialog() != true) return;
        ConfigTransaction.Restore(picker.FileName); ClearPreview(); Mappings.ItemsSource = null;
        Status.Text = "已還原原始設定。請重新啟動 Steam；既有測試記錄不代表目前狀態。";
    });
    private void OpenBackups(object sender, RoutedEventArgs e) => Run(() => { Directory.CreateDirectory(BackupDirectory); Process.Start(new ProcessStartInfo(BackupDirectory) {UseShellExecute = true}); });
    private void MappingInputChanged(object sender, TextChangedEventArgs e) { if (CopyMappingButton != null) { CopyMappingButton.IsEnabled = false; MappingOutput.Clear(); } }
    private void PasteMapping(object sender, RoutedEventArgs e) => Run(() => MappingInput.Text = Clipboard.GetText());
    private void PreviewMapping(object sender, RoutedEventArgs e) => Run(() =>
    {
        var edit = MappingEditor.RemoveGuide(MappingInput.Text.TrimEnd('\r', '\n'));
        MappingOutput.Text = edit.Modified; CopyMappingButton.IsEnabled = edit.ChangesGuide;
        Status.Text = edit.ChangesGuide ? "預覽已產生，尚未修改 Steam。保留原始 mapping，再將結果貼回 Steam 測試。" : "沒有 Guide 欄位；不代表衝突已排除。";
    });
    private void CopyMapping(object sender, RoutedEventArgs e) => Run(() => { Clipboard.SetText(MappingOutput.Text); Status.Text = "已複製結果。尚未套用至 Steam。"; });
    private void SaveValidation(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (string.IsNullOrWhiteSpace(GameName.Text) || string.IsNullOrWhiteSpace(EnvironmentNotes.Text)) throw new InvalidOperationException("請填寫遊戲／桌面與測試環境。");
        if (checks.All(c => c.SelectedIndex == 0)) throw new InvalidOperationException("尚未記錄任何觀察結果。");
        Directory.CreateDirectory(Path.Combine(DataDirectory, "Reports"));
        string? hash = File.Exists(ConfigPath.Text) ? ConfigTransaction.Hash(ConfigTransaction.ReadBounded(ConfigPath.Text)) : null;
        var record = new {SchemaVersion = 1, ObservedAt = DateTimeOffset.UtcNow, EvidenceSource = "user-observation", Game = GameName.Text,
            Environment = EnvironmentNotes.Text, Os = System.Environment.OSVersion.VersionString, Scope = ((ComboBoxItem)TestScope.SelectedItem).Content,
            ConfigHash = hash, Results = criteria.Select((c, i) => new {Criterion = c, Result = checks[i].SelectedItem}).ToArray()};
        var path = Path.Combine(DataDirectory, "Reports", Guid.NewGuid() + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(record, new JsonSerializerOptions {WriteIndented = true}));
        Status.Text = "已儲存本次人工觀察；不代表其他遊戲或環境已通過：" + path;
    });
    private void OpenGameBarSettings(object sender, RoutedEventArgs e) => Run(() => Process.Start(new ProcessStartInfo("ms-settings:gaming-gamebar") {UseShellExecute = true}));
}
