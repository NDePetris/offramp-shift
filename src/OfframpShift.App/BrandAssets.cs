using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OfframpShift.App;

// Local deployment copies of the owner-supplied v0.1 family artwork. No generated
// geometry, recoloring or proposed semantic theme mappings are used here.
internal static class BrandAssets
{
    private static Uri Asset(string name) => new($"pack://application:,,,/OfframpShift.App;component/Assets/Offramp/{name}");
    private static readonly BitmapSource primary = LoadBitmap("offramp-primary-128.png");
    private static readonly BitmapSource reversed = LoadBitmap("offramp-reversed-128.png");
    private static readonly BitmapFrame lightWindow = LoadFrame("offramp-family-light.ico");
    private static readonly BitmapFrame darkWindow = LoadFrame("offramp-family-dark.ico");

    private static BitmapSource LoadBitmap(string name)
    {
        var image = new BitmapImage(Asset(name)); image.Freeze(); return image;
    }
    private static BitmapFrame LoadFrame(string name)
    {
        var image = BitmapFrame.Create(Asset(name)); image.Freeze(); return image;
    }

    internal static BitmapSource Symbol(bool dark) => dark ? reversed : primary;
    internal static BitmapFrame WindowIcon(bool dark) => dark ? darkWindow : lightWindow;
    internal static bool IsDark(Brush? background) => background is SolidColorBrush brush
        ? 0.2126 * brush.Color.R + 0.7152 * brush.Color.G + 0.0722 * brush.Color.B < 128
        : IsDark(SystemColors.WindowBrush);

    internal static System.Drawing.Icon LoadTrayIcon(bool dark, int size = 32)
    {
        var resource = Application.GetResourceStream(Asset(dark ? "offramp-family-dark.ico" : "offramp-family-light.ico"))
            ?? throw new InvalidOperationException("Canonical Offramp icon resource unavailable.");
        using var stream = resource.Stream;
        using var source = new System.Drawing.Icon(stream, size, size);
        // An owned clone permits the resource stream to close immediately.
        return (System.Drawing.Icon)source.Clone();
    }
}
