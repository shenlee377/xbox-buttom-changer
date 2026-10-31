using System;
using System.Threading.Tasks;
using Microsoft.Gaming.XboxGameBar;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace GuideDeck.GameBar
{
    public sealed partial class MainPage
    {
        private XboxGameBarWidgetRecordingManager recorder;
        private bool recordingBusy;
        private async void InitializeRecording()
        {
            RecordingStatus.Text = "正在確認 Game Bar 錄影功能…";
            try
            {
                if (host == null || !Windows.Foundation.Metadata.ApiInformation.IsTypePresent("Microsoft.Gaming.XboxGameBar.XboxGameBarWidgetRecordingManager"))
                    throw new NotSupportedException();
                recorder = new XboxGameBarWidgetRecordingManager(host);
                recorder.NotificationsEnabled = true;
                recorder.RecordingStateChanged += RecordingChanged;
                await RefreshRecording();
            }
            catch { StopRecordingMonitor(); RecordingStatus.Text = "目前 Game Bar 不提供錄影接口。請更新 Xbox Game Bar，或使用原生擷取 widget。"; }
        }
        private void StopRecordingMonitor()
        {
            if (recorder != null)
            {
                try { recorder.RecordingStateChanged -= RecordingChanged; } catch { }
                recorder = null;
            }
            StartRecordingButton.IsEnabled = StopRecordingButton.IsEnabled = SaveReplayButton.IsEnabled = false;
        }
        private async void RecordingChanged(XboxGameBarWidgetRecordingManager sender, object args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                if (sender != recorder) return;
                try { await RefreshRecording(); }
                catch { StopRecordingMonitor(); RecordingStatus.Text = "錄影狀態無法取得，請重新開啟 widget。"; }
            });
        }
        private async Task RefreshRecording()
        {
            var current = recorder;
            StartRecordingButton.IsEnabled = StopRecordingButton.IsEnabled = SaveReplayButton.IsEnabled = false;
            if (current == null) return;
            var buffer = await current.IsTimeframeRecordingEnabledAsync();
            if (current != recorder) return;
            var state = current.RecordingState;
            StartRecordingButton.IsEnabled = !recordingBusy && state == XboxGameBarRecordingState.None;
            StopRecordingButton.IsEnabled = !recordingBusy && state == XboxGameBarRecordingState.Recording;
            SaveReplayButton.IsEnabled = !recordingBusy && buffer && state == XboxGameBarRecordingState.None;
            RecordingStatus.Text = state == XboxGameBarRecordingState.Recording ? "Game Bar 正在錄影。" :
                state != XboxGameBarRecordingState.None ? "Game Bar 正在處理錄影。" :
                buffer ? "可以開始錄影，或儲存最近 30 秒（依現有緩衝區而定）。" :
                "可以開始錄影。最近片段需先在 Windows 擷取設定啟用背景錄製；不會自動更改此設定。";
        }
        private async void RefreshRecordingClick(object sender, RoutedEventArgs e)
        {
            if (recordingBusy) return;
            if (recorder == null) InitializeRecording();
            else await Run(async () => await RefreshRecording());
        }
        private async void StartRecordingClick(object sender, RoutedEventArgs e) => await RecordingAction(async manager =>
            "開始錄影：" + Describe(await manager.StartRecordingAsync()));
        private async void StopRecordingClick(object sender, RoutedEventArgs e) => await RecordingAction(async manager =>
            Describe(await manager.StopRecordingAsync()));
        private async void SaveReplayClick(object sender, RoutedEventArgs e) => await RecordingAction(async manager =>
        {
            if (!await manager.IsTimeframeRecordingEnabledAsync()) return "背景錄製未啟用，沒有要求擷取。";
            return Describe(await manager.RecordLastAsync(TimeSpan.FromSeconds(30)));
        });
        private async Task RecordingAction(Func<XboxGameBarWidgetRecordingManager, Task<string>> action)
        {
            if (recordingBusy || recorder == null) return;
            recordingBusy = true;
            StartRecordingButton.IsEnabled = StopRecordingButton.IsEnabled = SaveReplayButton.IsEnabled = false;
            try { Status.Text = await action(recorder); }
            catch (Exception error) { Status.Text = "錄影未完成：" + error.Message; }
            finally
            {
                recordingBusy = false;
                try { await RefreshRecording(); } catch { StopRecordingMonitor(); }
            }
        }
        private static string Describe(XboxGameBarWidgetRecordingResultSummary result) =>
            Describe(result.Result) + (result.Result == XboxGameBarWidgetRecordingResult.Success && !string.IsNullOrEmpty(result.Path) ? "\n" + result.Path : "");
        private static string Describe(XboxGameBarWidgetRecordingResult result)
        {
            switch (result)
            {
                case XboxGameBarWidgetRecordingResult.Success: return "Game Bar 已接受操作。";
                case XboxGameBarWidgetRecordingResult.NotSupported: return "此環境不支援這項錄影操作。";
                case XboxGameBarWidgetRecordingResult.TargetNotAvailable: return "沒有可擷取的遊戲目標，請先返回遊戲。";
                case XboxGameBarWidgetRecordingResult.InProgress: return "Game Bar 正在處理另一項操作，未重送。";
                default: return "Game Bar 回報失敗，未重送。請檢查擷取設定、遊戲與可用磁碟空間。";
            }
        }
    }
}
