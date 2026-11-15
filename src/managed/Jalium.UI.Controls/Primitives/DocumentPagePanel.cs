using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;
/// <summary>Arranges retained document pages in a scrollable, optionally facing grid.</summary>
public class DocumentPagePanel : Panel, IScrollInfo
{
    public static readonly DependencyProperty PagesSourceProperty = DependencyProperty.Register(nameof(PagesSource), typeof(IReadOnlyList<DocumentPageView>), typeof(DocumentPagePanel), new PropertyMetadata(null, OnPagesSourceChanged));
    public static readonly DependencyProperty PageSizeProperty = DependencyProperty.Register(nameof(PageSize), typeof(Size), typeof(DocumentPagePanel), new PropertyMetadata(new Size(816, 1056), OnLayoutChanged), value => value is Size size && double.IsFinite(size.Width) && double.IsFinite(size.Height) && size.Width >= 0 && size.Height >= 0);
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(nameof(Columns), typeof(int), typeof(DocumentPagePanel), new PropertyMetadata(1, OnLayoutChanged), value => value is int count && count > 0);
    public static readonly DependencyProperty FirstPageOnRightProperty = DependencyProperty.Register(nameof(FirstPageOnRight), typeof(bool), typeof(DocumentPagePanel), new PropertyMetadata(false, OnLayoutChanged));
    public static readonly DependencyProperty HorizontalPageSpacingProperty = DependencyProperty.Register(nameof(HorizontalPageSpacing), typeof(double), typeof(DocumentPagePanel), new PropertyMetadata(10.0, OnLayoutChanged), IsValidLength);
    public static readonly DependencyProperty VerticalPageSpacingProperty = DependencyProperty.Register(nameof(VerticalPageSpacing), typeof(double), typeof(DocumentPagePanel), new PropertyMetadata(10.0, OnLayoutChanged), IsValidLength);
    public static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.Register(nameof(HorizontalOffset), typeof(double), typeof(DocumentPagePanel), new PropertyMetadata(0.0, OnOffsetChanged), IsValidLength);
    public static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.Register(nameof(VerticalOffset), typeof(double), typeof(DocumentPagePanel), new PropertyMetadata(0.0, OnOffsetChanged), IsValidLength);
    public DocumentPagePanel() => PreserveExistingLogicalParents = true;
    /// <summary>The page models to present. Their existing logical ownership is preserved.</summary>
    public IReadOnlyList<DocumentPageView>? PagesSource { get => (IReadOnlyList<DocumentPageView>? )GetValue(PagesSourceProperty); set => SetValue(PagesSourceProperty, value); }
    public Size PageSize { get => (Size)GetValue(PageSizeProperty)!; set => SetValue(PageSizeProperty, value); }
    public int Columns { get => (int)GetValue(ColumnsProperty)!; set => SetValue(ColumnsProperty, value); }
    public bool FirstPageOnRight { get => (bool)GetValue(FirstPageOnRightProperty)!; set => SetValue(FirstPageOnRightProperty, value); }
    public double HorizontalPageSpacing { get => (double)GetValue(HorizontalPageSpacingProperty)!; set => SetValue(HorizontalPageSpacingProperty, value); }
    public double VerticalPageSpacing { get => (double)GetValue(VerticalPageSpacingProperty)!; set => SetValue(VerticalPageSpacingProperty, value); }
    public double HorizontalOffset { get => (double)GetValue(HorizontalOffsetProperty)!; set => SetValue(HorizontalOffsetProperty, value); }
    public double VerticalOffset { get => (double)GetValue(VerticalOffsetProperty)!; set => SetValue(VerticalOffsetProperty, value); }
    public double ExtentWidth { get; private set; }
    public double ExtentHeight { get; private set; }
    internal Size Viewport { get; private set; }
    public double ViewportWidth => Viewport.Width;
    public double ViewportHeight => Viewport.Height;
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public ScrollViewer? ScrollOwner { get; set; }

