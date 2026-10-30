using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GuideDeck.Core;

namespace GuideDeck.Desktop;

internal static class UiCapture
{
    // Diagnostic capture renders the live WPF tree at standard and compact sizes.
    // VisualBrush removes the root's layout offset, so margins cannot crop the image.
    public static async Task SaveAsync(Window window, string directory)
    {
        Directory.CreateDirectory(directory);
        var themes = ((App)Application.Current).Themes;
        var original = themes.Mode;
        var path = Path.Combine(DesktopStorage.Root, "appearance.json");
        var before = File.Exists(path) ? File.ReadAllBytes(path) : null;
        try
        {
            foreach (var compact in new[] {false, true})
            {
                window.Width = compact ? 780 : 1140;
                window.Height = compact ? 580 : 860;
                foreach (var mode in new[] {ThemeMode.Light, ThemeMode.Dark})
                {
                    themes.SetMode(mode, persist: false);
                    await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                    if ((mode == ThemeMode.Dark) != (themes.Variant == ThemeVariant.Dark))
                        throw new InvalidOperationException("Cannot capture requested theme; check runner high-contrast settings.");
                    var content = (FrameworkElement)window.Content;
                    int width = (int)Math.Ceiling(content.ActualWidth), height = (int)Math.Ceiling(content.ActualHeight);
                    var visual = new DrawingVisual();
                    using (var drawing = visual.RenderOpen())
                        drawing.DrawRectangle(new VisualBrush(content) {Stretch = Stretch.Fill}, null, new Rect(0, 0, width, height));
                    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(visual);
                    // The root is opaque. Transparent corners signal an offset/cropping regression.
                    foreach (var corner in new[] {new Int32Rect(0, 0, 1, 1), new Int32Rect(width - 1, height - 1, 1, 1)})
                    {
                        var pixel = new byte[4]; bitmap.CopyPixels(corner, pixel, 4, 0);
                        if (pixel[3] != 255) throw new InvalidOperationException("UI capture has a transparent or cropped corner.");
                    }
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    var suffix = compact ? "-compact" : "";
                    using var file = File.Create(Path.Combine(directory, "desktop-" + mode.ToString().ToLowerInvariant() + suffix + ".png"));
                    encoder.Save(file);
                }
            }
        }
        finally { themes.SetMode(original, persist: false); }
        var after = File.Exists(path) ? File.ReadAllBytes(path) : null;
        if ((before == null) != (after == null) || (before != null && !before.SequenceEqual(after!)))
            throw new InvalidOperationException("Capturing previews must not modify appearance preferences.");
    }
}
