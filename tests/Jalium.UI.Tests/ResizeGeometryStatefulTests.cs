using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class ResizeGeometryStatefulTests
{
    private static readonly Size[] s_resizeSequence =
    [
        new Size(320, 240),
        new Size(20, 104.51953125),
        new Size(8, 8),
        new Size(1, 96),
        new Size(96, 1),
        new Size(0, 0),
        new Size(320, 240),
    ];

    [Fact]
    public void ConditionalRenderStates_ExtremeResizeMatrix_NeverCreatesInvalidGeometry()
    {
        var numberBox = new NumberBox
        {
            Header = "Header",
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };

        var autoCompleteBox = new AutoCompleteBox();
        autoCompleteBox.FilteredItems.Add("Alpha");
        autoCompleteBox.IsDropDownOpen = true;

        ExerciseResizeSequence(numberBox);
        ExerciseResizeSequence(autoCompleteBox);

        var calendar = new Calendar();
        foreach (var mode in new[] { CalendarMode.Month, CalendarMode.Year, CalendarMode.Decade })
        {
            calendar.DisplayMode = mode;
            ExerciseResizeSequence(calendar);
        }
    }

    [Fact]
    public void OrientedChrome_ExtremeResizeMatrix_RemainsInsideAllocatedBounds()
    {
        var tabControl = new TabControl();
        tabControl.Items.Add(new TabItem { Header = "Tab", Content = "Content" });

        foreach (var placement in new[] { Dock.Top, Dock.Bottom, Dock.Left, Dock.Right })
        {
            tabControl.TabStripPlacement = placement;
            ExerciseResizeSequence(tabControl);
        }

        var scrollBar = new ScrollBar
        {
            Minimum = 0,
            Maximum = 100,
            ViewportSize = 10,
        };

        scrollBar.Orientation = Orientation.Vertical;
        ExerciseResizeSequence(scrollBar);
        scrollBar.Orientation = Orientation.Horizontal;
        ExerciseResizeSequence(scrollBar);

        // This is the shape used by Gallery's virtualized navigation/content lists:
        // an IScrollInfo content owner with both desktop gutters present. A native
        // resize is allowed to produce a client area smaller than the 12px gutters.
        var scrollViewer = new ScrollViewer
        {
            Content = new VirtualizingStackPanel(),
            IsOverlayScrollBarEnabled = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
        };
        ExerciseResizeSequence(scrollViewer);
    }

    [Fact]
    public void MiniMap_InputDuringCollapsedResize_IsANoOp()
    {
        var miniMap = new MiniMap();
        RenderAt(miniMap, new Size(1, 1));

        var mouseDown = new MouseButtonEventArgs(
            UIElement.MouseDownEvent,
            new Point(0, 0),
            MouseButton.Left,
            MouseButtonState.Pressed,
            clickCount: 1,
            leftButton: MouseButtonState.Pressed,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 1)
        {
            Source = miniMap,
        };
        miniMap.RaiseEvent(mouseDown);

        typeof(MiniMap)
            .GetField("_isDraggingViewport", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(miniMap, true);

        var mouseMove = new MouseEventArgs(
            UIElement.MouseMoveEvent,
            new Point(0, 0),
            MouseButtonState.Pressed,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 2)
        {
            Source = miniMap,
        };
        miniMap.RaiseEvent(mouseMove);
    }

    private static void ExerciseResizeSequence(Control control)
    {
        control.Width = double.NaN;
        control.Height = double.NaN;
        control.MinWidth = 0;
        control.MinHeight = 0;
        control.Template = null;

        foreach (var size in s_resizeSequence)
        {
            RenderAt(control, size);
        }
    }

    private static void RenderAt(FrameworkElement element, Size size)
    {
        element.Measure(size);
        element.Arrange(new Rect(size));

        var drawing = new DrawingGroup();
        using var drawingContext = drawing.Open();
        element.Render(drawingContext);
    }
}