    public void LineUp() => SetVerticalOffset(VerticalOffset - 16);
    public void LineDown() => SetVerticalOffset(VerticalOffset + 16);
    public void LineLeft() => SetHorizontalOffset(HorizontalOffset - 16);
    public void LineRight() => SetHorizontalOffset(HorizontalOffset + 16);
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);
    public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - 48);
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + 48);
    public void MouseWheelLeft() => SetHorizontalOffset(HorizontalOffset - 48);
    public void MouseWheelRight() => SetHorizontalOffset(HorizontalOffset + 48);
    public void SetHorizontalOffset(double offset)
    {
        if (double.IsNaN(offset))
            throw new ArgumentOutOfRangeException(nameof(offset));
        SetCurrentValue(HorizontalOffsetProperty, Math.Clamp(offset, 0, Math.Max(0, ExtentWidth - ViewportWidth)));
    }

    public void SetVerticalOffset(double offset)
    {
        if (double.IsNaN(offset))
            throw new ArgumentOutOfRangeException(nameof(offset));
        SetCurrentValue(VerticalOffsetProperty, Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight)));
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        ArgumentNullException.ThrowIfNull(visual);
        if (rectangle.IsEmpty)
            return Rect.Empty;
        var origin = visual.TransformToVisual(this).Transform(new Point(rectangle.X, rectangle.Y));
        var bounds = new Rect(origin.X, origin.Y, rectangle.Width, rectangle.Height);
        var previousOffset = new Point(HorizontalOffset, VerticalOffset);
        if (bounds.X < 0)
            SetHorizontalOffset(HorizontalOffset + bounds.X);
        else if (bounds.Right > ViewportWidth)
            SetHorizontalOffset(HorizontalOffset + bounds.Right - ViewportWidth);
        if (bounds.Y < 0)
            SetVerticalOffset(VerticalOffset + bounds.Y);
        else if (bounds.Bottom > ViewportHeight)
            SetVerticalOffset(VerticalOffset + bounds.Bottom - ViewportHeight);
        bounds = new Rect(bounds.X + previousOffset.X - HorizontalOffset, bounds.Y + previousOffset.Y - VerticalOffset, bounds.Width, bounds.Height);
        return Rect.Intersect(bounds, new Rect(Viewport));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var previousExtent = new Size(ExtentWidth, ExtentHeight);
        var previousViewport = Viewport;
        var pageSize = PageSize;
        foreach (UIElement page in Children)
            page.Measure(pageSize);
        var count = Children.Count;
        var columns = Math.Max(1, FirstPageOnRight && count > 0 ? Columns : Math.Min(Columns, Math.Max(1, count)));
        var rows = count == 0 ? 0 : (int)Math.Ceiling((double)count / columns);
        ExtentWidth = count == 0 ? 0 : columns * pageSize.Width + (columns - 1) * HorizontalPageSpacing;
        ExtentHeight = rows == 0 ? 0 : rows * pageSize.Height + (rows - 1) * VerticalPageSpacing;
        Viewport = new Size(Normalize(availableSize.Width, ExtentWidth), Normalize(availableSize.Height, ExtentHeight));
        if (previousExtent != new Size(ExtentWidth, ExtentHeight) || previousViewport != Viewport)
            ScrollOwner?.InvalidateScrollInfo();
        return Viewport;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Math.Max(1, FirstPageOnRight && Children.Count > 0 ? Columns : Math.Min(Columns, Math.Max(1, Children.Count)));
        var size = PageSize;
        for (int index = 0; index < Children.Count; index++)
        {
            var page = (DocumentPageView)Children[index];
            var column = FirstPageOnRight && page.PageNumber == 0 ? columns - 1 : index % columns;
            page.Arrange(new Rect(column * (size.Width + HorizontalPageSpacing) - HorizontalOffset, index / columns * (size.Height + VerticalPageSpacing) - VerticalOffset, size.Width, size.Height));
        }

        if (Viewport != finalSize)
        {
            Viewport = finalSize;
            ScrollOwner?.InvalidateScrollInfo();
        }

        return finalSize;
    }

    private static void OnPagesSourceChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var panel = (DocumentPagePanel)owner;
        panel.Children.Clear();
        if (args.NewValue is IReadOnlyList<DocumentPageView> pages)
            foreach (var page in pages)
                panel.Children.Add(page);
        panel.InvalidateMeasure();
    }

    private static void OnLayoutChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) => ((DocumentPagePanel)owner).InvalidateMeasure();
    private static void OnOffsetChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var panel = (DocumentPagePanel)owner;
        panel.InvalidateArrange();
        panel.ScrollOwner?.InvalidateScrollInfo();
    }

    private static bool IsValidLength(object? value) => value is double number && double.IsFinite(number) && number >= 0;
    private static double Normalize(double value, double fallback) => double.IsFinite(value) ? Math.Max(0, value) : Math.Max(0, fallback);
}
