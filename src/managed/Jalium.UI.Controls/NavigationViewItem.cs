using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents an item in a NavigationView with WinUI-style appearance.
/// </summary>
public class NavigationViewItem : ContentControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.NavigationViewItemAutomationPeer(this);
    }

    #region Constants

    private const double IndentPerLevel = 28;
    private const double ExpandAnimationDurationMs = 260;
    private const double CollapseAnimationDurationMs = 180;
    private const double ClothStaggerProgress = 0.09;
    private static readonly BackEase s_expandHeightEase = new() { EasingMode = EasingMode.EaseOut, Amplitude = 0.85 };
    private static readonly CubicEase s_arrowExpandEase = new() { EasingMode = EasingMode.EaseOut };
    private static readonly CubicEase s_collapseEase = new() { EasingMode = EasingMode.EaseInOut };
    private static readonly BackEase s_clothEase = new() { EasingMode = EasingMode.EaseOut, Amplitude = 1.05 };

    #endregion

    #region Fields

    private int _indentLevel;
    private long _expandAnimationStartTick;
    private bool _expandAnimationTargetExpanded;
    private double _expandAnimationFromHeight;
    private double _expandAnimationToHeight;
    private double _expandAnimationFromAngle;
    private double _expandAnimationToAngle;
    private ClothChild[] _expandAnimationChildren = [];

    #endregion

    private readonly struct ClothChild
    {
        public ClothChild(UIElement element, double initialY, double progressDelay)
        {
            Element = element;
            InitialY = initialY;
            ProgressDelay = progressDelay;
        }

        public UIElement Element { get; }
        public double InitialY { get; }
        public double ProgressDelay { get; }
    }

    #region Template Parts

    private Border? _indentSpacer;
    private Shapes.Path? _chevron;
    private StackPanel? _childrenPanel;
    private Threading.DispatcherTimer? _expandAnimTimer;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Icon dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(object), typeof(NavigationViewItem),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the IsSelected dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(NavigationViewItem),
            new PropertyMetadata(false, OnIsSelectedChanged));

    /// <summary>
    /// Identifies the IsExpanded dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(NavigationViewItem),
            new PropertyMetadata(false, OnIsExpandedChanged));

    /// <summary>
    /// Identifies the IsPressed dependency property.
    /// </summary>
    public new static readonly DependencyProperty IsPressedProperty = UIElement.IsPressedProperty;

    /// <summary>
    /// Identifies the SelectsOnInvoked dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SelectsOnInvokedProperty =
        DependencyProperty.Register(nameof(SelectsOnInvoked), typeof(bool), typeof(NavigationViewItem),
            new PropertyMetadata(true));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the icon for this item. Can be a string (glyph), IconElement, or any UIElement.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this item is selected.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty)!;
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this item is expanded.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty)!;
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether the item is currently pressed.
    /// </summary>
    public new bool IsPressed => base.IsPressed;

    /// <summary>
    /// Gets or sets a value indicating whether this item becomes selected when invoked.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool SelectsOnInvoked
    {
        get => (bool)GetValue(SelectsOnInvokedProperty)!;
        set => SetValue(SelectsOnInvokedProperty, value);
    }

    /// <summary>
    /// Gets the collection of child menu items.
    /// </summary>
    public List<NavigationViewItem> MenuItems { get; } = new();

    /// <summary>
    /// Gets a value indicating whether this item has child menu items.
    /// </summary>
    public bool HasUnrealizedChildren => MenuItems.Count > 0;

    /// <summary>
    /// Gets or sets the indentation level of this item.
    /// </summary>
    public int IndentLevel
    {
        get => _indentLevel;
        set
        {
            _indentLevel = value;
            UpdateIndent();
            // Also update children's indent level
            foreach (var child in MenuItems)
            {
                child.IndentLevel = value + 1;
            }
        }
    }

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationViewItem"/> class.
    /// </summary>
    public NavigationViewItem()
    {
        Focusable = true;
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");

        // Use template-based content management (ContentPresenter in template handles Content)
        UseTemplateContentManagement();

        // Mouse down for click handling (expand/invoke)
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
    }

    #region Template

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        StopExpandAnimation();

        _indentSpacer = GetTemplateChild("PART_IndentSpacer") as Border;
        _chevron = GetTemplateChild("PART_Chevron") as Shapes.Path;
        _childrenPanel = GetTemplateChild("PART_ChildrenPanel") as StackPanel;

        // Sync initial state
        UpdateIndent();
        UpdateChevronVisibility();
        SyncExpandedVisualState();
    }

    /// <inheritdoc />
    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);

        // IsExpanded may be set before the item is attached to a window.
        // Re-sync once attached so layout/render is guaranteed to update.
        if (VisualParent != null)
        {
            SyncExpandedVisualState();
            InvalidateMeasure();
            InvalidateVisual();
        }
        else
        {
            StopExpandAnimation();
        }
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the item is invoked (clicked or tapped).
    /// </summary>
    public event EventHandler<NavigationViewItemInvokedEventArgs>? Invoked;

    /// <summary>
    /// Occurs when the selection state changes.
    /// </summary>
    public event EventHandler<bool>? SelectionChanged;

    /// <summary>
    /// Occurs when the expansion state changes.
    /// </summary>
    public event EventHandler<bool>? ExpansionChanged;

    #endregion

    #region Mouse Event Handlers

    private void OnMouseDownHandler(object? sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled)
            return;

        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();

            // Find parent NavigationView and delegate click handling
            var navView = FindParentNavigationView();
            if (navView != null)
            {
                navView.HandleItemClicked(this);
            }
            else
            {
                // Fallback: handle locally if no parent NavigationView found
                if (HasUnrealizedChildren)
                {
                    IsExpanded = !IsExpanded;
                }
            }

            e.Handled = true;
        }
    }

    private NavigationView? FindParentNavigationView()
    {
        Visual? current = VisualParent;
        while (current != null)
        {
            if (current is NavigationView nav)
                return nav;
            current = current.VisualParent;
        }
        return null;
    }

    #endregion

    #region Internal Methods

    /// <summary>
    /// Invokes this item.
    /// </summary>
    internal void Invoke()
    {
        Invoked?.Invoke(this, new NavigationViewItemInvokedEventArgs(this));
    }

    /// <summary>
    /// Gets the children panel for adding child items.
    /// </summary>
    internal StackPanel? GetChildrenPanel()
    {
        // Ensure template is applied so PART_ChildrenPanel is available
        if (_childrenPanel == null)
        {
            ApplyTemplate();
        }
        return _childrenPanel;
    }

    #endregion

    #region State Updates

    private void UpdateIndent()
    {
        if (_indentSpacer != null)
        {
            _indentSpacer.Width = 14 + _indentLevel * IndentPerLevel;
        }
    }

    private void UpdateChevronVisibility()
    {
        if (_chevron != null)
        {
            _chevron.Visibility = HasUnrealizedChildren ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    internal void RefreshHierarchyVisualState()
    {
        UpdateChevronVisibility();
        SyncExpandedVisualState();
    }

    private void SyncExpandedVisualState()
    {
        StopExpandAnimation();

        if (_childrenPanel != null)
        {
            _childrenPanel.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            _childrenPanel.MaxHeight = double.PositiveInfinity;
            _childrenPanel.ClipToBounds = false;
        }

        if (_chevron != null)
        {
            SetChevronAngle(IsExpanded ? 90 : 0);
        }
    }

    private bool ShouldAnimateExpandedStateChange() =>
        _childrenPanel != null
        && HasUnrealizedChildren
        && VisualParent != null;

    private void BeginExpandedStateAnimation(bool expanded)
    {
        if (_childrenPanel == null)
        {
            return;
        }

        var startHeight = GetCurrentChildrenPanelHeight();
        var targetHeight = expanded ? MeasureChildrenPanelNaturalHeight() : 0.0;
        var startAngle = GetCurrentChevronAngle();
        var targetAngle = expanded ? 90.0 : 0.0;

        if (Math.Abs(startHeight - targetHeight) < 0.5 && Math.Abs(startAngle - targetAngle) < 0.5)
        {
            SyncExpandedVisualState();
            return;
        }

        StopExpandAnimation();

        _expandAnimationTargetExpanded = expanded;
        _expandAnimationStartTick = Environment.TickCount64;
        _expandAnimationFromHeight = startHeight;
        _expandAnimationToHeight = targetHeight;
        _expandAnimationFromAngle = startAngle;
        _expandAnimationToAngle = targetAngle;
        _expandAnimationChildren = expanded
            ? CollectClothChildren(targetHeight)
            : [];

        _childrenPanel.Visibility = Visibility.Visible;
        _childrenPanel.ClipToBounds = true;
        _childrenPanel.MaxHeight = Math.Max(0, startHeight);

        _expandAnimTimer = new Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1, CompositionTarget.FrameIntervalMs))
        };
        _expandAnimTimer.Tick += OnExpandAnimationTick;
        _expandAnimTimer.Start();

        ApplyExpandAnimationFrame(0);
    }

    private void OnExpandAnimationTick(object? sender, EventArgs e)
    {
        var durationMs = _expandAnimationTargetExpanded
            ? ExpandAnimationDurationMs
            : CollapseAnimationDurationMs;
        var elapsedMs = Math.Max(0, Environment.TickCount64 - _expandAnimationStartTick);
        var progress = durationMs <= 0
            ? 1.0
            : Math.Clamp(elapsedMs / durationMs, 0.0, 1.0);

        ApplyExpandAnimationFrame(progress);

        if (progress >= 1.0)
        {
            CompleteExpandAnimation();
        }
    }

    private void ApplyExpandAnimationFrame(double progress)
    {
        if (_childrenPanel == null)
        {
            return;
        }

        var easedProgress = _expandAnimationTargetExpanded
            ? s_expandHeightEase.Ease(progress)
            : s_collapseEase.Ease(progress);
        var arrowProgress = _expandAnimationTargetExpanded
            ? s_arrowExpandEase.Ease(progress)
            : s_collapseEase.Ease(progress);

        _childrenPanel.MaxHeight = Math.Max(0, Lerp(_expandAnimationFromHeight, _expandAnimationToHeight, easedProgress));
        SetChevronAngle(Lerp(_expandAnimationFromAngle, _expandAnimationToAngle, arrowProgress));

        if (_expandAnimationTargetExpanded)
        {
            ApplyClothOffsets(progress);
        }
        else
        {
            ClearChildOffsets();
        }

        InvalidateMeasure();
    }

    private void CompleteExpandAnimation()
    {
        StopExpandAnimation();

        if (_childrenPanel != null)
        {
            _childrenPanel.Visibility = _expandAnimationTargetExpanded ? Visibility.Visible : Visibility.Collapsed;
            _childrenPanel.MaxHeight = double.PositiveInfinity;
            _childrenPanel.ClipToBounds = false;
        }

        ClearChildOffsets();
        SetChevronAngle(_expandAnimationTargetExpanded ? 90 : 0);
        InvalidateMeasure();
    }

    private void StopExpandAnimation()
    {
        if (_expandAnimTimer == null && _expandAnimationChildren.Length == 0)
        {
            return;
        }

        if (_expandAnimTimer != null)
        {
            _expandAnimTimer.Stop();
            _expandAnimTimer.Tick -= OnExpandAnimationTick;
            _expandAnimTimer = null;
        }

        ClearChildOffsets();
        _expandAnimationChildren = [];
    }

    private double GetCurrentChildrenPanelHeight()
    {
        if (_childrenPanel == null || _childrenPanel.Visibility != Visibility.Visible)
        {
            return 0;
        }

        if (_childrenPanel.ActualHeight > 0)
        {
            return _childrenPanel.ActualHeight;
        }

        if (!double.IsInfinity(_childrenPanel.MaxHeight))
        {
            return Math.Max(0, _childrenPanel.MaxHeight);
        }

        return MeasureChildrenPanelNaturalHeight();
    }

    private double MeasureChildrenPanelNaturalHeight()
    {
        if (_childrenPanel == null)
        {
            return 0;
        }

        var previousVisibility = _childrenPanel.Visibility;
        var previousMaxHeight = _childrenPanel.MaxHeight;
        var previousClipToBounds = _childrenPanel.ClipToBounds;

        var availableWidth = _childrenPanel.ActualWidth > 0
            ? _childrenPanel.ActualWidth
            : (ActualWidth > 0 ? ActualWidth : double.PositiveInfinity);

        _childrenPanel.Visibility = Visibility.Visible;
        _childrenPanel.MaxHeight = double.PositiveInfinity;
        _childrenPanel.ClipToBounds = false;
        _childrenPanel.Measure(new Size(availableWidth, double.PositiveInfinity));
        var desiredHeight = _childrenPanel.DesiredSize.Height;

        _childrenPanel.Visibility = previousVisibility;
        _childrenPanel.MaxHeight = previousMaxHeight;
        _childrenPanel.ClipToBounds = previousClipToBounds;

        return Math.Max(0, desiredHeight);
    }

    private double GetCurrentChevronAngle()
    {
        if (_chevron?.RenderTransform is RotateTransform rotateTransform)
        {
            return rotateTransform.Angle;
        }

        return IsExpanded ? 90 : 0;
    }

    private void SetChevronAngle(double angle)
    {
        if (_chevron == null)
        {
            return;
        }

        var rotateTransform = _chevron.RenderTransform as RotateTransform ?? new RotateTransform();
        rotateTransform.Angle = angle;
        _chevron.RenderTransformOrigin = new Point(0.5, 0.5);
        _chevron.RenderTransform = rotateTransform;
        _chevron.InvalidateVisual();
    }

    private ClothChild[] CollectClothChildren(double targetHeight)
    {
        if (_childrenPanel == null || _childrenPanel.Children.Count == 0)
        {
            return [];
        }

        var children = new List<UIElement>();
        foreach (var child in _childrenPanel.Children)
        {
            if (child is UIElement uiElement && uiElement.Visibility == Visibility.Visible)
            {
                children.Add(uiElement);
            }
        }

        if (children.Count == 0)
        {
            return [];
        }

        var baseOffset = Math.Min(Math.Max(12.0, targetHeight * 0.22), 36.0);
        var result = new ClothChild[children.Count];
        for (int i = 0; i < children.Count; i++)
        {
            var normalizedIndex = (i + 1.0) / children.Count;
            result[i] = new ClothChild(
                children[i],
                -baseOffset * normalizedIndex,
                Math.Min(0.45, i * ClothStaggerProgress));
        }

        return result;
    }

    private void ApplyClothOffsets(double progress)
    {
        if (_expandAnimationChildren.Length == 0)
        {
            return;
        }

        for (int i = 0; i < _expandAnimationChildren.Length; i++)
        {
            var child = _expandAnimationChildren[i];
            var localProgress = child.ProgressDelay >= 1.0
                ? 1.0
                : Math.Clamp((progress - child.ProgressDelay) / (1.0 - child.ProgressDelay), 0.0, 1.0);
            var eased = s_clothEase.Ease(localProgress);
            child.Element.RenderOffset = new Point(0, child.InitialY * (1.0 - eased));
        }
    }

    private void ClearChildOffsets()
    {
        if (_expandAnimationChildren.Length == 0)
        {
            return;
        }

        for (int i = 0; i < _expandAnimationChildren.Length; i++)
        {
            _expandAnimationChildren[i].Element.RenderOffset = default;
        }
    }

    private static double Lerp(double from, double to, double progress) =>
        from + ((to - from) * progress);

    #endregion

    #region Property Changed Callbacks

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationViewItem item)
        {
            item.SelectionChanged?.Invoke(item, (bool)(e.NewValue ?? false));
        }
    }

    private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationViewItem item)
        {
            var expanded = (bool)(e.NewValue ?? false);
            item.ExpansionChanged?.Invoke(item, expanded);

            if (item.ShouldAnimateExpandedStateChange())
            {
                item.BeginExpandedStateAnimation(expanded);
            }
            else
            {
                item.SyncExpandedVisualState();
            }

            item.InvalidateMeasure();
        }
    }

    #endregion
}

