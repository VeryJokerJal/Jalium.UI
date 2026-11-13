using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;
/// <summary>Templated scrolling for existing popup menu containers.</summary>
[TemplatePart(Name = "PART_ScrollViewer", Type = typeof(ScrollViewer))]
[TemplatePart(Name = "PART_ItemsPanel", Type = typeof(Panel))]
[TemplatePart(Name = "PART_ScrollUpButton", Type = typeof(RepeatButton))]
[TemplatePart(Name = "PART_ScrollDownButton", Type = typeof(RepeatButton))]
internal sealed class MenuPopupScrollHost : Control
{
    private const double ScrollStepPerClick = ScrollViewer.LineScrollAmount * 2.0;
    public static readonly DependencyProperty ScrollButtonHeightProperty = DependencyProperty.Register(nameof(ScrollButtonHeight), typeof(double), typeof(MenuPopupScrollHost), new PropertyMetadata(18.0, (d, _) => ((MenuPopupScrollHost)d).UpdateScrollState()), value => value is double height && double.IsFinite(height) && height >= 0);
    private static readonly DependencyPropertyKey IsOverflowingPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsOverflowing), typeof(bool), typeof(MenuPopupScrollHost), new PropertyMetadata(false));
    public static readonly DependencyProperty IsOverflowingProperty = IsOverflowingPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey CanScrollUpPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CanScrollUp), typeof(bool), typeof(MenuPopupScrollHost), new PropertyMetadata(false));
    public static readonly DependencyProperty CanScrollUpProperty = CanScrollUpPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey CanScrollDownPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CanScrollDown), typeof(bool), typeof(MenuPopupScrollHost), new PropertyMetadata(false));
    public static readonly DependencyProperty CanScrollDownProperty = CanScrollDownPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey EffectiveScrollButtonHeightPropertyKey = DependencyProperty.RegisterReadOnly(nameof(EffectiveScrollButtonHeight), typeof(double), typeof(MenuPopupScrollHost), new PropertyMetadata(0.0));
    public static readonly DependencyProperty EffectiveScrollButtonHeightProperty = EffectiveScrollButtonHeightPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey VerticalOffsetPropertyKey = DependencyProperty.RegisterReadOnly(nameof(VerticalOffset), typeof(double), typeof(MenuPopupScrollHost), new PropertyMetadata(0.0));
    public static readonly DependencyProperty VerticalOffsetProperty = VerticalOffsetPropertyKey.DependencyProperty;
    private Panel? _itemsPanel;
    private ScrollViewer? _scrollViewer;
    private RepeatButton? _scrollUpButton, _scrollDownButton;
    private bool _subscribed, _suspended, _updatingState, _pendingRestore;
    private double _savedOffset;
    static MenuPopupScrollHost() => DefaultStyleKeyProperty.OverrideMetadata(typeof(MenuPopupScrollHost), new PropertyMetadata(typeof(MenuPopupScrollHost)));
    public MenuPopupScrollHost()
    {
        Items = new MenuPopupItemCollection();
        Items.CollectionChanged += OnItemsChanged;
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(OnMouseWheelHandler));
        AddHandler(MouseWheelEvent, new MouseWheelEventHandler(OnMouseWheelHandler), true);
        SizeChanged += (_, _) => UpdateScrollState();
        LayoutUpdated += (_, _) => UpdateScrollState();
        Loaded += (_, _) => Resume();
        Unloaded += (_, _) =>
        {
            SaveOffset();
            _suspended = true;
            DetachListeners();
            _itemsPanel?.Children.Clear();
            UpdateScrollState();
        };
    }

    public MenuPopupItemCollection Items { get; }

    internal Panel? ItemsHostPanel
    {
        get
        {
            UpdateDefaultStyle();
            ApplyTemplate();
            return _itemsPanel;
        }
    }

    public double ScrollButtonHeight { get => (double)GetValue(ScrollButtonHeightProperty)!; set => SetValue(ScrollButtonHeightProperty, value); }
    public bool IsOverflowing => (bool)GetValue(IsOverflowingProperty)!;
    public bool CanScrollUp => (bool)GetValue(CanScrollUpProperty)!;
    public bool CanScrollDown => (bool)GetValue(CanScrollDownProperty)!;
    public double EffectiveScrollButtonHeight => (double)GetValue(EffectiveScrollButtonHeightProperty)!;
    public double VerticalOffset => (double)GetValue(VerticalOffsetProperty)!;

    public override void OnApplyTemplate()
    {
        ReleaseParts();
        base.OnApplyTemplate();
        _itemsPanel = GetTemplateChild("PART_ItemsPanel") as Panel;
        _scrollViewer = GetTemplateChild("PART_ScrollViewer") as ScrollViewer;
        _scrollUpButton = GetTemplateChild("PART_ScrollUpButton") as RepeatButton;
        _scrollDownButton = GetTemplateChild("PART_ScrollDownButton") as RepeatButton;
        if (_itemsPanel is not null)
            _itemsPanel.PreserveExistingLogicalParents = true;
        _pendingRestore = _savedOffset > 0;
        if (!_suspended)
        {
            AttachListeners();
            SyncItems();
        }

        UpdateScrollState();
    }

    internal override void OnTemplateContentClearing()
    {
        ReleaseParts();
        base.OnTemplateContentClearing();
    }

    private void ReleaseParts()
    {
        SaveOffset();
        DetachListeners();
        var panel = _itemsPanel;
        _itemsPanel = null;
        _scrollViewer = null;
        _scrollUpButton = _scrollDownButton = null;
        panel?.Children.Clear();
    }

    internal void RestoreVerticalOffset(double offset)
    {
        if (!double.IsFinite(offset) || offset < 0)
            return;
        _savedOffset = offset;
        _pendingRestore = true;
        UpdateScrollState();
    }

    private void SaveOffset()
    {
        if (!_pendingRestore && _scrollViewer is not null)
            _savedOffset = _scrollViewer.VerticalOffset;
    }

    private void AttachListeners()
    {
        if (_subscribed)
            return;
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged += OnScrollChanged;
        if (_scrollUpButton is not null)
            _scrollUpButton.Click += OnScrollUp;
        if (_scrollDownButton is not null)
            _scrollDownButton.Click += OnScrollDown;
        _subscribed = true;
    }

    private void DetachListeners()
    {
        if (!_subscribed)
            return;
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        if (_scrollUpButton is not null)
            _scrollUpButton.Click -= OnScrollUp;
        if (_scrollDownButton is not null)
            _scrollDownButton.Click -= OnScrollDown;
        _subscribed = false;
    }

    private void Resume()
    {
        _suspended = false;
        _pendingRestore = _savedOffset > 0;
        AttachListeners();
        SyncItems();
        UpdateScrollState();
    }

    internal void PrepareForOpen()
    {
        UpdateDefaultStyle();
        ApplyTemplate();
        Resume();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // A cached popup measures before its Loaded event. A closed/detached
        // popup must remain suspended even if an old layout pass reaches it.
        if (_suspended)
            for (var ancestor = VisualParent; ancestor is not null; ancestor = (ancestor as Visual)?.VisualParent)
                if (ancestor is PopupRoot root && root.OwnerPopup.IsOpen)
                {
                    Resume();
                    break;
                }

        return base.MeasureOverride(availableSize);
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            _savedOffset = 0;
            _pendingRestore = false;
        }

        if (_suspended || _itemsPanel is null)
            return;
        SyncItems();
        UpdateScrollState();
    }

    private void SyncItems()
    {
        if (_suspended || _itemsPanel is not { } panel)
            return;
        var current = Items.ToArray();
        var retained = new HashSet<UIElement>(current, ReferenceEqualityComparer.Instance);
        panel.Children.BeginBatchUpdate();
        try
        {
            for (int index = panel.Children.Count - 1; index >= 0; index--)
                if (!retained.Contains(panel.Children[index]))
                    panel.Children.RemoveAt(index);
            for (int index = 0; index < current.Length; index++)
            {
                var item = current[index];
                if (index < panel.Children.Count && ReferenceEquals(panel.Children[index], item))
                    continue;
                item.DetachFromVisualParent();
                panel.Children.Insert(index, item);
            }
        }
        finally
        {
            panel.Children.EndBatchUpdate();
        }
    }

    private void UpdateScrollState()
    {
        if (_updatingState)
            return;
        _updatingState = true;
        try
        {
            var viewer = _scrollViewer;
            if (!_suspended && _pendingRestore && viewer is { ViewportHeight: > 0 })
            {
                var restore = _savedOffset;
                _pendingRestore = false;
                viewer.ScrollToVerticalOffset(restore);
            }

            bool overflow = !_suspended && Items is { Count: > 0 } && viewer is not null && ActualHeight > 0 && viewer.ExtentHeight > ActualHeight + 0.5;
            if (!overflow && !_suspended && !_pendingRestore && viewer is { VerticalOffset: > 0 } && ActualHeight > 0)
                viewer.ScrollToVerticalOffset(0);
            double offset = viewer?.VerticalOffset ?? 0;
            bool up = overflow && IsEnabled && offset > 0.5;
            bool down = overflow && IsEnabled && viewer!.ScrollableHeight - offset > 0.5;
            SetValue(IsOverflowingPropertyKey, overflow);
            SetValue(CanScrollUpPropertyKey, up);
            SetValue(CanScrollDownPropertyKey, down);
            SetValue(EffectiveScrollButtonHeightPropertyKey, overflow ? Math.Min(ScrollButtonHeight, ActualHeight * 0.5) : 0.0);
            SetValue(VerticalOffsetPropertyKey, offset);
            if (!_pendingRestore && !_suspended)
                _savedOffset = offset;
            VisualStateManager.GoToState(this, !overflow ? "NoOverflow" : !up ? "AtStart" : !down ? "AtEnd" : "Scrolling", false);
            VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        }
        finally
        {
            _updatingState = false;
        }
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        UpdateScrollState();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs args) => UpdateScrollState();
    private void OnScrollUp(object sender, RoutedEventArgs args) => ScrollBy(-ScrollStepPerClick);
    private void OnScrollDown(object sender, RoutedEventArgs args) => ScrollBy(ScrollStepPerClick);
    private void ScrollBy(double delta)
    {
        if (!_suspended && IsEnabled && IsOverflowing && double.IsFinite(delta))
            _scrollViewer?.ScrollToVerticalOffset((_scrollViewer?.VerticalOffset ?? 0) + delta);
    }

    private void OnMouseWheelHandler(object sender, MouseWheelEventArgs args)
    {
        var input = new MouseWheelScrollInput(args, 0, ScrollStepPerClick, remapShift: false);
        if (_suspended || !IsEnabled || !IsOverflowing || input.Vertical == 0 || _scrollViewer is null)
            return;
        _scrollViewer.ScrollToVerticalOffset(_scrollViewer.VerticalOffset + input.Vertical);
        input.MarkHandled(args, horizontal: false, vertical: true);
    }
}

