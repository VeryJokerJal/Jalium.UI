namespace Jalium.UI.Controls;

/// <summary>
/// Base class for content controls.
/// </summary>
public class ContentControl : Control
{
    private UIElement? _contentElement;

    /// <summary>
    /// Identifies the Content dependency property.
    /// </summary>
    public static readonly DependencyProperty ContentProperty =
        DependencyProperty.Register(nameof(Content), typeof(object), typeof(ContentControl),
            new PropertyMetadata(null, OnContentChanged));

    /// <summary>
    /// Identifies the ContentTemplate dependency property.
    /// </summary>
    public static readonly DependencyProperty ContentTemplateProperty =
        DependencyProperty.Register(nameof(ContentTemplate), typeof(DataTemplate), typeof(ContentControl),
            new PropertyMetadata(null, OnContentTemplateChanged));

    /// <summary>
    /// Gets or sets the content of this control.
    /// </summary>
    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>
    /// Gets or sets the template used to display the content.
    /// </summary>
    public DataTemplate? ContentTemplate
    {
        get => (DataTemplate?)GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ContentControl control)
        {
            control.OnContentChanged(e.OldValue, e.NewValue);
        }
    }

    private static void OnContentTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ContentControl control)
        {
            // Re-apply content with new template
            control.OnContentChanged(null, control.Content);
        }
    }

    /// <summary>
    /// Called when the Content property changes.
    /// </summary>
    protected virtual void OnContentChanged(object? oldContent, object? newContent)
    {
        // If we have a template, the ContentPresenter in the template will handle content display
        // via template bindings. We only need to handle direct content if no template is applied.
        if (Template != null)
        {
            // Template's ContentPresenter will pick up the change via binding
            InvalidateMeasure();
            return;
        }

        // No template - manage content element directly
        // Remove old content from visual tree
        if (_contentElement != null)
        {
            RemoveVisualChild(_contentElement);
            _contentElement = null;
        }

        // Add new content to visual tree if it's a UIElement
        if (newContent is UIElement newElement)
        {
            _contentElement = newElement;
            AddVisualChild(newElement);
        }

        InvalidateMeasure();
    }

    /// <summary>
    /// Gets the content element if it's a UIElement.
    /// </summary>
    protected UIElement? ContentElement => _contentElement;

    /// <summary>
    /// Gets whether this control has a template applied.
    /// </summary>
    private bool HasTemplate => Template != null;

    /// <inheritdoc />
    public override int VisualChildrenCount
    {
        get
        {
            // When using templates, delegate to base class (Control) which handles template root
            if (HasTemplate)
                return base.VisualChildrenCount;

            // No template - use direct content element
            return _contentElement != null ? 1 : 0;
        }
    }

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        // When using templates, delegate to base class (Control) which handles template root
        if (HasTemplate)
            return base.GetVisualChild(index);

        // No template - use direct content element
        if (index == 0 && _contentElement != null)
            return _contentElement;
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // When using templates, let base class handle it (which measures template root)
        if (HasTemplate)
            return base.MeasureOverride(availableSize);

        // No template - measure content element directly
        if (_contentElement != null)
        {
            var padding = Padding;
            var border = BorderThickness;
            var contentAvailable = new Size(
                Math.Max(0, availableSize.Width - padding.TotalWidth - border.TotalWidth),
                Math.Max(0, availableSize.Height - padding.TotalHeight - border.TotalHeight));

            _contentElement.Measure(contentAvailable);

            return new Size(
                _contentElement.DesiredSize.Width + padding.TotalWidth + border.TotalWidth,
                _contentElement.DesiredSize.Height + padding.TotalHeight + border.TotalHeight);
        }

        return base.MeasureOverride(availableSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // When using templates, let base class handle it (which arranges template root)
        if (HasTemplate)
            return base.ArrangeOverride(finalSize);

        // No template - arrange content element directly
        if (_contentElement is FrameworkElement fe)
        {
            var padding = Padding;
            var border = BorderThickness;

            var contentRect = new Rect(
                padding.Left + border.Left,
                padding.Top + border.Top,
                Math.Max(0, finalSize.Width - padding.TotalWidth - border.TotalWidth),
                Math.Max(0, finalSize.Height - padding.TotalHeight - border.TotalHeight));

            fe.Arrange(contentRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        return finalSize;
    }
}
