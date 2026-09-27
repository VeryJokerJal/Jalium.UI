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
    private Styling.CssRoundedRectangleGeometry? _cssOuterClip;

    internal override Geometry? GetLayoutClip()
    {
        if (Clip is not null || !ClipToBounds || ClipToBoundsEdges == ClipEdges.None ||
            Styling.CssOverflowProperties.TryGetActiveClipMargin(this, out _) ||
            Styling.CssBorderRadiusProperties.Get(this) is not { } value)
            return base.GetLayoutClip();
        var reference = new Rect(RenderSize);
        var edges = ClipToBoundsEdges;
        var bounds = ExpandBoundsClip(reference, edges);
        var radii = value.Resolve(RenderSize).Mask(edges);
        if (_cssOuterClip is { } cached && cached.Rect == bounds && cached.Radii == radii && cached.ClipEdges == edges)
            return cached;
        // The template paints its chrome inside this outer contour. A bound
        // Border separately clips its content at the padding edge.
        var geometry = new Styling.CssRoundedRectangleGeometry(bounds, radii, edges, reference);
        geometry.Freeze();
        return _cssOuterClip = geometry;
    }

    protected override HitTestResult? HitTestCore(Point point)
    {
        var hit = base.HitTestCore(point);
        if (ReferenceEquals(hit?.VisualHit, this) && Styling.CssBorderRadiusProperties.Get(this) is { } radius)
        {
            var local = new Point(point.X - VisualBounds.X, point.Y - VisualBounds.Y);
            if (!Styling.CssRoundedRectangleGeometry.Contains(new Rect(RenderSize), radius.Resolve(RenderSize), local)) return null;
        }
        return hit;
    }

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

                // 开启本次展开的绑定重算轮次。下面三处会各自碰到同一批元素，靠这个轮次戳
                // 把「同一元素在一次展开里被重算三遍绑定」压成一遍——推导见
                // DependencyObject 的 "Binding reactivation epoch" 一节。
                // 嵌套展开（模板里的控件在 AddVisualChild 期间展开自己的模板）由
                // Begin/End 的保存-还原隔离，因此必须 try/finally。
                var previousEpoch = DependencyObject.BeginBindingReactivationEpoch();
                try
                {
                    ExpandTemplateContent(template);
                }
                finally
                {
                    DependencyObject.EndBindingReactivationEpoch(previousEpoch);
                }

                InvalidateMeasure();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <see cref="ApplyTemplateCore"/> 的主体：把已经 LoadContent 出来的模板树接上、重算、
    /// 挂触发器。单独拆出来是为了让绑定重算轮次的 try/finally 包住全部三趟递归。
    /// </summary>
    private void ExpandTemplateContent(ControlTemplate template)
    {
        var templateRoot = _templateRoot!;

        // Set the templated parent for template bindings.
        // reactivateBindings: false —— 紧接着的 AddVisualChild 会通过 OnVisualParentChanged
        // 对整棵可视子树重算一遍，够不到的非可视子节点再由下面的 ReactivateBindingsRecursive
        // 兜住，所以这里不必先算一遍。
        SetTemplatedParentRecursive(
            templateRoot, this, reactivateBindings: !DependencyObject.TemplateExpansionDedupEnabled);

        // Mark the template root so Visual.RenderDirect paints it as a
        // background layer (before this control's OnRender) instead of
        // as a normal child (after OnRender). Without this flag, a
        // template with an opaque Background would obscure the output
        // of a self-drawing control's OnRender — that's the BarChart
        // (and every other ChartBase-derived control) "blank box" bug.
        templateRoot.IsTemplatedRoot = true;

        // Add to visual tree.
        // 这一步经由 FrameworkElement.OnVisualParentChanged 对整棵可视子树做 scope 重算
        // **并且**重算一遍绑定——那就是本次展开唯一一趟覆盖可视子树的绑定重算。
        AddVisualChild(templateRoot);

        // Template-authored literal values must participate as template values instead
        // of local values so template triggers can override them.
        PromoteTemplateLocalValuesRecursive(templateRoot);

        // Reactivate bindings now that the template tree is fully connected.
        // This allows RelativeSource FindAncestor bindings to resolve.
        // 上一步的 AddVisualChild 已经覆盖了整棵可视子树，所以这一趟实际只对「可视树够不到的」
        // 节点（Popup.Child、尚未进入可视树的 Border.Child / ContentControl.Content /
        // Panel.Children）真正做事；其余节点靠轮次戳跳过重复重算，遍历照旧。
        ReactivateBindingsRecursive(templateRoot);

        // Apply template triggers
        // Triggers are attached to the templated control (this) but target elements in the template by name
        if (template.Triggers.Count > 0)
        {
            _appliedTemplateTriggers = template.Triggers;
            foreach (var trigger in _appliedTemplateTriggers)
            {
                // 声明归属：让 trigger 能找到兄弟 trigger 做声明顺序仲裁，并知道自己写哪个值层。
                // hostIsTemplatedParent: true —— ControlTemplate 的 trigger 挂在被模板化的控件上。
                trigger.DeclareTemplateOwner(_appliedTemplateTriggers, hostIsTemplatedParent: true);
                trigger.Attach(this);
            }
        }

        // Call OnApplyTemplate for derived classes to get template parts
        OnApplyTemplate();
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
                // 只 Detach，**不**清 trigger 的模板归属：ControlTemplate 及其 TriggerBase 实例
                // 被所有使用该模板的控件共享，清空会连带毁掉其它仍然活着的控件的值层归属与
                // 声明顺序仲裁（详见 TriggerBase.ParentTemplateTriggers 的注释）。
                trigger.Detach(this);
            }
            _appliedTemplateTriggers = null;
        }

        if (_templateRoot != null)
        {
            // ContentPresenter can be showing a visual that is logically owned by
            // this control (for example a Button's StackPanel content or a
            // NavigationView's page host). Release those visuals while the old
            // template is still connected. Otherwise the replacement presenter
            // sees them parented to a presenter in this soon-to-be-detached tree
            // and ends up rendering through a stale visual-parent chain.
            ReleasePresentedContentRecursive(_templateRoot);

            // Clear the templated-root marker so this visual is treated as a
            // normal child if it's ever re-parented somewhere else after the
            // template is rebuilt (rare, but the flag is conceptually owned
            // by the host control, not by the visual itself).
            var root = _templateRoot;
            _templateRoot = null;
            root.IsTemplatedRoot = false;
            RemoveVisualChild(root);
            ReleaseTemplateOwner(root, this);
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

    private static void ReleasePresentedContentRecursive(Visual visual)
    {
        if (visual is ContentPresenter presenter)
        {
            presenter.ReleaseContentElementForTemplateTeardown();
            return;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(visual);
        for (var index = 0; index < childCount; index++)
        {
            if (VisualTreeHelper.GetChild(visual, index) is Visual child)
            {
                ReleasePresentedContentRecursive(child);
            }
        }
    }

    private static void ReleaseTemplateOwner(FrameworkElement root, Control owner)
    {
        // Collect before changing ownership: binding reactivation may itself
        // replace nested templates. User content and nested controls' own
        // template parts keep their distinct owners.
        var pending = new Stack<FrameworkElement>();
        var seen = new HashSet<FrameworkElement>();
        var owned = new List<FrameworkElement>();
        pending.Push(root);
        while (pending.TryPop(out var element))
        {
            if (!seen.Add(element)) continue;
            if (ReferenceEquals(element.TemplatedParent, owner)) owned.Add(element);
            for (var i = 0; i < element.VisualChildrenCount; i++)
                if (element.GetVisualChild(i) is FrameworkElement child) pending.Push(child);
            if (element is Popup { Child: FrameworkElement popupChild }) pending.Push(popupChild);
            if (element is ContentControl { Content: FrameworkElement content }) pending.Push(content);
        }
        foreach (var element in owned) element.SetTemplatedParent(null, reactivateBindings: true);
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

    // 环检测集合的线程内复用池。模板展开是整页构建里最高频的操作之一——页面上**每个**带
    // ControlTemplate 的控件都要走一次这个递归，原先每次都新分配一个 HashSet。实测一个真实
    // 页面有几百个带模板控件（向导页 920 个可视元素里绝大多数由模板展开产生），于是这里是
    // 每页几百次纯属多余的分配 + 随之而来的 Gen0 压力。
    //
    // 复用是安全的：这个集合的生命周期严格限定在一次同步递归内，退出时清空；递归不跨线程
    // （模板展开在 UI 线程上做），[ThreadStatic] 天然隔离。嵌套调用（模板里的控件在自己的
    // 展开过程中又触发一次模板展开）用「借出即置空、归还时才写回」处理——嵌套那次拿不到
    // 池里的实例，自己新分配一个，不会与外层共用同一个集合。
    [ThreadStatic]
    private static HashSet<FrameworkElement>? t_templatedParentVisitedPool;

    /// <summary>
    /// 借出环检测集合。三趟模板递归共用一个池：它们在一次展开里顺序执行、互不嵌套，
    /// 而「借出即置空」的写法让万一发生的嵌套（例如某个 DP 写入间接引发另一次模板展开）
    /// 自行新分配，永远不会两层共用同一个集合。
    /// </summary>
    private static HashSet<FrameworkElement> RentTemplateWalkVisitedSet()
    {
        var visited = t_templatedParentVisitedPool;
        if (visited is null)
        {
            return new HashSet<FrameworkElement>(ReferenceEqualityComparer.Instance);
        }

        t_templatedParentVisitedPool = null;
        return visited;
    }

    private static void ReturnTemplateWalkVisitedSet(HashSet<FrameworkElement> visited)
    {
        visited.Clear();
        t_templatedParentVisitedPool = visited;
    }

    internal static void SetTemplatedParentRecursive(FrameworkElement element, FrameworkElement parent) =>
        SetTemplatedParentRecursive(element, parent, reactivateBindings: true);

    /// <param name="reactivateBindings">
    /// 传 false 表示调用方保证紧接着还会有一趟覆盖本子树的绑定重算——见
    /// <see cref="FrameworkElement.SetTemplatedParent(FrameworkElement?, bool)"/> 上的推导。
    /// 模板展开走这条路，把「每个元素重算三遍绑定」压到一遍。
    /// </param>
    internal static void SetTemplatedParentRecursive(
        FrameworkElement element,
        FrameworkElement parent,
        bool reactivateBindings)
    {
        var visited = RentTemplateWalkVisitedSet();
        try
        {
            // The first element of the walk is the root of this expanded template instance; every
            // ItemsPresenter reached from here is stamped with it so a discarded presenter can be
            // told apart from the live one (see ItemsPresenter.AttachToOwner).
            SetTemplatedParentRecursive(element, parent, element, visited, reactivateBindings);
        }
        finally
        {
            ReturnTemplateWalkVisitedSet(visited);
        }
    }

    private static void SetTemplatedParentRecursive(
        FrameworkElement element,
        FrameworkElement parent,
        FrameworkElement templateRoot,
        HashSet<FrameworkElement> visited,
        bool reactivateBindings)
    {
        if (!visited.Add(element))
        {
            return;
        }

        element.SetTemplatedParent(parent, reactivateBindings);

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
                SetTemplatedParentRecursive(nestedContentChild, parent, templateRoot, visited, reactivateBindings);
            }

            return;
        }

        // Process visual children
        var childCount = element.InternalVisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            if (element.InternalGetVisualChild(i) is FrameworkElement child)
            {
                SetTemplatedParentRecursive(child, parent, templateRoot, visited, reactivateBindings);
            }
        }

        // Special handling for Popup - its Child is not a visual child but needs TemplatedParent
        if (element is Popup popup && popup.Child is FrameworkElement popupChild)
        {
            SetTemplatedParentRecursive(popupChild, parent, templateRoot, visited, reactivateBindings);
        }

        // Special handling for Border - its Child might not be in visual tree yet
        if (element is Border border && border.Child is FrameworkElement borderChild)
        {
            SetTemplatedParentRecursive(borderChild, parent, templateRoot, visited, reactivateBindings);
        }

        // Special handling for ContentControl - its content might not be in visual tree yet
        if (element is ContentControl contentControl && contentControl.Content is FrameworkElement contentChild)
        {
            SetTemplatedParentRecursive(contentChild, parent, templateRoot, visited, reactivateBindings);
        }

        // Special handling for Panel - ensure all children are processed
        if (element is Panel panel)
        {
            foreach (UIElement panelChild in panel.Children)
            {
                if (panelChild is FrameworkElement panelChildElement)
                {
                    SetTemplatedParentRecursive(panelChildElement, parent, templateRoot, visited, reactivateBindings);
                }
            }
        }
    }

    internal static void PromoteTemplateLocalValuesRecursive(FrameworkElement element)
    {
        // visited 为 null 表示关闭环检测（仅基准对照用），此时行为与优化前逐字一致。
        var visited = DependencyObject.TemplateExpansionDedupEnabled
            ? RentTemplateWalkVisitedSet()
            : null;
        try
        {
            PromoteTemplateLocalValuesRecursive(element, visited);
        }
        finally
        {
            if (visited != null)
            {
                ReturnTemplateWalkVisitedSet(visited);
            }
        }
    }

    private static void PromoteTemplateLocalValuesRecursive(
        FrameworkElement element,
        HashSet<FrameworkElement>? visited)
    {
        // 环检测不是可选的正确性防线，而是复杂度防线：这两趟递归在遍历可视子节点之外，
        // 还对 Border.Child / Panel.Children / ContentControl.Content 各再走一遍，而这些
        // 在此刻**已经**是可视子节点，于是同一个节点每下一层就被重复访问一次，整体是
        // O(2^depth) 而不是 O(n)。去重后行为完全不变——第二次访问本来就是空操作
        // （Local 层已被搬空、绑定已重算过），只是白跑一遍子树。
        if (visited != null && !visited.Add(element))
        {
            return;
        }

        element.PromoteLocalValuesToLayer(DependencyObject.LayerValueSource.ParentTemplate);
        DynamicResourceBindingOperations.PromoteDynamicResourcesToLayer(
            element,
            DependencyObject.LayerValueSource.ParentTemplate);

        // If this element is a Control that has already applied its own template,
        // do NOT descend into its template children.
        if (element is Control control && control._templateApplied)
            return;

        // Process visual children
        var childCount = element.InternalVisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            if (element.InternalGetVisualChild(i) is FrameworkElement child)
            {
                PromoteTemplateLocalValuesRecursive(child, visited);
            }
        }

        // Special handling for Popup - its Child is not a visual child but needs template layering
        if (element is Popup popup && popup.Child is FrameworkElement popupChild)
        {
            PromoteTemplateLocalValuesRecursive(popupChild, visited);
        }

        // Special handling for Border - its Child might not be in visual tree yet
        if (element is Border border && border.Child is FrameworkElement borderChild)
        {
            PromoteTemplateLocalValuesRecursive(borderChild, visited);
        }

        // Special handling for ContentControl - its content might not be in visual tree yet
        if (element is ContentControl contentControl && contentControl.Content is FrameworkElement contentChild)
        {
            PromoteTemplateLocalValuesRecursive(contentChild, visited);
        }

        // Special handling for Panel - ensure all children are processed
        if (element is Panel panel)
        {
            foreach (UIElement panelChild in panel.Children)
            {
                if (panelChild is FrameworkElement panelChildElement)
                {
                    PromoteTemplateLocalValuesRecursive(panelChildElement, visited);
                }
            }
        }
    }

    internal static void ReactivateBindingsRecursive(FrameworkElement element)
    {
        // visited 为 null 表示关闭环检测（仅基准对照用），此时行为与优化前逐字一致。
        var visited = DependencyObject.TemplateExpansionDedupEnabled
            ? RentTemplateWalkVisitedSet()
            : null;
        try
        {
            ReactivateBindingsRecursive(element, visited);
        }
        finally
        {
            if (visited != null)
            {
                ReturnTemplateWalkVisitedSet(visited);
            }
        }
    }

    private static void ReactivateBindingsRecursive(
        FrameworkElement element,
        HashSet<FrameworkElement>? visited)
    {
        // 环检测不是可选的正确性防线，而是复杂度防线：这两趟递归在遍历可视子节点之外，
        // 还对 Border.Child / Panel.Children / ContentControl.Content 各再走一遍，而这些
        // 在此刻**已经**是可视子节点，于是同一个节点每下一层就被重复访问一次，整体是
        // O(2^depth) 而不是 O(n)。去重后行为完全不变——第二次访问本来就是空操作
        // （Local 层已被搬空、绑定已重算过），只是白跑一遍子树。
        if (visited != null && !visited.Add(element))
        {
            return;
        }

        // Reactivate bindings on this element.
        //
        // ★剪枝：模板展开期间，AddVisualChild 触发的 OnVisualParentChanged 已经把整棵**可视**
        // 子树重算过一遍了。本趟的不可替代价值只在于够到可视树够不到的节点（Popup.Child、
        // 尚未进入可视树的 Border.Child / ContentControl.Content / Panel.Children）。
        // 对本轮已经重算过的元素再跑一遍 ReactivateBindings，只是对已激活的绑定重复
        // UpdateTarget()：取同样的源值、写同样的目标值，可证明无可观察效果。
        // 于是这里跳过重算、但**照常继续遍历**——遍历本身很便宜，而漏掉它就会漏掉那些
        // 挂在已重算节点下面的非可视子节点。
        if (!element.WasBindingReactivationDoneInCurrentEpoch())
        {
            element.ReactivateBindings();
        }

        // If this element is a Control that has already applied its own template,
        // do NOT descend into its template children.
        if (element is Control control && control._templateApplied)
        {
            // 但外层模板写在它身上的 Content 属于外层模板的命名域，仍归本趟负责——
            // 这与 SetTemplatedParentRecursive 在 Control 边界上的处理一致（见那里的注释）。
            // 少了这一条，"模板里嵌一个已展开模板的 ContentControl，其 Content 尚未被
            // ContentPresenter 实体化" 这种形态就会漏掉绑定重算。
            if (element is ContentControl nestedContentControl &&
                nestedContentControl.Content is FrameworkElement nestedContentChild)
            {
                ReactivateBindingsRecursive(nestedContentChild, visited);
            }

            return;
        }

        // Process visual children
        var childCount = element.InternalVisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            if (element.InternalGetVisualChild(i) is FrameworkElement child)
            {
                ReactivateBindingsRecursive(child, visited);
            }
        }

        // Special handling for Popup
        if (element is Popup popup && popup.Child is FrameworkElement popupChild)
        {
            ReactivateBindingsRecursive(popupChild, visited);
        }

        // Special handling for Border
        if (element is Border border && border.Child is FrameworkElement borderChild)
        {
            ReactivateBindingsRecursive(borderChild, visited);
        }

        // Special handling for ContentControl
        if (element is ContentControl contentControl && contentControl.Content is FrameworkElement contentChild)
        {
            ReactivateBindingsRecursive(contentChild, visited);
        }

        // Special handling for Panel
        if (element is Panel panel)
        {
            foreach (UIElement panelChild in panel.Children)
            {
                if (panelChild is FrameworkElement panelChildElement)
                {
                    ReactivateBindingsRecursive(panelChildElement, visited);
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
