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
    private static readonly Thickness s_defaultBorderThickness = new(1);
    private static readonly Thickness s_defaultPadding = new(4);
    private static readonly CornerRadius s_defaultCornerRadius = new(8);
    private static readonly SolidColorBrush s_fallbackBackgroundBrush = new(Color.FromRgb(45, 45, 48));
    private static readonly SolidColorBrush s_fallbackBorderBrush = new(Color.FromRgb(67, 67, 70));

    private readonly MenuFlyout _flyout;
    private readonly StackPanel _panel;

    public MenuFlyoutPresenter(MenuFlyout flyout)
    {
        _flyout = flyout;
        BorderThickness = s_defaultBorderThickness;
        Padding = s_defaultPadding;
        CornerRadius = s_defaultCornerRadius;
        _panel = new StackPanel { Orientation = Orientation.Vertical };

        foreach (var item in _flyout.Items)
        {
            _panel.Children.Add(item);
        }

        AddVisualChild(_panel);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var borderThickness = GetEffectiveBorderThickness();
        var padding = GetEffectivePadding();
        var horizontalInset = borderThickness.Left + borderThickness.Right + padding.Left + padding.Right;
        var verticalInset = borderThickness.Top + borderThickness.Bottom + padding.Top + padding.Bottom;

        var innerSize = new Size(
            Math.Max(0, availableSize.Width - horizontalInset),
            Math.Max(0, availableSize.Height - verticalInset));
        _panel.Measure(innerSize);
        return new Size(_panel.DesiredSize.Width + horizontalInset, _panel.DesiredSize.Height + verticalInset);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var borderThickness = GetEffectiveBorderThickness();
        var padding = GetEffectivePadding();
        var leftInset = borderThickness.Left + padding.Left;
        var topInset = borderThickness.Top + padding.Top;
        var horizontalInset = borderThickness.Left + borderThickness.Right + padding.Left + padding.Right;
        var verticalInset = borderThickness.Top + borderThickness.Bottom + padding.Top + padding.Bottom;

        _panel.Arrange(new Rect(
            leftInset,
            topInset,
            Math.Max(0, finalSize.Width - horizontalInset),
            Math.Max(0, finalSize.Height - verticalInset)));
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
        var borderThickness = GetEffectiveBorderThickness();
        var penThickness = Math.Max(Math.Max(borderThickness.Left, borderThickness.Top), Math.Max(borderThickness.Right, borderThickness.Bottom));
        var cornerRadius = GetEffectiveCornerRadius();

        var pen = penThickness > 0 ? new Pen(border, penThickness) : null;
        dc.DrawRoundedRectangle(background, pen, new Rect(RenderSize), cornerRadius, cornerRadius);
    }

    private Brush ResolveBrush(string primaryKey, string secondaryKey, Brush fallback)
    {
        if (TryFindResource(primaryKey) is Brush primary)
            return primary;
        if (TryFindResource(secondaryKey) is Brush secondary)
            return secondary;
        return fallback;
    }

    private Thickness GetEffectiveBorderThickness()
    {
        return IsZero(BorderThickness) ? s_defaultBorderThickness : BorderThickness;
    }

    private Thickness GetEffectivePadding()
    {
        return IsZero(Padding) ? s_defaultPadding : Padding;
    }

    private double GetEffectiveCornerRadius()
    {
        var cornerRadius = CornerRadius;
        if (IsZero(cornerRadius))
            return s_defaultCornerRadius.TopLeft;

        return Math.Max(
            Math.Max(cornerRadius.TopLeft, cornerRadius.TopRight),
            Math.Max(cornerRadius.BottomRight, cornerRadius.BottomLeft));
    }

    private static bool IsZero(Thickness thickness)
    {
        return thickness.Left <= 0
            && thickness.Top <= 0
            && thickness.Right <= 0
            && thickness.Bottom <= 0;
    }

    private static bool IsZero(CornerRadius cornerRadius)
    {
        return cornerRadius.TopLeft <= 0
            && cornerRadius.TopRight <= 0
            && cornerRadius.BottomRight <= 0
            && cornerRadius.BottomLeft <= 0;
    }
}
