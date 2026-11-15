using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;
/// <summary>
/// Represents a top-level menu in a MenuBar control.
/// </summary>
[Jalium.UI.Markup.ContentProperty(nameof(Items))]
[TemplatePart(Name = "PART_Root", Type = typeof(Border))]
[TemplatePart(Name = "PART_Title", Type = typeof(TextBlock))]
public class MenuBarItem : Control
{
    /// <inheritdoc/>
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => new Jalium.UI.Automation.Peers.MenuBarItemAutomationPeer(this);
    private static readonly SolidColorBrush s_fallbackTextBrush = new(Color.FromRgb(255, 255, 255));
    private readonly ObservableCollection<Control> _items = new();
    private readonly HashSet<Control> _ownedItems = new(ReferenceEqualityComparer.Instance);
    private MenuFlyout? _flyout;
    private static readonly DependencyPropertyKey ItemsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Items), typeof(IList<Control>), typeof(MenuBarItem), new PropertyMetadata(null));
    public static readonly DependencyProperty ItemsProperty = ItemsPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey IsMenuOpenPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsMenuOpen), typeof(bool), typeof(MenuBarItem), new PropertyMetadata(false));
    public static readonly DependencyProperty IsMenuOpenProperty = IsMenuOpenPropertyKey.DependencyProperty;
    public static readonly DependencyProperty HoverBackgroundProperty = DependencyProperty.Register(nameof(HoverBackground), typeof(Brush), typeof(MenuBarItem), new PropertyMetadata(null));
    public Brush? HoverBackground { get => (Brush? )GetValue(HoverBackgroundProperty); set => SetValue(HoverBackgroundProperty, value); }
    protected internal override System.Collections.IEnumerator LogicalChildren => _items.GetEnumerator();

    protected override void OnDataContextChanged(object? oldValue, object? newValue)
    {
        base.OnDataContextChanged(oldValue, newValue);
        foreach (var item in _items.ToArray())
            ReactivateBindingsRecursive(item);
    }

#region Dependency Properties
    /// <summary>
    /// Identifies the Title dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(MenuBarItem), new PropertyMetadata(string.Empty));
#endregion
#region CLR Properties
    /// <summary>
    /// Gets or sets the title of the menu bar item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Title { get => (string? )GetValue(TitleProperty) ?? string.Empty; set => SetValue(TitleProperty, value); }
    /// <summary>
    /// Gets the collection of menu items in this menu.
    /// </summary>
    public IList<Control> Items => _items;
    internal ObservableCollection<Control> ItemCollection => _items;
    /// <summary>
    /// Gets a value indicating whether the drop-down menu is open.
    /// </summary>
    public bool IsMenuOpen => (bool)GetValue(IsMenuOpenProperty)!;
    /// <summary>
    /// Gets or sets the parent MenuBar.
    /// </summary>
    internal MenuBar? ParentMenuBar { get; set; }

