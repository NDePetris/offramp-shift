using System.Windows;
using System.Windows.Media;

namespace OfframpShift.App;

// Local, static decoration. Frozen drawings are shared by Home and the reminder;
// monitoring snapshots neither rebuild the paths nor drive an animation clock.
public sealed class QuietRoad : FrameworkElement
{
    private static readonly DrawingGroup light = Create(false), dark = Create(true);
    private static readonly DrawingGroup homeLight = CreateHome(false), homeDark = CreateHome(true);
    private static readonly Brush fade = CreateFade();
    private static readonly Brush homeFade = CreateHomeFade();
    private DrawingGroup scene = light;
    private bool isDark, homeBackground;
    public bool HomeBackground
    {
        get => homeBackground;
        set
        {
            homeBackground = value;
            scene = value ? isDark ? homeDark : homeLight : isDark ? dark : light;
            OpacityMask = value ? homeFade : fade;
            InvalidateVisual();
        }
    }
    public QuietRoad() { Focusable = false; IsHitTestVisible = false; ClipToBounds = true; OpacityMask = fade; }
    internal void ApplyTheme(bool isDark, bool highContrast)
    {
        this.isDark = isDark;
        Visibility = highContrast ? Visibility.Collapsed : Visibility.Visible;
        var next = HomeBackground ? isDark ? homeDark : homeLight : isDark ? dark : light;
        if (ReferenceEquals(scene, next)) return;
        scene = next; InvalidateVisual();
    }
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        // Home uses uniform width scaling, bottom anchored within the card. Its
        // height is owned by content, never by the illustration's desired size.
        var scale = ActualWidth / 400;
        if (HomeBackground) context.PushTransform(new TranslateTransform(0, ActualHeight - 180 * scale));
        context.PushTransform(HomeBackground ? new ScaleTransform(scale, scale) : new ScaleTransform(ActualWidth / 100, ActualHeight / 120));
        context.DrawDrawing(scene);
        context.Pop();
        if (HomeBackground) context.Pop();
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
    private static Brush CreateHomeFade()
    {
        var brush = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 0) };
        brush.GradientStops = new GradientStopCollection
        { new(Colors.Transparent, 0), new(Colors.Transparent, 0.34), new(Color.FromArgb(75, 255, 255, 255), 0.55), new(Colors.White, 0.83) };
        brush.Freeze(); return brush;
    }
    private static DrawingGroup CreateHome(bool isDark)
    {
        Brush Color(string value) => new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(value));
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            void Shape(string path, string color) => context.DrawGeometry(Color(color), null, Geometry.Parse(path));
            Shape("M 125,130 C 200,106 244,55 290,53 C 340,50 364,71 400,32 L 400,180 L 125,180 Z", isDark ? "#2D4B40" : "#D0E0D3");
            Shape("M 140,167 C 219,139 247,121 283,91 C 328,54 361,97 400,74 L 400,180 L 140,180 Z", isDark ? "#36564A" : "#BED3C6");
            Shape("M 175,180 C 249,148 327,166 400,130 L 400,180 Z", isDark ? "#29463C" : "#CBDBCD");
            var road = Geometry.Parse("M 350,197 C 274,148 317,112 418,84");
            context.DrawGeometry(null, new Pen(Color(isDark ? "#6D9298" : "#F6F3EC"), 37), road);
            context.DrawGeometry(null, new Pen(Color(isDark ? "#406574" : "#A9CCDD"), 32), road);
            context.DrawGeometry(null, new Pen(Color(isDark ? "#88ACB4" : "#F6F3EC"), 1.6)
            { DashStyle = new DashStyle([5, 6], 0), StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, road);
            Shape("M 250,113 L 255,89 L 260,113 Z M 263,117 L 269,86 L 274,117 Z M 277,120 L 282,97 L 287,120 Z", isDark ? "#486858" : "#AFC7B8");
            Shape("M 305,33 C 316,23 321,24 327,27 C 335,10 353,13 357,26 C 371,22 380,27 384,34 Z", isDark ? "#345448" : "#D1E0D3");
        }
        drawing.Freeze(); return drawing;
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
        target.Resources["ShiftHero"] = highContrast ? SystemColors.WindowBrush : Color(dark ? "#243E34" : "#DFEAE4");
    }
}