/// <summary>
/// Provides data for the NavigationViewItem.Invoked event.
/// </summary>
public sealed class NavigationViewItemInvokedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the invoked item.
    /// </summary>
    public NavigationViewItem InvokedItem { get; }

    /// <summary>
    /// Gets a value indicating whether the invoked item is a settings item.
    /// </summary>
    public bool IsSettingsInvoked { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationViewItemInvokedEventArgs"/> class.
    /// </summary>
    public NavigationViewItemInvokedEventArgs(NavigationViewItem invokedItem, bool isSettingsInvoked = false)
    {
        InvokedItem = invokedItem;
        IsSettingsInvoked = isSettingsInvoked;
    }
}

/// <summary>
/// Represents a header item in a NavigationView.
/// </summary>
public class NavigationViewItemHeader : ContentControl
{
    private static readonly SolidColorBrush s_defaultFgBrush = new(Color.FromRgb(157, 157, 157));

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationViewItemHeader"/> class.
    /// </summary>
    public NavigationViewItemHeader()
    {
        Focusable = false;
    }

    /// <summary>
    /// Override to prevent Content from being added to visual tree.
    /// </summary>
    protected override void OnContentChanged(object? oldContent, object? newContent)
    {
        // Don't call base - we render content manually
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnRender(object drawingContextObj)
    {
        if (drawingContextObj is not DrawingContext dc)
            return;

        var text = GetContentText();
        if (!string.IsNullOrEmpty(text))
        {
            var fontMetrics = TextMeasurement.GetFontMetrics("Segoe UI Semibold", 14);
            var brush = ResolveForegroundBrush();
            var formattedText = new FormattedText(text, "Segoe UI Semibold", 14)
            {
                Foreground = brush
            };
            var textY = (ActualHeight - fontMetrics.LineHeight) / 2;
            dc.DrawText(formattedText, new Point(0, textY));
        }
    }

    private string? GetContentText()
    {
        if (Content == null) return null;
        if (Content is string str) return str;
        if (Content is TextBlock textBlock) return textBlock.Text;
        return Content.ToString();
    }

    private Brush ResolveForegroundBrush()
    {
        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
        {
            return Foreground;
        }

        return TryFindResource("TextSecondary") as Brush
            ?? Foreground
            ?? s_defaultFgBrush;
    }
}

/// <summary>
/// Represents a separator in a NavigationView.
/// </summary>
public class NavigationViewItemSeparator : Control
{
    private static readonly SolidColorBrush s_defaultBackgroundBrush = new(Color.FromRgb(60, 60, 60));

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationViewItemSeparator"/> class.
    /// </summary>
    public NavigationViewItemSeparator()
    {
        Focusable = false;
    }

    protected override void OnRender(object drawingContextObj)
    {
        if (drawingContextObj is not DrawingContext dc)
        {
            base.OnRender(drawingContextObj);
            return;
        }

        var brush = ResolveBackgroundBrush();
        dc.DrawRectangle(brush, null, new Rect(0, 0, ActualWidth, 1));
    }

    private Brush ResolveBackgroundBrush()
    {
        if (HasLocalValue(Control.BackgroundProperty) && Background != null)
        {
            return Background;
        }

        return TryFindResource("ControlBorder") as Brush
            ?? Background
            ?? s_defaultBackgroundBrush;
    }
}
