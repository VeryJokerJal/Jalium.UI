using Jalium.UI.Media.Animation;

namespace Jalium.UI.Controls;

/// <summary>
/// Base class for content controls.
/// Uses direct content management by default. Controls with ControlTemplate (like Button)
/// rely on the template's ContentPresenter to display content instead.
/// </summary>
public class ContentControl : Control
{
    private UIElement? _contentElement;
    private bool _usesDirectContent = true; // Default to direct content management

    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.ContentControlAutomationPeer(this);
    }

    /// <summary>
    /// Identifies the Content dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ContentProperty =
        DependencyProperty.Register(nameof(Content), typeof(object), typeof(ContentControl),
            new PropertyMetadata(null, OnContentChanged));

    /// <summary>
    /// Identifies the ContentTemplate dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ContentTemplateProperty =
        DependencyProperty.Register(nameof(ContentTemplate), typeof(DataTemplate), typeof(ContentControl),
            new PropertyMetadata(null, OnContentTemplateChanged));

    /// <summary>
    /// Identifies the ContentTemplateSelector dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ContentTemplateSelectorProperty =
        DependencyProperty.Register(nameof(ContentTemplateSelector), typeof(DataTemplateSelector), typeof(ContentControl),
            new PropertyMetadata(null, OnContentTemplateSelectorChanged));

    /// <summary>
    /// Identifies the ContentTransition dependency property.
    /// When set, content changes are animated using the specified transition.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ContentTransitionProperty =
        DependencyProperty.Register(nameof(ContentTransition), typeof(ContentTransition), typeof(ContentControl),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the TransitionMode dependency property.
    /// Provides a shortcut to set common transitions without creating a ContentTransition instance.
    /// Used when <see cref="ContentTransition"/> is null.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty TransitionModeProperty =
        DependencyProperty.Register(nameof(TransitionMode), typeof(TransitionMode?), typeof(ContentControl),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the content of this control.
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
    /// Gets or sets the DataTemplateSelector used to choose a template for content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public DataTemplateSelector? ContentTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(ContentTemplateSelectorProperty);
        set => SetValue(ContentTemplateSelectorProperty, value);
    }

    /// <summary>
    /// Gets or sets the content transition animation.
    /// When set, content changes are animated using this transition.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ContentTransition? ContentTransition
    {
        get => (ContentTransition?)GetValue(ContentTransitionProperty);
        set => SetValue(ContentTransitionProperty, value);
    }

    /// <summary>
    /// Gets or sets the transition mode shortcut.
    /// Used when <see cref="ContentTransition"/> is null.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public TransitionMode? TransitionMode
    {
        get => (TransitionMode?)GetValue(TransitionModeProperty);
        set => SetValue(TransitionModeProperty, value);
    }

    /// <summary>
    /// Gets the content element for direct content management.
    /// </summary>
    protected UIElement? ContentElement => _contentElement;

    /// <summary>
    /// Disables direct content management. Call this in the constructor of controls
    /// that use ControlTemplate with ContentPresenter (e.g., Button).
    /// </summary>
    protected void UseTemplateContentManagement()
    {
        _usesDirectContent = false;
    }

    /// <summary>
    /// Enables direct content management. Call this in the constructor of controls
    /// that have their own OnRender implementation (e.g., CheckBox, RadioButton).
    /// This overrides any parent class's call to UseTemplateContentManagement().
    /// </summary>
    protected void UseDirectContentManagement()
    {
        _usesDirectContent = true;
    }

    /// <summary>
    /// Gets whether this control uses direct content management.
    /// Returns true for most controls; false for controls using ControlTemplate.
    /// </summary>
    protected bool UsesDirectContent => _usesDirectContent;

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

    private static void OnContentTemplateSelectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ContentControl control)
        {
            // Re-apply content with new selector
            control.OnContentChanged(null, control.Content);
        }
    }

    /// <summary>
    /// Called when the Content property changes.
    /// </summary>
    protected virtual void OnContentChanged(object? oldContent, object? newContent)
    {
        if (_usesDirectContent)
        {
            // Direct content management
            if (_contentElement != null)
            {
                RemoveVisualChild(_contentElement);
                _contentElement = null;
            }

            if (newContent is UIElement newElement)
            {
                _contentElement = newElement;
                AddVisualChild(newElement);
            }
            else if (newContent != null)
            {
                // Handle non-UIElement content (string, ViewModel objects, etc.)
                var element = CreateContentElementForDirectMode(newContent);
                if (element != null)
                {
                    _contentElement = element;
                    AddVisualChild(element);
                }
            }
        }

        InvalidateMeasure();
    }

    /// <summary>
    /// Creates a visual element for non-UIElement content in direct mode.
    /// </summary>
    private FrameworkElement? CreateContentElementForDirectMode(object content)
    {
        // Apply DataTemplate if available
        if (ContentTemplate != null)
        {
            var templateContent = ContentTemplate.LoadContent();
            if (templateContent != null)
            {
                templateContent.DataContext = content;
                return templateContent;
            }
        }

        // Create TextBlock for string content
        if (content is string text)
        {
            return new TextBlock { Text = text, Foreground = Foreground };
        }

        // For other objects, use ToString()
        return new TextBlock { Text = content.ToString() ?? string.Empty, Foreground = Foreground };
    }

    #region Visual Children

    /// <inheritdoc />
    public override int VisualChildrenCount
    {
        get
        {
            if (_usesDirectContent)
            {
                return _contentElement != null ? 1 : 0;
            }
            // Template-based: use Control's implementation
            return base.VisualChildrenCount;
        }
    }

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        if (_usesDirectContent)
        {
            if (index == 0 && _contentElement != null)
            {
                return _contentElement;
            }
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        // Template-based: use Control's implementation
        return base.GetVisualChild(index);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_usesDirectContent)
        {
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
            return Size.Empty;
        }
        // Template-based: use Control's implementation
        return base.MeasureOverride(availableSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_usesDirectContent)
        {
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
            }
            return finalSize;
        }
        // Template-based: use Control's implementation
        return base.ArrangeOverride(finalSize);
    }

    #endregion
}