/// <summary>Stable menu containers, with one reset notification for a batch.</summary>
internal sealed class MenuPopupItemCollection : ObservableCollection<UIElement>
{
    private int _batchDepth;
    private bool _pendingChange;
    public void BeginBatchUpdate() => _batchDepth++;
    public void EndBatchUpdate()
    {
        if (_batchDepth == 0)
            throw new InvalidOperationException("No menu item update batch is active.");
        if (--_batchDepth == 0 && _pendingChange)
        {
            _pendingChange = false;
            base.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs args)
    {
        if (_batchDepth > 0)
            _pendingChange = true;
        else
            base.OnCollectionChanged(args);
    }

    protected override void InsertItem(int index, UIElement item)
    {
        Validate(item, -1);
        base.InsertItem(index, item);
    }

    protected override void SetItem(int index, UIElement item)
    {
        Validate(item, index);
        base.SetItem(index, item);
    }

    private void Validate(UIElement item, int ignoreIndex)
    {
        ArgumentNullException.ThrowIfNull(item);
        for (int index = 0; index < Count; index++)
            if (index != ignoreIndex && ReferenceEquals(this[index], item))
                throw new ArgumentException("A menu container cannot appear twice in the same popup.", nameof(item));
    }
}

/// <summary>Reports natural popup height while keeping an active scrolling viewport finite.</summary>
internal sealed class MenuPopupScrollViewer : ScrollViewer
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsPositiveInfinity(availableSize.Height) && ActualHeight > 0)
        {
            var measured = base.MeasureOverride(new Size(availableSize.Width, ActualHeight));
            return new Size(measured.Width, Math.Max(0, ExtentHeight));
        }

        return base.MeasureOverride(availableSize);
    }
}
