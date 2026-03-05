using Jalium.UI.Controls.Primitives;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a top-level menu in a MenuBar control.
/// </summary>
public sealed class MenuBarItem : Control
{
    private static readonly SolidColorBrush s_fallbackHoverBrush = new(Color.FromRgb(61, 61, 61));
    private static readonly SolidColorBrush s_fallbackTextBrush = new(Color.FromRgb(255, 255, 255));

    private readonly List<Control> _items = new();
    private MenuFlyout? _flyout;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Title dependency property.
    /// </summary>
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(MenuBarItem),
            new PropertyMetadata(string.Empty));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the title of the menu bar item.
    /// </summary>
    public string Title
    {
        get => (string?)GetValue(TitleProperty) ?? string.Empty;
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets the collection of menu items in this menu.
    /// </summary>
    public IList<Control> Items => _items;

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
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(MouseEnterEvent, new RoutedEventHandler(OnMouseEnterHandler));
        AddHandler(MouseLeaveEvent, new RoutedEventHandler(OnMouseLeaveHandler));
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var fontSize = FontSize > 0 ? FontSize : 14;
        double textWidth = 0;
        if (!string.IsNullOrEmpty(Title))
        {
            var formattedText = new Jalium.UI.Media.FormattedText(
                Title, FontFamily ?? "Segoe UI", fontSize);
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
        if (IsMouseOver || IsMenuOpen)
        {
            var hoverBrush = ResolveBrush("OneSurfaceHover", "MenuBarItemBackgroundHover", s_fallbackHoverBrush);
            dc.DrawRoundedRectangle(hoverBrush, null, new Rect(RenderSize), 4, 4);
        }

        // Title text
        if (!string.IsNullOrEmpty(Title))
        {
            var fontSize = FontSize > 0 ? FontSize : 14;
            var textBrush = Foreground ?? ResolveBrush("OneTextPrimary", "TextPrimary", s_fallbackTextBrush);
            var textFormatted = new Jalium.UI.Media.FormattedText(
                Title, FontFamily ?? "Segoe UI", fontSize) { Foreground = textBrush };
            TextMeasurement.MeasureText(textFormatted);
            dc.DrawText(textFormatted,
                new Point((RenderSize.Width - textFormatted.Width) / 2,
                          (RenderSize.Height - textFormatted.Height) / 2));
        }
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

    /// <summary>
    /// Closes the drop-down menu.
    /// </summary>
    public void CloseMenu()
    {
        _flyout?.Hide();
        InvalidateVisual();
    }

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (IsMenuOpen)
            CloseMenu();
        else
            OpenMenu();
        e.Handled = true;
    }

    private void OnMouseEnterHandler(object sender, RoutedEventArgs e)
    {
        // Keep hover visual only. Top-level menu opens by click to avoid accidental popup.
        InvalidateVisual();
    }

    private void OnMouseLeaveHandler(object sender, RoutedEventArgs e)
    {
        InvalidateVisual();
    }
}
