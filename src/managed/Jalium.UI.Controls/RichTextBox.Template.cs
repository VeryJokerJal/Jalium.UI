using Jalium.UI.Media;

namespace Jalium.UI.Controls;

[TemplatePart(Name = "PART_TextView", Type = typeof(RichTextBoxView))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Focused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Unfocused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Editable", GroupName = "EditingStates")]
[TemplateVisualState(Name = "ReadOnly", GroupName = "EditingStates")]
public partial class RichTextBox
{
    private RichTextBoxView? _documentTextView;

    static RichTextBox() => DefaultStyleKeyProperty.OverrideMetadata(typeof(RichTextBox), new PropertyMetadata(typeof(RichTextBox)));

    private bool ShowsDocument => Template is null || _documentTextView is not null || HasContentHost;

    public override void OnApplyTemplate()
    {
        ReleaseDocumentTextView();
        base.OnApplyTemplate();
        _documentTextView = GetTemplateChild("PART_TextView") as RichTextBoxView;
        _documentTextView?.SetCurrentValue(RichTextBoxView.OwnerProperty, this);
        UpdateDocumentPresentation();
    }

    protected override void OnTemplateChanging()
    {
        ReleaseDocumentTextView();
        base.OnTemplateChanging();
        // A null template has no subsequent OnApplyTemplate callback. Preserve
        // the unthemed document surface in that case, including its AX geometry.
        UpdateDocumentHostParent();
        if (_cssDocumentHost is not null)
            _cssDocumentHost.Visibility = Template is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReleaseDocumentTextView()
    {
        if (_documentTextView is { } view && ReferenceEquals(view.Owner, this))
        {
            view.SetDocumentHost(null);
            view.ClearBinding(RichTextBoxView.OwnerProperty);
            view.SetCurrentValue(RichTextBoxView.OwnerProperty, null);
        }
        _documentTextView = null;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        _documentTextView?.InvalidateVisual();
        if (e.Property == FontSizeProperty || e.Property == FontFamilyProperty || e.Property == FontWeightProperty ||
            e.Property == FontStyleProperty || e.Property == PaddingProperty || e.Property == BorderThicknessProperty)
            _documentTextView?.InvalidateMeasure();
        if (e.Property == IsEnabledProperty || e.Property == IsKeyboardFocusedProperty || e.Property == IsReadOnlyProperty)
            UpdateDocumentPresentation();
    }

    private void UpdateDocumentPresentation()
    {
        UpdateDocumentHostParent();
        if (_cssDocumentHost is not null)
            _cssDocumentHost.Visibility = ShowsDocument ? Visibility.Visible : Visibility.Collapsed;
        if (TemplateRootInternal is null) return;
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, IsKeyboardFocused ? "Focused" : "Unfocused", false);
        VisualStateManager.GoToState(this, IsReadOnly ? "ReadOnly" : "Editable", false);
    }

    private void DetachDocumentHost()
    {
        if (_cssDocumentHost is not { } host) return;
        if (ReferenceEquals(host.VisualParent, this)) RemoveVisualChild(host);
        else if (host.VisualParent is RichTextBoxView view) view.SetDocumentHost(null);
    }

    private void UpdateDocumentHostParent()
    {
        if (_cssDocumentHost is not { } host) return;
        // CSS text is intrinsic document content, so a template must own its
        // placement and transforms just as it owns the non-CSS text surface.
        Visual? parent = ShowsDocument ? _documentTextView ?? (Visual)this : null;
        if (ReferenceEquals(host.VisualParent, parent)) return;
        DetachDocumentHost();
        if (parent is RichTextBoxView view) view.SetDocumentHost(host);
        else if (ReferenceEquals(parent, this)) AddVisualChild(host);
    }

    internal Size MeasureDocumentTextView(Size availableSize)
    {
        var horizontal = BorderThickness.Left + BorderThickness.Right + Padding.Left + Padding.Right;
        var vertical = BorderThickness.Top + BorderThickness.Bottom + Padding.Top + Padding.Bottom;
        var width = double.IsFinite(availableSize.Width) ? Math.Max(1, availableSize.Width - horizontal) : 816;
        var layout = EnsureLayout(width);
        var desiredWidth = Math.Max(0, (layout?.TotalWidth ?? 0) + horizontal);
        var desiredHeight = Math.Max(0, Math.Max(GetDefaultLineHeight(), layout?.TotalHeight ?? 0) + vertical);
        return new Size(double.IsFinite(availableSize.Width) ? Math.Min(availableSize.Width, desiredWidth) : desiredWidth,
            double.IsFinite(availableSize.Height) ? Math.Min(availableSize.Height, desiredHeight) : desiredHeight);
    }
}
