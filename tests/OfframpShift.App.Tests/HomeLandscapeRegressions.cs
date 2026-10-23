using System.Windows;
using System.Windows.Media;
using OfframpShift.App;

internal static class HomeLandscapeRegressions
{
    internal static void Run(Action<string, Action> test)
    {
        void Check(bool value) { if (!value) throw new InvalidOperationException("Home composition assertion failed."); }
        test("wide Home composition has a frozen broad road ribbon with separate subtle lane markings", () =>
        {
            Check(HomeLandscape.Ribbon.IsFrozen && HomeLandscape.Centerline.IsFrozen);
            Check(HomeLandscape.Ribbon.Bounds.Width > 170 && HomeLandscape.Ribbon.Bounds.Left > 210);
            Check(HomeLandscape.Centerline.Bounds.Width > 120);
            foreach (var drawing in new[] { HomeLandscape.Light, HomeLandscape.Dark })
            {
                Check(drawing.IsFrozen && drawing.ClipGeometry is RectangleGeometry { Rect: var rect } && rect == new Rect(0, 0, 400, 184));
                var shapes = drawing.Children.Cast<GeometryDrawing>().ToArray();
                var ribbon = shapes.Where(shape => ReferenceEquals(shape.Geometry, HomeLandscape.Ribbon)).ToArray();
                Check(ribbon.Length == 2 && ribbon[0].Brush is null && ribbon[0].Pen.Thickness == 2 && ribbon[1].Pen is null);
                var lane = shapes.Single(shape => ReferenceEquals(shape.Geometry, HomeLandscape.Centerline)).Pen;
                Check(lane.Thickness == 1.25 && lane.DashCap == PenLineCap.Round);
                Check(lane.DashStyle.Dashes.Select(value => value * lane.Thickness).SequenceEqual(new[] { 8d, 11 }));
            }
        });
        test("Home veils protect the left and foreground while the wide viewport stays proportional", () =>
        {
            foreach (var dark in new[] { false, true })
            {
                var horizontal = dark ? HomeLandscape.DarkReadingVeil : HomeLandscape.LightReadingVeil;
                var vertical = dark ? HomeLandscape.DarkVerticalFade : HomeLandscape.LightVerticalFade;
                var paper = dark ? HomeLandscape.DarkPaper : HomeLandscape.LightPaper;
                Check(paper.Color == (Color)ColorConverter.ConvertFromString(dark ? "#263F35" : "#DFE9E3"));
                Check(horizontal.IsFrozen && vertical.IsFrozen && horizontal.EndPoint == new Point(1, 0) && vertical.EndPoint == new Point(0, 1));
                Check(horizontal.GradientStops.Select(x => x.Offset).SequenceEqual(new[] { 0d, 0.55, 0.74, 0.86, 1 }));
                Check(horizontal.GradientStops[0].Color.A == 255 && horizontal.GradientStops[1].Color.A == 255 && horizontal.GradientStops[^1].Color.A <= 20);
                Check(vertical.GradientStops.Select(x => x.Offset).SequenceEqual(new[] { 0d, 0.72, 1 }));
                Check(vertical.GradientStops.Select(x => x.Color.A).SequenceEqual(new byte[] { 0, 0, 255 }));
                Check(vertical.GradientStops.All(x => x.Color.R == paper.Color.R && x.Color.G == paper.Color.G && x.Color.B == paper.Color.B));
            }
            foreach (var size in new[] { new Size(344, 179), new Size(385, 179), new Size(385, 219), new Size(520, 179) })
            {
                var transform = HomeLandscape.Viewport(size);
                var bounds = Rect.Transform(new Rect(0, 0, 400, 184), transform);
                Check(transform.M11 == transform.M22 && transform.M12 == 0 && transform.M21 == 0);
                Check(bounds.Left >= 0 && bounds.Top >= -0.001 && Math.Abs(bounds.Right - size.Width) < 0.001 && Math.Abs(bounds.Bottom - size.Height) < 0.001);
                Check(bounds.Width >= size.Width * 0.70);
            }
        });
    }
}
