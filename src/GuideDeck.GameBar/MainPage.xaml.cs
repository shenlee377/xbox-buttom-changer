using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GuideDeck.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace GuideDeck.GameBar
{
    public sealed partial class MainPage : Page
    {
        private MappingEdit edit;
        private readonly List<ComboBox> results = new List<ComboBox>();
        private readonly string[] criteria = {"Guide 只開 Game Bar", "Guide 未聚焦 Steam／進 Big Picture", "Guide 未開 Steam 遊戲 Overlay", "Shift+Tab 仍可開 Steam Overlay", "普通按鍵／搖桿／扳機正常", "震動與需要的額外按鍵正常"};
        public MainPage()
        {
            InitializeComponent();
            foreach (var criterion in criteria)
            {
                var combo = new ComboBox {Header = criterion, ItemsSource = new[] {"未測試", "通過", "失敗"}, SelectedIndex = 0};
                results.Add(combo); Checks.Children.Add(combo);
            }
        }
        public void OpenSettings() { Sections.SelectedIndex = 1; }
        private async Task Run(Func<Task> operation)
        {
            try { await operation(); }
            catch (Exception e) { Status.Text = "未完成：" + e.Message; }
        }
        private async void Paste(object sender, RoutedEventArgs e) => await Run(async () =>
        {
            var clipboard = Clipboard.GetContent();
            if (!clipboard.Contains(StandardDataFormats.Text)) throw new InvalidOperationException("剪貼簿沒有文字。");
            Input.Text = await clipboard.GetTextAsync();
        });
        private void InputChanged(object sender, TextChangedEventArgs e)
        {
            edit = null;
            if (CopyButton != null) { CopyButton.IsEnabled = false; Output.Text = ""; Consent.IsChecked = false; }
        }
        private async void Preview(object sender, RoutedEventArgs e) => await Run(() =>
        {
            edit = MappingEditor.RemoveGuide(Input.Text.TrimEnd('\r', '\n'));
            Output.Text = edit.Modified; CopyButton.IsEnabled = edit.ChangesGuide;
            Status.Text = edit.ChangesGuide ? "預覽完成；尚未修改 Steam。" : "沒有 Guide 欄位；不代表 Steam 不會響應。";
            return Task.CompletedTask;
        });
        private async void Copy(object sender, RoutedEventArgs e) => await Run(async () =>
        {
            var current = edit;
            if (current == null || !current.ChangesGuide || Consent.IsChecked != true) throw new InvalidOperationException("請先預覽並確認實驗性修改。");
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("mapping-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + ".txt", CreationCollisionOption.GenerateUniqueName);
            await FileIO.WriteTextAsync(file, current.Original);
            ApplicationData.Current.LocalSettings.Values["lastBackup"] = file.Name;
            var package = new DataPackage(); package.SetText(current.Modified); Clipboard.SetContent(package);
            Status.Text = "原始 mapping 已備份，結果已複製；尚未套用至 Steam。";
        });
        private async void RestoreClipboard(object sender, RoutedEventArgs e) => await Run(async () =>
        {
            var name = ApplicationData.Current.LocalSettings.Values["lastBackup"] as string;
            if (name == null) throw new InvalidOperationException("沒有 widget 的原始 mapping 備份。");
            var file = await ApplicationData.Current.LocalFolder.GetFileAsync(name);
            var original = await FileIO.ReadTextAsync(file);
            var package = new DataPackage(); package.SetText(original); Clipboard.SetContent(package);
            Status.Text = "已複製原始 mapping；請手動貼回 Steam 才完成復原。";
        });
        private async void SaveReport(object sender, RoutedEventArgs e) => await Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(Context.Text)) throw new InvalidOperationException("請填寫測試環境。");
            var data = new JsonArray(); bool observed = false;
            for (int i = 0; i < criteria.Length; i++)
            {
                if (results[i].SelectedIndex != 0) observed = true;
                data.Add(new JsonObject {{"criterion", JsonValue.CreateStringValue(criteria[i])}, {"result", JsonValue.CreateStringValue((string)results[i].SelectedItem)}});
            }
            if (!observed) throw new InvalidOperationException("尚未記錄任何觀察。");
            var record = new JsonObject {{"schemaVersion", JsonValue.CreateNumberValue(1)}, {"evidenceSource", JsonValue.CreateStringValue("user-observation")},
                {"observedAt", JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("O"))}, {"context", JsonValue.CreateStringValue(Context.Text)},
                {"scope", JsonValue.CreateStringValue((string)((ComboBoxItem)Scope.SelectedItem).Content)}, {"results", data}};
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("report-" + Guid.NewGuid() + ".json");
            await FileIO.WriteTextAsync(file, record.Stringify());
            Status.Text = "本次人工觀察已儲存：" + file.Name + "；不代表其他環境已通過。";
        });
    }
}
