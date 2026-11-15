using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Template-created document surface for RichTextBox. The owner retains document,
/// selection, editing, IME and automation state; the surface uses the owner's coordinates.
/// </summary>
public sealed class RichTextBoxView : FrameworkElement
{
    public RichTextBoxView()
    {
        Focusable = false;
    }

    private FrameworkElement? _documentHost;

    internal void SetDocumentHost(FrameworkElement? host)
    {
        if (ReferenceEquals(_documentHost, host)) return;
        var previous = _documentHost;
        // RemoveVisualChild refreshes the public child-count compatibility
        // field from this override. Update the slot before it takes that snapshot.
        _documentHost = null;
        if (previous is not null) RemoveVisualChild(previous);
        _documentHost = host;
        if (host is not null) AddVisualChild(host);
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override int VisualChildrenCount => _documentHost is null ? 0 : 1;
    protected override Visual? GetVisualChild(int index) => index == 0 && _documentHost is not null
        ? _documentHost : throw new ArgumentOutOfRangeException(nameof(index));

    internal override Geometry? GetAdditionalChildLayoutClip(Visual child) => ReferenceEquals(child, _documentHost) && Owner is { } owner
        ? new RectangleGeometry(owner.GetContentBounds()) : base.GetAdditionalChildLayoutClip(child);

    public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register(
        nameof(Owner), typeof(RichTextBox), typeof(RichTextBoxView), new PropertyMetadata(null, (d, _) =>
        {
            var view = (RichTextBoxView)d;
            view.SetDocumentHost(null);
            view.InvalidateMeasure();
            view.InvalidateVisual();
        }));

    public RichTextBox? Owner
    {
        get => (RichTextBox?)GetValue(OwnerProperty);
        set => SetValue(OwnerProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => Owner?.MeasureDocumentTextView(availableSize) ?? default;
    protected override void OnRender(DrawingContext drawingContext) => Owner?.RenderDocumentTextView(drawingContext);
    // Document, caret and selection state belong to the owner. Match the mature
    // TextBoxContentHost proxy so cached commands cannot retain an earlier edit.
    protected override bool ParticipatesInRenderCache => false;
}
