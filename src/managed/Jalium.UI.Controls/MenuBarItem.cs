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
public class MenuBarItem : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Automation.Peers.MenuBarItemAutomationPeer(this);

    private static readonly SolidColorBrush s_fallbackHoverBrush = new(Color.FromRgb(61, 61, 61));
    private static readonly SolidColorBrush s_fallbackTextBrush = new(Color.FromRgb(255, 255, 255));

    private readonly ObservableCollection<Control> _items = new();
    private MenuFlyout? _flyout;
    private Border? _cssBorderPainter;

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
        var insets = CssBoxMetrics.ContentInsets(this,
            CssLayout?.ContainingWidthCache ?? availableSize.Width);
        var fontSize = FontSize;
        double textWidth = 0;
        double textHeight = 0;
        if (!string.IsNullOrEmpty(Title))
        {
            var formattedText = new Jalium.UI.Media.FormattedText(
                Title, FontFamily?.GetRenderingSource(this) ?? FrameworkElement.DefaultFontFamilyName, fontSize);
            TextMeasurement.MeasureText(formattedText);
            textWidth = formattedText.Width;
            textHeight = formattedText.Height;
        }

        var width = textWidth + Math.Max(24, insets.Left + insets.Right);
        var height = Math.Max(32, textHeight + insets.Top + insets.Bottom);
        return new Size(
            ControlRenderGeometry.GetAvailableLength(width, availableSize.Width),
            ControlRenderGeometry.GetAvailableLength(height, availableSize.Height));
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;
        base.OnRender(drawingContext);
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0)
            return;

        var rect = new Rect(RenderSize);
        var backgroundLayer = GetEffectiveValueLayer(BackgroundProperty);
        var cssBackground = backgroundLayer is
            DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState;
        var hoverFallback = (IsMouseOver || IsMenuOpen || IsKeyboardFocused) &&
            (backgroundLayer is null or DependencyValueStore.Layer.StyleSetter);
        var background = hoverFallback
            ? ResolveBrush("OneSurfaceHover", "MenuBarItemBackgroundHover", s_fallbackHoverBrush)
            : Background;
        if (background is not null)
        {
            var cssRadius = CssBorderRadiusProperties.Get(this);
            if (hoverFallback && cssRadius is null)
                dc.DrawRoundedRectangle(background, null, rect, 4, 4);
            else if (!cssBackground && cssRadius is null)
                dc.DrawRoundedRectangle(background, null, rect, CornerRadius);
            else
            {
                var radii = cssRadius?.Resolve(RenderSize) ??
                    CssBackgroundPainter.CircularRadii(CornerRadius).Normalize(RenderSize);
                var shape = new CssRoundedRectangleGeometry(rect, radii);
                var (border, padding) = CssBoxMetrics.BackgroundInsets(this,
                    CssLayout?.ContainingWidthCache ?? RenderSize.Width);
                if (!CssBackgroundPainter.TryDraw(this, BackgroundProperty, background, dc,
                        rect, radii, border, padding,
                        brush => dc.DrawGeometry(brush, null, shape)))
                    dc.DrawGeometry(background, null, shape);
            }
        }

        // Focus indicator is painted by FocusVisualManager into the adorner layer.

        // Title text
        if (!string.IsNullOrEmpty(Title) && FontSize > 0)
        {
            var fontSize = FontSize;
            var textBrush = ResolveForegroundBrush();
            var textFormatted = new Jalium.UI.Media.FormattedText(
                Title, FontFamily?.GetRenderingSource(this) ?? FrameworkElement.DefaultFontFamilyName, fontSize) { Foreground = textBrush };
            TextMeasurement.MeasureText(textFormatted);
            var content = ControlRenderGeometry.GetContentRect(rect,
                CssBoxMetrics.ContentInsets(this, CssLayout?.ContainingWidthCache ?? RenderSize.Width));
            dc.DrawText(textFormatted,
                new Point(content.X + (content.Width - textFormatted.Width) / 2,
                          content.Y + (content.Height - textFormatted.Height) / 2));
        }
    }

    /// <inheritdoc />
    protected override void OnPostRender(DrawingContext drawingContext)
    {
        base.OnPostRender(drawingContext);
        CssBorderAdornment.Draw(this, drawingContext, ref _cssBorderPainter, drawNative: true);
    }

    private Brush ResolveForegroundBrush()
    {
        if (Foreground != null &&
            (HasLocalOrAnimatedValue(ForegroundProperty) ||
             GetEffectiveValueLayer(ForegroundProperty) is
                 (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState) ||
             DependencyPropertyHelper.GetValueSource(this, ForegroundProperty).BaseValueSource ==
                 BaseValueSource.Inherited))
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
