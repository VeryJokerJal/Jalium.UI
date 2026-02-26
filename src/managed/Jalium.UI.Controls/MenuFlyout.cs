using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a flyout that displays a menu of commands.
/// </summary>
public sealed class MenuFlyout : FlyoutBase
{
    private readonly List<Control> _items = new();

    /// <summary>
    /// Gets the collection of items in the MenuFlyout.
    /// </summary>
    public IList<Control> Items => _items;

    /// <summary>
    /// Gets or sets the style applied to the MenuFlyoutPresenter.
    /// </summary>
    public Style? MenuFlyoutPresenterStyle { get; set; }

    /// <inheritdoc />
    protected override Control CreatePresenter()
    {
        return new MenuFlyoutPresenter(this);
    }
}

/// <summary>
/// Displays the content of a MenuFlyout.
/// </summary>
internal sealed class MenuFlyoutPresenter : Control
{
    private static readonly SolidColorBrush s_fallbackBackgroundBrush = new(Color.FromRgb(45, 45, 48));
    private static readonly SolidColorBrush s_fallbackBorderBrush = new(Color.FromRgb(67, 67, 70));

    private readonly MenuFlyout _flyout;
    private readonly StackPanel _panel;

    public MenuFlyoutPresenter(MenuFlyout flyout)
    {
        _flyout = flyout;
        _panel = new StackPanel { Orientation = Orientation.Vertical };

        foreach (var item in _flyout.Items)
        {
            _panel.Children.Add(item);
        }

        AddVisualChild(_panel);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var innerSize = new Size(
            Math.Max(0, availableSize.Width - 2),
            Math.Max(0, availableSize.Height - 2));
        _panel.Measure(innerSize);
        return new Size(_panel.DesiredSize.Width + 2, _panel.DesiredSize.Height + 2);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _panel.Arrange(new Rect(
            1,
            1,
            Math.Max(0, finalSize.Width - 2),
            Math.Max(0, finalSize.Height - 2)));
        return finalSize;
    }

    public override int VisualChildrenCount => 1;

    public override Visual? GetVisualChild(int index)
    {
        if (index == 0) return _panel;
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;
        base.OnRender(drawingContext);

        var background = Background ?? ResolveBrush("OnePopupBackground", "MenuFlyoutPresenterBackground", s_fallbackBackgroundBrush);
        var border = BorderBrush ?? ResolveBrush("OnePopupBorder", "MenuFlyoutPresenterBorderBrush", s_fallbackBorderBrush);
        var borderThickness = BorderThickness.Left > 0 ? BorderThickness.Left : 1.0;

        dc.DrawRectangle(background, new Pen(border, borderThickness), new Rect(RenderSize));
    }

    private Brush ResolveBrush(string primaryKey, string secondaryKey, Brush fallback)
    {
        if (TryFindResource(primaryKey) is Brush primary)
            return primary;
        if (TryFindResource(secondaryKey) is Brush secondary)
            return secondary;
        return fallback;
    }
}
