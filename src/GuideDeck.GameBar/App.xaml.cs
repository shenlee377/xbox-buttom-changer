using Microsoft.Gaming.XboxGameBar;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace GuideDeck.GameBar
{
    sealed partial class App : Application
    {
        private XboxGameBarWidget widget;
        public App() { InitializeComponent(); }
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            if (args.PrelaunchActivated) return;
            var frame = new Frame();
            Window.Current.Content = frame;
            frame.Navigate(typeof(MainPage));
            Window.Current.Activate();
        }
        protected override void OnActivated(IActivatedEventArgs args)
        {
            var activated = args as XboxGameBarWidgetActivatedEventArgs;
            if (activated == null || !activated.IsLaunchActivation) return;
            var frame = new Frame();
            Window.Current.Content = frame;
            widget = new XboxGameBarWidget(activated, Window.Current.CoreWindow, frame);
            widget.PinningSupported = false;
            widget.SettingsSupported = true;
            frame.Navigate(typeof(MainPage));
            widget.SettingsClicked += async (sender, e) =>
                await frame.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => ((MainPage)frame.Content).OpenSettings());
            Window.Current.Closed += (sender, e) => { widget = null; };
            Window.Current.Activate();
        }
    }
}
