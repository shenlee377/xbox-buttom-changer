using System.Windows;
using System.Security.Cryptography;
using System.Text;
namespace GuideDeck.Desktop;
public partial class App : Application
{
    private Mutex? instance;
    private EventWaitHandle? activation;
    private RegisteredWaitHandle? registration;
    private bool owns;
    public ThemeManager Themes { get; private set; } = null!;
    public string? CaptureDirectory { get; private set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DesktopStorage.Root)))[..24];
        var name = @"Local\GuideDeck." + id;
        instance = new Mutex(false, name);
        try { owns = instance.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
        if (!owns)
        {
            try { using var signal = EventWaitHandle.OpenExisting(name + ".Activate"); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { /* First instance is still starting. */ }
            Shutdown(); return;
        }
        Themes = new ThemeManager();
        if (e.Args.Length == 2 && e.Args[0] == "--capture-ui") CaptureDirectory = System.IO.Path.GetFullPath(e.Args[1]);
        if (e.Args.Length == 2 && e.Args[0] == "--theme-probe")
        {
            var path = System.IO.Path.GetFullPath(e.Args[1]);
            Themes.SetMode(GuideDeck.Core.ThemeMode.System, persist: false);
            void WriteProbe() => GuideDeck.Core.AppFiles.Save(path, new {Mode = Themes.Mode.ToString(), Variant = Themes.Variant.ToString()});
            Themes.Changed += WriteProbe; WriteProbe();
        }
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
        registration = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) =>
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(() => {
                if (MainWindow == null) return;
                MainWindow.Show();
                if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
                MainWindow.Activate();
            });
        }, null, Timeout.Infinite, false);
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        registration?.Unregister(null); activation?.Dispose();
        Themes?.Dispose();
        if (owns) instance?.ReleaseMutex();
        instance?.Dispose(); base.OnExit(e);
    }
}