#endregion
    /// <summary>
    /// Initializes a new instance of the MenuBarItem class.
    /// </summary>
    static MenuBarItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MenuBarItem), new PropertyMetadata(typeof(MenuBarItem)));
        FocusableProperty.OverrideMetadata(typeof(MenuBarItem), new PropertyMetadata(true));
    }

    public MenuBarItem()
    {
        SetValue(ItemsPropertyKey, _items);
        _items.CollectionChanged += OnItemsCollectionChanged;
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseEnterEvent, new MouseEventHandler(OnMouseEnterHandler));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        Loaded += (_, _) => UpdateItemPresentation();
    }

    private static readonly DependencyPropertyKey ItemForegroundPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ItemForeground), typeof(Brush), typeof(MenuBarItem), new PropertyMetadata(s_fallbackTextBrush));
    public static readonly DependencyProperty ItemForegroundProperty = ItemForegroundPropertyKey.DependencyProperty;
    public Brush? ItemForeground => (Brush? )GetValue(ItemForegroundProperty);

    private static readonly DependencyPropertyKey ItemFontSizePropertyKey = DependencyProperty.RegisterReadOnly(nameof(ItemFontSize), typeof(double), typeof(MenuBarItem), new PropertyMetadata(14.0));
    public static readonly DependencyProperty ItemFontSizeProperty = ItemFontSizePropertyKey.DependencyProperty;
    public double ItemFontSize => (double)GetValue(ItemFontSizeProperty)!;

    private static readonly DependencyPropertyKey ItemFontFamilyPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ItemFontFamily), typeof(FontFamily), typeof(MenuBarItem), new PropertyMetadata(FontFamilyProperty.GetMetadata(typeof(Control)).DefaultValue));
    public static readonly DependencyProperty ItemFontFamilyProperty = ItemFontFamilyPropertyKey.DependencyProperty;
    public FontFamily ItemFontFamily => (FontFamily)GetValue(ItemFontFamilyProperty)!;

    private static readonly DependencyPropertyKey ItemFontWeightPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ItemFontWeight), typeof(FontWeight), typeof(MenuBarItem), new PropertyMetadata(FontWeightProperty.GetMetadata(typeof(Control)).DefaultValue));
    public static readonly DependencyProperty ItemFontWeightProperty = ItemFontWeightPropertyKey.DependencyProperty;
    public FontWeight ItemFontWeight => (FontWeight)GetValue(ItemFontWeightProperty)!;

    private static readonly DependencyPropertyKey ShowHighlightPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ShowHighlight), typeof(bool), typeof(MenuBarItem), new PropertyMetadata(false));
    public static readonly DependencyProperty ShowHighlightProperty = ShowHighlightPropertyKey.DependencyProperty;
    public bool ShowHighlight => (bool)GetValue(ShowHighlightProperty)!;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateItemPresentation();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        UpdateItemPresentation();
        return base.MeasureOverride(availableSize);
    }

    protected override void RenderTemplatedBackground(DrawingContext context)
    {
        UpdateItemPresentation();
        if (RenderSize.Width > 0 && RenderSize.Height > 0)
            base.RenderTemplatedBackground(context);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.Property == ForegroundProperty || args.Property == BackgroundProperty || args.Property == FontSizeProperty || args.Property == FontFamilyProperty || args.Property == FontWeightProperty || args.Property == IsMouseOverProperty || args.Property == HoverBackgroundProperty)
            UpdateItemPresentation();
    }

    protected override void OnIsKeyboardFocusedChanged(bool focused)
    {
        base.OnIsKeyboardFocusedChanged(focused);
        UpdateItemPresentation();
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        if (!newValue)
            CloseMenu();
        UpdateItemPresentation();
    }

    private void OnFlyoutStateChanged(object? sender, EventArgs args) => UpdateItemPresentation();
    private void UpdateItemPresentation()
    {
        var open = _flyout?.IsOpen == true;
        SetValue(IsMenuOpenPropertyKey, open);
        SetValue(ItemForegroundPropertyKey, ResolveForegroundBrush());
        SetValue(ItemFontSizePropertyKey, FontSize);
        SetValue(ItemFontFamilyPropertyKey, FontFamily);
        SetValue(ItemFontWeightPropertyKey, FontWeight);
        var transparent = Background is null || Background is SolidColorBrush brush && brush.Color.A == 0;
        SetValue(ShowHighlightPropertyKey, IsEnabled && transparent && (IsMouseOver || IsKeyboardFocused || open) && GetEffectiveValueLayer(BackgroundProperty)is null or DependencyValueStore.Layer.StyleSetter);
        VisualStateManager.GoToState(this, !IsEnabled ? "Disabled" : open ? "MenuOpen" : IsKeyboardFocused ? "Focused" : IsMouseOver ? "PointerOver" : "Normal", false);
        VisualStateManager.GoToState(this, open ? "Open" : "Closed", false);
    }

    private Brush ResolveForegroundBrush()
    {
        return Foreground != null && DependencyPropertyHelper.GetValueSource(this, ForegroundProperty).BaseValueSource != BaseValueSource.Default ? Foreground : ResolveBrush("OneTextPrimary", "TextPrimary", s_fallbackTextBrush);
    }

    private Brush ResolveBrush(string primaryKey, string secondaryKey, Brush fallback)
    {
        if (TryFindResource(primaryKey)is Brush primary)
            return primary;
        if (TryFindResource(secondaryKey)is Brush secondary)
            return secondary;
        return fallback;
    }

    /// <summary>
    /// Opens the drop-down menu.
    /// </summary>
    public void OpenMenu()
    {
        if (!IsEnabled)
            return;
        if (_flyout == null)
        {
            _flyout = new MenuFlyout();
            _flyout.Opened += OnFlyoutStateChanged;
            _flyout.Closed += OnFlyoutStateChanged;
            foreach (var item in _items)
                _flyout.Items.Add(item);
        }

        ParentMenuBar?.CloseAllMenus(this);
        _flyout.ShowAt(this);
        UpdateItemPresentation();
    }

    internal void OpenMenuAndFocusFirstItem()
    {
        OpenMenu();
        FocusFirstMenuItem();
    }

    /// <summary>
    /// Closes the drop-down menu.
    /// </summary>
    public void CloseMenu()
    {
        _flyout?.Hide();
        UpdateItemPresentation();
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled)
            return;
        if (IsMenuOpen)
            CloseMenu();
        else
            OpenMenu();
        e.Handled = true;
    }

    private void OnMouseEnterHandler(object sender, MouseEventArgs e)
    {
        // Keep hover visual only. Top-level menu opens by click to avoid accidental popup.
        UpdateItemPresentation();
    }

    private void OnMouseLeaveHandler(object sender, MouseEventArgs e)
    {
        UpdateItemPresentation();
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (!IsEnabled)
        {
            return;
        }

        var menuModeActive = ParentMenuBar?.IsAnyMenuOpen() == true;
        var handled = e.Key switch
        {
            Key.Enter or Key.Space or Key.Down => OpenFromKeyboard(),
            Key.Escape => CloseFromKeyboard(),
            Key.Left => ParentMenuBar?.FocusSibling(this, -1, menuModeActive) == true,
            Key.Right => ParentMenuBar?.FocusSibling(this, 1, menuModeActive) == true,
            Key.Home => ParentMenuBar?.FocusBoundaryItem(last: false, openMenu: menuModeActive) == true,
            Key.End => ParentMenuBar?.FocusBoundaryItem(last: true, openMenu: menuModeActive) == true,
            _ => false
        };
        if (handled)
        {
            e.Handled = true;
        }
    }

    private bool OpenFromKeyboard()
    {
        OpenMenuAndFocusFirstItem();
        return true;
    }

    private bool CloseFromKeyboard()
    {
        if (!IsMenuOpen)
        {
            return false;
        }

        CloseMenu();
        Focus();
        return true;
    }

    private void FocusFirstMenuItem()
    {
        if (_items.Count == 0)
        {
            return;
        }

        Dispatcher.BeginInvokeCritical(() =>
        {
            if (!IsMenuOpen || !IsEnabled)
                return;
            foreach (var item in _items)
            {
                if (!item.IsEnabled || item.Visibility != Visibility.Visible)
                {
                    continue;
                }

                if (item.Focus())
                {
                    return;
                }
            }
        });
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var oldItem in _ownedItems)
            if (!_items.Contains(oldItem))
                RemoveLogicalChild(oldItem);
        _ownedItems.Clear();
        foreach (var item in _items)
        {
            AddLogicalChild(item);
            _ownedItems.Add(item);
        }

        if (_flyout == null)
            return;
        _flyout.Items.Clear();
        foreach (var item in _items)
        {
            _flyout.Items.Add(item);
        }
    }
}
