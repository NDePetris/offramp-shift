using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

internal static class ScrollLayoutChecks
{
    internal static void Clearance(ScrollViewer viewer)
    {
        viewer.ApplyTemplate(); viewer.UpdateLayout();
        var presenter = (ScrollContentPresenter)viewer.Template.FindName("PART_ScrollContentPresenter", viewer);
        var content = (FrameworkElement)viewer.Content;
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(viewer).TransformBounds(new Rect(element.RenderSize));
        foreach (var control in HomeShellInspection.Descendants(content).OfType<Control>().Where(x => x.IsVisible && x.Name.Length > 0 && !x.Name.StartsWith("PART_", StringComparison.Ordinal) && x is Button or TextBox or ComboBox or CheckBox))
            if (Bounds(control).Left < Bounds(presenter).Left - 0.5 || Bounds(control).Right > Bounds(presenter).Right + 0.5)
                throw new InvalidOperationException("Setting/action clipped horizontally: " + control.Name);
        if (viewer.ComputedVerticalScrollBarVisibility != Visibility.Visible) return;
        var scrollbar = (ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar", viewer);
        if (Bounds(presenter).Right + 7.5 > Bounds(scrollbar).Left || Bounds(content).Right > Bounds(presenter).Right + 0.5)
            throw new InvalidOperationException("Scrollbar column overlaps page content.");
        if (scrollbar.ActualWidth <= 0 || viewer.ScrollableHeight <= 0)
            throw new InvalidOperationException("Overflow must retain a usable native scrollbar.");
    }
}
