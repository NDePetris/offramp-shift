using System.Globalization;
using System.Windows;
using System.Windows.Media;
using OfframpShift.App;

internal static class FadeLandscapeRegressions
{
    internal static void Run(Action<string, Action> test)
    {
        void Check(bool value) { if (!value) throw new InvalidOperationException("Pinned Fade artwork assertion failed."); }
        test("pinned Fade 360x280 artwork preserves all paths paint order colors and dash units", () =>
        {
            // Independent source contract from QuietRoadLandscape.kt at
            // e873280d492c4aa8e02c0fbaae56e887ecf16931, not a visual-equality claim.
            string[] paths = [
                "M270,51 C281,41 283,40 292,44 C296,27 315,27 320,43 C336,37 343,48 345,53 Z",
                "M135,180 C197,123 238,142 272,98 C304,56 335,99 360,70 L360,280 L135,280 Z",
                "M106,246 C184,171 231,198 295,139 C321,113 341,101 360,106 L360,280 L106,280 Z",
                "M182,280 C248,200 283,217 360,202 L360,280 Z",
                "M285,330 C248,219 260,181 368,134 L368,172 C290,207 302,241 347,330 Z",
                "M285,330 C248,219 260,181 368,134 L368,172 C290,207 302,241 347,330 Z",
                "M316,330 C268,218 282,196 365,153",
                "M238,164 L242,142 L246,164 Z", "M251,168 L255,138 L259,168 Z", "M264,170 L268,146 L272,170 Z"
            ];
            foreach (var dark in new[] { false, true })
            {
                var drawing = dark ? FadeLandscape.Dark : FadeLandscape.Light;
                var colors = dark
                    ? new[] { "#355448", "#355448", "#3C5F53", "#304A40", "#93B7C7", "#406679", "#93B7C7", "#638675", "#638675", "#638675" }
                    : new[] { "#D0DFD2", "#D0DFD2", "#B8CCBF", "#D3DECC", "#F6F3EC", "#B4D4E2", "#F6F3EC", "#8FAE9D", "#8FAE9D", "#8FAE9D" };
                Check(drawing.IsFrozen && drawing.Children.Count == paths.Length);
                Check(drawing.ClipGeometry is RectangleGeometry { Rect: var rect } && rect == new Rect(0, 0, 360, 280));
                for (var index = 0; index < paths.Length; index++)
                {
                    var shape = (GeometryDrawing)drawing.Children[index];
                    Check(shape.Geometry.ToString(CultureInfo.InvariantCulture) == Geometry.Parse(paths[index]).ToString(CultureInfo.InvariantCulture));
                    Check(((SolidColorBrush)(shape.Brush ?? shape.Pen.Brush)).Color == (Color)ColorConverter.ConvertFromString(colors[index]));
                }
                var shoulder = (GeometryDrawing)drawing.Children[4]; var fill = (GeometryDrawing)drawing.Children[5];
                Check(shoulder.Brush is null && shoulder.Pen.Thickness == 5 && fill.Pen is null);
                Check(ReferenceEquals(shoulder.Geometry, fill.Geometry));
                var lane = ((GeometryDrawing)drawing.Children[6]).Pen;
                Check(lane.Thickness == 2.5 && lane.DashCap == PenLineCap.Round && lane.StartLineCap == PenLineCap.Round && lane.EndLineCap == PenLineCap.Round);
                Check(lane.DashStyle.Dashes.Select(value => value * lane.Thickness).SequenceEqual(new[] { 10d, 12 }));
            }
        });
        test("Fade veils retain source stops and uniform right-aligned viewport without layout growth", () =>
        {
            foreach (var dark in new[] { false, true })
            {
                var horizontal = dark ? FadeLandscape.DarkReadingVeil : FadeLandscape.LightReadingVeil;
                var vertical = dark ? FadeLandscape.DarkVerticalFade : FadeLandscape.LightVerticalFade;
                var paper = dark ? FadeLandscape.DarkPaper : FadeLandscape.LightPaper;
                Check(paper.Color == (Color)ColorConverter.ConvertFromString(dark ? "#263F35" : "#DFE9E3"));
                Check(horizontal.IsFrozen && vertical.IsFrozen && horizontal.EndPoint == new Point(1, 0) && vertical.EndPoint == new Point(0, 1));
                Check(horizontal.GradientStops.Select(x => x.Offset).SequenceEqual(new[] { 0d, 0.48, 0.82, 1 }));
                Check(horizontal.GradientStops.Select(x => x.Color.A).SequenceEqual(new byte[] { 255, 255, 77, 20 }));
                Check(horizontal.GradientStops.All(x => x.Color.R == paper.Color.R && x.Color.G == paper.Color.G && x.Color.B == paper.Color.B));
                Check(vertical.GradientStops.Select(x => x.Offset).SequenceEqual(new[] { 0d, 0.6, 1 }));
                Check(vertical.GradientStops.Select(x => x.Color.A).SequenceEqual(new byte[] { 0, 0, 255 }));
                Check(vertical.GradientStops.All(x => x.Color.R == paper.Color.R && x.Color.G == paper.Color.G && x.Color.B == paper.Color.B));
            }
            foreach (var size in new[] { new Size(344, 179), new Size(385, 179), new Size(385, 219), new Size(520, 179) })
            {
                var transform = FadeLandscape.Viewport(size);
                var bounds = Rect.Transform(new Rect(0, 0, 360, 280), transform);
                Check(transform.M11 == transform.M22 && transform.M12 == 0 && transform.M21 == 0);
                Check(bounds.Left >= 0 && bounds.Top >= -0.001 && Math.Abs(bounds.Right - size.Width) < 0.001 && Math.Abs(bounds.Bottom - size.Height) < 0.001);
            }
        });
    }
}
