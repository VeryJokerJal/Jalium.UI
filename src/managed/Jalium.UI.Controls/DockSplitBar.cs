using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

using static Jalium.UI.Cursors;

namespace Jalium.UI.Controls;

/// <summary>
/// Internal splitter bar used between panes in a <see cref="DockSplitPanel"/>.
/// </summary>
internal sealed class DockSplitBar : Control
{
    private static readonly SolidColorBrush s_fallbackDraggingBrush = new(ThemeColors.Accent);
    private static readonly SolidColorBrush s_fallbackHoverBrush = new(ThemeColors.DockSplitterHover);
    private static readonly SolidColorBrush s_fallbackBackgroundBrush = new(ThemeColors.DockSplitterBackground);
    private static readonly SolidColorBrush s_fallbackEdgeBrush = new(ThemeColors.DockTabStripBorder);

    internal DockSplitPanel? Owner { get; set; }
    internal int PaneIndex1 { get; set; }
    internal int PaneIndex2 { get; set; }

    private bool _isDragging;
    private Point _dragStartPoint; // In Owner's coordinate space
    private double _originalSize1;
    private double _originalSize2;

    internal DockSplitBar()
    {
        Focusable = false;

        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new RoutedEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new RoutedEventHandler(OnMouseMoveHandler));
    }

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            // Use Owner coordinate space so the reference frame doesn't move with the splitter
            StartDrag(mouseArgs.GetPosition(Owner ?? (UIElement)this));
            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, RoutedEventArgs e)
    {
        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            if (_isDragging)
                FinishDrag();
            e.Handled = true;
        }
    }

    private void OnMouseMoveHandler(object sender, RoutedEventArgs e)
    {
        if (_isDragging && e is MouseEventArgs mouseArgs)
        {
            UpdateDrag(mouseArgs.GetPosition(Owner ?? (UIElement)this));
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        if (_isDragging)
            FinishDrag();
    }

    private void StartDrag(Point positionInOwner)
    {
        if (Owner == null) return;

        CaptureMouse();
        _isDragging = true;
        _dragStartPoint = positionInOwner;

        Owner.GetPaneSizes(PaneIndex1, PaneIndex2, out _originalSize1, out _originalSize2);
        InvalidateVisual();
    }

    private void UpdateDrag(Point positionInOwner)
    {
        if (Owner == null) return;

        var isHorizontal = Owner.Orientation == Orientation.Horizontal;
        var delta = isHorizontal
            ? positionInOwner.X - _dragStartPoint.X
            : positionInOwner.Y - _dragStartPoint.Y;

        Owner.ResizePanes(PaneIndex1, PaneIndex2, _originalSize1, _originalSize2, delta);
    }

    private void FinishDrag()
    {
        ReleaseMouseCapture();
        _isDragging = false;
        InvalidateVisual();
    }

    internal void UpdateCursorForOrientation()
    {
        if (Owner == null) return;
        Cursor = Owner.Orientation == Orientation.Horizontal ? SizeWE : SizeNS;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var splitterSize = Owner?.SplitterSize ?? 6.0;
        if (Owner?.Orientation == Orientation.Horizontal)
            return new Size(splitterSize, availableSize.Height);
        else
            return new Size(availableSize.Width, splitterSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    protected override void OnRender(object drawingContextObj)
    {
        if (drawingContextObj is not DrawingContext dc)
        {
            base.OnRender(drawingContextObj);
            return;
        }

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        var isVerticalBar = Owner?.Orientation == Orientation.Horizontal;

        Brush bgBrush;
        if (_isDragging)
            bgBrush = ResolveBrush("OneTabActiveBorder", "AccentBrush", s_fallbackDraggingBrush);
        else if (IsMouseOver)
            bgBrush = ResolveBrush("OneSurfaceHover", "DockSplitterHover", s_fallbackHoverBrush);
        else
            bgBrush = Background ?? ResolveBrush("OneBorderSubtle", "DockSplitterBackground", s_fallbackBackgroundBrush);

        // Fill the splitter interior first, keeping 1px edges for border continuity.
        Rect fillRect;
        if (isVerticalBar && ActualWidth > 2)
            fillRect = new Rect(1, 0, ActualWidth - 2, ActualHeight);
        else if (!isVerticalBar && ActualHeight > 2)
            fillRect = new Rect(0, 1, ActualWidth, ActualHeight - 2);
        else
            fillRect = bounds;

        dc.DrawRectangle(bgBrush, null, fillRect);

        // Draw subtle edge lines so DockTabPanel borders are not visually "covered" by the splitter.
        var edgeBrush = ResolveBrush("OneBorderDefault", "DockTabStripBorder", s_fallbackEdgeBrush);
        if (isVerticalBar)
        {
            dc.DrawRectangle(edgeBrush, null, new Rect(0, 0, 1, ActualHeight));
            dc.DrawRectangle(edgeBrush, null, new Rect(Math.Max(0, ActualWidth - 1), 0, 1, ActualHeight));
            // Keep top strip border visually continuous across splitter gap.
            dc.DrawRectangle(edgeBrush, null, new Rect(0, 0, ActualWidth, 1));
        }
        else
        {
            dc.DrawRectangle(edgeBrush, null, new Rect(0, 0, ActualWidth, 1));
            dc.DrawRectangle(edgeBrush, null, new Rect(0, Math.Max(0, ActualHeight - 1), ActualWidth, 1));
            dc.DrawRectangle(edgeBrush, null, new Rect(0, 0, 1, ActualHeight));
        }
    }

    private Brush ResolveBrush(string primaryKey, string secondaryKey, Brush fallback)
    {
        if (TryFindResource(primaryKey) is Brush primary)
            return primary;
        if (TryFindResource(secondaryKey) is Brush secondary)
            return secondary;
        return fallback;
    }
}
