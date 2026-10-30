using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GuideDeck.Core;
using Microsoft.Win32;

namespace GuideDeck.Desktop;

public sealed class ThemeManager : IDisposable
{
    private sealed record Settings(ThemeMode Mode);
    private readonly Dictionary<Window, HwndSource> windows = [];
    private readonly string settingsPath = Path.Combine(DesktopStorage.Root, "appearance.json");
    private ResourceDictionary? palette;
    private int refreshQueued;
    private FileSystemWatcher? settingsWatcher;
    public ThemeMode Mode { get; private set; } = ThemeMode.System;
    public ThemeVariant Variant { get; private set; }
    public event Action? Changed;

    public ThemeManager()
    {
        try { if (File.Exists(settingsPath)) Mode = ThemePolicy.Normalize(AppFiles.Load<Settings>(settingsPath)?.Mode ?? ThemeMode.System); }
        catch (Exception e) when (e is IOException or FormatException or System.Text.Json.JsonException or UnauthorizedAccessException) { Mode = ThemeMode.System; }
        SystemEvents.UserPreferenceChanged += UserPreferenceChanged;
        SystemParameters.StaticPropertyChanged += ParametersChanged;
        try
        {
            Directory.CreateDirectory(DesktopStorage.Root);
            settingsWatcher = new FileSystemWatcher(DesktopStorage.Root, "appearance.json") {NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size};
            settingsWatcher.Changed += SettingsChanged; settingsWatcher.Created += SettingsChanged;
            settingsWatcher.Deleted += SettingsChanged; settingsWatcher.Renamed += SettingsChanged;
            settingsWatcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { settingsWatcher?.Dispose(); settingsWatcher = null; }
        Refresh();
    }
    private void SettingsChanged(object sender, FileSystemEventArgs args)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(() => {
            try
            {
                var mode = File.Exists(settingsPath) ? ThemePolicy.Normalize(AppFiles.Load<Settings>(settingsPath)?.Mode ?? ThemeMode.System) : ThemeMode.System;
                if (Mode != mode) { Mode = mode; Refresh(); }
            }
            catch (Exception e) when (e is IOException or FormatException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
        });
    }
    public void SetMode(ThemeMode mode, bool persist = true)
    {
        mode = ThemePolicy.Normalize(mode);
        if (persist) AppFiles.Save(settingsPath, new Settings(mode));
        Mode = mode; Refresh();
    }
    private void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs args) => QueueRefresh();
    private void ParametersChanged(object? sender, PropertyChangedEventArgs args) => QueueRefresh();
    private void QueueRefresh()
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.HasShutdownStarted || Interlocked.Exchange(ref refreshQueued, 1) == 1) return;
        dispatcher.BeginInvoke(() => { Interlocked.Exchange(ref refreshQueued, 0); Refresh(); });
    }
    public void Refresh()
    {
        bool usesLight = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            usesLight = key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException) { }
        var next = ThemePolicy.Resolve(Mode, usesLight, SystemParameters.HighContrast);
        if (palette != null && next == Variant && next != ThemeVariant.HighContrast) { Changed?.Invoke(); return; }
        Variant = next;
        var resources = Application.Current.Resources.MergedDictionaries;
        if (palette != null) resources.Remove(palette);
        palette = Variant == ThemeVariant.HighContrast ? HighContrastPalette() : new ResourceDictionary {
            Source = new Uri($"/GuideDeck;component/Themes/{Variant}.xaml", UriKind.Relative)
        };
        resources.Add(palette);
        foreach (var source in windows.Values.ToArray()) if (!source.IsDisposed) ApplyTitleBar(source.Handle);
        Changed?.Invoke();
    }
    private static ResourceDictionary HighContrastPalette()
    {
        var result = new ResourceDictionary();
        foreach (var name in new[] {"AppBackgroundBrush", "SurfaceBrush", "InputBrush", "NoticeBrush", "HoverBrush"}) result[name] = new SolidColorBrush(SystemColors.WindowColor);
        foreach (var name in new[] {"TextBrush", "MutedTextBrush", "NoticeTextBrush", "BorderBrush", "ControlBorderBrush"}) result[name] = new SolidColorBrush(SystemColors.WindowTextColor);
        foreach (var name in new[] {"AccentBrush", "AccentSoftBrush"}) result[name] = new SolidColorBrush(SystemColors.HighlightColor);
        foreach (var name in new[] {"AccentTextBrush", "AccentSoftTextBrush"}) result[name] = new SolidColorBrush(SystemColors.HighlightTextColor);
        return result;
    }
    public void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            if (source != null) { windows[window] = source; source.AddHook(WindowMessage); ApplyTitleBar(source.Handle); }
        };
        window.Closed += (_, _) => { if (windows.Remove(window, out var source) && !source.IsDisposed) source.RemoveHook(WindowMessage); };
    }
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (message is 0x001A or 0x031A) QueueRefresh();
        return IntPtr.Zero;
    }
    private void ApplyTitleBar(IntPtr handle)
    {
        int dark = Variant == ThemeVariant.Dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= UserPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= ParametersChanged;
        settingsWatcher?.Dispose();
        foreach (var source in windows.Values) if (!source.IsDisposed) source.RemoveHook(WindowMessage);
        windows.Clear();
    }
}
