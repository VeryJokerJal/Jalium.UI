using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildContainersSection() => Section(
        "Layout & Containers",
        "Single-child and grouping containers, expanders and splitters.",
        Card("Border", new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x33, 0x40)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0x7A)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = "Decorated child",
                FontSize = 14,
                Foreground = TextPrimary,
            },
        }),
        Card("GroupBox", new GroupBox
        {
            Header = "Account",
            Content = MakeGroupBoxBody(),
        }),
        Card("Expander", new Expander
        {
            Header = "Advanced options",
            IsExpanded = true,
            Content = MakeExpanderBody(),
        }),
        Card("Viewbox", new Viewbox
        {
            Width = 140,
            Height = 110,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = "Aa",
                FontSize = 48,
                FontWeight = FontWeights.Bold,
                Foreground = Accent,
            },
        }),
        Card("UniformGrid", MakeUniformGrid()),
        Card("GridSplitter", MakeSplitterGrid()),
        Card("ScrollViewer", new ScrollViewer
        {
            Height = 130,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = MakeTallList(),
        }));

    private static UIElement MakeGroupBoxBody()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            Margin = new Thickness(10),
        };
        stack.Children.Add(new TextBlock { Text = "Name: Ada Lovelace", FontSize = 13, Foreground = TextPrimary });
        stack.Children.Add(new TextBlock { Text = "Role: Engineer", FontSize = 13, Foreground = TextSecondary });
        stack.Children.Add(new TextBlock { Text = "Status: Active", FontSize = 13, Foreground = TextSecondary });
        return stack;
    }

    private static UIElement MakeExpanderBody()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            Margin = new Thickness(10, 8, 10, 8),
        };
        stack.Children.Add(new TextBlock { Text = "Enable telemetry", FontSize = 13, Foreground = TextPrimary });
        stack.Children.Add(new TextBlock { Text = "Use experimental renderer", FontSize = 13, Foreground = TextSecondary });
        stack.Children.Add(new TextBlock { Text = "Cache shaders on disk", FontSize = 13, Foreground = TextSecondary });
        return stack;
    }

    private static UIElement MakeUniformGrid()
    {
        var grid = new UniformGrid
        {
            Rows = 2,
            Columns = 2,
            RowSpacing = 6,
            ColumnSpacing = 6,
            Width = 160,
            Height = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        grid.Children.Add(MakeColorCell(Color.FromRgb(0xE0, 0x6C, 0x75)));
        grid.Children.Add(MakeColorCell(Color.FromRgb(0x98, 0xC3, 0x79)));
        grid.Children.Add(MakeColorCell(Color.FromRgb(0x61, 0xAF, 0xEF)));
        grid.Children.Add(MakeColorCell(Color.FromRgb(0xE5, 0xC0, 0x7B)));
        return grid;
    }

    private static UIElement MakeColorCell(Color color) => new Border
    {
        Background = new SolidColorBrush(color),
        CornerRadius = new CornerRadius(6),
    };

    private static UIElement MakeSplitterGrid()
    {
        var grid = new Grid
        {
            Width = 240,
            Height = 100,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.FromPixels(6) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            CornerRadius = new CornerRadius(6),
            Child = new TextBlock
            {
                Text = "Left",
                FontSize = 13,
                Foreground = TextSecondary,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var splitter = new GridSplitter
        {
            ResizeDirection = GridResizeDirection.Columns,
        };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);

        var right = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            CornerRadius = new CornerRadius(6),
            Child = new TextBlock
            {
                Text = "Right",
                FontSize = 13,
                Foreground = TextSecondary,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        return grid;
    }

    private static UIElement MakeTallList()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
        };
        for (int i = 1; i <= 12; i++)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Scrollable item " + i,
                FontSize = 13,
                Foreground = i == 1 ? Accent : TextPrimary,
            });
        }
        return stack;
    }
}
