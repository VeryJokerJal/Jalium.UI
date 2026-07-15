using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Base class for all controls with a visual template.
/// </summary>
public class Control : FrameworkElement
{
    private static readonly Dictionary<string, SolidColorBrush> s_brushStringCache =
        new(StringComparer.OrdinalIgnoreCase);

    #region Static Constructor — MouseDoubleClick Class Handlers

    /// <summary>
    /// Identifies the PreviewMouseDoubleClick routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewMouseDoubleClickEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewMouseDoubleClick), RoutingStrategy.Tunnel, typeof(MouseButtonEventHandler), typeof(Control));

    /// <summary>
    /// Identifies the MouseDoubleClick routed event.
    /// </summary>
    public static readonly RoutedEvent MouseDoubleClickEvent =
        EventManager.RegisterRoutedEvent(nameof(MouseDoubleClick), RoutingStrategy.Bubble, typeof(MouseButtonEventHandler), typeof(Control));

    static Control()
    {
        // 类处理器订阅 UIElement.MouseDownEvent / PreviewMouseDownEvent — 这两个才是
        // WindowInputDispatcher 真正 raise 的路由事件。MouseLeftButtonDownEvent /
        // PreviewMouseLeftButtonDownEvent 仅作为 OnMouseDownThunk 内部派发到虚方法
        // OnMouseLeftButtonDown 的标记，不会被任何 dispatcher raise，订阅它们等于死代码。
        //
        // 不传 handledEventsToo（默认 false）：MouseDownEvent 是 Bubble，命中 element 沿
        // Bubble 链经过多层 Control（如嵌套 TreeViewItem）。如果允许 Handled 后还触发，
        // 每层 Control 都会再 raise 一次 MouseDoubleClickEvent，业务侧 ItemDoubleClicked
        // 在祖先节点上被重复触发。例如双击叶子 TreeViewItem 会同时打开父项目 .csproj。
        //
        // 默认 handledEventsToo:false 让最深层 Control（命中点的 source）独占双击事件 —
        // 它的 RaiseItemDoubleClicked 把 args.Handled 回填到 source.Handled (= MouseDownEvent.Handled)，
        // 后续 ancestor 的 class handler 不再触发，防止重复。
        EventManager.RegisterClassHandler(typeof(Control), UIElement.MouseDownEvent,
            new MouseButtonEventHandler(OnMouseDoubleClickThunk));

        EventManager.RegisterClassHandler(typeof(Control), UIElement.PreviewMouseDownEvent,
            new MouseButtonEventHandler(OnPreviewMouseDoubleClickThunk));

        // 让派生类 override OnMouseDoubleClick / OnPreviewMouseDoubleClick 仍然生效 —
        // thunk 现在用 RaiseEvent 走完整路由链，所以虚方法回调必须通过类处理器机制串联回来。
        EventManager.RegisterClassHandler(typeof(Control), MouseDoubleClickEvent,
            new MouseButtonEventHandler((s, e) => ((Control)s).OnMouseDoubleClick(e)));

        EventManager.RegisterClassHandler(typeof(Control), PreviewMouseDoubleClickEvent,
            new MouseButtonEventHandler((s, e) => ((Control)s).OnPreviewMouseDoubleClick(e)));
    }

    private static void OnMouseDoubleClickThunk(object sender, MouseButtonEventArgs e)
    {
        // 仅左键双击触发 MouseDoubleClick（与 WPF 行为一致）。
        if (e.ClickCount != 2 || e.ChangedButton != MouseButton.Left) return;

        var control = (Control)sender;
        var args = new MouseButtonEventArgs(
            MouseDoubleClickEvent,
            e.Position,
            e.ChangedButton,
            e.ButtonState,
            e.ClickCount,
            e.LeftButton,
            e.MiddleButton,
            e.RightButton,
            e.XButton1,
            e.XButton2,
            e.KeyboardModifiers,
            e.Timestamp);
        // RaiseEvent 让 Bubble 路由生效；OnMouseDoubleClick 虚方法由 static Control()
        // 注册的 MouseDoubleClickEvent 类处理器自动调用，避免 inline 调用绕过路由。
        control.RaiseEvent(args);
        if (args.Handled)
            e.Handled = true;
    }

    private static void OnPreviewMouseDoubleClickThunk(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || e.ChangedButton != MouseButton.Left) return;

