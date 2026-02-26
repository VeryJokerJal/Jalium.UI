using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Internal element that renders text content for TextBoxBase controls.
/// This element is inserted into the PART_ContentHost of the control's template.
/// Similar to WPF's TextBoxView.
/// </summary>
internal sealed class TextBoxContentHost : FrameworkElement
{
    private readonly TextBoxBase _owner;

    /// <summary>
    /// Initializes a new instance of the TextBoxContentHost class.
    /// </summary>
    /// <param name="owner">The owning TextBoxBase control.</param>
    public TextBoxContentHost(TextBoxBase owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

        // This element should be hit-testable so clicks are routed through it
        IsHitTestVisible = true;
    }

    /// <summary>
    /// Gets the owning TextBoxBase control.
    /// </summary>
    public TextBoxBase Owner => _owner;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // Delegate measurement to the owner
        return _owner.MeasureTextContent(availableSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Notify owner of content area size
        _owner.ArrangeTextContent(finalSize);
        return finalSize;
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        // Delegate rendering to the owner
        _owner.RenderTextContent(drawingContext);
    }
}
