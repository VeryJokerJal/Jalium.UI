using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

using static Jalium.UI.Input.Cursors;

namespace Jalium.UI.Controls;

/// <summary>
/// Internal splitter bar used between panes in a <see cref="DockSplitPanel"/>.
/// </summary>
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "PointerOver", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Pressed", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public sealed class Split : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Automation.Peers.GenericAutomationPeer(this, Jalium.UI.Automation.Peers.AutomationControlType.Pane);

    /// <summary>Identifies the pointer-over brush used by the default template.</summary>
    public static readonly DependencyProperty HoverBrushProperty = DependencyProperty.Register(nameof(HoverBrush), typeof(Brush), typeof(Split), new PropertyMetadata(null));
    /// <summary>Identifies the brush used while resizing panes.</summary>
    public static readonly DependencyProperty DraggingBrushProperty = DependencyProperty.Register(nameof(DraggingBrush), typeof(Brush), typeof(Split), new PropertyMetadata(null));
    private static readonly DependencyPropertyKey IsDraggingPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsDragging), typeof(bool), typeof(Split), new PropertyMetadata(false));
    /// <summary>Identifies the read-only drag state.</summary>
    public static readonly DependencyProperty IsDraggingProperty = IsDraggingPropertyKey.DependencyProperty;
    /// <summary>Gets or sets the pointer-over brush.</summary>
    public Brush? HoverBrush { get => (Brush?)GetValue(HoverBrushProperty); set => SetValue(HoverBrushProperty, value); }
    /// <summary>Gets or sets the active drag brush.</summary>
    public Brush? DraggingBrush { get => (Brush?)GetValue(DraggingBrushProperty); set => SetValue(DraggingBrushProperty, value); }
    /// <summary>Gets whether this splitter is resizing its owner panes.</summary>
    public bool IsDragging => (bool)GetValue(IsDraggingProperty)!;

    internal DockSplitPanel? Owner { get; set; }
    internal int PaneIndex1 { get; set; }
    internal int PaneIndex2 { get; set; }

    private Point _dragStartPoint; // In Owner's coordinate space
    private double _originalSize1;
    private double _originalSize2;

    /// <summary>Initializes a reusable splitter with template-owned chrome.</summary>
    public Split()
    {
        DefaultStyleKey = typeof(Split);
        Focusable = false;

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            // Use Owner coordinate space so the reference frame doesn't move with the splitter
            StartDrag(e.GetPosition(Owner ?? (UIElement)this));
            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (IsDragging)
                FinishDrag();
            e.Handled = true;
        }
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (IsDragging)
        {
            UpdateDrag(e.GetPosition(Owner ?? (UIElement)this));
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        if (IsDragging)
            FinishDrag();
    }

    private void StartDrag(Point positionInOwner)
    {
        if (Owner == null) return;

        CaptureMouse();
        SetValue(IsDraggingPropertyKey, true);
        UpdateStates();
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
        SetValue(IsDraggingPropertyKey, false);
        UpdateStates();
        InvalidateVisual();
    }

    internal void UpdateCursorForOrientation()
    {
        if (Owner == null) return;
        Cursor = Owner.Orientation == Orientation.Horizontal ? SizeWE : SizeNS;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);
        var splitterSize = Owner?.SplitterSize ?? 6.0;
        if (Owner?.Orientation == Orientation.Horizontal)
            return new Size(splitterSize, availableSize.Height);
        else
            return new Size(availableSize.Width, splitterSize);
    }

    protected override Size ArrangeOverride(Size finalSize) => base.ArrangeOverride(finalSize);

    /// <inheritdoc />
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateStates(); }

    /// <inheritdoc />
    protected override void OnIsMouseOverChanged(bool oldValue, bool newValue)
    {
        base.OnIsMouseOverChanged(oldValue, newValue); UpdateStates();
    }

    /// <inheritdoc />
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        if (!newValue && IsDragging) FinishDrag(); UpdateStates();
    }

    private void UpdateStates() => VisualStateManager.GoToState(this,
        !IsEnabled ? "Disabled" : IsDragging ? "Pressed" : IsMouseOver ? "PointerOver" : "Normal", false);
}
