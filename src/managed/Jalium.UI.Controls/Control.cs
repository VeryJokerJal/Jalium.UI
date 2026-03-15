using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Base class for all controls with a visual template.
/// </summary>
public class Control : FrameworkElement
{
    private static readonly Dictionary<string, SolidColorBrush> s_brushStringCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.FrameworkElementAutomationPeer(this);
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the Background dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(Control),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Foreground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(Control),
            new PropertyMetadata(new SolidColorBrush(Color.Black), OnVisualPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the BorderBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BorderBrushProperty =
        DependencyProperty.Register(nameof(BorderBrush), typeof(Brush), typeof(Control),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the BorderThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty BorderThicknessProperty =
        DependencyProperty.Register(nameof(BorderThickness), typeof(Thickness), typeof(Control),
            new PropertyMetadata(new Thickness(0), OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Padding dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(Control),
            new PropertyMetadata(new Thickness(0), OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the FontFamily dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(string), typeof(Control),
            new PropertyMetadata("Segoe UI", OnVisualPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the FontSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(Control),
            new PropertyMetadata(14.0, OnLayoutPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the FontWeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(Control),
            new PropertyMetadata(FontWeights.Normal, OnLayoutPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the FontStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontStyleProperty =
        DependencyProperty.Register(nameof(FontStyle), typeof(FontStyle), typeof(Control),
            new PropertyMetadata(FontStyles.Normal, OnLayoutPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the FontStretch dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontStretchProperty =
        DependencyProperty.Register(nameof(FontStretch), typeof(FontStretch), typeof(Control),
            new PropertyMetadata(FontStretches.Normal, OnLayoutPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the CornerRadius dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(Control),
            new PropertyMetadata(new CornerRadius(0), OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the HorizontalContentAlignment dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty HorizontalContentAlignmentProperty =
        DependencyProperty.Register(nameof(HorizontalContentAlignment), typeof(HorizontalAlignment), typeof(Control),
            new PropertyMetadata(HorizontalAlignment.Left, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the VerticalContentAlignment dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty VerticalContentAlignmentProperty =
        DependencyProperty.Register(nameof(VerticalContentAlignment), typeof(VerticalAlignment), typeof(Control),
            new PropertyMetadata(VerticalAlignment.Top, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Template dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Framework)]
    public static readonly DependencyProperty TemplateProperty =
        DependencyProperty.Register(nameof(Template), typeof(ControlTemplate), typeof(Control),
            new PropertyMetadata(null, OnTemplateChanged));

    #endregion

    #region Template Fields

    private FrameworkElement? _templateRoot;
    private bool _templateApplied;
    private IList<Trigger>? _appliedTemplateTriggers;

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the background brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Background
    {
        get => CoerceBrush(GetValue(BackgroundProperty));
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the foreground brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Foreground
    {
        get => CoerceBrush(GetValue(ForegroundProperty));
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the border brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? BorderBrush
    {
        get => CoerceBrush(GetValue(BorderBrushProperty));
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the border thickness.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Thickness BorderThickness
    {
        get => (Thickness)GetValue(BorderThicknessProperty)!;
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the padding.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty)!;
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>
    /// Gets or sets the font family.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public string FontFamily
    {
        get => (string)(GetValue(FontFamilyProperty) ?? "Segoe UI");
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>
    /// Gets or sets the font size.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty)!;
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the font weight.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty) is FontWeight fw ? fw : FontWeights.Normal;
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the font style.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public FontStyle FontStyle
    {
        get => GetValue(FontStyleProperty) is FontStyle fs ? fs : FontStyles.Normal;
        set => SetValue(FontStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the font stretch.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public FontStretch FontStretch
    {
        get => GetValue(FontStretchProperty) is FontStretch fst ? fst : FontStretches.Normal;
        set => SetValue(FontStretchProperty, value);
    }

    /// <summary>
    /// Gets or sets the corner radius for rounded corners.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty)!;
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal alignment of the control's content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public HorizontalAlignment HorizontalContentAlignment
    {
        get => (HorizontalAlignment)GetValue(HorizontalContentAlignmentProperty)!;
        set => SetValue(HorizontalContentAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical alignment of the control's content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public VerticalAlignment VerticalContentAlignment
    {
        get => (VerticalAlignment)GetValue(VerticalContentAlignmentProperty)!;
        set => SetValue(VerticalContentAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the template that defines the visual appearance of the control.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Framework)]
    public ControlTemplate? Template
    {
        get => (ControlTemplate?)GetValue(TemplateProperty);
        set => SetValue(TemplateProperty, value);
    }

    private static Brush? CoerceBrush(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is Brush brush)
        {
            return brush;
        }

        if (value is Color color)
        {
            return new SolidColorBrush(color);
        }

        if (value is string brushText)
        {
            var normalized = brushText.Trim();
            if (normalized.Length == 0)
            {
                return null;
            }

            lock (s_brushStringCache)
            {
                if (s_brushStringCache.TryGetValue(normalized, out var cached))
                {
                    return cached;
                }
            }

            if (ColorConverter.ConvertFromString(normalized) is Color parsedColor)
            {
                var parsedBrush = new SolidColorBrush(parsedColor);
                lock (s_brushStringCache)
                {
                    s_brushStringCache[normalized] = parsedBrush;
                }
                return parsedBrush;
            }
        }

        return null;
    }

    #endregion

    #region Template Methods

    /// <summary>
    /// Applies the template if not already applied.
    /// </summary>
    /// <returns>True if the template was applied; otherwise, false.</returns>
    public bool ApplyTemplate()
    {
        if (_templateApplied)
            return false;

        _templateApplied = true;

        // Clear existing template content
        ClearTemplateContent();

        // Load new template
        var template = Template;

        if (template != null)
        {
            _templateRoot = template.LoadContent();

            if (_templateRoot != null)
            {
                // Set the templated parent for template bindings
                SetTemplatedParentRecursive(_templateRoot, this);

                // Add to visual tree
                AddVisualChild(_templateRoot);

                // Template-authored literal values must participate as template values instead
                // of local values so template triggers can override them.
                PromoteTemplateLocalValuesRecursive(_templateRoot);

                // Reactivate bindings now that the template tree is fully connected
                // This allows RelativeSource FindAncestor bindings to resolve
                ReactivateBindingsRecursive(_templateRoot);

                // Apply template triggers
                // Triggers are attached to the templated control (this) but target elements in the template by name
                if (template.Triggers.Count > 0)
                {
                    _appliedTemplateTriggers = template.Triggers;
                    foreach (var trigger in _appliedTemplateTriggers)
                    {
                        // Set the parent template triggers so the trigger can find sibling triggers
                        // when it needs to re-apply values after deactivation
                        trigger.ParentTemplateTriggers = _appliedTemplateTriggers;
                        trigger.Attach(this);
                    }
                }

                // Call OnApplyTemplate for derived classes to get template parts
                OnApplyTemplate();

                InvalidateMeasure();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Called after the template is applied. Override this to get references to template parts.
    /// </summary>
    protected virtual void OnApplyTemplate()
    {
    }

    /// <summary>
    /// Gets a named element from the applied template.
    /// </summary>
    /// <param name="childName">The name of the element to find.</param>
    /// <returns>The named element, or null if not found.</returns>
    protected DependencyObject? GetTemplateChild(string childName)
    {
        if (string.IsNullOrEmpty(childName) || _templateRoot == null)
            return null;

        return FindNameInTemplate(_templateRoot, childName);
    }

    private static DependencyObject? FindNameInTemplate(FrameworkElement root, string name)
    {
        // Check if the root has the name
        if (root.Name == name)
            return root;

        // Check named elements registry
        var found = root.FindName(name);
        if (found != null)
            return found as DependencyObject;

        // Recursively search children
        var childCount = root.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            if (root.GetVisualChild(i) is FrameworkElement child)
            {
                if (child.Name == name)
                    return child;

                var result = FindNameInTemplate(child, name);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    private void ClearTemplateContent()
    {
        // Detach template triggers
        if (_appliedTemplateTriggers != null)
        {
            foreach (var trigger in _appliedTemplateTriggers)
            {
                trigger.Detach(this);
                trigger.ParentTemplateTriggers = null;
            }
            _appliedTemplateTriggers = null;
        }

        if (_templateRoot != null)
        {
            RemoveVisualChild(_templateRoot);
            _templateRoot = null;
        }
    }

    private static void SetTemplatedParentRecursive(FrameworkElement element, FrameworkElement parent)
    {
        element.SetTemplatedParent(parent);

        // Register name if set
        if (!string.IsNullOrEmpty(element.Name))
        {
            parent.RegisterName(element.Name, element);
        }

        // Process visual children
        var childCount = element.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            if (element.GetVisualChild(i) is FrameworkElement child)
            {
                SetTemplatedParentRecursive(child, parent);
            }
        }

        // Special handling for Popup - its Child is not a visual child but needs TemplatedParent
        if (element is Popup popup && popup.Child is FrameworkElement popupChild)
        {
            SetTemplatedParentRecursive(popupChild, parent);
        }

        // Special handling for Border - its Child might not be in visual tree yet
        if (element is Border border && border.Child is FrameworkElement borderChild)
        {
            SetTemplatedParentRecursive(borderChild, parent);
        }

        // Special handling for ContentControl - its content might not be in visual tree yet
        if (element is ContentControl contentControl && contentControl.Content is FrameworkElement contentChild)
        {
            SetTemplatedParentRecursive(contentChild, parent);
        }

        // Special handling for ScrollViewer - its Content might not be in visual tree yet
        if (element is ScrollViewer scrollViewer && scrollViewer.Content is FrameworkElement scrollContent)
        {
            SetTemplatedParentRecursive(scrollContent, parent);
        }

        // Special handling for Panel - ensure all children are processed
        if (element is Panel panel)
        {
            foreach (var panelChild in panel.Children)
            {
                if (panelChild is FrameworkElement panelChildElement)
                {
                    SetTemplatedParentRecursive(panelChildElement, parent);
                }
            }
        }
    }

    private static void PromoteTemplateLocalValuesRecursive(FrameworkElement element)
    {
        element.PromoteLocalValuesToLayer(DependencyObject.LayerValueSource.ParentTemplate);
        DynamicResourceBindingOperations.PromoteDynamicResourcesToLayer(
            element,
            DependencyObject.LayerValueSource.ParentTemplate);

        // Process visual children
        var childCount = element.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            if (element.GetVisualChild(i) is FrameworkElement child)
            {
                PromoteTemplateLocalValuesRecursive(child);
            }
        }

        // Special handling for Popup - its Child is not a visual child but needs template layering
        if (element is Popup popup && popup.Child is FrameworkElement popupChild)
        {
            PromoteTemplateLocalValuesRecursive(popupChild);
        }

        // Special handling for Border - its Child might not be in visual tree yet
        if (element is Border border && border.Child is FrameworkElement borderChild)
        {
            PromoteTemplateLocalValuesRecursive(borderChild);
        }

        // Special handling for ContentControl - its content might not be in visual tree yet
        if (element is ContentControl contentControl && contentControl.Content is FrameworkElement contentChild)
        {
            PromoteTemplateLocalValuesRecursive(contentChild);
        }

        // Special handling for ScrollViewer - its Content might not be in visual tree yet
        if (element is ScrollViewer scrollViewer && scrollViewer.Content is FrameworkElement scrollContent)
        {
            PromoteTemplateLocalValuesRecursive(scrollContent);
        }

        // Special handling for Panel - ensure all children are processed
        if (element is Panel panel)
        {
            foreach (var panelChild in panel.Children)
            {
                if (panelChild is FrameworkElement panelChildElement)
                {
                    PromoteTemplateLocalValuesRecursive(panelChildElement);
                }
            }
        }
    }

    private static void ReactivateBindingsRecursive(FrameworkElement element)
    {
        // Reactivate bindings on this element
        element.ReactivateBindings();

        // Process visual children
        var childCount = element.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            if (element.GetVisualChild(i) is FrameworkElement child)
            {
                ReactivateBindingsRecursive(child);
            }
        }

        // Special handling for Popup
        if (element is Popup popup && popup.Child is FrameworkElement popupChild)
        {
            ReactivateBindingsRecursive(popupChild);
        }

        // Special handling for Border
        if (element is Border border && border.Child is FrameworkElement borderChild)
        {
            ReactivateBindingsRecursive(borderChild);
        }

        // Special handling for ContentControl
        if (element is ContentControl contentControl && contentControl.Content is FrameworkElement contentChild)
        {
            ReactivateBindingsRecursive(contentChild);
        }

        // Special handling for ScrollViewer
        if (element is ScrollViewer scrollViewer && scrollViewer.Content is FrameworkElement scrollContent)
        {
            ReactivateBindingsRecursive(scrollContent);
        }

        // Special handling for Panel
        if (element is Panel panel)
        {
            foreach (var panelChild in panel.Children)
            {
                if (panelChild is FrameworkElement panelChildElement)
                {
                    ReactivateBindingsRecursive(panelChildElement);
                }
            }
        }
    }

    /// <inheritdoc />
    public override int VisualChildrenCount => _templateRoot != null ? 1 : 0;

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        if (index != 0 || _templateRoot == null)
            return base.GetVisualChild(index);

        return _templateRoot;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // Apply template if not yet applied
        if (!_templateApplied)
        {
            ApplyTemplate();
        }

        // Measure template root
        if (_templateRoot != null)
        {
            _templateRoot.Measure(availableSize);
            return _templateRoot.DesiredSize;
        }

        return base.MeasureOverride(availableSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Arrange template root
        if (_templateRoot != null)
        {
            _templateRoot.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
            return finalSize;
        }

        return base.ArrangeOverride(finalSize);
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Control control)
        {
            control.InvalidateVisual();
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Control control)
        {
            control.InvalidateMeasure();
        }
    }

    private static void OnTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Control control)
        {
            // Reset template application flag to force re-application
            control._templateApplied = false;
            control.ClearTemplateContent();

            // Apply template immediately if we have a new template
            // This ensures visual children are available right away
            if (e.NewValue != null)
            {
                control.ApplyTemplate();
            }

            control.InvalidateMeasure();
        }
    }

    #endregion
}
