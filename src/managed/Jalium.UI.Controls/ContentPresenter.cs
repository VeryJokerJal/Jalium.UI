using System.Diagnostics.CodeAnalysis;
using Jalium.UI.Data;
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
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ContentProperty =
        DependencyProperty.Register(nameof(Content), typeof(object), typeof(ContentPresenter),
            new PropertyMetadata(null, OnContentChanged));

    /// <summary>
    /// Identifies the ContentTemplate dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ContentTemplateProperty =
        DependencyProperty.Register(nameof(ContentTemplate), typeof(DataTemplate), typeof(ContentPresenter),
            new PropertyMetadata(null, OnContentTemplateChanged));

    /// <summary>
    /// Identifies the ContentTemplateSelector dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ContentTemplateSelectorProperty =
        DependencyProperty.Register(nameof(ContentTemplateSelector), typeof(DataTemplateSelector), typeof(ContentPresenter),
            new PropertyMetadata(null, OnContentTemplateSelectorChanged));

    /// <summary>
    /// Identifies the ContentSource dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ContentSourceProperty =
        DependencyProperty.Register(nameof(ContentSource), typeof(string), typeof(ContentPresenter),
            new PropertyMetadata("Content"));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the content to display.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>
    /// Gets or sets the template used to display the content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public DataTemplate? ContentTemplate
    {
        get => (DataTemplate?)GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    /// <summary>
    /// Gets or sets the DataTemplateSelector used to choose a template for the content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public DataTemplateSelector? ContentTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(ContentTemplateSelectorProperty);
        set => SetValue(ContentTemplateSelectorProperty, value);
    }

    /// <summary>
    /// Gets or sets the name of the property on the templated parent to use as content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string ContentSource
    {
        get => (string)(GetValue(ContentSourceProperty) ?? "Content");
        set => SetValue(ContentSourceProperty, value);
    }

    #endregion

    #region Fields

    private FrameworkElement? _contentElement;
    private bool _templateBindingsApplied;

    // The DataTemplate (if any) that produced the current _contentElement. When a recycled
    // container is rebound to a new item under the SAME template, the existing subtree is
    // reused (DataContext swap) instead of rebuilt — see OnContentChanged.
    private DataTemplate? _generatedFromTemplate;

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

    private void OnContentChanged(object? oldContent, object? newContent, bool forceRecreate = false)
    {
        // Guard: skip if content reference is the same and not forced (template change)
        if (!forceRecreate && ReferenceEquals(oldContent, newContent) && _contentElement != null)
            return;

        // Recycling fast path: when the current content element was materialized from an
        // explicit ContentTemplate (a data item shown via DataContext binding) and that same
        // template is still in effect, reuse the existing visual subtree and only re-point its
        // DataContext to the new item — instead of tearing the tree down and rebuilding it via
        // DataTemplate.LoadContent(). This is what makes VirtualizationMode.Recycling actually
        // pay off: a recycled ItemsControl container keeps its realized row visuals and just
        // rebinds, avoiding a full per-row template instantiation (Border/Grid/TextBlocks/
        // Bindings) plus the cold text re-measure that otherwise dominate the layout pass while
        // scrolling a large virtualized list. Scoped to the explicit-ContentTemplate case (no
        // selector) so a per-item template selector is never blindly rebound to the wrong tree.
        if (!forceRecreate
            && _contentElement != null
            && _generatedFromTemplate != null
            && ContentTemplateSelector == null
            && ReferenceEquals(_generatedFromTemplate, ContentTemplate)
            && newContent != null
            && newContent is not FrameworkElement)
        {
            _contentElement.DataContext = newContent;
            // New data → desired size may differ → the reused subtree must re-measure.
            InvalidateMeasure();
            return;
        }

        // Remove old content element
        if (_contentElement != null)
        {
            RemoveVisualChild(_contentElement);
            _contentElement = null;
            _generatedFromTemplate = null;
        }

        // Add new content
        if (newContent != null)
        {
            _contentElement = CreateContentElement(newContent);
            if (_contentElement != null && _contentElement.VisualParent == null)
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
            // Force re-create content with new template
            presenter.OnContentChanged(presenter.Content, presenter.Content, forceRecreate: true);
        }
    }

    private static void OnContentTemplateSelectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ContentPresenter presenter)
        {
            // Force re-create content with new selector
            presenter.OnContentChanged(presenter.Content, presenter.Content, forceRecreate: true);
        }
    }

    private FrameworkElement? CreateContentElement(object content)
    {
        // Track which DataTemplate (if any) produced the element, so OnContentChanged can later
        // reuse the subtree by rebinding DataContext when that same template still applies.
        // Only the explicit-ContentTemplate path sets this; every other path leaves it null so
        // the reuse fast path stays off for selector/implicit/string content.
        _generatedFromTemplate = null;

        // If content is already a UIElement, use it directly.
        // With shared properties (TextElement.ForegroundProperty via AddOwner),
        // TextBlock inherits Foreground from ancestor Controls natively.
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
                _generatedFromTemplate = ContentTemplate;
                return templateContent;
            }
        }

        // If we have a template selector, use it
        if (ContentTemplateSelector != null)
        {
            var selectedTemplate = ContentTemplateSelector.SelectTemplate(content, this);
            if (selectedTemplate != null)
            {
                var templateContent = selectedTemplate.LoadContent();
                if (templateContent != null)
                {
                    templateContent.DataContext = content;
                    return templateContent;
                }
            }
        }

        // Try implicit DataTemplate lookup (matching by DataType in resources)
        if (content is not string)
        {
            if (ResourceLookup.FindImplicitDataTemplate(this, content.GetType()) is DataTemplate implicitTemplate)
            {
                var templateContent = implicitTemplate.LoadContent();
                if (templateContent != null)
                {
                    templateContent.DataContext = content;
                    return templateContent;
                }
            }
        }

        // Find Foreground from TemplatedParent or visual parent chain.
        // Even though TextBlock now shares ForegroundProperty with Control via AddOwner,
        // we still set it explicitly here because the TextBlock is created detached
        // and won't inherit from the visual tree until it's added as a child.
        var foreground = FindForegroundBrush();

        // Default: create a TextBlock for string content
        if (content is string text)
        {
            var tb = new TextBlock { Text = text };
            ApplyTextBlockFormatting(tb, foreground);
            return tb;
        }

        // For other objects, use ToString()
        var otherTb = new TextBlock { Text = content.ToString() ?? string.Empty };
        ApplyTextBlockFormatting(otherTb, foreground);
        return otherTb;
    }

    /// <summary>
    /// Finds the Foreground brush from the TemplatedParent or visual parent chain.
    /// </summary>
    private Brush? FindForegroundBrush()
    {
        // First check TemplatedParent (most common case when inside a ControlTemplate)
        if (TemplatedParent is Control templatedControl)
            return templatedControl.Foreground;

        // Fall back to walking the visual parent chain
        Visual? current = VisualParent;
        while (current != null)
        {
            if (current is Control control)
                return control.Foreground;
            current = current.VisualParent;
        }

        return null;
    }

    #endregion

    #region Template Binding

    /// <inheritdoc />
    protected override void OnTemplatedParentChanged(FrameworkElement? oldParent, FrameworkElement? newParent)
    {
        base.OnTemplatedParentChanged(oldParent, newParent);

        // Unsubscribe from old parent's property changes
        if (oldParent != null)
        {
            oldParent.PropertyChangedInternal -= OnTemplatedParentPropertyChanged;
        }

        // Subscribe to new parent's property changes (for Foreground propagation)
        if (newParent != null)
        {
            newParent.PropertyChangedInternal += OnTemplatedParentPropertyChanged;
        }

        // When TemplatedParent is set, apply template bindings
        if (!_templateBindingsApplied && newParent != null)
        {
            ApplyTemplateBindings();
        }

        if (_contentElement is TextBlock textBlock)
        {
            ApplyTextBlockFormatting(textBlock, FindForegroundBrush());
        }
    }

    private void OnTemplatedParentPropertyChanged(DependencyProperty dp, object? oldValue, object? newValue)
    {
        if (_contentElement is not TextBlock tb)
        {
            return;
        }

        if (dp == Control.ForegroundProperty ||
            dp == Control.FontFamilyProperty ||
            dp == Control.FontSizeProperty ||
            dp == Control.FontStyleProperty ||
            dp == Control.FontWeightProperty)
        {
            ApplyTextBlockFormatting(tb, FindForegroundBrush());
        }
    }

    private void ApplyTextBlockFormatting(TextBlock textBlock, Brush? foreground)
    {
        if (foreground != null)
        {
            textBlock.Foreground = foreground;
        }

        if (TemplatedParent is Control templatedControl)
        {
            textBlock.FontFamily = templatedControl.FontFamily;
            textBlock.FontSize = templatedControl.FontSize;
            textBlock.FontStyle = templatedControl.FontStyle;
            textBlock.FontWeight = templatedControl.FontWeight;
            return;
        }

        Visual? current = VisualParent;
        while (current != null)
        {
            if (current is Control control)
            {
                textBlock.FontFamily = control.FontFamily;
                textBlock.FontSize = control.FontSize;
                textBlock.FontStyle = control.FontStyle;
                textBlock.FontWeight = control.FontWeight;
                return;
            }

            current = current.VisualParent;
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

        // Use the AOT-safe DependencyProperty registry instead of GetField reflection.
        if (!HasLocalValue(ContentProperty) && GetBindingExpression(ContentProperty) == null)
        {
            var contentDp = DependencyProperty.FromName(parentType, contentSource);
            if (contentDp != null)
            {
                this.SetTemplateBinding(ContentProperty, contentDp);
            }
        }

        if (!HasLocalValue(ContentTemplateProperty) && GetBindingExpression(ContentTemplateProperty) == null)
        {
            var templateDp = DependencyProperty.FromName(parentType, $"{contentSource}Template");
            if (templateDp != null)
                this.SetTemplateBinding(ContentTemplateProperty, templateDp);
        }

        if (!HasLocalValue(ContentTemplateSelectorProperty) && GetBindingExpression(ContentTemplateSelectorProperty) == null)
        {
            var selectorDp = DependencyProperty.FromName(parentType, $"{contentSource}TemplateSelector");
            if (selectorDp != null)
                this.SetTemplateBinding(ContentTemplateSelectorProperty, selectorDp);
        }

        // Bind HorizontalContentAlignment -> HorizontalAlignment
        if (!HasLocalValue(HorizontalAlignmentProperty) && GetBindingExpression(HorizontalAlignmentProperty) == null)
        {
            var hcaDp = DependencyProperty.FromName(parentType, "HorizontalContentAlignment");
            if (hcaDp != null)
                this.SetTemplateBinding(HorizontalAlignmentProperty, hcaDp);
        }

        // Bind VerticalContentAlignment -> VerticalAlignment
        if (!HasLocalValue(VerticalAlignmentProperty) && GetBindingExpression(VerticalAlignmentProperty) == null)
        {
            var vcaDp = DependencyProperty.FromName(parentType, "VerticalContentAlignment");
            if (vcaDp != null)
                this.SetTemplateBinding(VerticalAlignmentProperty, vcaDp);
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
            _contentElement.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        return finalSize;
    }

    #endregion
}
