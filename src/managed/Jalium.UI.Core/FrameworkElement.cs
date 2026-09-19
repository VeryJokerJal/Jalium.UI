using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using System.Collections;
using System.ComponentModel;
using Jalium.UI.Data;
using Jalium.UI.Markup;
using Jalium.UI.Media.Animation;
using AnimationHandoffBehavior = Jalium.UI.Media.Animation.HandoffBehavior;

namespace Jalium.UI;

/// <summary>
/// Provides a framework-level set of properties, events, and methods for UI elements.
/// </summary>
[Markup.XmlLangProperty(nameof(Language))]
public partial class FrameworkElement : UIElement, IFrameworkInputElement, Markup.IQueryAmbient, ISupportInitialize
{
    private static readonly ConditionalWeakTable<
        Jalium.UI.Threading.Dispatcher,
        PendingLoadedQueue> PendingLoadedQueues = new();

    // Virtualizing panels detach generated containers into a recycle pool and later attach the
    // same retained visual subtree back to the same panel. A normal reparent must recursively
    // refresh styles/resources/bindings, but that work is redundant (and very expensive) when
    // the resource scope is provably unchanged. The marker is opt-in and is set only by a
    // recycling panel immediately before it removes a container.
    private DependencyObject? _virtualizationRecycleParent;
    private int _virtualizationRecycleResourceGeneration;
    private ulong _virtualizationRecycleScopeFingerprint;

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
    /// Pixel snapping is DISABLED. This method returns the value unchanged
    /// (identity), keeping layout in continuous floating-point space. It is
    /// retained — rather than removed — so the many Border / StackPanel call
    /// sites that snapped <see cref="Thickness"/> sides and arrange offsets
    /// stay in agreement without per-site edits.
    /// </summary>
    /// <remarks>
    /// Forcing arranged children and the geometry painted around them onto the
    /// physical-pixel grid quantizes smooth animations (e.g. a spring sweeping
    /// a Margin by sub-pixel amounts) into 1px step jitter. The native renderer
    /// performs sub-pixel positioning / AA at draw time, so fractional layout
    /// values render cleanly. Matches WPF, where layout rounding is opt-in via
    /// UseLayoutRounding (default off).
    /// </remarks>
    internal static double SnapLayoutValue(double value)
        => double.IsFinite(value) ? value : 0;

    #region Dependency Properties

    /// <summary>
    /// ValidateValueCallback for <see cref="WidthProperty"/>/<see cref="HeightProperty"/>:
    /// NaN (auto) or a non-negative finite number. Mirrors WPF's
    /// <c>FrameworkElement.IsWidthHeightValid</c> — rejecting the value at the property
    /// boundary keeps a negative or infinite explicit size from flowing into layout math
    /// and surfacing much later as a Size-constructor throw mid measure/arrange.
    /// Also used by panels whose per-item size shares the same "NaN means auto" contract
    /// (e.g. <c>WrapPanel.ItemWidth</c>/<c>ItemHeight</c>).
    /// </summary>
    internal static bool IsWidthHeightValid(object? value)
        => value is double v && (double.IsNaN(v) || (v >= 0.0 && !double.IsPositiveInfinity(v)));

    /// <summary>
    /// ValidateValueCallback for <see cref="MinWidthProperty"/>/<see cref="MinHeightProperty"/>:
    /// a non-negative finite number — unlike Width/Height, NaN is not a valid minimum.
    /// Mirrors WPF's <c>FrameworkElement.IsMinWidthHeightValid</c>.
    /// </summary>
    internal static bool IsMinWidthHeightValid(object? value)
        => value is double v && !double.IsNaN(v) && v >= 0.0 && !double.IsPositiveInfinity(v);

    /// <summary>
    /// ValidateValueCallback for <see cref="MaxWidthProperty"/>/<see cref="MaxHeightProperty"/>:
    /// non-negative and not NaN; PositiveInfinity is the (default) "unconstrained" value.
    /// Mirrors WPF's <c>FrameworkElement.IsMaxWidthHeightValid</c>.
    /// </summary>
    internal static bool IsMaxWidthHeightValid(object? value)
        => value is double v && !double.IsNaN(v) && v >= 0.0;

    /// <summary>
    /// Identifies the Width dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register(nameof(Width), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(double.NaN, OnLayoutPropertyChanged), IsWidthHeightValid);

    /// <summary>
    /// Identifies the Height dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty HeightProperty =
        DependencyProperty.Register(nameof(Height), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(double.NaN, OnLayoutPropertyChanged), IsWidthHeightValid);

