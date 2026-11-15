using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

[TemplatePart(Name = "PART_TextView", Type = typeof(HexEditorTextView))]
[TemplatePart(Name = "PART_VerticalScrollBar", Type = typeof(ScrollBar))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Focused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Unfocused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Editable", GroupName = "EditingStates")]
[TemplateVisualState(Name = "ReadOnly", GroupName = "EditingStates")]
[TemplateVisualState(Name = "Empty", GroupName = "DataStates")]
[TemplateVisualState(Name = "HasData", GroupName = "DataStates")]
public partial class HexEditor
{
    private HexEditorTextView? _textView;
    private Brush[]? _viewBrushes;

    private Size EditorAreaSize => Template is not null && _textView is { } view
        ? new Size(view.RenderSize.Width + ScrollBarWidth, view.RenderSize.Height)
        : RenderSize;

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _textView = GetTemplateChild("PART_TextView") as HexEditorTextView;
        _verticalScrollBar = GetTemplateChild("PART_VerticalScrollBar") as ScrollBar;
        if (_verticalScrollBar is { } bar) bar.Scroll += OnScrollBarScroll;
        RefreshViewBrushOwners();
        UpdateTemplateStates();
    }

    internal override void OnTemplateContentClearing()
    {
        ClearViewBrushOwners();
        if (_verticalScrollBar is { } bar) bar.Scroll -= OnScrollBarScroll;
        _verticalScrollBar = null;
        _textView = null;
        _visibleRowCount = 0;
        _rowCache.Clear();
        CancelTemplateInteraction();
        base.OnTemplateContentClearing();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        UpdateDefaultStyle();
        return Template is not null ? base.MeasureOverride(availableSize) : MeasureEditorArea(availableSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
        => Template is not null ? base.ArrangeOverride(finalSize) : ArrangeEditorArea(finalSize);

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Template is not null) base.OnRender(drawingContext);
        else RenderEditorArea(drawingContext);
    }

    /// <inheritdoc />
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        if (!newValue) CancelTemplateInteraction();
        UpdateTemplateStates();
        InvalidateEditorVisual();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (typeof(Brush).IsAssignableFrom(e.Property.PropertyType))
            RefreshViewBrushOwners();
        if (e.Property == DataProperty || e.Property == IsReadOnlyProperty)
            UpdateTemplateStates();
        if (e.Property == DataProperty || e.Property == FontFamilyProperty || e.Property == FontSizeProperty ||
            e.Property == BytesPerRowProperty || e.Property == ColumnGroupSizeProperty ||
            e.Property == ShowOffsetColumnProperty || e.Property == ShowAsciiColumnProperty ||
            e.Property == ShowDataInterpretationProperty || e.Property == OffsetFormatProperty)
        {
            _textView?.InvalidateMeasure();
            InvalidateMeasure();
        }
        _textView?.InvalidateVisual();
    }

    private void RefreshViewBrushOwners()
    {
        if (_textView is not { } view) return;
        var brushes = new[]
        {
            ResolveOffsetForeground(), ResolveHexForeground(), ResolveAsciiForeground(),
            ResolveModifiedByteBrush(), ResolveSelectionBrush(), ResolveGutterBackground(),
            ResolveColumnSeparatorBrush()
        }.Where(brush => !brush.IsFrozen).Distinct(ReferenceEqualityComparer.Instance).Cast<Brush>().ToArray();
        if (_viewBrushes is { } previous && previous.SequenceEqual(brushes)) return;
        ClearViewBrushOwners();
        _viewBrushes = brushes;
        foreach (var brush in brushes) brush.AddRenderOwner(view);
    }

    private void ClearViewBrushOwners()
    {
        if (_textView is { } view && _viewBrushes is { } brushes)
            foreach (var brush in brushes) brush.RemoveRenderOwner(view);
        _viewBrushes = null;
    }

    private void CancelTemplateInteraction()
    {
        _isDragging = false;
        _wheelRowRemainder = 0;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private void UpdateTemplateStates()
    {
        if (TemplateRootInternal is null) return;
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, IsKeyboardFocused ? "Focused" : "Unfocused", false);
        VisualStateManager.GoToState(this, IsReadOnly ? "ReadOnly" : "Editable", false);
        VisualStateManager.GoToState(this, Data is { Length: > 0 } ? "HasData" : "Empty", false);
    }

    private void InvalidateEditorVisual()
    {
        InvalidateVisual();
        _textView?.InvalidateVisual();
    }

    internal bool OwnsTextView(HexEditorTextView view) => ReferenceEquals(_textView, view);

    internal Size MeasureTextView(Size availableSize)
    {
        var desired = MeasureEditorArea(new Size(availableSize.Width + ScrollBarWidth, availableSize.Height));
        return new Size(Math.Max(0, desired.Width - ScrollBarWidth), desired.Height);
    }

    internal Size ArrangeTextView(Size finalSize)
    {
        ArrangeEditorArea(new Size(finalSize.Width + ScrollBarWidth, finalSize.Height));
        return finalSize;
    }

    internal void RenderTextView(DrawingContext drawingContext) => RenderEditorArea(drawingContext);
}
