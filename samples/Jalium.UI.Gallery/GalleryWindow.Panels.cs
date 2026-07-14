using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

/// <summary>
/// Layout-panel section. Each panel arranges a handful of colored boxes so the
/// gallery shows how the framework's layout containers position their children.
/// </summary>
internal static partial class GalleryWindow
{
    public static UIElement BuildPanelsSection() => Section(
        "Layout Panels",
        "How the layout panels arrange their children.",
        Card("Grid", GridPanelDemo(), width: 0),
        Card("StackPanel", StackPanelDemo(), width: 0),
        Card("DockPanel", DockPanelDemo(), width: 0),
        Card("WrapPanel", WrapPanelDemo(), width: 220),
        Card("Canvas", CanvasPanelDemo(), width: 0));

    // Unique helper name (members are shared across all GalleryWindow partial files).
    private static Border PanelBox(string label, byte r, byte g, byte b, double w = double.NaN, double h = double.NaN)
    {
        var box = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(r, g, b)),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(3),
            Child = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        if (!double.IsNaN(w)) box.Width = w;
        if (!double.IsNaN(h)) box.Height = h;
        return box;
    }

    private static UIElement GridPanelDemo()
    {
        var grid = new Grid { Width = 168, Height = 104, HorizontalAlignment = HorizontalAlignment.Left };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddToGrid(grid, PanelBox("0,0", 0x4C, 0xC2, 0x7A), 0, 0);
        AddToGrid(grid, PanelBox("0,1", 0x3B, 0x82, 0xF6), 0, 1);
        AddToGrid(grid, PanelBox("1,0", 0xE0, 0x7B, 0x39), 1, 0);
        AddToGrid(grid, PanelBox("1,1", 0xA7, 0x55, 0xF7), 1, 1);
        return grid;
    }

    private static void AddToGrid(Grid grid, UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static UIElement StackPanelDemo()
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(PanelBox("1", 0x4C, 0xC2, 0x7A, 44, 44));
        stack.Children.Add(PanelBox("2", 0x3B, 0x82, 0xF6, 44, 44));
        stack.Children.Add(PanelBox("3", 0xE0, 0x7B, 0x39, 44, 44));
        stack.Children.Add(PanelBox("4", 0xA7, 0x55, 0xF7, 44, 44));
        return stack;
    }

    private static UIElement DockPanelDemo()
    {
        var dock = new DockPanel { Width = 200, Height = 120, LastChildFill = true, HorizontalAlignment = HorizontalAlignment.Left };

        var top = PanelBox("Top", 0x4C, 0xC2, 0x7A, h: 30);
        DockPanel.SetDock(top, Dock.Top);
        var left = PanelBox("Left", 0x3B, 0x82, 0xF6, w: 46);
        DockPanel.SetDock(left, Dock.Left);
        var right = PanelBox("Right", 0xE0, 0x7B, 0x39, w: 46);
        DockPanel.SetDock(right, Dock.Right);
        var fill = PanelBox("Fill", 0xA7, 0x55, 0xF7);

        dock.Children.Add(top);
        dock.Children.Add(left);
        dock.Children.Add(right);
        dock.Children.Add(fill);
        return dock;
    }

    private static UIElement WrapPanelDemo()
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Width = 196, HorizontalAlignment = HorizontalAlignment.Left };
        byte[][] colors =
        {
            new byte[] { 0x4C, 0xC2, 0x7A }, new byte[] { 0x3B, 0x82, 0xF6 },
            new byte[] { 0xE0, 0x7B, 0x39 }, new byte[] { 0xA7, 0x55, 0xF7 },
            new byte[] { 0x2D, 0xD4, 0xBF }, new byte[] { 0xF4, 0x72, 0xB6 },
        };
        for (int i = 0; i < colors.Length; i++)
            wrap.Children.Add(PanelBox((i + 1).ToString(), colors[i][0], colors[i][1], colors[i][2], 56, 36));
        return wrap;
    }

    private static UIElement CanvasPanelDemo()
    {
        var canvas = new Canvas { Width = 180, Height = 110, HorizontalAlignment = HorizontalAlignment.Left };

        var a = PanelBox("A", 0x4C, 0xC2, 0x7A, 50, 36);
        Canvas.SetLeft(a, 8);
        Canvas.SetTop(a, 8);
        var b = PanelBox("B", 0x3B, 0x82, 0xF6, 50, 36);
        Canvas.SetLeft(b, 70);
        Canvas.SetTop(b, 34);
        var c = PanelBox("C", 0xE0, 0x7B, 0x39, 50, 36);
        Canvas.SetLeft(c, 120);
        Canvas.SetTop(c, 64);

        canvas.Children.Add(a);
        canvas.Children.Add(b);
        canvas.Children.Add(c);
        return canvas;
    }
}
