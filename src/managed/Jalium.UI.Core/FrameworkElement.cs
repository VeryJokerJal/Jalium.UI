using System.IO;
using System.Runtime.InteropServices;
using Jalium.UI.Media;

namespace Jalium.UI;

/// <summary>
/// Provides a framework-level set of properties, events, and methods for UI elements.
/// </summary>
public partial class FrameworkElement : UIElement
{
    /// <summary>
    /// The default font family name used across the UI framework.
    /// Initialized from the Windows system message font (NONCLIENTMETRICS.lfMessageFont).
    /// </summary>
    public static readonly string DefaultFontFamilyName = GetSystemMessageFontName() ?? GetPlatformDefaultFontName();

    /// <summary>
    /// The default font size used across the UI framework.
    /// Initialized from the Windows system message font height.
    /// </summary>
    public static readonly double DefaultFontSize = GetSystemMessageFontSize() ?? 14.0;

    #region System Font P/Invoke

    private const uint SPI_GETNONCLIENTMETRICS = 0x0029;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LOGFONTW
    {
        public int lfHeight;
        public int lfWidth;
        public int lfEscapement;
        public int lfOrientation;
        public int lfWeight;
        public byte lfItalic;
        public byte lfUnderline;
        public byte lfStrikeOut;
        public byte lfCharSet;
        public byte lfOutPrecision;
        public byte lfClipPrecision;
        public byte lfQuality;
        public byte lfPitchAndFamily;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string lfFaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NONCLIENTMETRICSW
    {
        public uint cbSize;
        public int iBorderWidth;
        public int iScrollWidth;
        public int iScrollHeight;
        public int iCaptionWidth;
        public int iCaptionHeight;
        public LOGFONTW lfCaptionFont;
        public int iSmCaptionWidth;
        public int iSmCaptionHeight;
        public LOGFONTW lfSmCaptionFont;
        public int iMenuWidth;
        public int iMenuHeight;
        public LOGFONTW lfMenuFont;
        public LOGFONTW lfStatusFont;
        public LOGFONTW lfMessageFont;
        public int iPaddedBorderWidth;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(
        uint uiAction, uint uiParam, ref NONCLIENTMETRICSW pvParam, uint fWinIni);

    private static string? GetSystemMessageFontName()
    {
        try
        {
            var ncm = new NONCLIENTMETRICSW();
            ncm.cbSize = (uint)Marshal.SizeOf<NONCLIENTMETRICSW>();
            if (SystemParametersInfoW(SPI_GETNONCLIENTMETRICS, ncm.cbSize, ref ncm, 0))
            {
                var name = ncm.lfMessageFont.lfFaceName;
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        catch
        {
            // Fallback to default on any platform error
        }
        return null;
    }

    private static double? GetSystemMessageFontSize()
    {
        try
        {
            var ncm = new NONCLIENTMETRICSW();
            ncm.cbSize = (uint)Marshal.SizeOf<NONCLIENTMETRICSW>();
            if (SystemParametersInfoW(SPI_GETNONCLIENTMETRICS, ncm.cbSize, ref ncm, 0))
            {
                int height = ncm.lfMessageFont.lfHeight;
                if (height != 0)
                {
                    // lfHeight is negative for character height; convert to DIPs (96 DPI base).
                    // Absolute value gives the em height in logical pixels at system DPI.
                    double abs = Math.Abs(height);
                    // Convert from logical pixels to WPF-style DIPs (96 DPI reference).
                    // System DPI is typically 96 on standard displays; the value is already
                    // in logical units so we use it directly as approximate DIP size.
                    if (abs >= 6 && abs <= 72)
                        return abs;
                }
            }
        }
        catch
        {
            // Fallback to default on any platform error
        }
        return null;
    }

    private static string GetPlatformDefaultFontName()
    {
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsLinux())
            return "Roboto";
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS())
            return "SF Pro";
        return "Segoe UI";
    }

    #endregion

    /// <summary>
    /// The DPI scale factor used for pixel-snapping layout values.
    /// Set by Window during initialization and DPI changes.
    /// A value of 2.625 means each DIP is 2.625 physical pixels.
    /// </summary>
    internal static double LayoutDpiScale { get; set; } = 1.0;

    /// <summary>
    /// Snaps a layout value to the nearest physical pixel boundary so that
    /// arranged children and any geometry a parent paints around those
    /// children (background fills, border strokes) land on the same pixel
    /// rows/columns. Internal callers must use this when computing rects
    /// that need to align with a child's <c>_visualBounds</c> — otherwise
    /// fractional thicknesses (e.g. mid-transition values like 2.5) leave
    /// 0.5 px seams between the parent's background and the child.
    /// </summary>
    internal static double SnapLayoutValue(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0;
        }

        double scale = LayoutDpiScale;
        if (scale <= 1.0)
        {
            return Math.Round(value, MidpointRounding.AwayFromZero);
        }

        // Snap to nearest physical pixel boundary to prevent sub-pixel misalignment
        return Math.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the Width dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register(nameof(Width), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(double.NaN, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Height dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty HeightProperty =
        DependencyProperty.Register(nameof(Height), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(double.NaN, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the MinWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinWidthProperty =
        DependencyProperty.Register(nameof(MinWidth), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the MinHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinHeightProperty =
        DependencyProperty.Register(nameof(MinHeight), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the MaxWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaxWidthProperty =
        DependencyProperty.Register(nameof(MaxWidth), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(double.PositiveInfinity, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the MaxHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaxHeightProperty =
        DependencyProperty.Register(nameof(MaxHeight), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(double.PositiveInfinity, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Margin dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MarginProperty =
        DependencyProperty.Register(nameof(Margin), typeof(Thickness), typeof(FrameworkElement),
            new PropertyMetadata(new Thickness(0), OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the HorizontalAlignment dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty HorizontalAlignmentProperty =
        DependencyProperty.Register(nameof(HorizontalAlignment), typeof(HorizontalAlignment), typeof(FrameworkElement),
            new PropertyMetadata(HorizontalAlignment.Stretch, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the VerticalAlignment dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty VerticalAlignmentProperty =
        DependencyProperty.Register(nameof(VerticalAlignment), typeof(VerticalAlignment), typeof(FrameworkElement),
            new PropertyMetadata(VerticalAlignment.Stretch, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the DataContext dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public static readonly DependencyProperty DataContextProperty =
        DependencyProperty.Register(nameof(DataContext), typeof(object), typeof(FrameworkElement),
            new PropertyMetadata(null, OnDataContextChanged));

    /// <summary>
    /// Identifies the Name dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Framework)]
    public static readonly DependencyProperty NameProperty =
        DependencyProperty.Register(nameof(Name), typeof(string), typeof(FrameworkElement),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the Tag dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Framework)]
    public static readonly DependencyProperty TagProperty =
        DependencyProperty.Register(nameof(Tag), typeof(object), typeof(FrameworkElement),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the ToolTip dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ToolTipProperty =
        DependencyProperty.Register(nameof(ToolTip), typeof(object), typeof(FrameworkElement),
            new PropertyMetadata(null, OnToolTipPropertyChanged));

    /// <summary>
    /// Identifies the <see cref="ContextMenu"/> dependency property.
    /// </summary>
    /// <remarks>
    /// Declared as <see cref="object"/> because the concrete <c>ContextMenu</c> control type lives in
    /// <c>Jalium.UI.Controls</c>, which depends on this Core assembly. <c>ContextMenuService</c> aliases this
    /// DP so <c>ContextMenuService.ContextMenuProperty</c> and <c>FrameworkElement.ContextMenuProperty</c>
    /// resolve to the same identifier — assigning either updates the same store.
    /// </remarks>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ContextMenuProperty =
        DependencyProperty.Register(nameof(ContextMenu), typeof(object), typeof(FrameworkElement),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Style dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StyleProperty =
        DependencyProperty.Register(nameof(Style), typeof(Style), typeof(FrameworkElement),
            new PropertyMetadata(null, OnStyleChanged));

    /// <summary>
    /// Identifies the FocusVisualStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty FocusVisualStyleProperty =
        DependencyProperty.Register(nameof(FocusVisualStyle), typeof(Style), typeof(FrameworkElement),
            new PropertyMetadata(null));

    /// <summary>
    /// Resource key used to look up the ambient default <see cref="FocusVisualStyle"/> from
    /// the resource tree when an element has not set one explicitly. Themes register a
    /// <see cref="UI.Style"/> under this key to supply the framework-wide focus indicator.
    /// </summary>
    public static readonly string DefaultFocusVisualStyleKey = "DefaultFocusVisualStyle";

    /// <summary>
    /// Identifies the Cursor dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty CursorProperty =
        DependencyProperty.Register(nameof(Cursor), typeof(Cursor), typeof(FrameworkElement),
            new PropertyMetadata(null, null, null, inherits: true));

    #endregion

    #region SizeChanged Event

    /// <summary>
    /// The previous render size, used to detect size changes.
    /// </summary>
    private Size _previousRenderSize;

    /// <summary>
    /// Occurs when either ActualWidth or ActualHeight properties change value.
    /// </summary>
    public virtual event SizeChangedEventHandler? SizeChanged;

    /// <summary>
    /// Occurs when the element is laid out, rendered, and ready for interaction (added to visual tree).
    /// </summary>
    public virtual event RoutedEventHandler? Loaded;

    /// <summary>
    /// Occurs when the element is removed from the visual tree.
    /// </summary>
    public virtual event RoutedEventHandler? Unloaded;

    /// <summary>
    /// Called when the render size changes.
    /// </summary>
    /// <param name="sizeInfo">Details of the size change.</param>
    protected virtual void OnSizeChanged(SizeChangedInfo sizeInfo)
    {
        SizeChanged?.Invoke(this, new SizeChangedEventArgs(sizeInfo));
    }

    #endregion

    #region Internal Fields

    /// <summary>
    /// Stores original property values before style application.
    /// Used internally by the style system.
    /// </summary>
    internal readonly Dictionary<DependencyProperty, object?> _styleOriginalValues = new();

    /// <summary>
    /// Stores original property values before ANY trigger modified them.
    /// Key is (target element, property), value is
    /// (original value, active trigger count, suspended dynamic resource key).
    /// Used internally by the trigger system to ensure correct restoration.
    /// </summary>
    internal readonly Dictionary<(FrameworkElement, DependencyProperty), (object? OriginalValue, int ActiveCount, object? SuspendedDynamicResourceKey)> _triggerOriginalValues = new();

    /// <summary>
    /// The implicit style applied to this element based on its type.
    /// </summary>
    private Style? _implicitStyle;

    /// <summary>
    /// The element that owns the template in which this element is defined.
    /// </summary>
    private FrameworkElement? _templatedParent;

    /// <summary>
    /// Named elements registered in this element's scope (when it's a template root).
    /// </summary>
    private Dictionary<string, FrameworkElement>? _namedElements;

    #endregion

    #region Template Properties

    /// <summary>
    /// Gets the element that owns the template in which this element is defined.
    /// </summary>
    public FrameworkElement? TemplatedParent => _templatedParent;

    /// <summary>
    /// Sets the templated parent. This is called internally when applying templates.
    /// </summary>
    internal void SetTemplatedParent(FrameworkElement? parent)
    {
        var oldParent = _templatedParent;
        _templatedParent = parent;

        // Notify derived classes that TemplatedParent has changed
        if (oldParent != parent)
        {
            OnTemplatedParentChanged(oldParent, parent);
        }

        // Reactivate bindings now that TemplatedParent is set.
        // This allows deferred template bindings (TemplateBinding) to resolve.
        if (parent != null)
        {
            ReactivateBindings();
        }
    }

    /// <summary>
    /// Called when the TemplatedParent property changes.
    /// </summary>
    /// <param name="oldParent">The old templated parent.</param>
    /// <param name="newParent">The new templated parent.</param>
    protected virtual void OnTemplatedParentChanged(FrameworkElement? oldParent, FrameworkElement? newParent)
    {
    }

    /// <summary>
    /// Registers a named element in this element's template scope.
    /// </summary>
    /// <param name="name">The name of the element.</param>
    /// <param name="element">The element to register.</param>
    public void RegisterName(string name, FrameworkElement element)
    {
        _namedElements ??= new Dictionary<string, FrameworkElement>();
        _namedElements[name] = element;
    }

    /// <summary>
    /// Unregisters a named element from this element's template scope.
    /// </summary>
    /// <param name="name">The name to unregister.</param>
    public void UnregisterName(string name)
    {
        _namedElements?.Remove(name);
    }

    /// <summary>
    /// Finds a named element in this element's template scope.
    /// </summary>
    /// <param name="name">The name of the element to find.</param>
    /// <returns>The element, or null if not found.</returns>
    public object? FindName(string name)
    {
        if (NameScope.GetNameScope(this)?.FindName(name) is { } scopedElement)
        {
            return scopedElement;
        }

        if (_namedElements != null && _namedElements.TryGetValue(name, out var element))
        {
            return element;
        }

        // Stop at template boundary: if the parent is the TemplatedParent of this element,
        // we've reached the template root and should not continue searching upward.
        if (VisualParent is FrameworkElement parent && parent != _templatedParent)
        {
            return parent.FindName(name);
        }

        return null;
    }

    #endregion

    #region Property Inheritance

    /// <inheritdoc />
    public override object? GetValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);

        var localSource = base.GetValueSourceInternal(dp);
        if (localSource.BaseValueSource != BaseValueSource.Default || localSource.IsAnimated)
        {
            return base.GetValue(dp);
        }

        // For inheriting properties, check parent chain
        if (dp.DefaultMetadata.Inherits && VisualParent is FrameworkElement parent)
        {
            if (TryGetInheritedBaseValue(parent, dp, out var inheritedValue))
            {
                return inheritedValue;
            }
        }

        return base.GetValue(dp);
    }

    internal override ValueSource GetValueSourceInternal(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);

        var localSource = base.GetValueSourceInternal(dp);
        if (localSource.BaseValueSource != BaseValueSource.Default || localSource.IsAnimated)
            return localSource;

        if (dp.DefaultMetadata.Inherits && VisualParent is FrameworkElement parent)
            return new ValueSource(BaseValueSource.Inherited, localSource.IsExpression, localSource.IsAnimated, localSource.IsCoerced);

        return localSource;
    }

    internal override (object? value, BaseValueSource source) GetUncoercedBaseValueInternal(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);

        var localValue = base.GetUncoercedBaseValueInternal(dp);
        if (localValue.source != BaseValueSource.Default)
        {
            return localValue;
        }

        if (dp.DefaultMetadata.Inherits && VisualParent is FrameworkElement parent)
        {
            if (TryGetInheritedBaseValue(parent, dp, out var inheritedValue))
            {
                return (inheritedValue, BaseValueSource.Inherited);
            }
        }

        return localValue;
    }

    private static bool TryGetInheritedBaseValue(FrameworkElement parent, DependencyProperty dp, out object? value)
    {
        if (parent.HasAnimatedValue(dp))
        {
            value = parent.GetValue(dp);
            return true;
        }

        var parentBaseValue = parent.GetUncoercedBaseValueInternal(dp);
        if (parentBaseValue.source != BaseValueSource.Default)
        {
            value = parentBaseValue.value;
            return true;
        }

        value = null;
        return false;
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the width of the element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Width
    {
        get => (double)GetValue(WidthProperty)!;
        set => SetValue(WidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the height of the element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Height
    {
        get => (double)GetValue(HeightProperty)!;
        set => SetValue(HeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum width of the element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MinWidth
    {
        get => (double)GetValue(MinWidthProperty)!;
        set => SetValue(MinWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum height of the element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MinHeight
    {
        get => (double)GetValue(MinHeightProperty)!;
        set => SetValue(MinHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum width of the element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MaxWidth
    {
        get => (double)GetValue(MaxWidthProperty)!;
        set => SetValue(MaxWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum height of the element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MaxHeight
    {
        get => (double)GetValue(MaxHeightProperty)!;
        set => SetValue(MaxHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the margin around the element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Thickness Margin
    {
        get => (Thickness)GetValue(MarginProperty)!;
        set => SetValue(MarginProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal alignment.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public HorizontalAlignment HorizontalAlignment
    {
        get => (HorizontalAlignment)GetValue(HorizontalAlignmentProperty)!;
        set => SetValue(HorizontalAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical alignment.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public VerticalAlignment VerticalAlignment
    {
        get => (VerticalAlignment)GetValue(VerticalAlignmentProperty)!;
        set => SetValue(VerticalAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the data context for data binding.
    /// When no local value is set, the value is inherited from the nearest ancestor
    /// that has a DataContext, matching WPF's inherited-property behaviour.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public object? DataContext
    {
        get
        {
            // If a local value is set (even null), honour it — the user explicitly
            // cleared or assigned DataContext on this element.
            if (HasLocalValue(DataContextProperty))
                return GetValue(DataContextProperty);

            // Walk up the visual tree to find inherited DataContext,
            // matching WPF's inherited-property behaviour.
            var parent = VisualParent as FrameworkElement;
            while (parent != null)
            {
                if (parent.HasLocalValue(DataContextProperty))
                    return parent.GetValue(DataContextProperty);
                parent = parent.VisualParent as FrameworkElement;
            }

            return null;
        }
        set => SetValue(DataContextProperty, value);
    }

    /// <summary>
    /// Gets or sets the name of the element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Framework)]
    public string Name
    {
        get => (string)(GetValue(NameProperty) ?? string.Empty);
        set => SetValue(NameProperty, value);
    }

    /// <summary>
    /// Gets or sets arbitrary object data associated with this element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Framework)]
    public object? Tag
    {
        get => GetValue(TagProperty);
        set => SetValue(TagProperty, value);
    }

    /// <summary>
    /// Gets or sets the tooltip for this element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? ToolTip
    {
        get => GetValue(ToolTipProperty);
        set => SetValue(ToolTipProperty, value);
    }

    /// <summary>
    /// Gets or sets the context menu shown when the user requests it on this element. The runtime
    /// type is <c>Jalium.UI.Controls.ContextMenu</c>; the property is typed as <see cref="object"/>
    /// because <c>FrameworkElement</c> lives in Core and cannot reference the Controls assembly.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public object? ContextMenu
    {
        get => GetValue(ContextMenuProperty);
        set => SetValue(ContextMenuProperty, value);
    }

    /// <summary>
    /// Delegate invoked when mouse enters an element with a ToolTip.
    /// Set by Controls assembly to show tooltip popup.
    /// </summary>
    internal static Action<FrameworkElement, RoutedEventArgs>? ToolTipShowRequested { get; set; }

    /// <summary>
    /// Delegate invoked when mouse leaves an element with a ToolTip.
    /// Set by Controls assembly to hide tooltip popup.
    /// </summary>
    internal static Action<UIElement>? ToolTipHideRequested { get; set; }

    private static void OnToolTipPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            // Subscribe/unsubscribe directly in Core — no delegate timing issues
            element.MouseEnter -= OnToolTipMouseEnter;
            element.MouseLeave -= OnToolTipMouseLeave;

            if (e.NewValue != null)
            {
                element.MouseEnter += OnToolTipMouseEnter;
                element.MouseLeave += OnToolTipMouseLeave;
            }
        }
    }

    private static void OnToolTipMouseEnter(object? sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ToolTip != null)
            ToolTipShowRequested?.Invoke(fe, e);
    }

    private static void OnToolTipMouseLeave(object? sender, RoutedEventArgs e)
    {
        if (sender is UIElement element)
            ToolTipHideRequested?.Invoke(element);
    }

    private ResourceDictionary? _resources;

    /// <summary>
    /// Occurs when the Resources property has changed.
    /// </summary>
    public event EventHandler? ResourcesChanged;

    /// <summary>
    /// 是否已实例化本地 Resources 字典（包含至少一项）。
    /// 与直接访问 <see cref="Resources"/> 不同——本属性不会触发懒构造，
    /// 用于资源快照 / 诊断等场景，避免对每个元素都生成空字典污染查找。
    /// </summary>
    internal bool HasResources => _resources != null && _resources.Count > 0;

    /// <summary>
    /// Gets or sets the locally-defined resource dictionary.
    /// </summary>
    public ResourceDictionary Resources
    {
        get
        {
            if (_resources == null)
            {
                _resources = new ResourceDictionary();
                _resources.Changed += OnLocalResourcesDictionaryChanged;
            }

            return _resources;
        }
        set
        {
            if (_resources != value)
            {
                if (_resources != null)
                {
                    _resources.Changed -= OnLocalResourcesDictionaryChanged;
                }

                _resources = value ?? new ResourceDictionary();
                _resources.Changed += OnLocalResourcesDictionaryChanged;
                OnResourcesChanged();
            }
        }
    }

    /// <summary>
    /// Raises the ResourcesChanged event.
    /// </summary>
    protected virtual void OnResourcesChanged()
    {
        RaiseResourcesChangedInSubtree();
    }

    private void OnLocalResourcesDictionaryChanged(object? sender, EventArgs e)
    {
        OnResourcesChanged();
    }

    internal void NotifyResourcesChangedFromRoot()
    {
        RaiseResourcesChangedInSubtree();
    }

    private void RaiseResourcesChangedInSubtree()
    {
        // Use iterative BFS with an explicit stack to avoid deep recursion overhead
        // and to allow early pruning of subtrees that don't need notification.
        var stack = s_subtreeStack ??= new List<FrameworkElement>(32);
        stack.Add(this);

        while (stack.Count > 0)
        {
            var current = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);

            if (current.ResourcesChanged != null)
            {
                current.ResourcesChanged.Invoke(current, EventArgs.Empty);
            }

            if (current.Style == null)
            {
                current.ReEvaluateImplicitStyle();
            }

            var childCount = current.VisualChildrenCount;
            for (int i = 0; i < childCount; i++)
            {
                if (current.GetVisualChild(i) is FrameworkElement child)
                {
                    stack.Add(child);
                }
            }
        }
    }

    [ThreadStatic]
    private static List<FrameworkElement>? s_subtreeStack;

    /// <summary>
    /// Searches for a resource with the specified key, and throws an exception if not found.
    /// </summary>
    /// <param name="resourceKey">The key identifier for the requested resource.</param>
    /// <returns>The requested resource.</returns>
    /// <exception cref="InvalidOperationException">The resource was not found.</exception>
    public object FindResource(object resourceKey)
    {
        var result = TryFindResource(resourceKey);
        if (result == null)
        {
            throw new InvalidOperationException($"Resource '{resourceKey}' not found.");
        }
        return result;
    }

    /// <summary>
    /// Searches for a resource with the specified key, and returns null if not found.
    /// </summary>
    /// <param name="resourceKey">The key identifier for the requested resource.</param>
    /// <returns>The requested resource, or null if not found.</returns>
    public object? TryFindResource(object resourceKey)
    {
        return ResourceLookup.FindResource(this, resourceKey);
    }

    /// <summary>
    /// Gets the actual rendered width of this element.
    /// </summary>
    public double ActualWidth => RenderSize.Width;

    /// <summary>
    /// Gets the actual rendered height of this element.
    /// </summary>
    public double ActualHeight => RenderSize.Height;

    /// <summary>
    /// Gets or sets the style used by this element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Style? Style
    {
        get => (Style?)GetValue(StyleProperty);
        set => SetValue(StyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the style describing how this element is decorated when it has keyboard
    /// focus. The style's <see cref="UI.ControlTemplate"/> is instantiated on the nearest
    /// <see cref="Documents.AdornerLayer"/>, so it does not replace the element's own
    /// template or participate in its layout. When this value is null, the framework
    /// resolves <see cref="DefaultFocusVisualStyleKey"/> from the resource tree.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Style? FocusVisualStyle
    {
        get => (Style?)GetValue(FocusVisualStyleProperty);
        set => SetValue(FocusVisualStyleProperty, value);
    }

    /// <summary>
    /// Controls whether this element type opts out of the framework's default focus
    /// visual ring. Derived types that always want to suppress the ring (e.g. code
    /// editors where the caret already conveys focus, or controls with their own
    /// custom focus presentation) override this to return true. An explicit
    /// <see cref="FocusVisualStyle"/> assignment still wins over this opt-out so
    /// callers can re-enable a custom ring per-instance.
    /// </summary>
    protected virtual bool SuppressFocusVisualByDefault => false;

    /// <summary>
    /// Resolves the effective focus visual style. Lookup order:
    /// <list type="number">
    ///   <item>If <see cref="FocusVisualStyle"/> was explicitly assigned (any source —
    ///   SetValue, Style Setter, Trigger, Template, Animation), use it — even an
    ///   explicit null acts as a per-instance opt-out.</item>
    ///   <item>If the element type opts out via <see cref="SuppressFocusVisualByDefault"/>,
    ///   return null (no ring).</item>
    ///   <item>Otherwise fall back to the ambient <see cref="DefaultFocusVisualStyleKey"/>
    ///   resource so the framework default ring shows up everywhere by default.</item>
    /// </list>
    /// </summary>
    internal Style? ResolveFocusVisualStyle()
    {
        // HasValueAboveInherited distinguishes "never assigned" from "explicitly
        // assigned to null". HasLocalValue would be too narrow — it only covers
        // SetValue, not Style Setters (which write through _styleSetterValues), so a
        // XAML `<Setter Property="FocusVisualStyle" Value="{x:Null}" />` would be
        // invisible and the default ring would still appear.
        if (HasValueAboveInherited(FocusVisualStyleProperty))
            return FocusVisualStyle;

        if (SuppressFocusVisualByDefault)
            return null;

        return ResourceLookup.FindResource(this, DefaultFocusVisualStyleKey) as Style;
    }

    /// <summary>
    /// Gets or sets the cursor that displays when the mouse pointer is over this element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public Cursor? Cursor
    {
        get => (Cursor?)GetValue(CursorProperty);
        set => SetValue(CursorProperty, value);
    }

    #endregion

    #region Layout

    private Rect _visualBounds;

    /// <summary>
    /// Gets the visual bounds of this element in parent coordinates.
    /// </summary>
    public override Rect VisualBounds => _visualBounds;

    /// <summary>
    /// Sets the visual bounds of this element.
    /// This should only be called by parent containers after Arrange().
    /// Note: ArrangeCore already sets _visualBounds based on the finalRect,
    /// so this call is typically used to ensure consistency.
    /// </summary>
    public void SetVisualBounds(Rect bounds)
    {
        if (_visualBounds != bounds)
        {
            InvalidateScreenOffsetCache();
        }

        _visualBounds = bounds;
    }

    /// <summary>
    /// Debug helper: Gets the absolute position of this element in window coordinates
    /// by walking up the visual tree and accumulating VisualBounds offsets.
    /// </summary>
    /// <returns>The absolute position in window coordinates.</returns>
    public Point GetAbsolutePosition()
    {
        double x = 0;
        double y = 0;

        Visual? current = this;
        while (current != null)
        {
            if (current.VisualParent == null)
                break;

            if (current is FrameworkElement fe)
            {
                x += fe._visualBounds.X;
                y += fe._visualBounds.Y;
            }
            current = current.VisualParent;
        }

        return new Point(x, y);
    }

    /// <inheritdoc />
    protected override Size MeasureCore(Size availableSize)
    {
        var margin = Margin;
        var marginWidth = margin.Left + margin.Right;
        var marginHeight = margin.Top + margin.Bottom;

        // Calculate available size for content
        var contentAvailable = new Size(
            Math.Max(0, availableSize.Width - marginWidth),
            Math.Max(0, availableSize.Height - marginHeight));

        // Apply explicit size constraints
        if (!double.IsNaN(Width))
        {
            contentAvailable = new Size(Width, contentAvailable.Height);
        }
        if (!double.IsNaN(Height))
        {
            contentAvailable = new Size(contentAvailable.Width, Height);
        }

        // Apply min/max constraints
        contentAvailable = new Size(
            Math.Clamp(contentAvailable.Width, MinWidth, MaxWidth),
            Math.Clamp(contentAvailable.Height, MinHeight, MaxHeight));

        // Measure content
        var contentSize = MeasureOverride(contentAvailable);

        // Apply constraints to result
        var resultWidth = double.IsNaN(Width) ? contentSize.Width : Width;
        var resultHeight = double.IsNaN(Height) ? contentSize.Height : Height;

        resultWidth = Math.Clamp(resultWidth, MinWidth, MaxWidth);
        resultHeight = Math.Clamp(resultHeight, MinHeight, MaxHeight);

        return new Size(
            Math.Max(0, resultWidth + marginWidth),
            Math.Max(0, resultHeight + marginHeight));
    }

    /// <inheritdoc />
    protected override Size ArrangeCore(Rect finalRect)
    {
        var margin = Margin;
        var marginWidth = margin.Left + margin.Right;
        var marginHeight = margin.Top + margin.Bottom;

        // Calculate available size for content
        var availableWidth = Math.Max(0, finalRect.Width - marginWidth);
        var availableHeight = Math.Max(0, finalRect.Height - marginHeight);

        // Get the desired size (set during Measure)
        var desiredWidth = DesiredSize.Width - marginWidth;
        var desiredHeight = DesiredSize.Height - marginHeight;

        // Determine arrange size based on alignment
        // When alignment is Stretch, use available size; otherwise use desired size (clamped to available)
        var arrangeWidth = HorizontalAlignment == HorizontalAlignment.Stretch
            ? availableWidth
            : Math.Min(desiredWidth, availableWidth);

        var arrangeHeight = VerticalAlignment == VerticalAlignment.Stretch
            ? availableHeight
            : Math.Min(desiredHeight, availableHeight);

        var arrangeSize = new Size(arrangeWidth, arrangeHeight);

        // Apply explicit size constraints
        if (!double.IsNaN(Width))
        {
            arrangeSize = new Size(Width, arrangeSize.Height);
        }
        if (!double.IsNaN(Height))
        {
            arrangeSize = new Size(arrangeSize.Width, Height);
        }

        // Apply min/max constraints
        arrangeSize = new Size(
            Math.Clamp(arrangeSize.Width, MinWidth, MaxWidth),
            Math.Clamp(arrangeSize.Height, MinHeight, MaxHeight));

        // Arrange content
        var renderSize = ArrangeOverride(arrangeSize);

        // Calculate visual bounds based on alignment
        var x = finalRect.X + margin.Left;
        var y = finalRect.Y + margin.Top;

        // Horizontal alignment
        var extraWidth = availableWidth - renderSize.Width;
        if (extraWidth > 0)
        {
            switch (HorizontalAlignment)
            {
                case HorizontalAlignment.Center:
                    x += extraWidth / 2;
                    break;
                case HorizontalAlignment.Right:
                    x += extraWidth;
                    break;
            }
        }

        // Vertical alignment
        var extraHeight = availableHeight - renderSize.Height;
        if (extraHeight > 0)
        {
            switch (VerticalAlignment)
            {
                case VerticalAlignment.Center:
                    y += extraHeight / 2;
                    break;
                case VerticalAlignment.Bottom:
                    y += extraHeight;
                    break;
            }
        }

        // Keep the arranged origin as continuous floating-point. Rounding here turns
        // smooth animations (e.g. a spring oscillating margin by ±0.4px) into 1px
        // step jitter because consecutive frames snap to different integer pixels.
        // The renderer is responsible for sub-pixel positioning / AA — that's its
        // job. Static layouts don't suffer from "drift" because the inputs to x/y
        // don't actually drift when nothing is animating; this comment used to claim
        // otherwise but the real defect it was masking was elsewhere.
        // (WPF parity: layout rounding is opt-in via UseLayoutRounding, off by default.)
        _visualBounds = new Rect(x, y, renderSize.Width, renderSize.Height);

        // Update _renderSize BEFORE firing SizeChanged so that handlers
        // reading ActualWidth/ActualHeight/RenderSize see the new values.
        _renderSize = renderSize;

        // Check for size change and raise SizeChanged event
        if (renderSize != _previousRenderSize)
        {
            var widthChanged = renderSize.Width != _previousRenderSize.Width;
            var heightChanged = renderSize.Height != _previousRenderSize.Height;

            var sizeInfo = new SizeChangedInfo(this, _previousRenderSize, widthChanged, heightChanged);
            _previousRenderSize = renderSize;

            OnSizeChanged(sizeInfo);
        }

        return renderSize;
    }

    /// <summary>
    /// Override to implement custom measure logic.
    /// </summary>
    /// <param name="availableSize">The available size.</param>
    /// <returns>The desired size.</returns>
    protected virtual Size MeasureOverride(Size availableSize)
    {
        return Size.Empty;
    }

    /// <summary>
    /// Override to implement custom arrange logic.
    /// </summary>
    /// <param name="finalSize">The final size.</param>
    /// <returns>The render size.</returns>
    protected virtual Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    #endregion

    #region Hit Testing

    /// <inheritdoc />
    protected override HitTestResult? HitTestCore(Point point)
    {
        // 整个子树的"接收输入"开关：Visibility 不可见或显式拒收 → 子和自己都不会被命中。
        if (Visibility != Visibility.Visible || !IsHitTestVisible)
        {
            return null;
        }

        // Transform point to local coordinates (relative to this element)
        var localPoint = new Point(point.X - _visualBounds.X, point.Y - _visualBounds.Y);

        // Layout clip 是真正的"硬遮罩"——渲染时父对自己 + children 都 PushClip(GetLayoutClip())，
        // 视觉上看不见的内容就不该接收输入。典型场景：TextBox 被 ScrollViewer 滚出视口、
        // 标题栏被 Clip 切掉一半。clip 之外的子也算被遮挡 → 整个子树 return null。
        if (!IsPointInsideLayoutClip(localPoint))
        {
            return null;
        }

        // ⚠ WPF 一致性关键：child 递归不受 self _visualBounds.Contains 约束。
        // 子元素可以通过负 Margin、Canvas.Left/Top、RenderTransform(Scale/Translate) 自由
        // 落到 self 名义 bounds 之外（典型：ZoomableCanvas 内 Canvas 名义 822×209，
        // 缩放平移后视觉区域和 layout size 完全脱钩；积木 BlockItem 负下边距让末块底部
        // 凸起落在父 StackPanel bounds 之外）。如果在这里用 `if (!_visualBounds.Contains
        // (point)) return null;` 拦截，整个子树就被一刀切，深嵌套 / 重叠 / 平移的子完全
        // 不可点。正确做法：先无条件递归 children，children 不命中再回头判定 self。
        for (int i = VisualChildrenCount - 1; i >= 0; i--)
        {
            var child = GetVisualChild(i);
            if (child is FrameworkElement fe)
            {
                // 子有 RenderTransform 则反向变换 localPoint 到子的"原始"局部空间，
                // 与渲染时父对子做 PushTransform(child.RenderTransform, originX, originY) 对偶。
                var pointForChild = localPoint;
                var childTransform = fe.RenderTransform;
                if (childTransform != null)
                {
                    var matrix = childTransform.Value;
                    if (matrix.TryInvert(out var inv))
                    {
                        var origin = fe.RenderTransformOrigin;
                        var size = fe.RenderSize;
                        var originAbsX = fe._visualBounds.X + origin.X * size.Width;
                        var originAbsY = fe._visualBounds.Y + origin.Y * size.Height;

                        var translated = new Point(localPoint.X - originAbsX, localPoint.Y - originAbsY);
                        var inverted = inv.Transform(translated);
                        pointForChild = new Point(inverted.X + originAbsX, inverted.Y + originAbsY);
                    }
                    else
                    {
                        // 非可逆 transform（如 ScaleTransform 0）：子整体不可见，跳过。
                        continue;
                    }
                }

                var childResult = fe.HitTestCore(pointForChild);
                if (childResult != null)
                {
                    return childResult;
                }
            }
        }

        // children 没命中 → 再判定 self：必须落在自己的 _visualBounds 内才算命中 self。
        // 与 WPF 一致：self bounds 决定的是"是否命中 self"，不参与决定"能否递归 children"。
        // Panel 等无 Background 控件还有第二层过滤 (Panel.HitTestCore override)，把"hit 自己
        // 又无 Background"的情况进一步丢弃，让下层 z-order sibling 接住空白点击。
        if (!_visualBounds.Contains(point))
        {
            return null;
        }

        return HitTestResult.GetReusable(this);
    }

    /// <summary>
    /// Performs hit testing at the specified point.
    /// </summary>
    /// <param name="point">The point to test in this element's coordinate space.</param>
    /// <returns>The hit test result, or null if nothing was hit.</returns>
    public HitTestResult? HitTest(Point point)
    {
        return HitTestCore(point);
    }

    #endregion

    #region BringIntoView

    /// <summary>
    /// Identifies the RequestBringIntoView routed event.
    /// </summary>
    public static readonly RoutedEvent RequestBringIntoViewEvent =
        EventManager.RegisterRoutedEvent(nameof(RequestBringIntoView), RoutingStrategy.Bubble,
            typeof(RequestBringIntoViewEventHandler), typeof(FrameworkElement));

    /// <summary>
    /// Occurs when BringIntoView is called on this element.
    /// </summary>
    public event RequestBringIntoViewEventHandler RequestBringIntoView
    {
        add => AddHandler(RequestBringIntoViewEvent, value);
        remove => RemoveHandler(RequestBringIntoViewEvent, value);
    }

    /// <summary>
    /// Attempts to bring this element into view, within any scrollable regions it is contained within.
    /// </summary>
    public void BringIntoView()
    {
        BringIntoView(Rect.Empty);
    }

    /// <summary>
    /// Attempts to bring the provided region size of this element into view,
    /// within any scrollable regions it is contained within.
    /// </summary>
    /// <param name="targetRectangle">The rectangular region to bring into view. Use Rect.Empty for the entire element.</param>
    public void BringIntoView(Rect targetRectangle)
    {
        var args = new RequestBringIntoViewEventArgs(RequestBringIntoViewEvent, this)
        {
            TargetObject = this,
            TargetRect = targetRectangle.IsEmpty ? new Rect(0, 0, ActualWidth, ActualHeight) : targetRectangle
        };

        RaiseEvent(args);
    }

    /// <summary>
    /// Calculates this element's position relative to an ancestor element.
    /// </summary>
    /// <param name="ancestor">The ancestor element. If null, calculates to the root.</param>
    /// <returns>The position offset relative to the ancestor.</returns>
    public Point TransformToAncestor(Visual? ancestor)
    {
        double x = 0;
        double y = 0;

        Visual? current = this;
        while (current != null && current != ancestor)
        {
            if (current is FrameworkElement fe)
            {
                x += fe._visualBounds.X;
                y += fe._visualBounds.Y;
            }

            if (current.VisualParent == null)
                break;

            current = current.VisualParent;
        }

        return new Point(x, y);
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            element.InvalidateMeasure();
        }
    }

    private static void OnDataContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            element.OnDataContextChanged(e.OldValue, e.NewValue);
            element.DataContextChanged?.Invoke(element, e);

            // Propagate DataContext change to descendants that don't have their own DataContext.
            // In WPF, DataContext is an inherited dependency property, so children automatically
            // see the change. Since Jalium.UI uses a visual tree walk-up approach instead,
            // we need to manually notify descendants so their bindings can re-resolve.
            PropagateDataContextToDescendants(element, e);
        }
    }

    /// <summary>
    /// Propagates DataContext changes to descendant elements that rely on inherited DataContext.
    /// Only descendants without their own explicit DataContext are notified.
    /// </summary>
    private static void PropagateDataContextToDescendants(Visual parent, DependencyPropertyChangedEventArgs e)
    {
        var childCount = parent.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = parent.GetVisualChild(i);
            if (child is FrameworkElement childElement)
            {
                // Only propagate if the child doesn't have its own explicit (local) DataContext.
                // Cannot use `childElement.DataContext == null` because DataContext is inherited
                // via the visual tree — GetValue returns the parent's DataContext, which is non-null
                // when the parent has one, causing this check to wrongly skip propagation.
                if (!childElement.HasLocalValue(DataContextProperty))
                {
                    // Reactivate any Unattached bindings that couldn't resolve earlier
                    // (they haven't subscribed to DataContextChanged yet)
                    childElement.ReactivateBindings();

                    // Fire DataContextChanged on the child so its active bindings re-resolve
                    childElement.OnDataContextChanged(e.OldValue, e.NewValue);
                    childElement.DataContextChanged?.Invoke(childElement, e);

                    // Continue propagating to this child's descendants
                    PropagateDataContextToDescendants(childElement, e);
                }
            }
            else if (child != null)
            {
                // Non-FrameworkElement visuals: still propagate to their children
                PropagateDataContextToDescendants(child, e);
            }
        }
    }

    /// <summary>
    /// Called when the DataContext property changes.
    /// </summary>
    protected virtual void OnDataContextChanged(object? oldValue, object? newValue)
    {
    }

    /// <summary>
    /// Occurs when the DataContext property changes.
    /// </summary>
    public event EventHandler<DependencyPropertyChangedEventArgs>? DataContextChanged;

    private static void OnStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            // Remove implicit style if explicit style is being set
            if (e.NewValue != null && element._implicitStyle != null)
            {
                element._implicitStyle.Remove(element);
                element._implicitStyle = null;
            }

            // Remove old style
            if (e.OldValue is Style oldStyle)
            {
                oldStyle.Remove(element);
            }

            // Apply new style
            if (e.NewValue is Style newStyle)
            {
                newStyle.Apply(element);
            }
            else
            {
                // If explicit style is cleared, try to apply implicit style
                element.ApplyImplicitStyleIfNeeded();
            }

            element.InvalidateVisual();
        }
    }

    #endregion

    #region Visual Parent Changed

    /// <inheritdoc />
    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);

        // Invalidate cached Window/LayoutManager references for this subtree
        InvalidateHostCaches();

        // Reactivate bindings when visual parent changes
        // This allows DataContext and RelativeSource FindAncestor bindings to resolve
        // after being added to the visual tree
        if (VisualParent != null)
        {
            // Re-evaluate implicit styles for this entire subtree.
            // During XAML parsing, children are added bottom-up: a Button is added
            // to a StackPanel before the StackPanel is connected to the Window.
            // The initial implicit style lookup may only reach Application resources
            // (theme styles) because the ancestor with user resources isn't reachable
            // yet. When the subtree is later connected to the full tree, we must
            // re-evaluate so that closer-scope user-defined implicit styles take
            // precedence over theme styles.
            ReEvaluateImplicitStylesRecursive(this);

            // Re-resolve dynamic resources for this entire subtree, for the SAME reason the
            // implicit-style pass above runs: attaching to a visual parent widens the
            // resource-lookup scope to include ancestor / application resources that were
            // unreachable while the subtree was detached. A {DynamicResource} written inline
            // as element content (e.g. <Path Stroke="{DynamicResource ...}">) is resolved
            // only once, at construction time. If it resolved to null then — because
            // Application.Current / its resources were not yet reachable at that instant —
            // it would otherwise stay null forever (Shape.Stroke/Fill default to null, so
            // the shape simply draws nothing), since nothing re-resolves inline dynamic
            // resources on attach. This mirrors WPF, which re-evaluates resource references
            // when the tree changes.
            RefreshDynamicResourcesRecursive(this);

            // Recursively reactivate bindings on this element and ALL descendants,
            // because descendants may have bindings that depend on DataContext
            // inherited from an ancestor that is now reachable via the visual tree
            ReactivateBindingsRecursive(this);

            // Reattaching only this root is not sufficient when descendants remain
            // "valid" with cached geometry from a prior host. Mark descendants dirty
            // so the next pass cannot skip their measure/arrange.
            MarkDescendantLayoutInvalidForReattach(this);

            // Reparented elements must be re-measured/re-arranged in the new host tree.
            InvalidateMeasure();
            InvalidateArrange();
            InvalidateVisual();

            // Defer Loaded event until after layout completes.
            // WPF fires Loaded after the first Measure/Arrange pass, so
            // ActualWidth/ActualHeight are available in handlers.
            var dispatcher = Dispatcher.CurrentDispatcher;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(() => Loaded?.Invoke(this, new RoutedEventArgs()));
            }
            else
            {
                Loaded?.Invoke(this, new RoutedEventArgs());
            }
        }
        else if (oldParent != null)
        {
            // Release mouse capture when removed from the visual tree
            // to prevent orphan capture references and stuck interaction state
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            // Remove from LayoutManager queues when detached from tree
            RemoveFromLayoutManager(oldParent);

            Unloaded?.Invoke(this, new RoutedEventArgs());
        }
    }

    /// <summary>
    /// Removes this element subtree from the LayoutManager's queues when detached from the visual tree.
    /// </summary>
    private void RemoveFromLayoutManager(Visual oldParent)
    {
        Visual? current = oldParent;
        while (current != null)
        {
            if (current is ILayoutManagerHost host)
            {
                RemoveSubtreeFromLayoutManager(host.LayoutManager, this);
                return;
            }
            current = current.VisualParent;
        }
    }

    private static void RemoveSubtreeFromLayoutManager(LayoutManager layoutManager, Visual root)
    {
        if (root is UIElement element)
        {
            layoutManager.Remove(element);
        }

        var childCount = root.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = root.GetVisualChild(i);
            if (child != null)
            {
                RemoveSubtreeFromLayoutManager(layoutManager, child);
            }
        }
    }

    /// <summary>
    /// Recursively reactivates bindings on the given element and all its visual descendants.
    /// </summary>
    private static void ReactivateBindingsRecursive(Visual visual)
    {
        if (visual is DependencyObject depObj)
        {
            depObj.ReactivateBindings();
        }

        var childCount = visual.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = visual.GetVisualChild(i);
            if (child != null)
            {
                ReactivateBindingsRecursive(child);
            }
        }
    }

    private static void MarkDescendantLayoutInvalidForReattach(Visual root)
    {
        var childCount = root.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = root.GetVisualChild(i);
            if (child != null)
            {
                MarkSubtreeLayoutInvalid(child);
            }
        }
    }

    private static void MarkSubtreeLayoutInvalid(Visual visual)
    {
        if (visual is UIElement uiElement)
        {
            uiElement.MarkMeasureInvalid();
        }

        var childCount = visual.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = visual.GetVisualChild(i);
            if (child != null)
            {
                MarkSubtreeLayoutInvalid(child);
            }
        }
    }


    /// <summary>
    /// Applies an implicit style to this element if no explicit style is set.
    /// Called from OnVisualParentChanged and also by Window.Show() to handle
    /// elements created before the theme was loaded.
    /// </summary>
    internal void ApplyImplicitStyleIfNeeded()
    {
        // Explicit style set — nothing to do.
        if (Style != null)
            return;

        // Already have an implicit style — skip first-time application.
        // Re-evaluation after tree changes is handled by ReEvaluateImplicitStyle().
        if (_implicitStyle != null)
            return;

        var implicitStyle = LookupImplicitStyle();
        if (implicitStyle != null)
        {
            _implicitStyle = implicitStyle;
            implicitStyle.Apply(this);
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Re-evaluates the implicit style for this element after a tree change.
    /// If a closer-scope implicit style is now available (e.g., user-defined style
    /// in Window.Resources after the subtree was connected), it replaces the
    /// previously applied style (e.g., theme style from Application.Resources).
    /// </summary>
    private void ReEvaluateImplicitStyle()
    {
        if (Style != null)
            return;

        var newImplicit = LookupImplicitStyle();
        if (newImplicit == null)
        {
            // No implicit style found; apply first-time if needed.
            ApplyImplicitStyleIfNeeded();
            return;
        }

        if (ReferenceEquals(_implicitStyle, newImplicit))
            return; // Same style — no change needed.

        // Different (closer-scope) implicit style found — swap.
        if (_implicitStyle != null)
        {
            _implicitStyle.Remove(this);
        }

        _implicitStyle = newImplicit;
        newImplicit.Apply(this);
        InvalidateVisual();
    }

    /// <summary>
    /// Looks up the implicit style for this element by walking up the type
    /// hierarchy and searching resources.
    /// </summary>
    private Style? LookupImplicitStyle()
    {
        var currentType = GetType();
        while (currentType != null && currentType != typeof(FrameworkElement))
        {
            var style = TryFindResource(currentType) as Style;
            if (style != null && IsStyleApplicable(style))
                return style;
            currentType = currentType.BaseType;
        }
        return null;
    }

    /// <summary>
    /// Recursively re-evaluates implicit styles for the given subtree.
    /// Called when a subtree gains a visual parent, since the resource lookup
    /// scope may now include ancestor resources that were unreachable before.
    /// </summary>
    private static void ReEvaluateImplicitStylesRecursive(Visual visual)
    {
        if (visual is FrameworkElement fe)
        {
            fe.ReEvaluateImplicitStyle();
        }

        for (int i = 0; i < visual.VisualChildrenCount; i++)
        {
            var child = visual.GetVisualChild(i);
            if (child != null)
            {
                ReEvaluateImplicitStylesRecursive(child);
            }
        }
    }

    /// <summary>
    /// Recursively re-resolves dynamic-resource references for the given subtree.
    /// Companion to <see cref="ReEvaluateImplicitStylesRecursive"/>: when a subtree gains a
    /// visual parent, its resource-lookup scope may now reach ancestor / application
    /// resources that were unreachable before, so every dynamic-resource subscription that
    /// previously resolved to null is retried. Cheap for elements without subscriptions
    /// (a single dictionary probe) and idempotent for properties already resolved.
    /// </summary>
    private static void RefreshDynamicResourcesRecursive(Visual visual)
    {
        if (visual is FrameworkElement fe)
        {
            DynamicResourceBindingOperations.RefreshElement(fe);
        }

        for (int i = 0; i < visual.VisualChildrenCount; i++)
        {
            var child = visual.GetVisualChild(i);
            if (child != null)
            {
                RefreshDynamicResourcesRecursive(child);
            }
        }
    }

    /// <summary>
    /// Checks if a style is applicable to this element.
    /// </summary>
    private bool IsStyleApplicable(Style style)
    {
        if (style.TargetType == null)
            return true;

        return style.TargetType.IsAssignableFrom(GetType());
    }

    #endregion

    #region Compiled Bundle Support

    private object? _compiledBundle;

    /// <summary>
    /// Render callback for compiled bundle. Set by Jalium.UI.Xaml to inject rendering logic.
    /// </summary>
    private Action<DrawingContext>? _bundleRenderCallback;

    /// <summary>
    /// Gets the compiled UI bundle associated with this element, if any.
    /// The bundle is stored as object to avoid circular dependencies with Jalium.UI.Gpu.
    /// </summary>
    public object? CompiledBundle => _compiledBundle;

    /// <summary>
    /// Sets the compiled UI bundle for this element.
    /// This is called by the generated InitializeComponent method when using Source Generator embedded binary data.
    /// </summary>
    /// <param name="bundle">The compiled UI bundle to associate with this element (typically Jalium.UI.Gpu.CompiledUIBundle).</param>
    public void SetCompiledBundle(object bundle)
    {
        _compiledBundle = bundle;

        // Mark the element as needing a visual update
        InvalidateVisual();
    }

    /// <summary>
    /// Sets the render callback for bundle rendering.
    /// Called by XamlReader.ApplyBundle to inject rendering logic from Jalium.UI.Xaml.
    /// </summary>
    /// <param name="callback">The callback that renders the bundle to a DrawingContext.</param>
    public void SetBundleRenderCallback(Action<DrawingContext>? callback)
    {
        _bundleRenderCallback = callback;
    }

    /// <summary>
    /// Override to render the compiled bundle.
    /// </summary>
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        // Invoke the bundle render callback if set
        _bundleRenderCallback?.Invoke(drawingContext);
    }

    #endregion
}

/// <summary>
/// Specifies horizontal alignment.
/// </summary>
public enum HorizontalAlignment
{
    Left,
    Center,
    Right,
    Stretch
}

/// <summary>
/// Specifies vertical alignment.
/// </summary>
public enum VerticalAlignment
{
    Top,
    Center,
    Bottom,
    Stretch
}

/// <summary>
/// Provides data for the RequestBringIntoView event.
/// </summary>
public sealed class RequestBringIntoViewEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the object that should be made visible.
    /// </summary>
    public DependencyObject? TargetObject { get; init; }

    /// <summary>
    /// Gets the rectangular region in the object's coordinate space which should be made visible.
    /// </summary>
    public Rect TargetRect { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestBringIntoViewEventArgs"/> class.
    /// </summary>
    public RequestBringIntoViewEventArgs(RoutedEvent routedEvent, object source)
        : base(routedEvent, source)
    {
    }
}

/// <summary>
/// Delegate for handling RequestBringIntoView events.
/// </summary>
public delegate void RequestBringIntoViewEventHandler(object sender, RequestBringIntoViewEventArgs e);

/// <summary>
/// Delegate for handling SizeChanged events.
/// </summary>
public delegate void SizeChangedEventHandler(object sender, SizeChangedEventArgs e);

/// <summary>
/// Provides data for the SizeChanged event.
/// </summary>
public sealed class SizeChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SizeChangedEventArgs"/> class.
    /// </summary>
    /// <param name="info">Information about the size change.</param>
    public SizeChangedEventArgs(SizeChangedInfo info)
    {
        PreviousSize = info.PreviousSize;
        NewSize = info.NewSize;
        WidthChanged = info.WidthChanged;
        HeightChanged = info.HeightChanged;
    }

    /// <summary>
    /// Gets the previous size of the element.
    /// </summary>
    public Size PreviousSize { get; }

    /// <summary>
    /// Gets the new size of the element.
    /// </summary>
    public Size NewSize { get; }

    /// <summary>
    /// Gets a value indicating whether the width component changed.
    /// </summary>
    public bool WidthChanged { get; }

    /// <summary>
    /// Gets a value indicating whether the height component changed.
    /// </summary>
    public bool HeightChanged { get; }
}

/// <summary>
/// Contains information about a size change.
/// </summary>
public sealed class SizeChangedInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SizeChangedInfo"/> class.
    /// </summary>
    /// <param name="element">The element whose size changed.</param>
    /// <param name="previousSize">The previous size.</param>
    /// <param name="widthChanged">Whether the width changed.</param>
    /// <param name="heightChanged">Whether the height changed.</param>
    public SizeChangedInfo(UIElement element, Size previousSize, bool widthChanged, bool heightChanged)
    {
        Element = element;
        PreviousSize = previousSize;
        WidthChanged = widthChanged;
        HeightChanged = heightChanged;
    }

    /// <summary>
    /// Gets the element that was measured.
    /// </summary>
    public UIElement Element { get; }

    /// <summary>
    /// Gets the previous size of the element before the size change.
    /// </summary>
    public Size PreviousSize { get; }

    /// <summary>
    /// Gets the new size of the element. This is the element's current RenderSize.
    /// </summary>
    public Size NewSize => Element.RenderSize;

    /// <summary>
    /// Gets a value indicating whether the Width component of the size changed.
    /// </summary>
    public bool WidthChanged { get; }

    /// <summary>
    /// Gets a value indicating whether the Height component of the size changed.
    /// </summary>
    public bool HeightChanged { get; }
}

