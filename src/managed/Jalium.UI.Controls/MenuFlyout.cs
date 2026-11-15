using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;
/// <summary>
/// Represents a flyout that displays a menu of commands.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Items")]
public sealed class MenuFlyout : FlyoutBase
{
    /// <summary>Identifies the MenuFlyoutPresenterStyle dependency property.</summary>
    public static readonly DependencyProperty MenuFlyoutPresenterStyleProperty = DependencyProperty.Register(nameof(MenuFlyoutPresenterStyle), typeof(Style), typeof(MenuFlyout), new PropertyMetadata(null, (d, args) =>
    {
        if (((MenuFlyout)d)._presenter is { } presenter)
            presenter.SetCurrentValue(FrameworkElement.StyleProperty, args.NewValue);
    }));
    private readonly ObservableCollection<Control> _items = new();
    private MenuFlyoutPresenter? _presenter;
    public MenuFlyout()
    {
        Opened += (_, _) => Dispatcher.BeginInvokeCritical(() =>
        {
            if (IsOpen)
                _presenter?.FocusFirstItem();
        });
    }

    /// <summary>
    /// Gets the collection of items in the MenuFlyout.
    /// </summary>
    public IList<Control> Items => _items;
    internal ObservableCollection<Control> ItemCollection => _items;
    /// <summary>
    /// Gets or sets the style applied to the MenuFlyoutPresenter.
    /// </summary>
    public Style? MenuFlyoutPresenterStyle { get => (Style? )GetValue(MenuFlyoutPresenterStyleProperty); set => SetValue(MenuFlyoutPresenterStyleProperty, value); }

    /// <inheritdoc/>
    protected override Control CreatePresenter()
    {
        return _presenter = new MenuFlyoutPresenter(this)
        {
            Style = MenuFlyoutPresenterStyle
        };
    }
}

/// <summary>Displays the original containers of a MenuFlyout through a replaceable template.</summary>
[TemplatePart(Name = "PART_ScrollHost", Type = typeof(MenuPopupScrollHost))]
internal sealed class MenuFlyoutPresenter : Control
{
    private readonly MenuFlyout _flyout;
    private MenuPopupScrollHost? _scrollHost;
    private bool _observing, _suspended;
    private double _savedOffset;
    static MenuFlyoutPresenter()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MenuFlyoutPresenter), new PropertyMetadata(typeof(MenuFlyoutPresenter)));
        FocusableProperty.OverrideMetadata(typeof(MenuFlyoutPresenter), new PropertyMetadata(true));
        UseLayoutRoundingProperty.OverrideMetadata(typeof(MenuFlyoutPresenter), new PropertyMetadata(true));
        BorderThicknessProperty.OverrideMetadata(typeof(MenuFlyoutPresenter), new PropertyMetadata(new Thickness(1)));
        PaddingProperty.OverrideMetadata(typeof(MenuFlyoutPresenter), new PropertyMetadata(new Thickness(4)));
        CornerRadiusProperty.OverrideMetadata(typeof(MenuFlyoutPresenter), new PropertyMetadata(new CornerRadius(8)));
    }

    public MenuFlyoutPresenter(MenuFlyout flyout)
    {
        _flyout = flyout;
        AttachItemsListener();
        Loaded += (_, _) => Resume();
        Unloaded += (_, _) =>
        {
            _suspended = true;
            DetachItemsListener();
        };
        KeyDown += OnKeyDown;
    }

    public override void OnApplyTemplate()
    {
        ReleaseParts();
        base.OnApplyTemplate();
        _scrollHost = GetTemplateChild("PART_ScrollHost") as MenuPopupScrollHost;
        RefreshItems();
        _scrollHost?.RestoreVerticalOffset(_savedOffset);
        UpdateVisualState();
    }

    internal override void OnTemplateContentClearing()
    {
        ReleaseParts();
        base.OnTemplateContentClearing();
    }

    private void ReleaseParts()
    {
        if (_scrollHost is not { } old)
            return;
        _savedOffset = old.VerticalOffset;
        _scrollHost = null;
        old.Items.Clear();
    }

    private void AttachItemsListener()
    {
        if (_observing)
            return;
        _flyout.ItemCollection.CollectionChanged += OnFlyoutItemsChanged;
        _observing = true;
    }

    private void DetachItemsListener()
    {
        if (!_observing)
            return;
        _flyout.ItemCollection.CollectionChanged -= OnFlyoutItemsChanged;
        _observing = false;
    }

    private void Resume()
    {
        _suspended = false;
        AttachItemsListener();
        RefreshItems();
        UpdateVisualState();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Popup layout precedes Loaded when a cached presenter is reopened.
        if (_suspended)
            for (var ancestor = VisualParent; ancestor is not null; ancestor = (ancestor as Visual)?.VisualParent)
                if (ancestor is PopupRoot root && root.OwnerPopup.IsOpen)
                {
                    Resume();
                    break;
                }

        return base.MeasureOverride(availableSize);
    }

    private void OnFlyoutItemsChanged(object? sender, NotifyCollectionChangedEventArgs args) => RefreshItems();
    private void RefreshItems()
    {
        if (_suspended || _scrollHost is not { } host)
            return;
        if (host.Items.Count == _flyout.Items.Count && host.Items.SequenceEqual(_flyout.Items))
            return;
        host.Items.BeginBatchUpdate();
        try
        {
            host.Items.Clear();
            foreach (var item in _flyout.Items)
                host.Items.Add(item);
        }
        finally
        {
            host.Items.EndBatchUpdate();
        }
    }

    internal void FocusFirstItem()
    {
        // Opening can request focus before the first native layout pass.
        // Materialize the current template's host so keyboard routes can walk
        // from the focused command back to its popup and top-level owner.
        UpdateDefaultStyle();
        ApplyTemplate();
        if (_suspended)
            Resume();
        _scrollHost?.PrepareForOpen();
        // Cached popup rows can outlive their owner's DataContext. Re-resolve
        // bindings before choosing the first enabled command on each opening.
        foreach (var item in _flyout.Items.ToArray())
            ReactivateBindingsRecursive(item);
        foreach (var item in _flyout.Items)
            if (item.IsEnabled && item.Visibility == Visibility.Visible && item.Focusable && item.Focus())
                return;
        Focus();
    }

    internal FrameworkElement? OwnerTarget => _flyout.PlacementTarget;

    internal void CloseMenu() => _flyout.Hide();
    internal void CloseAndRestoreFocus()
    {
        var target = OwnerTarget;
        _flyout.Hide();
        Dispatcher.BeginInvokeCritical(() => target?.Focus());
    }

    private void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape)
            return;
        CloseAndRestoreFocus();
        args.Handled = true;
    }

    internal MenuPopupChrome GetChrome() => new(CornerRadius, Padding, BorderThickness, Background, BorderBrush);
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        UpdateVisualState();
    }

    private void UpdateVisualState() => VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
}
