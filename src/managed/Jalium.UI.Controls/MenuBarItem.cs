using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a top-level menu in a MenuBar control.
/// </summary>
public class MenuBarItem : Control
{
    private static readonly SolidColorBrush s_fallbackHoverBrush = new(Color.FromRgb(61, 61, 61));
    private static readonly SolidColorBrush s_fallbackTextBrush = new(Color.FromRgb(255, 255, 255));

    private readonly ObservableCollection<Control> _items = new();
    private MenuFlyout? _flyout;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Title dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(MenuBarItem),
            new PropertyMetadata(string.Empty));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the title of the menu bar item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Title
    {
        get => (string?)GetValue(TitleProperty) ?? string.Empty;
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets the collection of menu items in this menu.
    /// </summary>
    public IList<Control> Items => _items;

    internal ObservableCollection<Control> ItemCollection => _items;

    /// <summary>
    /// Gets a value indicating whether the drop-down menu is open.
    /// </summary>
    public bool IsMenuOpen => _flyout?.IsOpen == true;

    /// <summary>
    /// Gets or sets the parent MenuBar.
    /// </summary>
    internal MenuBar? ParentMenuBar { get; set; }

    #endregion

    /// <summary>
    /// Initializes a new instance of the MenuBarItem class.
    /// </summary>
    public MenuBarItem()
    {
        Focusable = true;
        _items.CollectionChanged += OnItemsCollectionChanged;
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseEnterEvent, new MouseEventHandler(OnMouseEnterHandler));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var fontSize = FontSize > 0 ? FontSize : 14;
        double textWidth = 0;
        if (!string.IsNullOrEmpty(Title))
        {
            var formattedText = new Jalium.UI.Media.FormattedText(
                Title, FontFamily ?? FrameworkElement.DefaultFontFamilyName, fontSize);
            TextMeasurement.MeasureText(formattedText);
            textWidth = formattedText.Width;
        }

        var width = textWidth + 24; // 12px padding each side
        return new Size(Math.Min(width, availableSize.Width), Math.Min(32, availableSize.Height));
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;
        base.OnRender(drawingContext);

        // Background
        if (IsMouseOver || IsMenuOpen || IsKeyboardFocused)
        {
            var hoverBrush = ResolveBrush("OneSurfaceHover", "MenuBarItemBackgroundHover", s_fallbackHoverBrush);
            dc.DrawRoundedRectangle(hoverBrush, null, new Rect(RenderSize), 4, 4);
        }

        if (IsKeyboardFocused)
        {
            ControlFocusVisual.Draw(dc, this, new Rect(RenderSize), new CornerRadius(4));
        }

        // Title text
        if (!string.IsNullOrEmpty(Title))
        {
            var fontSize = FontSize > 0 ? FontSize : 14;
            var textBrush = ResolveForegroundBrush();
            var textFormatted = new Jalium.UI.Media.FormattedText(
                Title, FontFamily ?? FrameworkElement.DefaultFontFamilyName, fontSize) { Foreground = textBrush };
            TextMeasurement.MeasureText(textFormatted);
            dc.DrawText(textFormatted,
                new Point((RenderSize.Width - textFormatted.Width) / 2,
                          (RenderSize.Height - textFormatted.Height) / 2));
        }
    }

    private Brush ResolveForegroundBrush()
    {
        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
        {
            return Foreground;
        }

        return ResolveBrush("OneTextPrimary", "TextPrimary", s_fallbackTextBrush);
    }

    private Brush ResolveBrush(string primaryKey, string secondaryKey, Brush fallback)
    {
        if (TryFindResource(primaryKey) is Brush primary)
            return primary;
        if (TryFindResource(secondaryKey) is Brush secondary)
            return secondary;
        return fallback;
    }

    /// <summary>
    /// Opens the drop-down menu.
    /// </summary>
    public void OpenMenu()
    {
        if (_flyout == null)
        {
            _flyout = new MenuFlyout();
            foreach (var item in _items)
                _flyout.Items.Add(item);
        }

        ParentMenuBar?.CloseAllMenus(this);
        _flyout.ShowAt(this);
        InvalidateVisual();
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
        InvalidateVisual();
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (IsMenuOpen)
            CloseMenu();
        else
            OpenMenu();
        e.Handled = true;
    }

    private void OnMouseEnterHandler(object sender, MouseEventArgs e)
    {
        // Keep hover visual only. Top-level menu opens by click to avoid accidental popup.
        InvalidateVisual();
    }

    private void OnMouseLeaveHandler(object sender, MouseEventArgs e)
    {
        InvalidateVisual();
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
        if (_flyout == null)
            return;

        _flyout.Items.Clear();
        foreach (var item in _items)
        {
            _flyout.Items.Add(item);
        }
    }
}
