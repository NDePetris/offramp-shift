using System.Windows;
using System.Windows.Media;

namespace OfframpShift.App;

// Shift-specific 400 x 184 composition matching the owner's final Home mockup.
// Palette/softness derive from NDePetris/phone-fade at
// e873280d492c4aa8e02c0fbaae56e887ecf16931; geometry is intentionally independent.
internal static class HomeLandscape
{
    internal static readonly Geometry Ribbon = FrozenGeometry("M 218,207 C 239,178 369,159 362,125 C 358,105 293,104 264,94 C 238,85 249,76 318,69 L 337,66 C 282,73 269,80 289,87 C 322,95 387,96 392,122 C 403,159 305,190 277,207 Z");
    internal static readonly Geometry Centerline = FrozenGeometry("M 247,210 C 270,176 384,156 377,123 C 370,101 305,101 277,91 C 249,83 263,76 330,67");
    internal static readonly DrawingGroup Light = Create(false), Dark = Create(true);
    internal static readonly SolidColorBrush LightPaper = Brush("#DFE9E3"), DarkPaper = Brush("#263F35");
    internal static readonly LinearGradientBrush LightReadingVeil = ReadingVeil(LightPaper.Color), DarkReadingVeil = ReadingVeil(DarkPaper.Color);
    internal static readonly LinearGradientBrush LightVerticalFade = VerticalFade(LightPaper.Color), DarkVerticalFade = VerticalFade(DarkPaper.Color);

    private static SolidColorBrush Brush(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        brush.Freeze(); return brush;
    }
    private static Geometry FrozenGeometry(string path)
    {
        var geometry = Geometry.Parse(path); geometry.Freeze(); return geometry;
    }
    private static DrawingGroup Create(bool dark)
    {
        var distant = Brush(dark ? "#355448" : "#D0DFD2");
        var hill = Brush(dark ? "#3C5F53" : "#B8CCBF");
        var near = Brush(dark ? "#304A40" : "#D3DECC");
        var road = Brush(dark ? "#406679" : "#B4D4E2");
        var lane = Brush(dark ? "#93B7C7" : "#F6F3EC");
        var tree = Brush(dark ? "#638675" : "#8FAE9D");
        var drawing = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, 400, 184)) };
        using (var context = drawing.Open())
        {
            void Shape(string path, Brush fill) => context.DrawGeometry(fill, null, Geometry.Parse(path));
            Shape("M 297,29 C 306,20 311,19 316,22 C 324,7 343,10 347,23 C 357,20 365,25 368,30 Z", Brush(dark ? "#385448" : "#EEF2E8"));
            Shape("M 110,112 C 159,72 185,67 221,46 C 250,29 269,63 302,51 C 344,33 360,26 400,36 L 400,184 L 110,184 Z", distant);
            Shape("M 118,123 C 167,97 188,57 228,77 C 269,95 292,49 334,60 C 364,69 385,42 400,49 L 400,184 L 118,184 Z", hill);
            Shape("M 150,158 C 202,118 229,101 264,114 C 307,128 329,87 400,89 L 400,184 L 150,184 Z", near);
            Shape("M 170,194 C 212,164 238,141 268,146 C 311,170 349,127 400,132 L 400,184 L 170,184 Z", hill);
            void Tree(double x, double y, double height) => context.DrawGeometry(tree, null,
                Geometry.Parse(FormattableString.Invariant($"M {x},{y} L {x + 3},{y - height} L {x + 6},{y} Z")));
            Tree(305, 70, 10); Tree(312, 70, 13); Tree(320, 69, 11);
            Tree(370, 63, 12); Tree(377, 63, 16); Tree(383, 64, 11);
            context.DrawGeometry(null, new Pen(lane, 2) { MiterLimit = 4 }, Ribbon);
            context.DrawGeometry(road, null, Ribbon);
            context.DrawGeometry(null, new Pen(lane, 1.25)
            {
                DashStyle = new DashStyle([6.4, 8.8], 0),
                DashCap = PenLineCap.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            }, Centerline);
            Tree(216, 107, 12); Tree(228, 105, 24); Tree(241, 107, 19);
            Tree(298, 138, 18); Tree(310, 136, 25); Tree(321, 138, 17);
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
                new(paper, 0), new(paper, 0.55),
                new(Color.FromArgb(46, paper.R, paper.G, paper.B), 0.74),
                new(Color.FromArgb(13, paper.R, paper.G, paper.B), 0.86),
                new(Color.FromArgb(5, paper.R, paper.G, paper.B), 1)
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
            { new(Color.FromArgb(0, paper.R, paper.G, paper.B), 0), new(Color.FromArgb(0, paper.R, paper.G, paper.B), 0.72), new(paper, 1) }
        };
        brush.Freeze(); return brush;
    }

    internal static Matrix Viewport(Size size)
    {
        // Content alone owns height. Fit without distortion and meet the lower
        // card edge even in verbose states, avoiding a rectangular scene cutoff.
        var scale = Math.Min(size.Width / 400, size.Height / 184);
        return new Matrix(scale, 0, 0, scale, size.Width - 400 * scale, size.Height - 184 * scale);
    }
}
