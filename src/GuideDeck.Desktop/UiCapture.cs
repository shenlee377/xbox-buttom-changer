using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GuideDeck.Core;

namespace GuideDeck.Desktop;

internal static class UiCapture
{
    // Diagnostic capture renders the actual WPF visual tree. It never edits theme preferences.
    public static async Task SaveAsync(Window window, string directory)
    {
        Directory.CreateDirectory(directory);
        var themes = ((App)Application.Current).Themes;
        var original = themes.Mode;
        var path = Path.Combine(DesktopStorage.Root, "appearance.json");
        var before = File.Exists(path) ? File.ReadAllBytes(path) : null;
        try
        {
            foreach (var mode in new[] {ThemeMode.Light, ThemeMode.Dark})
            {
                themes.SetMode(mode, persist: false);
                await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                if ((mode == ThemeMode.Dark) != (themes.Variant == ThemeVariant.Dark)) throw new InvalidOperationException("Cannot capture requested theme; check high-contrast settings on this runner.");
                var content = (FrameworkElement)window.Content;
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(directory, "desktop-" + mode.ToString().ToLowerInvariant() + ".png"));
                encoder.Save(file);
            }
        }
        finally { themes.SetMode(original, persist: false); }
        var after = File.Exists(path) ? File.ReadAllBytes(path) : null;
        if ((before == null) != (after == null) || (before != null && !before.SequenceEqual(after!)))
            throw new InvalidOperationException("Capturing previews must not modify appearance preferences.");
    }
}
