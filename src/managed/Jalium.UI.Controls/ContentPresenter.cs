using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Displays the content of a ContentControl.
/// </summary>
public class ContentPresenter : FrameworkElement
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Content dependency property.
    /// </summary>
    public static readonly DependencyProperty ContentProperty =
        DependencyProperty.Register(nameof(Content), typeof(object), typeof(ContentPresenter),
            new PropertyMetadata(null, OnContentChanged));

    /// <summary>
    /// Identifies the ContentTemplate dependency property.
    /// </summary>
    public static readonly DependencyProperty ContentTemplateProperty =
        DependencyProperty.Register(nameof(ContentTemplate), typeof(DataTemplate), typeof(ContentPresenter),
            new PropertyMetadata(null, OnContentTemplateChanged));

    /// <summary>
    /// Identifies the ContentSource dependency property.
    /// </summary>
    public static readonly DependencyProperty ContentSourceProperty =
        DependencyProperty.Register(nameof(ContentSource), typeof(string), typeof(ContentPresenter),
            new PropertyMetadata("Content"));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the content to display.
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

    /// <summary>
    /// Gets or sets the name of the property on the templated parent to use as content.
    /// </summary>
    public string ContentSource
    {
        get => (string)(GetValue(ContentSourceProperty) ?? "Content");
        set => SetValue(ContentSourceProperty, value);
    }

    #endregion

    #region Fields

    private FrameworkElement? _contentElement;
    private bool _templateBindingsApplied;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentPresenter"/> class.
    /// </summary>
    public ContentPresenter()
    {
    }

    #endregion

    #region Content Changed

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ContentPresenter presenter)
        {
            presenter.OnContentChanged(e.OldValue, e.NewValue);
        }
    }

    private void OnContentChanged(object? oldContent, object? newContent)
    {
        // Remove old content element
        if (_contentElement != null)
        {
            RemoveVisualChild(_contentElement);
            _contentElement = null;
        }

        // Add new content
        if (newContent != null)
        {
            _contentElement = CreateContentElement(newContent);
            if (_contentElement != null)
            {
                AddVisualChild(_contentElement);
            }
        }

        InvalidateMeasure();
    }

    private static void OnContentTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ContentPresenter presenter)
        {
            // Re-create content with new template
            presenter.OnContentChanged(presenter.Content, presenter.Content);
        }
    }

    private FrameworkElement? CreateContentElement(object content)
    {
        // If content is already a UIElement, use it directly
        if (content is FrameworkElement fe)
        {
            return fe;
        }

        // If we have a template, use it
        if (ContentTemplate != null)
        {
            var templateContent = ContentTemplate.LoadContent();
            if (templateContent != null)
            {
                templateContent.DataContext = content;
                return templateContent;
            }
        }

        // Default: create a TextBlock for string content
        if (content is string text)
        {
            return new TextBlock { Text = text };
        }

        // For other objects, use ToString()
        return new TextBlock { Text = content.ToString() ?? string.Empty };
    }

    #endregion

    #region Template Binding

    /// <inheritdoc />
    protected override void OnTemplatedParentChanged(FrameworkElement? oldParent, FrameworkElement? newParent)
    {
        base.OnTemplatedParentChanged(oldParent, newParent);

        // Reset binding state when templated parent changes
        _templateBindingsApplied = false;

        // When templated parent is set, apply template bindings
        if (newParent != null)
        {
            ApplyTemplateBindings();
        }
    }

    private void ApplyTemplateBindings()
    {
        if (TemplatedParent == null)
            return;

        _templateBindingsApplied = true;

        // Get the content source property name
        var contentSource = ContentSource;
        if (string.IsNullOrEmpty(contentSource))
            return;

        // Find the property on the templated parent
        var parentType = TemplatedParent.GetType();

        // Try to find Content property
        var contentPropInfo = parentType.GetProperty(contentSource);
        if (contentPropInfo != null)
        {
            // Get the DependencyProperty
            var dpField = parentType.GetField($"{contentSource}Property",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy);
            if (dpField?.GetValue(null) is DependencyProperty contentDp)
            {
                this.SetTemplateBinding(ContentProperty, contentDp);
            }
        }

        // Try to find ContentTemplate property
        var templatePropInfo = parentType.GetProperty($"{contentSource}Template");
        if (templatePropInfo != null)
        {
            var dpField = parentType.GetField($"{contentSource}TemplateProperty",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy);
            if (dpField?.GetValue(null) is DependencyProperty templateDp)
            {
                this.SetTemplateBinding(ContentTemplateProperty, templateDp);
            }
        }
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    public override int VisualChildrenCount => _contentElement != null ? 1 : 0;

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        if (index != 0 || _contentElement == null)
            throw new ArgumentOutOfRangeException(nameof(index));

        return _contentElement;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_contentElement == null)
            return Size.Empty;

        _contentElement.Measure(availableSize);
        return _contentElement.DesiredSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_contentElement != null)
        {
            var contentDesired = _contentElement.DesiredSize;

            // Calculate content size - for Stretch use full size, otherwise use desired size
            double contentWidth = HorizontalAlignment == HorizontalAlignment.Stretch
                ? finalSize.Width
                : Math.Min(contentDesired.Width, finalSize.Width);
            double contentHeight = VerticalAlignment == VerticalAlignment.Stretch
                ? finalSize.Height
                : Math.Min(contentDesired.Height, finalSize.Height);

            // Arrange content at (0, 0) - alignment positioning is handled by ArrangeCore
            _contentElement.Arrange(new Rect(0, 0, contentWidth, contentHeight));

            // Return actual render size so ArrangeCore can apply alignment
            return new Size(contentWidth, contentHeight);
        }

        return finalSize;
    }

    #endregion
}

/// <summary>
/// Defines a template for data display.
/// </summary>
public class DataTemplate
{
    private Func<FrameworkElement>? _visualTree;
    private bool _isSealed;

    /// <summary>
    /// Gets or sets the type of data for which this template is intended.
    /// </summary>
    public Type? DataType { get; set; }

    /// <summary>
    /// Gets a value indicating whether this template is read-only.
    /// </summary>
    public bool IsSealed => _isSealed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataTemplate"/> class.
    /// </summary>
    public DataTemplate()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DataTemplate"/> class with the specified data type.
    /// </summary>
    /// <param name="dataType">The type of data for which this template is intended.</param>
    public DataTemplate(Type dataType)
    {
        DataType = dataType;
    }

    /// <summary>
    /// Sets the visual tree factory for this template.
    /// </summary>
    /// <param name="visualTreeFactory">A function that creates the visual tree.</param>
    public void SetVisualTree(Func<FrameworkElement> visualTreeFactory)
    {
        if (_isSealed)
            throw new InvalidOperationException("Cannot modify a sealed DataTemplate.");

        _visualTree = visualTreeFactory;
    }

    /// <summary>
    /// Seals the template so that it can no longer be modified.
    /// </summary>
    public void Seal()
    {
        _isSealed = true;
    }

    /// <summary>
    /// Creates the visual tree defined by this template.
    /// </summary>
    /// <returns>The root element of the visual tree.</returns>
    public FrameworkElement? LoadContent()
    {
        return _visualTree?.Invoke();
    }
}