    /// <summary>
    /// Identifies the MinWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinWidthProperty =
        DependencyProperty.Register(nameof(MinWidth), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged), IsMinWidthHeightValid);

    /// <summary>
    /// Identifies the MinHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinHeightProperty =
        DependencyProperty.Register(nameof(MinHeight), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged), IsMinWidthHeightValid);

    /// <summary>
    /// Identifies the MaxWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaxWidthProperty =
        DependencyProperty.Register(nameof(MaxWidth), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(double.PositiveInfinity, OnLayoutPropertyChanged), IsMaxWidthHeightValid);

    /// <summary>
    /// Identifies the MaxHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaxHeightProperty =
        DependencyProperty.Register(nameof(MaxHeight), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(double.PositiveInfinity, OnLayoutPropertyChanged), IsMaxWidthHeightValid);

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
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ContextMenuProperty =
        DependencyProperty.Register(nameof(ContextMenu), typeof(ContextMenu), typeof(FrameworkElement),
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
            new PropertyMetadata(null, OnFocusVisualStyleChanged));

    /// <summary>
    /// Raised when the effective focus visual style of an element that currently holds keyboard
    /// focus may have changed — a <see cref="FocusVisualStyle"/> value change, or a style-stack
    /// change (explicit / implicit / theme style applied or removed, which is how a
    /// <c>&lt;Setter Property="FocusVisualStyle"/&gt;</c> arrives, including <c>{x:Null}</c>
    /// which does not move the effective value and therefore never fires the property callback).
    /// Consumed by the focus visual manager in Controls to rebuild the ring instead of leaving the
    /// one created at focus time in place.
    /// </summary>
    internal static event Action<FrameworkElement>? FocusVisualStyleInvalidatedStatic;

    private static void OnFocusVisualStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
            element.NotifyFocusVisualStyleInvalidated();
    }

    private void NotifyFocusVisualStyleInvalidated()
    {
        if (IsKeyboardFocused)
            FocusVisualStyleInvalidatedStatic?.Invoke(this);
    }

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
    private bool _hasActualSizeValues;

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
        // Size changes are extremely common during layout, while listeners are
        // uncommon. Keep the observable paths intact, but do not allocate an
        // event-args object or enter routed-event dispatch when nobody can see it.
        if (SizeChanged is null
            && !HasRoutedEventHandler(SizeChangedEvent)
            && EventManager.GetClassHandlers(SizeChangedEvent, GetType()).Length == 0
            && !Diagnostics.RoutedEventDiagnostics.IsRecording)
        {
            return;
        }

        var args = new SizeChangedEventArgs(sizeInfo)
        {
            RoutedEvent = SizeChangedEvent,
            Source = this,
        };
        RaiseEvent(args);
        SizeChanged?.Invoke(this, args);
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
    /// The implicit style applied to this element based on its type. Never references
    /// the theme default style — that lives in <see cref="_themeStyle"/> so an explicit
    /// or implicit user style layers ON TOP of the theme defaults instead of replacing them.
    /// </summary>
    private Style? _implicitStyle;

    /// <summary>
    /// The theme default style (from the framework theme dictionary) currently applied
    /// as the bottom layer of this element's style stack. WPF parity: an explicit
    /// <see cref="Style"/> only overrides the properties it sets; everything else —
    /// most critically the control Template — still comes from the theme default style.
    /// </summary>
    private Style? _themeStyle;

    /// <summary>
    /// Per-element CSS engine state (classes, inline declarations, values the engine owns).
    /// Null until CSS first touches this element, so non-CSS apps pay one pointer field.
    /// </summary>
    internal Jalium.UI.Styling.CssElementState? CssRuntimeState;

    /// <summary>
    /// Resolves the theme default style for a concrete element type. Injected by
    /// Jalium.UI.Controls' ThemeManager (Core cannot reference the theme dictionaries
    /// directly). Returns null when no theme is loaded or the type has no default style.
    /// The FrameworkElement side walks the base-type chain; the resolver only needs to
    /// answer for the exact type it is given.
    /// </summary>
    internal static Func<Type, Style?>? ThemeStyleResolver { get; set; }

    /// <summary>
    /// The element that owns the template in which this element is defined.
    /// </summary>
    private FrameworkElement? _templatedParent;

    /// <summary>
    /// Named elements registered in this element's scope (when it's a template root).
    /// </summary>
    private Dictionary<string, object>? _namedElements;

    #endregion

    #region Template Properties

    /// <summary>
    /// Gets the element that owns the template in which this element is defined.
    /// </summary>
    public DependencyObject? TemplatedParent => _templatedParent;

    /// <summary>
    /// Sets the templated parent. This is called internally when applying templates.
    /// </summary>
    internal void SetTemplatedParent(FrameworkElement? parent) =>
        SetTemplatedParent(parent, reactivateBindings: true);

    /// <summary>
    /// Sets the templated parent. This is called internally when applying templates.
    /// </summary>
    /// <param name="parent">The element that owns the template this element belongs to.</param>
    /// <param name="reactivateBindings">
    /// 是否顺带重算本元素的绑定。
    /// <para>只有当调用方能保证「本次调用返回后、控制权交还给应用代码之前，必定还会有一趟覆盖
    /// 本元素的绑定重算」时才可传 false。目前唯一这样的调用方是模板展开
    /// （<c>Control.ApplyTemplateCore</c> / <c>Page.RebuildVisualTree</c>）：它在设完
    /// TemplatedParent 之后**立刻** <c>AddVisualChild(templateRoot)</c>，由
    /// <see cref="OnVisualParentChanged"/> 对整棵可视子树重算一遍；够不到的非可视子节点
    /// （Popup.Child 等）再由随后的 <c>Control.ReactivateBindingsRecursive</c> 兜住。
    /// 于是同一批元素从「重算三遍」降到「重算一遍」，而三趟依然全部在模板展开返回前完成。</para>
    /// </param>
    internal void SetTemplatedParent(FrameworkElement? parent, bool reactivateBindings)
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
        if (parent != null && reactivateBindings)
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
    /// <param name="scopedElement">The object to register.</param>
    public void RegisterName(string name, object scopedElement)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(scopedElement);

        if (NameScope.GetNameScope(this) is { } nameScope)
        {
            nameScope.RegisterName(name, scopedElement);
            return;
        }

        _namedElements ??= new Dictionary<string, object>(StringComparer.Ordinal);
        if (!_namedElements.TryAdd(name, scopedElement))
        {
            throw new ArgumentException($"Name '{name}' is already registered in this scope.", nameof(name));
        }
    }

    /// <summary>
    /// Unregisters a named element from this element's template scope.
    /// </summary>
    /// <param name="name">The name to unregister.</param>
    public void UnregisterName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (NameScope.GetNameScope(this) is { } nameScope)
        {
            nameScope.UnregisterName(name);
            return;
        }

        if (_namedElements == null || !_namedElements.Remove(name))
        {
            throw new ArgumentException($"Name '{name}' was not found in this scope.", nameof(name));
        }
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
        if (FrameworkParent is FrameworkElement parent && parent != _templatedParent)
        {
            return parent.FindName(name);
        }

        return null;
    }

    #endregion

    #region Property Inheritance

    private static long s_inheritanceTreeVersion;
    internal static long InheritanceTreeVersion => Volatile.Read(ref s_inheritanceTreeVersion);
    private Dictionary<DependencyProperty, InheritedSourceEntry>? _inheritedSources;
    private readonly record struct InheritedSourceEntry(
        long PropertyVersion, long TreeVersion, WeakReference<FrameworkElement>? Source);

    /// <inheritdoc />
    public override object? GetValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        // DependencyObject.GetValue resolves through the virtual
        // GetUncoercedBaseValueInternal method below, so inheritance and mutable
        // brush ownership are already handled in a single pass. The former
        // preflight called GetValueSourceInternal first and then resolved the
        // same property again, doubling dictionary lookups and coercion callbacks
        // for every FrameworkElement property read.
        return base.GetValue(dp);
    }

    internal override ValueSource GetValueSourceInternal(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);

        var localSource = base.GetValueSourceInternal(dp);
        if (localSource.BaseValueSource != BaseValueSource.Default || localSource.IsAnimated)
            return localSource;

        if (dp.GetMetadata(GetType()).Inherits && FrameworkParent is FrameworkElement parent)
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

        if (dp.GetMetadata(GetType()).Inherits && FrameworkParent is FrameworkElement parent)
        {
            var source = FindInheritedSource(parent, dp);
            if (source != null && TryGetInheritedBaseValue(source, dp, out var inheritedValue))
            {
                return (inheritedValue, BaseValueSource.Inherited);
            }
        }

        return localValue;
    }

    private FrameworkElement? FindInheritedSource(FrameworkElement parent, DependencyProperty dp)
    {
        long propertyVersion = dp.InheritanceVersion;
        long treeVersion = InheritanceTreeVersion;
        if (_inheritedSources?.TryGetValue(dp, out var cached) == true &&
            cached.PropertyVersion == propertyVersion && cached.TreeVersion == treeVersion)
        {
            if (cached.Source is null) return null;
            if (cached.Source.TryGetTarget(out var source)) return source;
        }

        FrameworkElement? current = parent;
        while (current != null)
        {
            if (current.HasValueAboveInherited(dp)) break;
            if (!dp.GetMetadata(current.GetType()).Inherits)
            {
                current = null;
                break;
            }
            current = current.FrameworkParent;
        }

        // Cache the provider, not its value: animated values and coercion on
        // that provider must still be evaluated on every read. Weak ownership
        // also prevents a detached child from retaining its former window.
        (_inheritedSources ??= new())[dp] = new InheritedSourceEntry(
            propertyVersion, treeVersion,
            current is null ? null : new WeakReference<FrameworkElement>(current));
        return current;
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
            var parent = FrameworkParent;
            while (parent != null)
            {
                if (parent.HasLocalValue(DataContextProperty))
                    return parent.GetValue(DataContextProperty);
                parent = parent.FrameworkParent;
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
    /// Gets or sets the context menu shown when the user requests it on this element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ContextMenu? ContextMenu
    {
        get => (ContextMenu?)GetValue(ContextMenuProperty);
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
            // 提示内容换了（含被清成 null）：正挂在屏幕上的那个气泡必须先收掉 —— 它显示的是旧文案；
            // 清成 null 的情形更糟，连 MouseLeave 都不再订阅，等于没人再来关它，气泡会一直留着。
            ToolTipHideRequested?.Invoke(element);

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
    /// 本地 Resources 字典；未实例化时返回 <see langword="null"/>。
    ///
    /// <para>框架内部的资源查找必须走这里，不得走 <see cref="Resources"/>。
    /// 那个 getter 带懒构造副作用（new 字典 + 订阅 Changed + 注册诊断 owner），
    /// 于是「沿祖先链探测有无本地资源」这种纯只读遍历会给**每个经过的元素**都
    /// 实体化一个空字典——把 O(1) 的探测变成 O(祖先数) 的分配，并让后续查找
    /// 每级多探一个恒空的字典。整页视图替换时这一项按元素数线性放大。</para>
    /// </summary>
    internal ResourceDictionary? ResourcesOrNull => _resources;


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
                Diagnostics.ResourceDictionaryDiagnosticsStore.RegisterOwner(
                    _resources,
                    this,
                    Diagnostics.ResourceDictionaryOwnerKind.FrameworkElement);
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
                    Diagnostics.ResourceDictionaryDiagnosticsStore.UnregisterOwner(_resources, this);
                }

                _resources = value ?? new ResourceDictionary();
                _resources.Changed += OnLocalResourcesDictionaryChanged;
                Diagnostics.ResourceDictionaryDiagnosticsStore.RegisterOwner(
                    _resources,
                    this,
                    Diagnostics.ResourceDictionaryOwnerKind.FrameworkElement);
                OnResourcesChanged();
            }
        }
    }

    bool Markup.IQueryAmbient.IsAmbientPropertyAvailable(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        return propertyName switch
        {
            nameof(Resources) => _resources is not null,
            nameof(Style) => HasLocalValue(StyleProperty),
            _ => DependencyProperty.FromName(GetType(), propertyName) is { } property && HasLocalValue(property),
        };
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

    /// <summary>
    /// Notifies resource-dependent state after a theme-key change without reapplying the
    /// structurally invariant implicit/theme styles. Controls can refresh manual palette caches
    /// through ResourcesChanged while their existing templates remain intact.
    /// </summary>
    internal void NotifyThemeResourcesChangedFromRoot()
    {
        RaiseResourcesChangedInSubtree(reEvaluateImplicitStyles: false);
    }

    private void RaiseResourcesChangedInSubtree(bool reEvaluateImplicitStyles = true)
    {
        // Use iterative BFS with an explicit stack to avoid deep recursion overhead
        // and to allow early pruning of subtrees that don't need notification.
        var stack = s_subtreeStack ??= new List<FrameworkElement>(32);

        // Snapshot the current depth. A ResourcesChanged handler (public event) or an
        // implicit-style Apply can re-enter this method or throw; capturing a baseline
        // and unwinding to it in the finally makes nested calls operate on their own
        // slice and guarantees a mid-walk exception cannot strand descendants in the
        // [ThreadStatic] list — otherwise the next caller reuses (??=) a dirty stack
        // and re-styles stale, possibly-detached elements at the wrong time.
        int baseline = stack.Count;
        stack.Add(this);

        try
        {
            while (stack.Count > baseline)
            {
                var current = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);

                if (current.ResourcesChanged != null)
                {
                    current.ResourcesChanged.Invoke(current, EventArgs.Empty);
                }

                if (reEvaluateImplicitStyles)
                {
                    // General dictionary mutations can replace implicit/theme styles, so those
                    // changes still require a full style-stack re-evaluation.
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

                if (current._logicalChildren != null)
                {
                    foreach (var logicalChild in current._logicalChildren.OfType<FrameworkElement>())
                    {
                        if (logicalChild.VisualParent == null)
                        {
                            stack.Add(logicalChild);
                        }
                    }
                }
            }
        }
        finally
        {
            // Normal exit already leaves Count == baseline; an exception mid-walk
            // unwinds the residue here so the shared stack stays clean for reuse.
            if (stack.Count > baseline)
            {
                stack.RemoveRange(baseline, stack.Count - baseline);
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
    public double ActualWidth => (double)(GetValue(ActualWidthProperty) ?? 0.0);

    /// <summary>
    /// Gets the actual rendered height of this element.
    /// </summary>
    public double ActualHeight => (double)(GetValue(ActualHeightProperty) ?? 0.0);

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
    /// focus. The style's <see cref="Controls.ControlTemplate"/> is instantiated on the nearest
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
        // CSS layout state (%, aspect-ratio, box-sizing) resolves here because the
        // containing-block basis only exists during layout. Null = zero-cost path.
        var cssLayout = CssLayout;

        LastCssMeasureBox = CssParentBox;
        var containingBlock = CssParentBox?.ContainingBlock ?? availableSize;
        var margin = CssParentBox is not null ? default : cssLayout is { HasMargin: true } && !HasLocalOrAnimatedValue(MarginProperty)
            ? ResolveCssMargin(cssLayout, containingBlock.Width)
            : Margin;
        if (cssLayout is not null)
        {
            cssLayout.MeasureMarginCache = margin;
            cssLayout.ContainingWidthCache = containingBlock.Width;
        }

        var marginWidth = margin.Left + margin.Right;
        var marginHeight = margin.Top + margin.Bottom;

        // Calculate available size for content
        var contentAvailable = new Size(
            Math.Max(0, availableSize.Width - marginWidth),
            Math.Max(0, availableSize.Height - marginHeight));

        // %-padding materializes into the Padding DP before MeasureOverride so the
        // padding consumers (Control/Border MeasureOverride) see plain pixels.
        if (cssLayout is { HasPadding: true })
        {
            MaterializeCssPadding(cssLayout, containingBlock.Width);
        }

        // Apply explicit size constraints (DP wins; CSS % resolves against the basis)
        var effectiveWidth = ResolveEffectiveWidth(cssLayout, containingBlock.Width);
        var effectiveHeight = ResolveEffectiveHeight(cssLayout, containingBlock.Height);
        if (!double.IsNaN(effectiveWidth))
        {
            contentAvailable = new Size(effectiveWidth, contentAvailable.Height);
        }
        if (!double.IsNaN(effectiveHeight))
        {
            contentAvailable = new Size(contentAvailable.Width, effectiveHeight);
        }

        // Apply min/max constraints
        var (minWidth, maxWidth) = ResolveEffectiveWidthBounds(cssLayout, containingBlock.Width);
        var (minHeight, maxHeight) = ResolveEffectiveHeightBounds(cssLayout, containingBlock.Height);
        contentAvailable = new Size(
            Math.Clamp(contentAvailable.Width, minWidth, maxWidth),
            Math.Clamp(contentAvailable.Height, minHeight, maxHeight));

        // Measure content
        var contentSize = CssDisplayMode != Jalium.UI.Styling.CssDisplayMode.Native &&
            Jalium.UI.Styling.CssDisplayLayout.TryMeasure(this, contentAvailable, out var cssContentSize)
                ? cssContentSize : MeasureOverride(contentAvailable);
        if (CssRuntimeState is not null)
            contentSize = Jalium.UI.Styling.CssContainerProperties.ContainedDesiredSize(this, contentSize, containingBlock.Width);

        // Apply constraints to result
        var resultWidth = double.IsNaN(effectiveWidth) ? contentSize.Width : effectiveWidth;
        var resultHeight = double.IsNaN(effectiveHeight) ? contentSize.Height : effectiveHeight;

        // aspect-ratio derives the auto axis; min/max clamp afterwards (CSS order).
        if (cssLayout is not null && cssLayout.AspectRatio > 0)
        {
            (resultWidth, resultHeight) = ApplyCssAspectRatio(
                cssLayout.AspectRatio,
                resultWidth, !double.IsNaN(effectiveWidth),
                resultHeight, !double.IsNaN(effectiveHeight));
        }

        resultWidth = Math.Clamp(resultWidth, minWidth, maxWidth);
        resultHeight = Math.Clamp(resultHeight, minHeight, maxHeight);

        var transformedSize = ApplyLayoutTransformToDesiredSize(new Size(resultWidth, resultHeight));
        return new Size(
            Math.Max(0, transformedSize.Width + marginWidth),
            Math.Max(0, transformedSize.Height + marginHeight));
    }

    /// <inheritdoc />
    protected sealed override void ArrangeCore(Rect finalRect)
    {
        var cssLayout = CssLayout;
        LastCssParentAlignment = CssParentAlignment;
        LastCssArrangeBox = CssParentBox;
        var containingBlock = CssParentBox?.ContainingBlock ?? finalRect.Size;

        // An absolutely positioned element's slot was ALREADY solved by the panel against
        // the panel's size (its true containing block) — the slot itself is not a
        // containing block, so re-resolving percentages here would apply them twice
        // (a 50%-wide child in a 200px panel gets a 100px slot; taking 50% of the slot
        // again would shrink it to 50). Reuse the measure-side margin and skip the rest.
        var isCssAbsolute = cssLayout is { Position: Jalium.UI.Styling.CssPositionMode.Absolute };
        if (cssLayout is not null && !isCssAbsolute) cssLayout.ContainingWidthCache = containingBlock.Width;

        // Arrange-side containing block is the final rect the parent allotted.
        Thickness margin;
        if (CssParentBox is not null) margin = default;
        else if (cssLayout is { HasMargin: true } && !HasLocalOrAnimatedValue(MarginProperty))
        {
            margin = isCssAbsolute
                ? cssLayout.MeasureMarginCache
                : ResolveCssMargin(cssLayout, containingBlock.Width);
        }
        else
        {
            margin = Margin;
        }

        if (isCssAbsolute)
        {
            cssLayout = null;
        }
        var marginWidth = margin.Left + margin.Right;
        var marginHeight = margin.Top + margin.Bottom;

        // Calculate available size for content
        var availableWidth = Math.Max(0, finalRect.Width - marginWidth);
        var availableHeight = Math.Max(0, finalRect.Height - marginHeight);

        // Get the desired size (set during Measure). DesiredSize can be stale relative
        // to the current margin (margin changed after the last measure, or the element
        // was never measured — e.g. a template rebuild arranged before its measure pass),
        // so the subtraction must clamp to zero like WPF does; Size throws on negatives.
        // %-margins may resolve differently per pass, so subtract the margin that was
        // actually added during Measure — otherwise the content desired size is skewed.
        var measureMargin = CssParentBox is not null ? default : cssLayout is { HasMargin: true } ? cssLayout.MeasureMarginCache : margin;
        var desiredWidth = Math.Max(0, DesiredSize.Width - (measureMargin.Left + measureMargin.Right));
        var desiredHeight = Math.Max(0, DesiredSize.Height - (measureMargin.Top + measureMargin.Bottom));

        // Determine arrange size based on alignment
        // When alignment is Stretch, use available size; otherwise use desired size (clamped to available)
        var horizontalAlignment = ResolveCssParentHorizontalAlignment(cssLayout, containingBlock.Width);
        var verticalAlignment = ResolveCssParentVerticalAlignment(cssLayout, containingBlock.Height);
        var arrangeWidth = horizontalAlignment == HorizontalAlignment.Stretch
            ? availableWidth
            : Math.Min(desiredWidth, availableWidth);

        var arrangeHeight = verticalAlignment == VerticalAlignment.Stretch
            ? availableHeight
            : Math.Min(desiredHeight, availableHeight);

        var arrangeSize = new Size(arrangeWidth, arrangeHeight);

        // Apply explicit size constraints (DP wins; CSS % resolves against the final rect)
        var effectiveWidth = ResolveEffectiveWidth(cssLayout, containingBlock.Width);
        var effectiveHeight = ResolveEffectiveHeight(cssLayout, containingBlock.Height);
        if (!double.IsNaN(effectiveWidth))
        {
            arrangeSize = new Size(effectiveWidth, arrangeSize.Height);
        }
        if (!double.IsNaN(effectiveHeight))
        {
            arrangeSize = new Size(arrangeSize.Width, effectiveHeight);
        }

        // aspect-ratio mirrors the Measure-side derivation so render size stays consistent.
        if (cssLayout is not null && cssLayout.AspectRatio > 0 &&
            (!double.IsNaN(effectiveWidth) || !double.IsNaN(effectiveHeight)))
        {
            var (ratioWidth, ratioHeight) = ApplyCssAspectRatio(
                cssLayout.AspectRatio,
                arrangeSize.Width, !double.IsNaN(effectiveWidth),
                arrangeSize.Height, !double.IsNaN(effectiveHeight));
            arrangeSize = new Size(ratioWidth, ratioHeight);
        }

        // Apply min/max constraints
        var (minWidth, maxWidth) = ResolveEffectiveWidthBounds(cssLayout, containingBlock.Width);
        var (minHeight, maxHeight) = ResolveEffectiveHeightBounds(cssLayout, containingBlock.Height);
        arrangeSize = new Size(
            Math.Clamp(arrangeSize.Width, minWidth, maxWidth),
            Math.Clamp(arrangeSize.Height, minHeight, maxHeight));

        // Percentage values resolved against a drifted basis (∞ measure constraint,
        // star-track shrink) converge through a one-shot re-measure, mirroring Grid's
        // correction pattern; the flag prevents oscillation loops.
        if (CssParentBox is null && cssLayout is { UsesPercent: true })
        {
            var measureBasis = PreviousAvailableSize;
            var drifted =
                Math.Abs(finalRect.Width - measureBasis.Width) > 0.5 ||
                Math.Abs(finalRect.Height - measureBasis.Height) > 0.5;
            if (drifted && !cssLayout.ArrangeCorrectionQueued)
            {
                cssLayout.ArrangeCorrectionQueued = true;
                InvalidateMeasure();
            }
            else if (!drifted)
            {
                cssLayout.ArrangeCorrectionQueued = false;
            }
        }

        // Arrange content
        var renderSize = CssDisplayMode != Jalium.UI.Styling.CssDisplayMode.Native &&
            Jalium.UI.Styling.CssDisplayLayout.TryArrange(this, arrangeSize, out var cssRenderSize)
                ? cssRenderSize : ArrangeOverride(arrangeSize);

        // Calculate visual bounds based on alignment
        var x = finalRect.X + margin.Left;
        var y = finalRect.Y + margin.Top;

        // Horizontal alignment
        var extraWidth = availableWidth - renderSize.Width;
        if (extraWidth > 0)
        {
            switch (horizontalAlignment)
            {
                case HorizontalAlignment.Center:
                // WPF treats Stretch as Center when an explicit Width or a
                // size constraint prevents the element from consuming its
                // full arrange slot. Without this, a fixed-width Viewbox in a
                // wider vertical StackPanel hugs the left edge of the longest
                // sibling instead of staying centered.
                case HorizontalAlignment.Stretch when arrangeSize.Width < availableWidth:
                    x += extraWidth / 2;
                    break;
                case HorizontalAlignment.Right:
                    x += extraWidth;
                    break;
            }
        }

        if (CssParentBox is null && cssLayout is { HasMargin: true } && !HasLocalOrAnimatedValue(MarginProperty) &&
            (cssLayout.MarginLeft.IsAuto || cssLayout.MarginRight.IsAuto))
        {
            var free = Math.Max(0, extraWidth);
            x = finalRect.X + margin.Left + (cssLayout.MarginLeft.IsAuto
                ? cssLayout.MarginRight.IsAuto ? free / 2 : free : 0);
        }

        // Vertical alignment
        var extraHeight = availableHeight - renderSize.Height;
        if (extraHeight > 0)
        {
            switch (verticalAlignment)
            {
                case VerticalAlignment.Center:
                // Same effective-alignment rule as the horizontal axis.
                case VerticalAlignment.Stretch when arrangeSize.Height < availableHeight:
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
        if (UseLayoutRounding)
        {
            x = RoundLayoutValue(x);
            y = RoundLayoutValue(y);
            renderSize = new Size(
                Math.Max(0, RoundLayoutValue(renderSize.Width)),
                Math.Max(0, RoundLayoutValue(renderSize.Height)));
        }

        var previousVisualBounds = _visualBounds;
        _visualBounds = new Rect(x, y, renderSize.Width, renderSize.Height);

        // OnRender records in element-local coordinates. A size change can alter
        // those commands (and covers the first 0x0 -> arranged pass that once left
        // text blank), so it invalidates the retained drawing. A position-only
        // change does not: the parent applies the current child offset while
        // replaying/compositing. Treating WrapPanel reflow as content damage made
        // every moved card discard both its drawing and GPU layer each resize tick.
        if (_visualBounds != previousVisualBounds)
        {
            // ArrangeOverride runs before this element's final parent-space bounds are
            // stored and may let descendants populate their screen-offset cache against
            // the old ancestor position. Bump the global epoch after the new bounds are
            // committed so those descendant caches cannot remain falsely current.
            // The cache stores only the accumulated screen-space origin. Width/height
            // are read live from RenderSize, so stretch-heavy window resizes must not
            // invalidate every element's cached ancestor walk when X/Y stayed put.
            if (_visualBounds.X != previousVisualBounds.X ||
                _visualBounds.Y != previousVisualBounds.Y)
            {
                InvalidateScreenOffsetCache();
            }
            if (_visualBounds.Width != previousVisualBounds.Width ||
                _visualBounds.Height != previousVisualBounds.Height)
            {
                SetRenderDirty();
            }
        }

        // Update _renderSize BEFORE firing SizeChanged so that handlers
        // reading ActualWidth/ActualHeight/RenderSize see the new values.
        var previousRenderSize = _renderSize;
        _renderSize = renderSize;
        if (!_hasActualSizeValues || previousRenderSize.Width != renderSize.Width)
            SetValue(ActualWidthPropertyKey, renderSize.Width);
        if (!_hasActualSizeValues || previousRenderSize.Height != renderSize.Height)
            SetValue(ActualHeightPropertyKey, renderSize.Height);
        _hasActualSizeValues = true;
        CssRuntimeState?.QueryContainer?.RefreshSize();
    }

    /// <summary>
    /// Override to implement custom measure logic.
    /// </summary>
    /// <param name="availableSize">The available size.</param>
    /// <returns>The desired size.</returns>
    protected virtual Size MeasureOverride(Size availableSize)
    {
        return default(Size);
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

        // ClipToBounds is the common containment contract for ScrollViewer and
        // rounded Border item trees. Reject points outside the selected bounds
        // edges before constructing their layout geometry (and, for rounded
        // clips, doing the exact corner test). An explicit Clip still takes
        // precedence and may intentionally extend outside RenderSize.
        if (ClipToBounds &&
            !IsPointInsideClipToBoundsEdges(localPoint, new Rect(RenderSize)) &&
            Clip == null)
        {
            return null;
        }

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
    /// <remarks>
    /// Transform-aware: maps this element's local origin through the composed
    /// <see cref="RenderTransform"/> + <see cref="VisualBounds"/> chain up to (excluding)
    /// <paramref name="ancestor"/> via <see cref="GetRenderMatrixTo"/>. Under a scale/rotate
    /// ancestor (e.g. a <c>Viewbox</c> or zoom subtree) the returned point follows that transform,
    /// fixing IME/UI-Automation/dock-hit placement that previously ignored it. With no transform on
    /// the chain this reduces to the historical sum of <see cref="VisualBounds"/> offsets.
    /// </remarks>
    public new Point TransformToAncestor(Visual? ancestor)
    {
        return GetRenderMatrixTo(ancestor).Transform(Point.Zero);
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
        var childCount = parent.InternalVisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = parent.InternalGetVisualChild(i);
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
    public event DependencyPropertyChangedEventHandler? DataContextChanged;

    private static void OnStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            var oldStyle = e.OldValue as Style;
            var newStyle = e.NewValue as Style;

            element.UpdateStyleStack(oldStyle, newStyle);

            element.OnStyleChanged(oldStyle, newStyle);
        }
    }

    #endregion

    #region Visual Parent Changed

    /// <summary>
    /// Marks this generated container for a possible same-scope virtualization reattach.
    /// The next attach takes the fast path only when the exact parent, its ancestor chain,
    /// and the resource generation all still match.
    /// </summary>
    internal void PrepareForVirtualizationRecycle(DependencyObject expectedParent)
    {
        if (!ReferenceEquals(VisualParent, expectedParent))
        {
            ClearVirtualizationRecycleMarker();
            return;
        }

        _virtualizationRecycleParent = expectedParent;
        _virtualizationRecycleResourceGeneration = ResourceLookup.CacheGeneration;
        _virtualizationRecycleScopeFingerprint = ComputeResourceScopeFingerprint(expectedParent);
    }

    /// <summary>
    /// Returns whether logical-tree removal/addition for this container keeps the same resource
    /// scope. This lets the logical-tree helpers avoid globally flushing the resource cache for
    /// a deliberate detach/reattach pair whose lookup path did not change.
    /// </summary>
    private bool CanPreserveVirtualizationResourceScope(DependencyObject parent)
    {
        return ReferenceEquals(_virtualizationRecycleParent, parent) &&
               _virtualizationRecycleResourceGeneration == ResourceLookup.CacheGeneration &&
               _virtualizationRecycleScopeFingerprint == ComputeResourceScopeFingerprint(parent);
    }

    private bool ConsumeVirtualizationReattachFastPath()
    {
        var parent = VisualParent;
        var canUseFastPath = parent != null && CanPreserveVirtualizationResourceScope(parent);
        ClearVirtualizationRecycleMarker();
        return canUseFastPath;
    }

    private void ClearVirtualizationRecycleMarker()
    {
        _virtualizationRecycleParent = null;
        _virtualizationRecycleResourceGeneration = 0;
        _virtualizationRecycleScopeFingerprint = 0;
    }

    private static ulong ComputeResourceScopeFingerprint(DependencyObject parent)
    {
        // FNV-1a over the exact FrameworkParent chain. Resource dictionary mutations are
        // guarded independently by CacheGeneration; this fingerprint detects a panel (or one
        // of its ancestors) being moved to a different resource scope while a child is pooled.
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var hash = offsetBasis;
        var current = parent as FrameworkElement;
        var depth = 0;
        while (current != null && depth++ < 2048)
        {
            hash ^= unchecked((uint)RuntimeHelpers.GetHashCode(current));
            hash *= prime;

            FrameworkElement? next = null;
            if (ResourceLookup.AncestorRedirectLookup != null)
            {
                next = ResourceLookup.AncestorRedirectLookup(current);
                if (ReferenceEquals(next, current))
                {
                    next = null;
                }
            }

            current = next ?? current.FrameworkParent;
        }

        hash ^= unchecked((uint)depth);
        return hash * prime;
    }

    /// <inheritdoc />
    protected internal override void OnVisualParentChanged(DependencyObject? oldParent)
    {
        Interlocked.Increment(ref s_inheritanceTreeVersion);
        _inheritedSources = null;
        base.OnVisualParentChanged(oldParent);
        if (Jalium.UI.Styling.CssEngine.IsActive)
        {
            Jalium.UI.Styling.CssSelectorDependencies.TreeChanged(this);
            if (oldParent is FrameworkElement or FrameworkContentElement)
                Jalium.UI.Styling.CssSelectorDependencies.TreeChanged(Jalium.UI.Styling.CssNode.Get(oldParent));
            if (FrameworkParent is { } currentParent) Jalium.UI.Styling.CssSelectorDependencies.TreeChanged(currentParent);
        }
        if (Jalium.UI.Styling.CssEngine.IsActive && Jalium.UI.Styling.CssRegisteredProperties.IsActive)
        {
            Jalium.UI.Styling.CssRegisteredProperties.Invalidate(this);
            if (oldParent is FrameworkElement or FrameworkContentElement)
                Jalium.UI.Styling.CssRegisteredProperties.Invalidate(Jalium.UI.Styling.CssNode.Get(oldParent));
        }

        // Invalidate cached Window/LayoutManager references for this subtree
        InvalidateHostCaches();

        // Reactivate bindings when visual parent changes
        // This allows DataContext and RelativeSource FindAncestor bindings to resolve
        // after being added to the visual tree
        if (VisualParent != null)
        {
            // 在 Consume 之前记下 marker：它内部会清除标记，而下面的 scope 重算需要知道
            // 「这是一次虚拟化回收重挂」以便保持完整刷新语义。
            var hadVirtualizationRecycleMarker = _virtualizationRecycleParent != null;
            var isSameScopeVirtualizationReattach = ConsumeVirtualizationReattachFastPath();

            // Re-evaluate implicit styles for this entire subtree.
            // During XAML parsing, children are added bottom-up: a Button is added
            // to a StackPanel before the StackPanel is connected to the Window.
            // The initial implicit style lookup may only reach Application resources
            // (theme styles) because the ancestor with user resources isn't reachable
            // yet. When the subtree is later connected to the full tree, we must
            // re-evaluate so that closer-scope user-defined implicit styles take
            // precedence over theme styles.
            //
            // ★ 这三趟必须在 attach 返回前完成，不能推迟到 Measure：框架的既定契约是
            // 「Children.Add() 返回时子树已按新 scope 重算」，且该契约有回归测试钉死
            // （DynamicResourceLayeredTests.InlineDynamicResource_OnDetachedElement_
            // ResolvesWhenAttachedToAncestorWithResource、ChartTests.LineChart_SeriesBinding_
            // ResolvesWhenDetachedSubtreeIsAttached 等），对应真实 bug「内联
            // {DynamicResource} 在游离元素上解析为 null 后永不恢复 → 图标画不出来」。
            // 那些用例里的宿主自身也是游离的，所以「等接入 host 再算」同样不满足契约。
            //
            // 代价是 attach 期的子树重算为 O(n x depth)（源生成器 bottom-up 构树，深度 d
            // 的节点被 d 个祖先各重扫一遍）。实测见 PageSwapCostBenchmarkTests：元素数固定
            // 800、深度 4→40，一次换页 23.74ms→194.24ms。根治方向是让源生成器改为 top-down
            // 构树（先挂父再填子，届时子树为空，每次 attach 天然 O(1)，与 WPF/Avalonia 一致），
            // 而不是削弱这里的语义。
            // 与之配套的还有内联 DynamicResource 的重解析，两者走同一条查找路径，因此合并到
            // 一趟 DFS 里（RescopeSubtreeAfterAttach），并带上「祖先链无资源即可剪掉已重算过的
            // 子树」的等价判据——见该方法上方的推导。合并本身也把两次全子树遍历省成一次。
            //
            // 内联 DynamicResource 为什么必须在 attach 时重解析：<Path Stroke="{DynamicResource
            // ...}"> 这类写法只在构造时解析一次，若当时 key 不可达（Application.Current 或带
            // 该 key 的祖先尚未接入）就会解析成 null，而 Shape.Stroke/Fill 默认就是 null，
            // 于是图形什么都不画且永不自愈。这与 WPF 在树变化时重新求值资源引用一致。
            if (!isSameScopeVirtualizationReattach)
            {
                RescopeSubtreeAfterAttach(hadVirtualizationRecycleMarker);
            }

            // CSS re-evaluation is deliberately deferred (batched, deduplicated by subtree)
            // rather than synchronous like the rescope above: there is no "attach returns with
            // styles readable" contract, and the batch avoids the O(n × depth) re-scan cost
            // documented for bottom-up tree construction.
            if (Jalium.UI.Styling.CssEngine.IsActive)
            {
                Jalium.UI.Styling.CssEvaluationScheduler.InvalidateSubtree(this);
            }

            // Recursively reactivate bindings on this element and ALL descendants,
            // because descendants may have bindings that depend on DataContext
            // inherited from an ancestor that is now reachable via the visual tree
            if (!isSameScopeVirtualizationReattach)
            {
                ReactivateBindingsRecursive(this);
            }

            // Reattaching only this root is not sufficient when descendants remain
            // "valid" with cached geometry from a prior host. Mark descendants dirty
            // so the next pass cannot skip their measure/arrange.
            if (!isSameScopeVirtualizationReattach)
            {
                MarkDescendantLayoutInvalidForReattach(this);
            }

            // Reparented elements must be re-measured/re-arranged in the new host tree.
            InvalidateMeasure();
            InvalidateArrange();
            InvalidateVisual();

            // A subtree becomes loaded only when its new framework parent is already
            // connected to a presentation host. Window/PopupWindow set their own root
            // state after the first layout pass and this recursively updates descendants.
            if (FrameworkParent?.IsLoaded == true)
            {
                QueueLoadedState();
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
            if (oldParent is Visual oldVisualParent)
            {
                RemoveFromLayoutManager(oldVisualParent);
            }

            SetLoadedState(false);

            // Ancestor-scoped rules no longer apply after a detach; the batched pass diffs
            // the subtree's CSS values against the reduced scope set.
            if (Jalium.UI.Styling.CssEngine.IsActive)
            {
                Jalium.UI.Styling.CssEvaluationScheduler.InvalidateSubtree(this);
            }
        }
    }

    private void QueueLoadedState()
    {
        var dispatcher = Jalium.UI.Threading.Dispatcher.CurrentDispatcher;
        if (dispatcher == null)
        {
            SetLoadedState(true);
            return;
        }

        var queue = PendingLoadedQueues.GetValue(
            dispatcher,
            static _ => new PendingLoadedQueue());
        var scheduleDrain = false;
        lock (queue.SyncRoot)
        {
            queue.Elements.Add(this);
            if (!queue.DrainScheduled)
            {
                queue.DrainScheduled = true;
                scheduleDrain = true;
            }
        }

        if (scheduleDrain)
        {
            dispatcher.BeginInvoke(
                Jalium.UI.Threading.DispatcherPriority.Loaded,
                () => DrainLoadedQueue(queue));
        }
    }

    private static void DrainLoadedQueue(PendingLoadedQueue queue)
    {
        FrameworkElement[] elements;
        lock (queue.SyncRoot)
        {
            elements = queue.Elements.ToArray();
            queue.Elements.Clear();
            queue.DrainScheduled = false;
        }

        foreach (var element in elements)
        {
            if (element.FrameworkParent?.IsLoaded == true)
            {
                element.SetLoadedState(true);
            }
        }
    }

    private sealed class PendingLoadedQueue
    {
        internal object SyncRoot { get; } = new();
        internal HashSet<FrameworkElement> Elements { get; } =
            new(ReferenceEqualityComparer.Instance);
        internal bool DrainScheduled { get; set; }
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

        var childCount = root.InternalVisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = root.InternalGetVisualChild(i);
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

        var childCount = visual.InternalVisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = visual.InternalGetVisualChild(i);
            if (child != null)
            {
                ReactivateBindingsRecursive(child);
            }
        }
    }

    private static void MarkDescendantLayoutInvalidForReattach(Visual root)
    {
        var childCount = root.InternalVisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = root.InternalGetVisualChild(i);
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

        var childCount = visual.InternalVisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = visual.InternalGetVisualChild(i);
            if (child != null)
            {
                MarkSubtreeLayoutInvalid(child);
            }
        }
    }


    /// <summary>
    /// Applies the style stack to this element if it hasn't been initialized yet.
    /// Called from OnVisualParentChanged and also by Window.Show() to handle
    /// elements created before the theme was loaded.
    /// </summary>
    internal void ApplyImplicitStyleIfNeeded()
    {
        // Fast path: the style stack was already initialized once. Re-evaluation
        // after tree/resource changes is handled by ReEvaluateImplicitStyle().
        if (_themeStyle != null || _implicitStyle != null)
            return;

        UpdateStyleStack(Style, Style);
    }

    /// <summary>
    /// Re-evaluates the style stack for this element after a tree or resource change.
    /// If a closer-scope implicit style is now available (e.g., user-defined style
    /// in Window.Resources after the subtree was connected), it replaces the previous
    /// top-layer style; the theme default style is re-resolved as the bottom layer.
    /// </summary>
    private void ReEvaluateImplicitStyle()
    {
        UpdateStyleStack(Style, Style);
    }

    /// <summary>
    /// Recomputes and re-applies this element's two-layer style stack:
    /// theme default style (bottom) + explicit-or-implicit style (top).
    /// The top layer is applied last so its setters win for the properties it sets,
    /// while everything else (most critically the control Template) still falls back
    /// to the theme default style — WPF's Style/ThemeStyle split.
    /// </summary>
    /// <param name="oldExplicit">The explicit Style before this update (for Style changes).</param>
    /// <param name="newExplicit">The explicit Style after this update. For non-Style-change
    /// re-evaluation both parameters are the current explicit Style.</param>
    private void UpdateStyleStack(Style? oldExplicit, Style? newExplicit)
    {
        var newTheme = LookupThemeStyle();

        // The implicit style only participates when no explicit style is set (WPF parity),
        // and never duplicates the theme layer (LookupImplicitStyle can surface the theme
        // style itself via the application resource chain).
        Style? newImplicit = null;
        if (newExplicit == null)
        {
            var found = LookupImplicitStyle();
            newImplicit = ReferenceEquals(found, newTheme) ? null : found;
        }

        // An explicit Style can literally be the theme style object (e.g. captured via
        // a typed StaticResource). The theme layer already applies it — applying the
        // same Style twice would double-attach its triggers and leak their handlers.
        // Normalize both the old and new top the same way so the unchanged-check and
        // the unwind below see what was actually applied.
        var oldTop = oldExplicit ?? _implicitStyle;
        if (ReferenceEquals(oldTop, _themeStyle))
            oldTop = null;

        var newTop = newExplicit ?? newImplicit;
        if (ReferenceEquals(newTop, newTheme))
            newTop = null;

        if (ReferenceEquals(_themeStyle, newTheme) && ReferenceEquals(oldTop, newTop))
            return; // Stack unchanged.

        // Unwind in LIFO order. Both layers write the same StyleSetter value layer, so
        // removing the top style also clears any overlapping properties the theme style
        // set — the theme layer must be re-applied afterwards, not just left in place.
        oldTop?.Remove(this);
        if (!ReferenceEquals(_themeStyle, oldTop))
        {
            _themeStyle?.Remove(this);
        }

        _themeStyle = newTheme;
        _implicitStyle = newImplicit;

        newTheme?.Apply(this);
        newTop?.Apply(this);

        InvalidateVisual();

        // A style that arrives (or leaves) while this element has keyboard focus may carry a
        // FocusVisualStyle setter; the ring drawn at focus time must follow it.
        NotifyFocusVisualStyleInvalidated();
    }

    /// <summary>
    /// Looks up the theme default style for this element through the injected
    /// <see cref="ThemeStyleResolver"/> — first by <see cref="DefaultStyleKey"/>
    /// (WPF's ThemeStyle lookup key when it is a type), then by walking up the
    /// type hierarchy. <see cref="OverridesDefaultStyle"/> opts out entirely,
    /// mirroring WPF.
    /// </summary>
    private Style? LookupThemeStyle()
    {
        var resolver = ThemeStyleResolver;
        if (resolver == null)
            return null;

        if (OverridesDefaultStyle)
            return null;

        if (DefaultStyleKey is Type keyType)
        {
            var keyed = resolver(keyType);
            if (keyed != null && IsStyleApplicable(keyed))
                return keyed;
        }

        var currentType = GetType();
        while (currentType != null && currentType != typeof(FrameworkElement))
        {
            var style = resolver(currentType);
            if (style != null && IsStyleApplicable(style))
                return style;
            currentType = currentType.BaseType;
        }
        return null;
    }

    /// <summary>
    /// Looks up the implicit style for this element by walking up the type
    /// hierarchy and searching resources.
    /// </summary>
    // 隐式样式查找的「无作用域」缓存。
    //
    // 动机（实测）：整页构建时样式/资源解析占 parenting 成本的 57%（约 4.6 µs/元素）。
    // 每个元素要做 3-4 次 TryFindResource（DefaultStyleKey + 类型继承链），而
    // ResourceLookup 的 memo 以**元素身份**为 key —— 于是一页里几百个 TextBlock 会把同一个
    // 查找各做一遍，缓存完全帮不上忙。
    //
    // 等价判据（与 attach 期剪枝同源）：元素的祖先链上没有任何本地资源时，查找必然一路落到
    // 全局 ApplicationResourceLookup，结果只取决于 key 与资源内容，**与元素身份无关**。
    // 这种情况下整页同类元素可以共享一次查找结果。
    //
    // 失效用 ResourceDictionary.ContentGeneration（只在资源内容真变时递增），线程隔离用
    // [ThreadStatic]，与框架既有的两个缓存同构。
    [ThreadStatic]
    private static Dictionary<object, object?>? t_scopelessResourceCache;
    [ThreadStatic]
    private static int t_scopelessResourceGeneration;

    private const int MaxScopelessResourceCacheEntries = 4096;

    /// <summary>
    /// Releases scopeless implicit-style resource results owned by the current thread.
    /// </summary>
    internal static void ClearScopelessResourceThreadCache()
    {
        t_scopelessResourceCache = null;
        t_scopelessResourceGeneration = ResourceDictionary.ContentGeneration;
    }

    /// <summary>
    /// 隐式样式解析专用的资源查找：祖先链无本地资源时走进程内共享缓存，否则退回按元素查找。
    /// </summary>
    private object? LookupResourceForImplicitStyle(object key, bool scopeless)
    {
        if (!scopeless)
        {
            return TryFindResource(key);
        }

        var generation = ResourceDictionary.ContentGeneration;
        var cache = t_scopelessResourceCache;
        if (cache is null)
        {
            cache = t_scopelessResourceCache = new Dictionary<object, object?>();
            t_scopelessResourceGeneration = generation;
        }
        else if (t_scopelessResourceGeneration != generation)
        {
            cache.Clear();
            t_scopelessResourceGeneration = generation;
        }
        else if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var result = TryFindResource(key);
        if (cache.Count < MaxScopelessResourceCacheEntries)
        {
            cache[key] = result;
        }

        return result;
    }

    private Style? LookupImplicitStyle()
    {
        // 一次 O(depth) 的字段读，换掉下面 3-4 次完整查找里各自的祖先链遍历与字典操作。
        var scopeless = !HasResourceBearingAncestor();

        var defaultStyleKey = DefaultStyleKey;
        if (defaultStyleKey != null)
        {
            var keyedStyle = LookupResourceForImplicitStyle(defaultStyleKey, scopeless) as Style;
            if (keyedStyle != null && IsStyleApplicable(keyedStyle))
            {
                return keyedStyle;
            }

            if (OverridesDefaultStyle)
            {
                return null;
            }
        }

        var currentType = GetType();
        while (currentType != null && currentType != typeof(FrameworkElement))
        {
            var style = LookupResourceForImplicitStyle(currentType, scopeless) as Style;
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

        for (int i = 0; i < visual.InternalVisualChildrenCount; i++)
        {
            var child = visual.InternalGetVisualChild(i);
            if (child != null)
            {
                ReEvaluateImplicitStylesRecursive(child);
            }
        }
    }

    // ── attach 期 scope 重算的剪枝 ─────────────────────────────────────────
    //
    // 隐式样式与内联 DynamicResource 走的是同一条查找路径（FindResourceCore 沿
    // FrameworkParent 链逐级探 Resources / Style.Resources，走完落到全局的
    // ApplicationResourceLookup）。由此可得一条**精确**的等价判据：
    //
    //   若某元素的整条祖先链上没有任何本地资源，它这两项的解析结果只取决于全局
    //   Application 资源——与它挂在树的哪个位置无关。
    //
    // 于是当一次 attach 没有引入任何带资源的祖先时，子树里「已经在同一份资源内容下、
    // 且同样没有资源祖先的情况下重算过」的节点，结果不可能改变，可以整棵剪掉。
    //
    // 这消除了 bottom-up 构树的 O(n x depth)：深度 d 的节点原本被 d 个祖先的 attach
    // 各重扫一遍，现在从第二次起即被剪枝，总成本回到 O(n)。而祖先带资源时（框架契约
    // 用例正是这个形状：host.Resources["X"]=... 之后 host.Children.Add(child)）判据为
    // 真，照常做完整的全子树重算，语义分毫不变。
    //
    // 两个记录缺一不可：
    //   _resourceScopeStamp —— 上次重算时的资源**内容**版本（ResourceDictionary.
    //     ContentGeneration；不能用 ResourceLookup.CacheGeneration，那个被逻辑父变更
    //     污染得几乎每步都变）。资源内容一变就必须重算。
    //   _rescopedWithResourceAncestor —— 上次重算时链上**是否**有资源祖先。用于处理
    //     scope **变窄**：元素原本在带资源的祖先下解析出了近作用域样式，被移到没有资源
    //     祖先的位置时结果应当回退，此时不能剪枝。
    private int _resourceScopeStamp = -1;
    private bool _rescopedWithResourceAncestor;

    /// <summary>
    /// 本元素的祖先链上是否存在任何本地资源（含 Style.Resources）。
    /// </summary>
    private bool HasResourceBearingAncestor()
    {
        var redirect = ResourceLookup.AncestorRedirectLookup;
        var depth = 0;
        var current = NextResourceScopeAncestor(this, redirect);

        while (current != null && depth++ < 2048)
        {
            if (current.HasResources)
            {
                return true;
            }

            if (current.Style?.ResourcesOrNull is { Count: > 0 })
            {
                return true;
            }

            current = NextResourceScopeAncestor(current, redirect);
        }

        return false;
    }

    /// <summary>
    /// 按 <see cref="ResourceLookup.FindResourceCore"/> 的同一套规则取下一级资源作用域祖先：
    /// 先给控件层一次机会把查找重定向到非可视祖先（例如 PopupRoot 回落到 Popup 的 owner），
    /// 否则走 <see cref="FrameworkParent"/>。
    ///
    /// <para>必须与查找路径逐跳一致——判据一旦和真实查找走不同的链，剪枝就会漏掉真正改变了
    /// 解析结果的祖先。注意 <see cref="ResourceLookup.AncestorRedirectLookup"/> 在 Application
    /// 初始化时就被无条件赋值，所以「非 null 即放弃剪枝」等于永不剪枝，必须真的调用它。</para>
    /// </summary>
    private static FrameworkElement? NextResourceScopeAncestor(
        FrameworkElement current,
        Func<FrameworkElement, FrameworkElement?>? redirect)
    {
        FrameworkElement? next = null;
        if (redirect != null)
        {
            next = redirect(current);
            if (ReferenceEquals(next, current))
            {
                next = null;
            }
        }

        return next ?? current.FrameworkParent;
    }

    /// <summary>
    /// attach 后重算本子树的隐式样式与内联 DynamicResource，带上述剪枝。
    /// </summary>
    private void RescopeSubtreeAfterAttach(bool forceFullRescope)
    {
        // forceFullRescope：容器刚从虚拟化回收池里出来（带过 recycle marker）但没能走成
        // 同 scope 快路径。这条路径的既定契约是「回退到完整递归刷新」，有测试钉死
        // （VirtualizationRecycleResourceScopeTests.DifferentPanelReattach_
        // FallsBackToFullRecursiveRefresh）。剪枝只作用于普通构树路径——那才是
        // bottom-up 构建页面时 O(n x depth) 的来源。
        var chainHasResources = forceFullRescope || HasResourceBearingAncestor();

        RescopeSubtreeRecursive(
            this,
            chainHasResources,
            ResourceDictionary.ContentGeneration,
            ResourceLookup.AncestorRedirectLookup);
    }

    private static void RescopeSubtreeRecursive(
        Visual visual,
        bool chainHasResources,
        int stamp,
        Func<FrameworkElement, FrameworkElement?>? redirect)
    {
        if (visual is FrameworkElement fe)
        {
            // 子树内部若有节点的资源作用域被重定向到树外（PopupRoot → Popup owner），
            // 「按树结构累加祖先资源」这个判据对它不成立——它的 scope 不由这棵子树决定。
            // 保守地当作有资源祖先，对它及其后代退回全量重算。
            if (redirect != null && !chainHasResources)
            {
                var redirected = redirect(fe);
                if (redirected is not null &&
                    !ReferenceEquals(redirected, fe) &&
                    !ReferenceEquals(redirected, fe.FrameworkParent))
                {
                    chainHasResources = true;
                }
            }

            // 剪枝：本次没有引入带资源的祖先，且该节点已在同样「无资源祖先 + 同一份资源
            // 内容」的条件下重算过 —— 它与整棵子树的解析结果都不会变。
            // （子树内部的资源分布没变，所以后代的判据同样成立，可以安全整棵跳过。）
            if (!chainHasResources &&
                !fe._rescopedWithResourceAncestor &&
                fe._resourceScopeStamp == stamp)
            {
                return;
            }

            fe.ReEvaluateImplicitStyle();
            DynamicResourceBindingOperations.RefreshElement(fe);

            fe._resourceScopeStamp = stamp;
            fe._rescopedWithResourceAncestor = chainHasResources;

            // 往下传递时把本节点自己的资源计入：对后代而言它也是祖先。
            chainHasResources = chainHasResources
                || fe.HasResources
                || fe.Style?.ResourcesOrNull is { Count: > 0 };
        }

        for (int i = 0; i < visual.InternalVisualChildrenCount; i++)
        {
            var child = visual.InternalGetVisualChild(i);
            if (child != null)
            {
                RescopeSubtreeRecursive(child, chainHasResources, stamp, redirect);
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

        for (int i = 0; i < visual.InternalVisualChildrenCount; i++)
        {
            var child = visual.InternalGetVisualChild(i);
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

    #region WPF Lifecycle & Logical Tree

    private static readonly DependencyPropertyKey ActualWidthPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(ActualWidth), typeof(double), typeof(FrameworkElement), new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey ActualHeightPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(ActualHeight), typeof(double), typeof(FrameworkElement), new PropertyMetadata(0.0));

    public static readonly DependencyProperty ActualWidthProperty = ActualWidthPropertyKey.DependencyProperty;
    public static readonly DependencyProperty ActualHeightProperty = ActualHeightPropertyKey.DependencyProperty;

    public static readonly DependencyProperty FlowDirectionProperty =
        DependencyProperty.RegisterAttached(
            nameof(FlowDirection),
            typeof(FlowDirection),
            typeof(FrameworkElement),
            new PropertyMetadata(FlowDirection.LeftToRight, OnFrameworkLayoutPropertyChanged, null, inherits: true),
            static value => value is FlowDirection direction && Enum.IsDefined(direction));

    public static readonly DependencyProperty ForceCursorProperty =
        DependencyProperty.Register(nameof(ForceCursor), typeof(bool), typeof(FrameworkElement), new PropertyMetadata(false));

    public static readonly DependencyProperty LayoutTransformProperty =
        DependencyProperty.Register(
            nameof(LayoutTransform),
            typeof(Transform),
            typeof(FrameworkElement),
            new PropertyMetadata(null, OnLayoutTransformChanged));

    public static readonly DependencyProperty OverridesDefaultStyleProperty =
        DependencyProperty.Register(
            nameof(OverridesDefaultStyle),
            typeof(bool),
            typeof(FrameworkElement),
            new PropertyMetadata(false, OnDefaultStyleSelectionChanged));

    public static readonly DependencyProperty UseLayoutRoundingProperty =
        DependencyProperty.Register(
            nameof(UseLayoutRounding),
            typeof(bool),
            typeof(FrameworkElement),
            new PropertyMetadata(false, OnFrameworkLayoutPropertyChanged, null, inherits: true));

    protected internal static readonly DependencyProperty DefaultStyleKeyProperty =
        DependencyProperty.Register(
            nameof(DefaultStyleKey),
            typeof(object),
            typeof(FrameworkElement),
            new PropertyMetadata(null, OnDefaultStyleSelectionChanged));

    public static readonly DependencyProperty BindingGroupProperty =
        DependencyProperty.Register(
            nameof(BindingGroup),
            typeof(BindingGroup),
            typeof(FrameworkElement),
            new PropertyMetadata(null, null, null, inherits: true));

    public static readonly RoutedEvent LoadedEvent =
        EventManager.RegisterRoutedEvent(nameof(Loaded), RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(FrameworkElement));

    public static readonly RoutedEvent UnloadedEvent =
        EventManager.RegisterRoutedEvent(nameof(Unloaded), RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(FrameworkElement));

    public static readonly RoutedEvent SizeChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SizeChanged), RoutingStrategy.Direct, typeof(SizeChangedEventHandler), typeof(FrameworkElement));

    private List<object>? _logicalChildren;
    private FrameworkElement? _logicalParent;
    private int _initializationDepth;
    private bool _isInitialized;
    private bool _isLoaded;
    private InheritanceBehavior _inheritanceBehavior;

    public FlowDirection FlowDirection
    {
        get => (FlowDirection)(GetValue(FlowDirectionProperty) ?? FlowDirection.LeftToRight);
        set => SetValue(FlowDirectionProperty, value);
    }

    public bool ForceCursor
    {
        get => (bool)(GetValue(ForceCursorProperty) ?? false);
        set => SetValue(ForceCursorProperty, value);
    }

    public Transform? LayoutTransform
    {
        get => (Transform?)GetValue(LayoutTransformProperty);
        set => SetValue(LayoutTransformProperty, value);
    }

    public bool OverridesDefaultStyle
    {
        get => (bool)(GetValue(OverridesDefaultStyleProperty) ?? false);
        set => SetValue(OverridesDefaultStyleProperty, value);
    }

    public bool UseLayoutRounding
    {
        get => (bool)(GetValue(UseLayoutRoundingProperty) ?? false);
        set => SetValue(UseLayoutRoundingProperty, value);
    }

    protected internal object? DefaultStyleKey
    {
        get => GetValue(DefaultStyleKeyProperty);
        set => SetValue(DefaultStyleKeyProperty, value);
    }

    public BindingGroup? BindingGroup
    {
        get => (BindingGroup?)GetValue(BindingGroupProperty);
        set
        {
            BindingGroup? oldValue = BindingGroup;
            if (ReferenceEquals(oldValue, value))
            {
                return;
            }

            if (ReferenceEquals(oldValue?.Owner, this))
            {
                oldValue.SetOwner(null);
            }

            value?.SetOwner(this);
            SetValue(BindingGroupProperty, value);
        }
    }

    public bool IsInitialized => _isInitialized;
    public bool IsLoaded => _isLoaded;
    public DependencyObject? Parent => _logicalParent ?? VisualParent as DependencyObject;

    protected internal InheritanceBehavior InheritanceBehavior
    {
        get => _inheritanceBehavior;
        set
        {
            if (_isInitialized || Parent != null)
            {
                throw new InvalidOperationException("InheritanceBehavior must be set before initialization and parenting.");
            }

            _inheritanceBehavior = value;
        }
    }

    protected internal virtual IEnumerator LogicalChildren
        => (_logicalChildren ?? Enumerable.Empty<object>()).GetEnumerator();

    internal FrameworkElement? FrameworkParent => _logicalParent ?? VisualParent as FrameworkElement;

    /// <summary>
    /// The pure logical parent (no visual-parent fallback, unlike <see cref="Parent"/>).
    /// Used by <see cref="Controls.UIElementCollection"/> to migrate logical ownership when a
    /// child is handed off between panels (the logical half of automatic reparenting).
    /// </summary>
    internal FrameworkElement? LogicalParentInternal => _logicalParent;

    public static FlowDirection GetFlowDirection(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (FlowDirection)(element.GetValue(FlowDirectionProperty) ?? FlowDirection.LeftToRight);
    }

    public static void SetFlowDirection(DependencyObject element, FlowDirection value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FlowDirectionProperty, value);
    }

    public virtual void BeginInit()
    {
        if (_isInitialized)
        {
            throw new InvalidOperationException("An initialized FrameworkElement cannot begin initialization again.");
        }

        _initializationDepth++;
    }

    public virtual void EndInit()
    {
        if (_isInitialized)
        {
            return;
        }

        if (_initializationDepth > 0)
        {
            _initializationDepth--;
        }

        if (_initializationDepth == 0)
        {
            EnsureInitialized();
        }
    }

    protected virtual void OnInitialized(EventArgs e) => Initialized?.Invoke(this, e);

    public event EventHandler? Initialized;

    protected internal void AddLogicalChild(object? child)
    {
        if (child == null)
        {
            return;
        }

        if (ReferenceEquals(child, this))
        {
            throw new InvalidOperationException("An element cannot be its own logical child.");
        }

        _logicalChildren ??= new List<object>();
        if (_logicalChildren.Contains(child))
        {
            return;
        }

        var preservesVirtualizationResourceScope =
            child is FrameworkElement candidate &&
            candidate.CanPreserveVirtualizationResourceScope(this);

        if (child is FrameworkElement element)
        {
            if (element._logicalParent != null && !ReferenceEquals(element._logicalParent, this))
            {
                // 模板内的元素通过 TemplateBinding 接过宿主的内容时（SplitButton 默认模板里
                // PrimaryButton Content="{TemplateBinding Content}" 就是这种形状），内容的逻辑父
                // 必须留在宿主一侧：模板内元素只负责视觉承载，这与 WPF 的 ContentPresenter 语义一致。
                // 此处静默让位——既不夺父也不抛，否则宿主 Content 一旦是 UIElement 就必崩。
                if (IsInsideTemplateOf(element._logicalParent))
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"The logical child already has a parent (child: {element.GetType().Name}, current parent: {element._logicalParent.GetType().Name}, attempted parent: {GetType().Name}).");
            }

            element._logicalParent = this;
            Interlocked.Increment(ref s_inheritanceTreeVersion);
            element._inheritedSources = null;
            element.ReactivateBindings();
            if (IsLoaded)
            {
                element.SetLoadedState(true);
            }
        }

        if (child is FrameworkContentElement contentChild)
        {
            if (contentChild.Parent is { } previousParent && !ReferenceEquals(previousParent, this))
            {
                if (previousParent is FrameworkElement visualOwner && IsInsideTemplateOf(visualOwner)) return;
                throw new InvalidOperationException("The content element already has a logical parent.");
            }
            contentChild.SetContentParent(this);
            contentChild.ReactivateBindings();
            if (IsLoaded) contentChild.SetLoadedState(true);
        }

        _logicalChildren.Add(child);
        if (!preservesVirtualizationResourceScope)
        {
            ResourceLookup.InvalidateResourceCache();
        }

        if (Jalium.UI.Styling.CssEngine.IsActive && child is FrameworkElement or FrameworkContentElement)
        {
            Jalium.UI.Styling.CssEngine.InvalidateSelectorDependents(this);
            Jalium.UI.Styling.CssRegisteredProperties.Invalidate(this);
            Jalium.UI.Styling.CssEvaluationScheduler.InvalidateSubtree(Jalium.UI.Styling.CssNode.Get((DependencyObject)child));
        }
    }

    /// <summary>
    /// 本元素是否位于 <paramref name="host"/> 的模板内（沿 TemplatedParent 链上溯，
    /// 覆盖模板套模板的多层嵌套）。用于识别「模板内元素接过宿主内容」这一合法形状。
    /// </summary>
    private bool IsInsideTemplateOf(FrameworkElement host)
    {
        // 模板嵌套层数有限；上限只是防御性护栏，避免异常的自引用链把这里挂死。
        const int MaxTemplateDepth = 64;

        var scope = _templatedParent;
        for (var depth = 0; scope != null && depth < MaxTemplateDepth; depth++)
        {
            if (ReferenceEquals(scope, host))
            {
                return true;
            }

            scope = scope._templatedParent;
        }

        return false;
    }

    protected internal void RemoveLogicalChild(object? child)
    {
        if (child == null || _logicalChildren == null || !_logicalChildren.Remove(child))
        {
            return;
        }

        var preservesVirtualizationResourceScope =
            child is FrameworkElement candidate &&
            candidate.CanPreserveVirtualizationResourceScope(this);

        if (child is FrameworkElement element && ReferenceEquals(element._logicalParent, this))
        {
            if (element.IsLoaded)
            {
                element.SetLoadedState(false);
            }

            element._logicalParent = null;
            Interlocked.Increment(ref s_inheritanceTreeVersion);
            element._inheritedSources = null;
        }

        if (child is FrameworkContentElement contentChild && ReferenceEquals(contentChild.Parent, this))
        {
            if (contentChild.IsLoaded) contentChild.SetLoadedState(false);
            contentChild.SetContentParent(null);
        }

        if (!preservesVirtualizationResourceScope)
        {
            ResourceLookup.InvalidateResourceCache();
        }

        if (Jalium.UI.Styling.CssEngine.IsActive && child is FrameworkElement or FrameworkContentElement)
        {
            Jalium.UI.Styling.CssEngine.InvalidateSelectorDependents(this);
            Jalium.UI.Styling.CssRegisteredProperties.Invalidate(this);
            Jalium.UI.Styling.CssEvaluationScheduler.InvalidateSubtree(Jalium.UI.Styling.CssNode.Get((DependencyObject)child));
        }
    }

    protected internal override DependencyObject? GetUIParentCore() => Parent;

    public sealed override bool MoveFocus(Input.TraversalRequest request) => base.MoveFocus(request);

    public bool ApplyTemplate() => ApplyTemplateCore();

    protected virtual bool ApplyTemplateCore() => false;

    public virtual void OnApplyTemplate()
    {
    }

    protected internal DependencyObject? GetTemplateChild(string childName)
    {
        if (string.IsNullOrEmpty(childName))
        {
            return null;
        }

        return FindTemplateChild(this, childName);
    }

    public void SetResourceReference(DependencyProperty dp, object name)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ArgumentNullException.ThrowIfNull(name);
        DynamicResourceBindingOperations.SetDynamicResource(this, dp, name);
    }

    /// <summary>
    /// Removes a local dynamic resource expression and its resolved local value.
    /// Resource expressions supplied by styles and templates remain active.
    /// </summary>
    public void ClearResourceReference(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        if (!DynamicResourceBindingOperations.TryGetLocalDynamicResourceKey(this, dp, out _))
            return;

        DynamicResourceBindingOperations.ClearLocalDynamicResource(this, dp);
        ClearValue(dp);
    }

    public bool ShouldSerializeResources() => _resources is { Count: > 0 };
    public bool ShouldSerializeStyle() => HasLocalValue(StyleProperty);

    public void UpdateDefaultStyle() => ReEvaluateImplicitStyle();

    internal void SetLoadedState(bool loaded)
    {
        if (_isLoaded == loaded)
        {
            return;
        }

        if (loaded)
        {
            EnsureInitialized();
        }

        _isLoaded = loaded;
        var routedEvent = loaded ? LoadedEvent : UnloadedEvent;
        RaiseEvent(new RoutedEventArgs(routedEvent, this));

        if (loaded)
        {
            Loaded?.Invoke(this, new RoutedEventArgs(routedEvent, this));
        }
        else
        {
            Unloaded?.Invoke(this, new RoutedEventArgs(routedEvent, this));
        }

        for (var i = 0; i < VisualChildrenCount; i++)
        {
            if (GetVisualChild(i) is FrameworkElement child)
            {
                child.SetLoadedState(loaded);
            }
        }

        if (_logicalChildren != null)
        {
            foreach (var child in _logicalChildren.OfType<FrameworkElement>())
            {
                if (child.VisualParent == null)
                {
                    child.SetLoadedState(loaded);
                }
            }
        }
    }

    internal static Cursor? ResolveEffectiveCursor(FrameworkElement element)
    {
        Cursor? nearest = null;
        Cursor? forced = null;
        FrameworkElement? current = element;
        while (current != null)
        {
            if (current.Cursor != null)
            {
                nearest ??= current.Cursor;
                if (current.ForceCursor)
                {
                    forced = current.Cursor;
                    break;
                }
            }

            current = current.FrameworkParent;
        }

        var args = new Input.QueryCursorEventArgs
        {
            RoutedEvent = UIElement.QueryCursorEvent,
            Source = element,
            Cursor = forced ?? nearest,
        };
        element.RaiseEvent(args);
        return forced ?? args.Cursor;
    }

    internal Size ApplyLayoutTransformToDesiredSize(Size size)
    {
        var transform = LayoutTransform;
        if (transform == null || transform.Value.IsIdentity)
        {
            return size;
        }

        var matrix = transform.Value;
        var points = new[]
        {
            matrix.Transform(Point.Zero),
            matrix.Transform(new Point(size.Width, 0)),
            matrix.Transform(new Point(0, size.Height)),
            matrix.Transform(new Point(size.Width, size.Height)),
        };
        var minX = points.Min(static point => point.X);
        var maxX = points.Max(static point => point.X);
        var minY = points.Min(static point => point.Y);
        var maxY = points.Max(static point => point.Y);
        return new Size(Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    internal double RoundLayoutValue(double value)
    {
        if (!UseLayoutRounding || !double.IsFinite(value))
        {
            return value;
        }

        var scale = LayoutDpiScale > 0 && double.IsFinite(LayoutDpiScale) ? LayoutDpiScale : 1.0;
        return Math.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
    }

    private void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        OnInitialized(EventArgs.Empty);
    }

    private static DependencyObject? FindTemplateChild(FrameworkElement root, string name)
    {
        if (root.Name == name)
        {
            return root;
        }

        if (root.FindName(name) is DependencyObject named)
        {
            return named;
        }

        for (var i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is FrameworkElement child
                && FindTemplateChild(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static void OnFrameworkLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            element.InvalidateMeasure();
            element.InvalidateArrange();
            element.InvalidateVisual();
        }
    }

    private static void OnLayoutTransformChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        if (e.OldValue is Transform oldTransform)
        {
            oldTransform.Changed -= element.OnLayoutTransformSubPropertyChanged;
        }

        if (e.NewValue is Transform newTransform)
        {
            newTransform.Changed += element.OnLayoutTransformSubPropertyChanged;
        }

        OnFrameworkLayoutPropertyChanged(d, e);
    }

    private void OnLayoutTransformSubPropertyChanged(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    private static void OnDefaultStyleSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            element.ReEvaluateImplicitStyle();
            element.InvalidateMeasure();
            element.InvalidateVisual();
        }
    }

    #endregion

    #region WpfSurface

    /// <summary>Identifies the <see cref="InputScope"/> dependency property.</summary>
    public static readonly DependencyProperty InputScopeProperty =
        DependencyProperty.Register(
            nameof(InputScope),
            typeof(InputScope),
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Identifies the <see cref="Language"/> dependency property.</summary>
    public static readonly DependencyProperty LanguageProperty =
        DependencyProperty.RegisterAttached(
            nameof(Language),
            typeof(XmlLanguage),
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(
                XmlLanguage.GetLanguage("en-US"),
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Identifies the <see cref="ToolTipOpening"/> routed event.</summary>
    public static readonly RoutedEvent ToolTipOpeningEvent =
        EventManager.RegisterRoutedEvent(
            nameof(ToolTipOpening),
            RoutingStrategy.Direct,
            typeof(ToolTipEventHandler),
            typeof(FrameworkElement));

    /// <summary>Identifies the <see cref="ToolTipClosing"/> routed event.</summary>
    public static readonly RoutedEvent ToolTipClosingEvent =
        EventManager.RegisterRoutedEvent(
            nameof(ToolTipClosing),
            RoutingStrategy.Direct,
            typeof(ToolTipEventHandler),
            typeof(FrameworkElement));

    /// <summary>Identifies the <see cref="ContextMenuOpening"/> routed event.</summary>
    public static readonly RoutedEvent ContextMenuOpeningEvent =
        EventManager.RegisterRoutedEvent(
            nameof(ContextMenuOpening),
            RoutingStrategy.Bubble,
            typeof(ContextMenuEventHandler),
            typeof(FrameworkElement));

    /// <summary>Identifies the <see cref="ContextMenuClosing"/> routed event.</summary>
    public static readonly RoutedEvent ContextMenuClosingEvent =
        EventManager.RegisterRoutedEvent(
            nameof(ContextMenuClosing),
            RoutingStrategy.Bubble,
            typeof(ContextMenuEventHandler),
            typeof(FrameworkElement));

    private TriggerCollection? _triggers;

    static FrameworkElement()
    {
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            ToolTipOpeningEvent,
            new ToolTipEventHandler(static (sender, e) => ((FrameworkElement)sender).OnToolTipOpening(e)));
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            ToolTipClosingEvent,
            new ToolTipEventHandler(static (sender, e) => ((FrameworkElement)sender).OnToolTipClosing(e)));
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            ContextMenuOpeningEvent,
            new ContextMenuEventHandler(static (sender, e) => ((FrameworkElement)sender).OnContextMenuOpening(e)));
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            ContextMenuClosingEvent,
            new ContextMenuEventHandler(static (sender, e) => ((FrameworkElement)sender).OnContextMenuClosing(e)));
    }

    /// <summary>Gets or sets the context for alternative text-input methods.</summary>
    public InputScope? InputScope
    {
        get => (InputScope?)GetValue(InputScopeProperty);
        set => SetValue(InputScopeProperty, value);
    }

    /// <summary>Gets or sets the localization language inherited by this element.</summary>
    public XmlLanguage Language
    {
        get => (XmlLanguage)(GetValue(LanguageProperty) ?? XmlLanguage.GetLanguage("en-US"));
        set => SetValue(LanguageProperty, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Gets triggers established directly on this element.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public TriggerCollection Triggers => _triggers ??= new TriggerCollection(this);

    public event ToolTipEventHandler ToolTipOpening
    {
        add => AddHandler(ToolTipOpeningEvent, value);
        remove => RemoveHandler(ToolTipOpeningEvent, value);
    }

    public event ToolTipEventHandler ToolTipClosing
    {
        add => AddHandler(ToolTipClosingEvent, value);
        remove => RemoveHandler(ToolTipClosingEvent, value);
    }

    public event ContextMenuEventHandler ContextMenuOpening
    {
        add => AddHandler(ContextMenuOpeningEvent, value);
        remove => RemoveHandler(ContextMenuOpeningEvent, value);
    }

    public event ContextMenuEventHandler ContextMenuClosing
    {
        add => AddHandler(ContextMenuClosingEvent, value);
        remove => RemoveHandler(ContextMenuClosingEvent, value);
    }

    public event EventHandler<DataTransferEventArgs> TargetUpdated
    {
        add => AddHandler(Binding.TargetUpdatedEvent, value);
        remove => RemoveHandler(Binding.TargetUpdatedEvent, value);
    }

    public event EventHandler<DataTransferEventArgs> SourceUpdated
    {
        add => AddHandler(Binding.SourceUpdatedEvent, value);
        remove => RemoveHandler(Binding.SourceUpdatedEvent, value);
    }

    /// <summary>Returns the ordinary binding expression on <paramref name="dp"/>.</summary>
    public new BindingExpression? GetBindingExpression(DependencyProperty dp)
        => base.GetBindingExpression(dp) as BindingExpression;

    /// <summary>Creates a binding from a source property path.</summary>
    public BindingExpression SetBinding(DependencyProperty dp, string path)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ArgumentNullException.ThrowIfNull(path);
        return (BindingExpression)SetBinding(dp, new Binding(path));
    }

    /// <summary>Begins a storyboard using snapshot-and-replace handoff.</summary>
    public void BeginStoryboard(Storyboard storyboard)
        => BeginStoryboard(storyboard, AnimationHandoffBehavior.SnapshotAndReplace, isControllable: false);

    /// <summary>Begins a storyboard with the specified handoff behavior.</summary>
    public void BeginStoryboard(Storyboard storyboard, AnimationHandoffBehavior handoffBehavior)
        => BeginStoryboard(storyboard, handoffBehavior, isControllable: false);

    /// <summary>Begins a storyboard and optionally exposes its control operations.</summary>
    public void BeginStoryboard(
        Storyboard storyboard,
        AnimationHandoffBehavior handoffBehavior,
        bool isControllable)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        storyboard.Begin(this, handoffBehavior, isControllable);
    }

    /// <summary>Returns whether direct element triggers should be serialized.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeTriggers() => _triggers is { Count: > 0 };

    /// <summary>Determines the next element for directional focus without moving focus.</summary>
    public sealed override DependencyObject? PredictFocus(FocusNavigationDirection direction)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new InvalidEnumArgumentException(nameof(direction), (int)direction, typeof(FocusNavigationDirection));
        }

        return base.PredictFocus(direction);
    }

    /// <summary>Invoked when an unhandled tooltip-opening event reaches this class.</summary>
    protected virtual void OnToolTipOpening(ToolTipEventArgs e)
    {
    }

    /// <summary>Invoked when an unhandled tooltip-closing event reaches this class.</summary>
    protected virtual void OnToolTipClosing(ToolTipEventArgs e)
    {
    }

    /// <summary>Invoked when an unhandled context-menu-opening event reaches this class.</summary>
    protected virtual void OnContextMenuOpening(ContextMenuEventArgs e)
    {
    }

    /// <summary>Invoked when an unhandled context-menu-closing event reaches this class.</summary>
    protected virtual void OnContextMenuClosing(ContextMenuEventArgs e)
    {
    }

    /// <summary>Raises size notifications after the render size changes.</summary>
    protected internal override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        ArgumentNullException.ThrowIfNull(sizeInfo);
        _previousRenderSize = sizeInfo.NewSize;
        // A horizontal live resize changes the width of most elements while
        // leaving their height untouched (and vice versa). Avoid running a full
        // read-only dependency-property mutation for the unchanged dimension.
        if (sizeInfo.WidthChanged && ActualWidth != sizeInfo.NewSize.Width)
            SetValue(ActualWidthPropertyKey, sizeInfo.NewSize.Width);
        if (sizeInfo.HeightChanged && ActualHeight != sizeInfo.NewSize.Height)
            SetValue(ActualHeightPropertyKey, sizeInfo.NewSize.Height);
        OnSizeChanged(sizeInfo);
    }

    /// <summary>Invoked when this element's active style changes.</summary>
    protected internal virtual void OnStyleChanged(Style? oldStyle, Style? newStyle)
    {
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    /// <summary>Notifies a specialized layout parent that a child invalidated parent layout.</summary>
    protected internal virtual void ParentLayoutInvalidated(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
    }

    /// <inheritdoc />
    protected override Geometry? GetLayoutClip(Size layoutSlotSize)
        => base.GetLayoutClip(layoutSlotSize);

    #endregion

    #region Dpi

    /// <summary>
    /// Called when the DPI scale used to render this element changes.
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
    }

    /// <summary>
    /// Notifies this element and its visual descendants of a host DPI transition.
    /// </summary>
    internal void NotifyDpiChangedRecursive(DpiScale oldDpi, DpiScale newDpi)
    {
        OnDpiChanged(oldDpi, newDpi);

        List<FrameworkElement>? children = null;
        for (var index = 0; index < VisualChildrenCount; index++)
        {
            if (GetVisualChild(index) is FrameworkElement child)
            {
                children ??= new List<FrameworkElement>();
                children.Add(child);
            }
        }

        if (children == null)
        {
            return;
        }

        foreach (var child in children)
        {
            child.NotifyDpiChangedRecursive(oldDpi, newDpi);
        }
    }

    #endregion
}

/// <summary>
/// Specifies the horizontal flow direction of layout and content.
/// </summary>
public enum FlowDirection
{
    LeftToRight,
    RightToLeft,
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
public class SizeChangedEventArgs : RoutedEventArgs
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

    /// <inheritdoc />
    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
    {
        if (genericHandler is SizeChangedEventHandler handler)
        {
            handler(genericTarget, this);
        }
        else
        {
            base.InvokeEventHandler(genericHandler, genericTarget);
        }
    }
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
