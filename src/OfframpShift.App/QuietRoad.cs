using System.Windows;
using System.Windows.Media;

namespace OfframpShift.App;

// Local, static decoration. Frozen drawings are shared by Home and the reminder;
// monitoring snapshots neither rebuild the paths nor drive an animation clock.
public sealed class QuietRoad : FrameworkElement
{
    private static readonly DrawingGroup light = Create(false), dark = Create(true);

    private static readonly Brush fade = CreateFade();

    private DrawingGroup scene = light;
    private bool isDark, homeBackground;
    public bool HomeBackground
    {
        get => homeBackground;
        set
        {
            homeBackground = value;
            scene = value ? isDark ? HomeLandscape.Dark : HomeLandscape.Light : isDark ? dark : light;
            OpacityMask = value ? null : fade;
            InvalidateVisual();
        }
    }
    public QuietRoad() { Focusable = false; IsHitTestVisible = false; ClipToBounds = true; OpacityMask = fade; }
    internal void ApplyTheme(bool isDark, bool highContrast)
    {
        this.isDark = isDark;
        Visibility = highContrast ? Visibility.Collapsed : Visibility.Visible;
        var next = HomeBackground ? isDark ? HomeLandscape.Dark : HomeLandscape.Light : isDark ? dark : light;
        if (ReferenceEquals(scene, next)) return;
        scene = next; InvalidateVisual();
    }
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if (!HomeBackground)
        {
            context.PushTransform(new ScaleTransform(ActualWidth / 100, ActualHeight / 120));
            context.DrawDrawing(scene); context.Pop(); return;
        }
        var bounds = new Rect(RenderSize);
        context.DrawRectangle(isDark ? HomeLandscape.DarkPaper : HomeLandscape.LightPaper, null, bounds);
        context.PushTransform(new MatrixTransform(HomeLandscape.Viewport(RenderSize)));
        context.DrawDrawing(scene); context.Pop();
        // Reading veils affect decoration only, below all hero content.
        context.DrawRectangle(isDark ? HomeLandscape.DarkReadingVeil : HomeLandscape.LightReadingVeil, null, bounds);
        context.DrawRectangle(isDark ? HomeLandscape.DarkVerticalFade : HomeLandscape.LightVerticalFade, null, bounds);
    }
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (!HomeBackground) return;
        // Border's CornerRadius does not clip children. Match its outer curve;
        // only layout-size changes replace this small frozen clip, not snapshots.
        var clip = new RectangleGeometry(new Rect(sizeInfo.NewSize), 20, 20);
        clip.Freeze(); Clip = clip;
    }
    private static Brush CreateFade()
    {
        var brush = new LinearGradientBrush(Colors.White, Colors.Transparent, new Point(0, 0), new Point(0, 1));
        brush.GradientStops = new GradientStopCollection { new(Colors.White, 0), new(Colors.White, 0.65), new(Colors.Transparent, 1) };
        brush.Freeze(); return brush;
    }
    private static DrawingGroup Create(bool isDark)
    {
        Brush Color(string value) => new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(value));
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            void Shape(string path, string color) => context.DrawGeometry(Color(color), null, Geometry.Parse(path));
            Shape("M 0,62 C 30,58 42,20 73,30 C 88,34 96,28 100,20 L 100,120 L 0,120 Z", isDark ? "#2E4B40" : "#D4E2D7");
            Shape("M 0,98 C 35,88 58,46 100,49 L 100,120 L 0,120 Z", isDark ? "#36594E" : "#C0D4C9");
            Shape("M 0,117 C 30,98 72,108 100,88 L 100,120 L 0,120 Z", isDark ? "#29473D" : "#CCDCD2");
            var edge = new Pen(Color(isDark ? "#6D9298" : "#F6F3EC"), 25);
            var road = Geometry.Parse("M 65,135 C 20,90 55,78 112,57");
            context.DrawGeometry(null, edge, road);
            context.DrawGeometry(null, new Pen(Color(isDark ? "#406574" : "#ACCFDE"), 21), road);
            context.DrawGeometry(null, new Pen(Color(isDark ? "#88ACB4" : "#F6F3EC"), 1.3) { DashStyle = new DashStyle([4, 5], 0), StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, road);
            Shape("M 13,79 L 17,60 L 21,79 Z M 24,82 L 29,57 L 33,82 Z", isDark ? "#4B6D5D" : "#ADC7B9");
            Shape("M 40,14 C 47,7 49,8 54,10 C 59,-2 72,0 76,9 C 88,6 92,11 95,15 Z", isDark ? "#345548" : "#D1E0D3");
        }
        drawing.Freeze(); return drawing;
    }
}

internal static class QuietRoadPalette
{
    internal static void Apply(FrameworkElement target, bool dark, bool highContrast)
    {
        Brush Color(string value)
        {
            var brush = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(value));
            brush.Freeze(); return brush;
        }
        target.Resources["ShiftCanvas"] = highContrast ? SystemColors.WindowBrush : Color(dark ? "#1F2B28" : "#F6F3EC");
        target.Resources["ShiftText"] = highContrast ? SystemColors.WindowTextBrush : Color(dark ? "#F6F3EC" : "#1F2B28");
        target.Resources["ShiftSupporting"] = highContrast ? SystemColors.WindowTextBrush : Color(dark ? "#C8BCA7" : "#405F54");
        target.Resources["ShiftAccent"] = highContrast ? SystemColors.WindowTextBrush : Color(dark ? "#79AFC9" : "#2E5A4F");
        target.Resources["ShiftTrack"] = highContrast ? SystemColors.ControlBrush : Color(dark ? "#50675D" : "#C4D1C9");
        target.Resources["ShiftDivider"] = highContrast ? SystemColors.WindowTextBrush : Color(dark ? "#50675D" : "#C8BCA7");
        target.Resources["ShiftHomeHero"] = highContrast ? SystemColors.WindowBrush : dark ? HomeLandscape.DarkPaper : HomeLandscape.LightPaper;
        target.Resources["ShiftHero"] = highContrast ? SystemColors.WindowBrush : Color(dark ? "#243E34" : "#DFEAE4");
    }
}