        var control = (Control)sender;
        var args = new MouseButtonEventArgs(
            PreviewMouseDoubleClickEvent,
            e.Position,
            e.ChangedButton,
            e.ButtonState,
            e.ClickCount,
            e.LeftButton,
            e.MiddleButton,
            e.RightButton,
            e.XButton1,
            e.XButton2,
            e.KeyboardModifiers,
            e.Timestamp);
        control.RaiseEvent(args);
        if (args.Handled)
            e.Handled = true;
    }

    /// <summary>
    /// Occurs when the left mouse button is double-clicked (tunnel).
    /// </summary>
    public event MouseButtonEventHandler PreviewMouseDoubleClick
    {
        add => AddHandler(PreviewMouseDoubleClickEvent, value);
        remove => RemoveHandler(PreviewMouseDoubleClickEvent, value);
    }

    /// <summary>
    /// Occurs when the left mouse button is double-clicked.
    /// </summary>
    public event MouseButtonEventHandler MouseDoubleClick
    {
        add => AddHandler(MouseDoubleClickEvent, value);
        remove => RemoveHandler(MouseDoubleClickEvent, value);
    }

    /// <summary>
    /// Invoked when the mouse is double-clicked. Override to handle this event.
    /// </summary>
    protected virtual void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when the mouse is double-clicked (tunnel). Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewMouseDoubleClick(MouseButtonEventArgs e)
    {
    }

    #endregion

    /// <inheritdoc />
    /// <remarks>
    /// Returns null by default — only controls that explicitly override this method
    /// appear in the UIA tree. This matches WPF behavior and prevents layout containers
    /// from polluting the accessibility tree with unnamed Custom elements.
    /// </remarks>
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return null;
    }

    /// <summary>
    /// Gets whether this control provides its own keyboard scrolling behavior for a
    /// <see cref="ScrollViewer"/> in its template.
    /// </summary>
    /// <remarks>
    /// A templated <see cref="ScrollViewer"/> consults this property before handling
    /// navigation keys so controls such as <see cref="ListBox"/> can own selection and
    /// scrolling as one operation.
    /// </remarks>
    protected internal virtual bool HandlesScrolling => false;

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
    /// Shared with TextBlock and TextElement via AddOwner for cross-tree property inheritance.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(typeof(Control),
            new PropertyMetadata(new SolidColorBrush(Themes.ThemeColors.TextPrimary), OnVisualPropertyChanged, null, inherits: true));

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
    /// Shared with TextBlock and TextElement via AddOwner.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner(typeof(Control),
            new FrameworkPropertyMetadata(
                SystemFonts.MessageFontFamily,
                FrameworkPropertyMetadataOptions.Inherits,
                OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the FontSize dependency property.
    /// Shared with TextBlock and TextElement via AddOwner.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner(typeof(Control),
            new PropertyMetadata(14.0, OnLayoutPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the FontWeight dependency property.
    /// Shared with TextBlock and TextElement via AddOwner.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontWeightProperty =
        TextElement.FontWeightProperty.AddOwner(typeof(Control),
            new PropertyMetadata(FontWeights.Normal, OnLayoutPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the FontStyle dependency property.
    /// Shared with TextBlock and TextElement via AddOwner.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontStyleProperty =
        TextElement.FontStyleProperty.AddOwner(typeof(Control),
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

    /// <summary>Identifies the tab-order index property.</summary>
    public static readonly DependencyProperty TabIndexProperty =
        KeyboardNavigation.TabIndexProperty.AddOwner(typeof(Control));

    /// <summary>Identifies whether a control participates in tab navigation.</summary>
    public static readonly DependencyProperty IsTabStopProperty =
        KeyboardNavigation.IsTabStopProperty.AddOwner(typeof(Control));

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
    private IList<TriggerBase>? _appliedTemplateTriggers;
    private readonly Dictionary<string, FrameworkElement> _templateRegisteredNames =
        new(StringComparer.Ordinal);

    /// <summary>
    /// The root of the currently expanded template tree (null before a template is applied or
    /// after it is cleared). Used by <see cref="ItemsPresenter.AttachToOwner"/> to reject a
    /// presenter left over from a template instance this control has since replaced.
    /// </summary>
    internal FrameworkElement? TemplateRootInternal => _templateRoot;

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
    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty)!;
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

    /// <summary>Gets or sets this control's position in tab navigation order.</summary>
    public int TabIndex
    {
        get => (int)(GetValue(TabIndexProperty) ?? int.MaxValue);
        set => SetValue(TabIndexProperty, value);
    }

    /// <summary>Gets or sets whether this control is a tab-navigation stop.</summary>
    public bool IsTabStop
    {
        get => (bool)(GetValue(IsTabStopProperty) ?? true);
        set => SetValue(IsTabStopProperty, value);
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
    protected override bool ApplyTemplateCore()
    {
        if (_templateApplied)
            return false;

        // Clear existing template content
        ClearTemplateContent();

        // Load new template
        var template = Template;

        if (template != null)
        {
            _templateRoot = template.LoadContent();

            if (_templateRoot != null)
            {
                _templateApplied = true;

                // Set the templated parent for template bindings
                SetTemplatedParentRecursive(_templateRoot, this);

                // Mark the template root so Visual.RenderDirect paints it as a
                // background layer (before this control's OnRender) instead of
                // as a normal child (after OnRender). Without this flag, a
                // template with an opaque Background would obscure the output
                // of a self-drawing control's OnRender — that's the BarChart
                // (and every other ChartBase-derived control) "blank box" bug.
                _templateRoot.IsTemplatedRoot = true;

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
    public override void OnApplyTemplate()
    {
    }

    private void ClearTemplateContent()
    {
        // Let subclasses release references into the template tree that is about to be
        // discarded. This must happen for every teardown path (Template replaced at
        // runtime, theme switch swapping the theme template, Template cleared to null,
        // and the defensive clear at the start of ApplyTemplateCore) — a cached part
        // that survives the teardown keeps resolving into a detached tree.
        OnTemplateContentClearing();

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
            // Clear the templated-root marker so this visual is treated as a
            // normal child if it's ever re-parented somewhere else after the
            // template is rebuilt (rare, but the flag is conceptually owned
            // by the host control, not by the visual itself).
            _templateRoot.IsTemplatedRoot = false;
            RemoveVisualChild(_templateRoot);
            _templateRoot = null;
        }

        // Template names belong to this particular expanded template instance.
        // A control can replace its template more than once (for example ScrollBar's
        // fallback Thumb template is replaced by the theme template). Keeping the old
        // names in FrameworkElement's scope makes the next instance look like an
        // illegal duplicate even though the old visual tree is already gone.
        foreach (var (name, registeredElement) in _templateRegisteredNames)
        {
            if (ReferenceEquals(FindName(name), registeredElement))
            {
                UnregisterName(name);
            }
        }
        _templateRegisteredNames.Clear();
    }

    /// <summary>
    /// Called at the start of tearing down the currently expanded template content,
    /// before the old template tree is detached. Subclasses that cache references to
    /// elements inside the expanded tree (e.g. ItemsControl's ItemsPresenter) must drop
    /// and decommission them here; the tree is discarded right after this call, and any
    /// cached part that lingers resolves into a detached, invisible tree. May be invoked
    /// when no template content exists (first application) — implementations must
    /// tolerate that and stay idempotent.
    /// </summary>
    internal virtual void OnTemplateContentClearing()
    {
    }

    private void RegisterTemplateName(string name, FrameworkElement element)
    {
        if (_templateRegisteredNames.TryGetValue(name, out var existing))
        {
            if (ReferenceEquals(existing, element))
            {
                return;
            }

            throw new ArgumentException(
                $"Name '{name}' is already registered by another element in this template.",
                nameof(name));
        }

        RegisterName(name, element);
        _templateRegisteredNames.Add(name, element);
    }

    internal static void SetTemplatedParentRecursive(FrameworkElement element, FrameworkElement parent)
    {
        // The first element of the walk is the root of this expanded template instance; every
        // ItemsPresenter reached from here is stamped with it so a discarded presenter can be
        // told apart from the live one (see ItemsPresenter.AttachToOwner).
        SetTemplatedParentRecursive(element, parent, element, new HashSet<FrameworkElement>(ReferenceEqualityComparer.Instance));
    }

    private static void SetTemplatedParentRecursive(
        FrameworkElement element,
        FrameworkElement parent,
        FrameworkElement templateRoot,
        HashSet<FrameworkElement> visited)
    {
        if (!visited.Add(element))
        {
            return;
        }

        element.SetTemplatedParent(parent);

        // Stamp the presenter with its template instance root so a stray measure of a discarded
        // presenter cannot hijack the owner's live registration.
        if (element is ItemsPresenter itemsPresenter)
        {
            itemsPresenter.TemplateInstanceRoot = templateRoot;
        }

        // Register name if set
        if (!string.IsNullOrEmpty(element.Name))
        {
            if (parent is Control controlParent)
            {
                controlParent.RegisterTemplateName(element.Name, element);
            }
            else
            {
                parent.RegisterName(element.Name, element);
            }
        }

        // A nested Control is a boundary for its implementation visuals, but
        // content authored by the outer template remains part of the outer
        // template namescope and must still receive that templated parent.
        if (element is Control)
        {
            if (element is ContentControl nestedContentControl && nestedContentControl.Content is FrameworkElement nestedContentChild)
            {
                SetTemplatedParentRecursive(nestedContentChild, parent, templateRoot, visited);
            }

            return;
        }

        // Process visual children
        var childCount = element.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            if (element.GetVisualChild(i) is FrameworkElement child)
            {
                SetTemplatedParentRecursive(child, parent, templateRoot, visited);
            }
        }

        // Special handling for Popup - its Child is not a visual child but needs TemplatedParent
        if (element is Popup popup && popup.Child is FrameworkElement popupChild)
        {
            SetTemplatedParentRecursive(popupChild, parent, templateRoot, visited);
        }

        // Special handling for Border - its Child might not be in visual tree yet
        if (element is Border border && border.Child is FrameworkElement borderChild)
        {
            SetTemplatedParentRecursive(borderChild, parent, templateRoot, visited);
        }

        // Special handling for ContentControl - its content might not be in visual tree yet
        if (element is ContentControl contentControl && contentControl.Content is FrameworkElement contentChild)
        {
            SetTemplatedParentRecursive(contentChild, parent, templateRoot, visited);
        }

        // Special handling for Panel - ensure all children are processed
        if (element is Panel panel)
        {
            foreach (UIElement panelChild in panel.Children)
            {
                if (panelChild is FrameworkElement panelChildElement)
                {
                    SetTemplatedParentRecursive(panelChildElement, parent, templateRoot, visited);
                }
            }
        }
    }

    internal static void PromoteTemplateLocalValuesRecursive(FrameworkElement element)
    {
        element.PromoteLocalValuesToLayer(DependencyObject.LayerValueSource.ParentTemplate);
        DynamicResourceBindingOperations.PromoteDynamicResourcesToLayer(
            element,
            DependencyObject.LayerValueSource.ParentTemplate);

        // If this element is a Control that has already applied its own template,
        // do NOT descend into its template children.
        if (element is Control control && control._templateApplied)
            return;

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

        // Special handling for Panel - ensure all children are processed
        if (element is Panel panel)
        {
            foreach (UIElement panelChild in panel.Children)
            {
                if (panelChild is FrameworkElement panelChildElement)
                {
                    PromoteTemplateLocalValuesRecursive(panelChildElement);
                }
            }
        }
    }

    internal static void ReactivateBindingsRecursive(FrameworkElement element)
    {
        // Reactivate bindings on this element
        element.ReactivateBindings();

        // If this element is a Control that has already applied its own template,
        // do NOT descend into its template children.
        if (element is Control control && control._templateApplied)
            return;

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

        // Special handling for Panel
        if (element is Panel panel)
        {
            foreach (UIElement panelChild in panel.Children)
            {
                if (panelChild is FrameworkElement panelChildElement)
                {
                    ReactivateBindingsRecursive(panelChildElement);
                }
            }
        }
    }

    /// <inheritdoc />
    protected override int VisualChildrenCount => _templateRoot != null ? 1 : 0;

    /// <inheritdoc />
    protected override Visual? GetVisualChild(int index)
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

    /// <inheritdoc />
    /// <remarks>
    /// Paints the <c>ControlTemplate</c> root (background / border / corner-radius
    /// decoration) BEFORE this control's own <see cref="Visual.OnRender"/> runs.
    /// The template root is flagged with <see cref="Visual.IsTemplatedRoot"/> so
    /// the regular children-render loop in <see cref="Visual.RenderDirect"/>
    /// skips it — otherwise it would be drawn a second time on top of OnRender
    /// and obscure the output of self-drawing controls (charts, custom
    /// visualisations) whose templates have an opaque <c>Background</c>.
    /// </remarks>
    protected override void RenderTemplatedBackground(DrawingContext drawingContext)
    {
        if (_templateRoot != null)
        {
            RenderChildVisualInline(drawingContext, _templateRoot);
        }
    }

    #endregion

    #region Property Changed Callbacks

    internal static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
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
            control.OnTemplateChanged(
                (e.OldValue as ControlTemplate)!,
                (e.NewValue as ControlTemplate)!);

            // Apply template immediately if we have a new template.
            // TreeViewItem overrides ShouldDeferTemplateApplication to return true
            // for items in collapsed subtrees, deferring to MeasureOverride instead.
            if (e.NewValue != null && !control.ShouldDeferTemplateApplication())
            {
                control.ApplyTemplate();
            }

            control.InvalidateMeasure();
        }
    }

    /// <summary>
    /// Returns true to defer template application to MeasureOverride instead of
    /// applying eagerly in OnTemplateChanged. Override in controls that participate
    /// in large deferred trees (e.g. TreeViewItem in collapsed subtrees).
    /// </summary>
    internal virtual bool ShouldDeferTemplateApplication() => false;

    /// <summary>Called whenever this control's template changes.</summary>
    protected virtual void OnTemplateChanged(ControlTemplate oldTemplate, ControlTemplate newTemplate)
    {
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var text = this switch
        {
            ContentControl { Content: not null } contentControl
                when !ReferenceEquals(contentControl.Content, contentControl) => contentControl.Content.ToString(),
            TextBox textBox => textBox.Text,
            _ => null
        };

        var baseText = base.ToString() ?? GetType().FullName ?? GetType().Name;
        return string.IsNullOrEmpty(text) ? baseText : $"{baseText} Content:{text}";
    }

    #endregion
}
