using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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

    /// <summary>Identifies the <see cref="ContentStringFormat"/> dependency property.</summary>
    public static readonly DependencyProperty ContentStringFormatProperty =
        DependencyProperty.Register(nameof(ContentStringFormat), typeof(string), typeof(ContentPresenter),
            new PropertyMetadata(null, OnContentStringFormatPropertyChanged));

    /// <summary>Identifies the <see cref="RecognizesAccessKey"/> dependency property.</summary>
    public static readonly DependencyProperty RecognizesAccessKeyProperty =
        DependencyProperty.Register(nameof(RecognizesAccessKey), typeof(bool), typeof(ContentPresenter),
            new PropertyMetadata(false, OnRecognizesAccessKeyChanged));

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

    /// <summary>Gets or sets the composite format string used for textual content.</summary>
    public string? ContentStringFormat
    {
        get => (string?)GetValue(ContentStringFormatProperty);
        set => SetValue(ContentStringFormatProperty, value);
    }

    /// <summary>Gets or sets whether underscore access-key markers are recognized.</summary>
    public bool RecognizesAccessKey
    {
        get => (bool)(GetValue(RecognizesAccessKeyProperty) ?? false);
        set => SetValue(RecognizesAccessKeyProperty, value);
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

    /// <summary>
    /// Releases the presented visual before the owning control discards this
    /// presenter's template tree.
    /// </summary>
    /// <remarks>
    /// A visual supplied through <see cref="ContentControl.Content"/> is owned by
    /// the control, not by one particular expansion of its control template. If
    /// the old presenter keeps that visual parented after its template root is
    /// detached, the replacement presenter can render the same instance through
    /// its cached field while the instance's <see cref="Visual.VisualParent"/>
    /// still points into the retired tree. Routed input and layout invalidation
    /// then stop at that detached root. Detaching here lets the next presenter
    /// establish the real parent chain.
    /// </remarks>
    internal void ReleaseContentElementForTemplateTeardown()
    {
        if (_contentElement == null)
            return;

        var element = _contentElement;
        _contentElement = null;
        _generatedFromTemplate = null;
        if (ReferenceEquals(element.VisualParent, this))
        {
            RemoveVisualChild(element);
        }
    }

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
            var element = _contentElement;
            _contentElement = null;
            _generatedFromTemplate = null;
            RemoveVisualChild(element);
        }

        // Add new content
        if (newContent != null)
        {
            _contentElement = CreateContentElement(newContent);
            if (_contentElement != null)
            {
                ReclaimContentFromRetiredTemplatePresenter(_contentElement);
                if (_contentElement.VisualParent == null)
                {
                    AddVisualChild(_contentElement);
                }
            }
        }

        InvalidateMeasure();
    }

    private void ReclaimContentFromRetiredTemplatePresenter(FrameworkElement contentElement)
    {
        if (contentElement.VisualParent is not ContentPresenter previousPresenter ||
            ReferenceEquals(previousPresenter, this) ||
            TemplatedParent is not Control owner ||
            !ReferenceEquals(previousPresenter.TemplatedParent, owner))
        {
            return;
        }

        // During template expansion Control assigns _templateRoot before it sets
        // TemplatedParent recursively. Therefore this presenter can distinguish
        // the current expansion from a presenter retained by the discarded
        // expansion of the same control. Only reclaim from that retired tree;
        // two live presenters in one template must not steal a UIElement from
        // each other.
        var currentRoot = owner.TemplateRootInternal;
        if (currentRoot == null || IsVisualDescendantOf(previousPresenter, currentRoot))
            return;

        previousPresenter.ReleaseSpecificContentElement(contentElement);
    }

    private void ReleaseSpecificContentElement(FrameworkElement contentElement)
    {
        if (!ReferenceEquals(_contentElement, contentElement) ||
            !ReferenceEquals(contentElement.VisualParent, this))
        {
            return;
        }

        _contentElement = null;
        _generatedFromTemplate = null;
        RemoveVisualChild(contentElement);
    }

    private static bool IsVisualDescendantOf(Visual descendant, Visual ancestor)
    {
        for (Visual? current = descendant; current != null; current = current.VisualParent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private static void OnContentTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ContentPresenter presenter)
        {
            var oldTemplate = (DataTemplate?)e.OldValue;
            var newTemplate = (DataTemplate?)e.NewValue;
            presenter.OnContentTemplateChanged(oldTemplate, newTemplate);
            presenter.OnTemplateChanged(oldTemplate, newTemplate);
            // Force re-create content with new template
            presenter.OnContentChanged(presenter.Content, presenter.Content, forceRecreate: true);
        }
    }

    private static void OnContentTemplateSelectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ContentPresenter presenter)
        {
            presenter.OnContentTemplateSelectorChanged(
                (DataTemplateSelector?)e.OldValue,
                (DataTemplateSelector?)e.NewValue);
            // Force re-create content with new selector
            presenter.OnContentChanged(presenter.Content, presenter.Content, forceRecreate: true);
        }
    }

    private static void OnContentStringFormatPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var presenter = (ContentPresenter)d;
        presenter.OnContentStringFormatChanged((string?)e.OldValue, (string?)e.NewValue);
        presenter.OnContentChanged(presenter.Content, presenter.Content, forceRecreate: true);
    }

    private static void OnRecognizesAccessKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var presenter = (ContentPresenter)d;
        presenter.OnContentChanged(presenter.Content, presenter.Content, forceRecreate: true);
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

        var template = ChooseTemplate();
        if (template != null)
        {
            var templateContent = template.LoadContent();
            if (templateContent != null)
            {
                templateContent.DataContext = content;
                if (ReferenceEquals(template, ContentTemplate))
                {
                    _generatedFromTemplate = template;
                }
                return templateContent;
            }
        }

        // Find Foreground from TemplatedParent or visual parent chain.
        // Even though TextBlock now shares ForegroundProperty with Control via AddOwner,
        // we still set it explicitly here because the TextBlock is created detached
        // and won't inherit from the visual tree until it's added as a child.
        var foreground = FindForegroundBrush();

        // Default: create a TextBlock for string content
        var formattedText = FormatContent(content);
        if (content is string && RecognizesAccessKey)
        {
            var accessText = new AccessText { Text = formattedText, Foreground = foreground };
            ApplyAccessTextFormatting(accessText);
            return accessText;
        }

        var otherTb = new TextBlock { Text = formattedText };
        ApplyTextBlockFormatting(otherTb, foreground);
        return otherTb;
    }

    /// <summary>Chooses the explicit, selected, or implicit template for the current content.</summary>
    protected virtual DataTemplate? ChooseTemplate()
    {
        if (ContentTemplate != null)
        {
            return ContentTemplate;
        }

        var content = Content;
        var selected = ContentTemplateSelector?.SelectTemplate(content, this);
        if (selected != null || content == null || content.GetType() == typeof(object))
        {
            return selected;
        }

        return ResourceLookup.FindImplicitDataTemplate(this, content.GetType()) as DataTemplate;
    }

    /// <summary>Called when <see cref="ContentStringFormat"/> changes.</summary>
    protected virtual void OnContentStringFormatChanged(string? oldContentStringFormat, string? newContentStringFormat)
    {
    }

    /// <summary>Called when <see cref="ContentTemplate"/> changes.</summary>
    protected virtual void OnContentTemplateChanged(DataTemplate? oldContentTemplate, DataTemplate? newContentTemplate)
    {
    }

    /// <summary>Called when <see cref="ContentTemplateSelector"/> changes.</summary>
    protected virtual void OnContentTemplateSelectorChanged(
        DataTemplateSelector? oldContentTemplateSelector,
        DataTemplateSelector? newContentTemplateSelector)
    {
    }

    /// <summary>Called when the selected template changes.</summary>
    protected virtual void OnTemplateChanged(DataTemplate? oldTemplate, DataTemplate? newTemplate)
    {
    }

    /// <summary>Returns whether the selector has a local value that should be serialized.</summary>
    public bool ShouldSerializeContentTemplateSelector() => HasLocalValue(ContentTemplateSelectorProperty);

    private string FormatContent(object content)
    {
        return string.IsNullOrEmpty(ContentStringFormat)
            ? content.ToString() ?? string.Empty
            : string.Format(CultureInfo.CurrentCulture, ContentStringFormat, content);
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
        Visual? current = ParentVisual;
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
        else if (_contentElement is AccessText accessText)
        {
            ApplyAccessTextFormatting(accessText);
        }
    }

    private void OnTemplatedParentPropertyChanged(DependencyProperty dp, object? oldValue, object? newValue)
    {
        if (dp == Control.ForegroundProperty ||
            dp == Control.FontFamilyProperty ||
            dp == Control.FontSizeProperty ||
            dp == Control.FontStyleProperty ||
            dp == Control.FontWeightProperty)
        {
            if (_contentElement is TextBlock textBlock)
            {
                ApplyTextBlockFormatting(textBlock, FindForegroundBrush());
            }
            else if (_contentElement is AccessText accessText)
            {
                ApplyAccessTextFormatting(accessText);
            }
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

        Visual? current = ParentVisual;
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

    private void ApplyAccessTextFormatting(AccessText accessText)
    {
        if (TemplatedParent is Control templatedControl)
        {
            accessText.FontFamily = templatedControl.FontFamily;
            accessText.FontSize = templatedControl.FontSize;
            accessText.FontStyle = templatedControl.FontStyle;
            accessText.FontWeight = templatedControl.FontWeight;
            accessText.Foreground = templatedControl.Foreground;
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
        if (!HasLocalValue(ContentProperty) && BindingOperations.GetBindingExpressionBase(this, ContentProperty) == null)
        {
            var contentDp = DependencyProperty.FromName(parentType, contentSource);
            if (contentDp != null)
            {
                this.SetTemplateBinding(ContentProperty, contentDp);
            }
        }

        if (!HasLocalValue(ContentTemplateProperty) && BindingOperations.GetBindingExpressionBase(this, ContentTemplateProperty) == null)
        {
            var templateDp = DependencyProperty.FromName(parentType, $"{contentSource}Template");
            if (templateDp != null)
                this.SetTemplateBinding(ContentTemplateProperty, templateDp);
        }

        if (!HasLocalValue(ContentTemplateSelectorProperty) && BindingOperations.GetBindingExpressionBase(this, ContentTemplateSelectorProperty) == null)
        {
            var selectorDp = DependencyProperty.FromName(parentType, $"{contentSource}TemplateSelector");
            if (selectorDp != null)
                this.SetTemplateBinding(ContentTemplateSelectorProperty, selectorDp);
        }

        if (!HasLocalValue(ContentStringFormatProperty) && BindingOperations.GetBindingExpressionBase(this, ContentStringFormatProperty) == null)
        {
            var stringFormatDp = DependencyProperty.FromName(parentType, $"{contentSource}StringFormat");
            if (stringFormatDp != null)
                this.SetTemplateBinding(ContentStringFormatProperty, stringFormatDp);
        }

        // Bind HorizontalContentAlignment -> HorizontalAlignment
        if (!HasLocalValue(HorizontalAlignmentProperty) && BindingOperations.GetBindingExpressionBase(this, HorizontalAlignmentProperty) == null)
        {
            var hcaDp = DependencyProperty.FromName(parentType, "HorizontalContentAlignment");
            if (hcaDp != null)
                this.SetTemplateBinding(HorizontalAlignmentProperty, hcaDp);
        }

        // Bind VerticalContentAlignment -> VerticalAlignment
        if (!HasLocalValue(VerticalAlignmentProperty) && BindingOperations.GetBindingExpressionBase(this, VerticalAlignmentProperty) == null)
        {
            var vcaDp = DependencyProperty.FromName(parentType, "VerticalContentAlignment");
            if (vcaDp != null)
                this.SetTemplateBinding(VerticalAlignmentProperty, vcaDp);
        }
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override int VisualChildrenCount => _contentElement != null ? 1 : 0;

    /// <inheritdoc />
    protected override Visual? GetVisualChild(int index)
    {
        if (index != 0 || _contentElement == null)
            throw new ArgumentOutOfRangeException(nameof(index));

        return _contentElement;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_contentElement == null)
            return default(Size);

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
