using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a flyout that displays a menu of commands.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Items")]
public sealed class MenuFlyout : FlyoutBase
{
    private readonly ObservableCollection<Control> _items = new();

    /// <summary>
    /// Gets the collection of items in the MenuFlyout.
    /// </summary>
    public IList<Control> Items => _items;

    internal ObservableCollection<Control> ItemCollection => _items;

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
    private readonly MenuPopupScrollHost _scrollHost;

    public MenuFlyoutPresenter(MenuFlyout flyout)
    {
        _flyout = flyout;
        BorderThickness = s_defaultBorderThickness;
        Padding = s_defaultPadding;
        CornerRadius = s_defaultCornerRadius;
        _scrollHost = new MenuPopupScrollHost();
        _flyout.ItemCollection.CollectionChanged += OnFlyoutItemsChanged;
        RefreshItems();

        AddVisualChild(_scrollHost);
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
        _scrollHost.Measure(innerSize);
        // Negative BorderThickness/Padding are legal; the Size constructor is not —
        // clamp at the summation sink.
        return new Size(
            Math.Max(0, _scrollHost.DesiredSize.Width + horizontalInset),
            Math.Max(0, _scrollHost.DesiredSize.Height + verticalInset));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var borderThickness = GetEffectiveBorderThickness();
        var padding = GetEffectivePadding();
        var leftInset = borderThickness.Left + padding.Left;
        var topInset = borderThickness.Top + padding.Top;
        var horizontalInset = borderThickness.Left + borderThickness.Right + padding.Left + padding.Right;
        var verticalInset = borderThickness.Top + borderThickness.Bottom + padding.Top + padding.Bottom;

        _scrollHost.Arrange(new Rect(
            leftInset,
            topInset,
            Math.Max(0, finalSize.Width - horizontalInset),
            Math.Max(0, finalSize.Height - verticalInset)));
        return finalSize;
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual? GetVisualChild(int index)
    {
        if (index == 0) return _scrollHost;
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;
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

    private void OnFlyoutItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshItems();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void RefreshItems()
    {
        _scrollHost.ItemsPanel.Children.Clear();
        foreach (var item in _flyout.Items)
        {
            _scrollHost.ItemsPanel.Children.Add(item);
        }
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
