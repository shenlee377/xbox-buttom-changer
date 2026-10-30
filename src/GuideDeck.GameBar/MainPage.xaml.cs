using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GuideDeck.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Microsoft.Gaming.XboxGameBar;

namespace GuideDeck.GameBar
{
    public sealed partial class MainPage : Page
    {
        private MappingEdit edit;
        private readonly DispatcherTimer timer = new DispatcherTimer {Interval = TimeSpan.FromSeconds(5)};
        private XboxGameBarWidget host;
        private bool refreshing, copying;
        private bool updatingAppearance = true;
        private ThemeMode appearance = ThemeMode.System;
        private int appearanceRevision;
        private readonly Windows.UI.ViewManagement.AccessibilitySettings accessibility = new Windows.UI.ViewManagement.AccessibilitySettings();
        private readonly Windows.UI.ViewManagement.UISettings systemUi = new Windows.UI.ViewManagement.UISettings();
        private readonly List<ComboBox> results = new List<ComboBox>();
        private readonly IReadOnlyList<string> criteria = ValidationContract.Labels;
        public MainPage()
        {
            InitializeComponent();
            timer.Tick += async (sender, args) => { await ReadAppearance(); await ReadSummary(); };
            Unloaded += (sender, args) => StopMonitor();
            foreach (var criterion in criteria)
            {
                var combo = new ComboBox {Header = criterion, ItemsSource = new[] {"未測試", "通過", "失敗", "不適用"}, SelectedIndex = 0};
                results.Add(combo); Checks.Children.Add(combo);
            }
            Game.TextChanged += (sender, args) => ClearObservations();
            Context.TextChanged += (sender, args) => ClearObservations();
            Scope.SelectionChanged += (sender, args) => ClearObservations();
            ApplyAppearance();
        }
        private void ClearObservations() { foreach (var result in results) result.SelectedIndex = 0; }
        public void OpenSettings() { Sections.SelectedIndex = 1; }
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            host = e.Parameter as XboxGameBarWidget;
            if (host != null) { host.VisibleChanged += HostVisibleChanged; host.RequestedThemeChanged += HostThemeChanged; }
            accessibility.HighContrastChanged += HighContrastChanged;
            systemUi.ColorValuesChanged += SystemColorsChanged;
            ApplyAppearance();
            _ = ReadAppearance();
            UpdateTimer();
        }
        private async void HostThemeChanged(XboxGameBarWidget sender, object args) => await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, ApplyAppearance);
        private async void HighContrastChanged(Windows.UI.ViewManagement.AccessibilitySettings sender, object args) => await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, ApplyAppearance);
        private async void SystemColorsChanged(Windows.UI.ViewManagement.UISettings sender, object args) => await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, ApplyAppearance);
        private void ApplyAppearance()
        {
            var background = systemUi.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            bool light = background.R * 299 + background.G * 587 + background.B * 114 >= 128000;
            var variant = ThemePolicy.Resolve(appearance, light, accessibility.HighContrast);
            RequestedTheme = variant == ThemeVariant.HighContrast ? ElementTheme.Default :
                appearance == ThemeMode.System && host != null && host.RequestedTheme != ElementTheme.Default ? host.RequestedTheme :
                variant == ThemeVariant.Light ? ElementTheme.Light : ElementTheme.Dark;
            updatingAppearance = true;
            AppearanceChoice.SelectedIndex = (int)appearance;
            updatingAppearance = false;
        }
        private async Task ReadAppearance()
        {
            int revision = appearanceRevision;
            try
            {
                var folder = await ApplicationData.Current.LocalFolder.GetFolderAsync("GuideDeck");
                var file = await folder.GetFileAsync("appearance.json");
                if ((await file.GetBasicPropertiesAsync()).Size > 4096) return;
                var data = JsonObject.Parse(await FileIO.ReadTextAsync(file));
                if (!Enum.TryParse(data.GetNamedString("Mode", "System"), out ThemeMode mode)) mode = ThemeMode.System;
                mode = ThemePolicy.Normalize(mode);
                if (revision == appearanceRevision && appearance != mode) { appearance = mode; ApplyAppearance(); }
            }
            catch { /* Keep the current valid theme if the file is absent or temporarily unavailable. */ }
        }
        private async void AppearanceChanged(object sender, SelectionChangedEventArgs args) => await Run(async () =>
        {
            if (updatingAppearance || AppearanceChoice.SelectedIndex < 0) return;
            appearance = ThemePolicy.Normalize((ThemeMode)AppearanceChoice.SelectedIndex);
            appearanceRevision++;
            var selected = appearance;
            ApplyAppearance();
            var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("GuideDeck", CreationCollisionOption.OpenIfExists);
            var temp = await folder.CreateFileAsync("appearance-" + Guid.NewGuid() + ".tmp");
            try {
                await FileIO.WriteTextAsync(temp, new JsonObject {{"Mode", JsonValue.CreateStringValue(selected.ToString())}}.Stringify());
                await temp.RenameAsync("appearance.json", NameCollisionOption.ReplaceExisting);
            } catch { await temp.DeleteAsync(); throw; }
        });
        private async void HostVisibleChanged(XboxGameBarWidget sender, object args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, UpdateTimer);
        }
        private void UpdateTimer()
        {
            if (host == null || host.Visible) timer.Start(); else timer.Stop();
        }
        public void StopMonitor()
        {
            timer.Stop();
            if (host != null) { host.VisibleChanged -= HostVisibleChanged; host.RequestedThemeChanged -= HostThemeChanged; }
            accessibility.HighContrastChanged -= HighContrastChanged;
            systemUi.ColorValuesChanged -= SystemColorsChanged;
            host = null;
        }
        private async void OpenManager(object sender, RoutedEventArgs e) => await Run(async () =>
        {
            await Windows.ApplicationModel.FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
            Status.Text = "已要求開啟桌面管理器；可能會切出遊戲。";
        });
        private async void RefreshSummary(object sender, RoutedEventArgs e) => await ReadSummary();
        private async Task ReadSummary()
        {
            if (refreshing) return;
            refreshing = true;
            try
            {
                var folder = await ApplicationData.Current.LocalFolder.GetFolderAsync("GuideDeck");
                var file = await folder.GetFileAsync("status-summary.json");
                if ((await file.GetBasicPropertiesAsync()).Size > 65536) throw new FormatException();
                var data = JsonObject.Parse(await FileIO.ReadTextAsync(file));
                if (data.GetNamedNumber("SchemaVersion") != 1) throw new FormatException();
                var timestamp = DateTimeOffset.Parse(data.GetNamedString("UpdatedAt"), System.Globalization.CultureInfo.InvariantCulture);
                var age = DateTimeOffset.UtcNow - timestamp;
                if (age < TimeSpan.FromMinutes(-2)) throw new FormatException();
                Summary.Text = (age > TimeSpan.FromSeconds(30) ? "摘要已過期，請開啟桌面管理器重新檢查。\n" : "最近檢查（不是攔截證明）\n") +
                    data.GetNamedString("Detail") + "\n" + timestamp.ToLocalTime().ToString("MM-dd HH:mm:ss");
            }
            catch { Summary.Text = "無法取得有效摘要。請開啟同套件的桌面管理器並檢查設定。"; }
            finally { refreshing = false; }
        }
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
            if (copying) return;
            var current = edit;
            if (current == null || !current.ChangesGuide || Consent.IsChecked != true) throw new InvalidOperationException("請先預覽並確認實驗性修改。");
            copying = true;
            try {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("mapping-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + ".txt", CreationCollisionOption.GenerateUniqueName);
            await FileIO.WriteTextAsync(file, current.Original);
            ApplicationData.Current.LocalSettings.Values["lastBackup"] = file.Name;
            var package = new DataPackage(); package.SetText(current.Modified); Clipboard.SetContent(package);
            Status.Text = "原始 mapping 已備份，結果已複製；尚未套用至 Steam。";
            } finally { copying = false; }
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
            if (string.IsNullOrWhiteSpace(Context.Text) || string.IsNullOrWhiteSpace(Game.Text)) throw new InvalidOperationException("請填寫遊戲與測試環境。");
            if (Context.Text.Length > 2048 || Game.Text.Length > 256 || Notes.Text.Length > 4096) throw new InvalidOperationException("描述過長。");
            var data = new JsonObject();
            var observations = new Dictionary<string, Observation>();
            for (int i = 0; i < criteria.Count; i++)
            {
                var observation = (Observation)results[i].SelectedIndex;
                observations.Add(ValidationContract.Keys[i], observation);
                data.Add(ValidationContract.Keys[i], JsonValue.CreateStringValue(observation.ToString()));
            }
            ValidationContract.ValidateResults((TestScope)Scope.SelectedIndex, observations, Notes.Text);
            var id = Guid.NewGuid();
            // No direct Steam file access: leave binding null instead of copying a possibly stale summary hash.
            var record = new JsonObject {{"SchemaVersion", JsonValue.CreateNumberValue(2)}, {"EvidenceSource", JsonValue.CreateStringValue("user-observation")},
                {"Id", JsonValue.CreateStringValue(id.ToString())}, {"ObservedAt", JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("O"))},
                {"Game", JsonValue.CreateStringValue(Game.Text)}, {"Environment", JsonValue.CreateStringValue(Context.Text)},
                {"Scope", JsonValue.CreateStringValue(((TestScope)Scope.SelectedIndex).ToString())}, {"OsVersion", JsonValue.CreateStringValue("widget-user-context")},
                {"ConfigHash", JsonValue.Parse("null")}, {"MappingIdentity", JsonValue.Parse("null")}, {"MappingHash", JsonValue.Parse("null")},
                {"Notes", JsonValue.CreateStringValue(Notes.Text)}, {"Results", data}};
            var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("GuideDeck", CreationCollisionOption.OpenIfExists);
            var reports = await folder.CreateFolderAsync("Reports", CreationCollisionOption.OpenIfExists);
            var file = await reports.CreateFileAsync(id + ".json");
            await FileIO.WriteTextAsync(file, record.Stringify());
            ClearObservations();
            Status.Text = "觀察已儲存，可在同套件桌面管理器查看。未綁定設定，只供參考。";
        });
    }
}
