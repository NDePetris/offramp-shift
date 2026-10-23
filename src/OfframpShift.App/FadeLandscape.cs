using System.Windows;
using System.Windows.Media;

namespace OfframpShift.App;

// Direct port: NDePetris/phone-fade, QuietRoadLandscape.kt at
// e873280d492c4aa8e02c0fbaae56e887ecf16931. Original 360 x 280 logical coordinates.
// Container colors come from FadeTheme.kt at that same commit. No runtime dependency.
internal static class FadeLandscape
{
    internal static readonly DrawingGroup Light = Create(false), Dark = Create(true);
    internal static readonly SolidColorBrush LightPaper = Brush("#DFE9E3"), DarkPaper = Brush("#263F35");
    internal static readonly LinearGradientBrush LightReadingVeil = ReadingVeil(LightPaper.Color), DarkReadingVeil = ReadingVeil(DarkPaper.Color);
    internal static readonly LinearGradientBrush LightVerticalFade = VerticalFade(LightPaper.Color), DarkVerticalFade = VerticalFade(DarkPaper.Color);

    private static SolidColorBrush Brush(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        brush.Freeze(); return brush;
    }
    private static DrawingGroup Create(bool dark)
    {
        var distant = Brush(dark ? "#355448" : "#D0DFD2");
        var hill = Brush(dark ? "#3C5F53" : "#B8CCBF");
        var near = Brush(dark ? "#304A40" : "#D3DECC");
        var road = Brush(dark ? "#406679" : "#B4D4E2");
        var lane = Brush(dark ? "#93B7C7" : "#F6F3EC");
        var tree = Brush(dark ? "#638675" : "#8FAE9D");
        var drawing = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, 360, 280)) };
        using (var context = drawing.Open())
        {
            void Shape(string path, Brush fill) => context.DrawGeometry(fill, null, Geometry.Parse(path));
            Shape("M 270,51 C 281,41 283,40 292,44 C 296,27 315,27 320,43 C 336,37 343,48 345,53 Z", distant);
            Shape("M 135,180 C 197,123 238,142 272,98 C 304,56 335,99 360,70 L 360,280 L 135,280 Z", distant);
            Shape("M 106,246 C 184,171 231,198 295,139 C 321,113 341,101 360,106 L 360,280 L 106,280 Z", hill);
            Shape("M 182,280 C 248,200 283,217 360,202 L 360,280 Z", near);
            var ribbon = Geometry.Parse("M 285,330 C 248,219 260,181 368,134 L 368,172 C 290,207 302,241 347,330 Z");
            context.DrawGeometry(null, new Pen(lane, 5) { MiterLimit = 4 }, ribbon);
            context.DrawGeometry(road, null, ribbon);
            // WPF dash lengths are multiples of pen width; Compose uses logical
            // lengths. [10,12] / 2.5 = [4,4.8], with round caps on every dash.
            context.DrawGeometry(null, new Pen(lane, 2.5)
            {
                DashStyle = new DashStyle([4, 4.8], 0),
                DashCap = PenLineCap.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            }, Geometry.Parse("M 316,330 C 268,218 282,196 365,153"));
            Shape("M 238,164 L 242,142 L 246,164 Z", tree);
            Shape("M 251,168 L 255,138 L 259,168 Z", tree);
            Shape("M 264,170 L 268,146 L 272,170 Z", tree);
        }
        drawing.Freeze(); return drawing;
    }
    private static LinearGradientBrush ReadingVeil(Color paper)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new(0, 0),
            EndPoint = new(1, 0),
            ColorInterpolationMode = ColorInterpolationMode.SRgbLinearInterpolation,
            GradientStops = new GradientStopCollection
            {
                new(paper, 0), new(paper, 0.48),
                new(Color.FromArgb(77, paper.R, paper.G, paper.B), 0.82),
                new(Color.FromArgb(20, paper.R, paper.G, paper.B), 1)
            }
        };
        brush.Freeze(); return brush;
    }
    private static LinearGradientBrush VerticalFade(Color paper)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new(0, 0),
            EndPoint = new(0, 1),
            ColorInterpolationMode = ColorInterpolationMode.SRgbLinearInterpolation,
            // WPF's named Transparent contains white RGB; interpolating it can
            // create a pale band in dark mode. Zero-alpha paper preserves the
            // source's transparent-to-paper compositing without that color fringe.
            GradientStops = new GradientStopCollection
            { new(Color.FromArgb(0, paper.R, paper.G, paper.B), 0), new(Color.FromArgb(0, paper.R, paper.G, paper.B), 0.6), new(paper, 1) }
        };
        brush.Freeze(); return brush;
    }

    internal static Matrix Viewport(Size size)
    {
        // Fit the complete source plane in the right-hand ~60% of the normal
        // card. Only the source's own 360x280 clip crops the ribbon closure at
        // y=330. Extra-tall text states retain a compact bottom-aligned scene.
        var scale = Math.Min(size.Width / 480, size.Height / 280);
        return new Matrix(scale, 0, 0, scale, size.Width - 360 * scale, size.Height - 280 * scale);
    }
}
