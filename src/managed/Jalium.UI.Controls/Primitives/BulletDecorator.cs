using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a layout control that aligns a bullet and content.
/// </summary>
public class BulletDecorator : FrameworkElement
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Bullet dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty BulletProperty =
        DependencyProperty.Register(nameof(Bullet), typeof(UIElement), typeof(BulletDecorator),
            new PropertyMetadata(null, OnBulletChanged));

    /// <summary>
    /// Identifies the Child dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ChildProperty =
        DependencyProperty.Register(nameof(Child), typeof(UIElement), typeof(BulletDecorator),
            new PropertyMetadata(null, OnChildChanged));

    /// <summary>
    /// Identifies the BulletAlignment dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty BulletAlignmentProperty =
        DependencyProperty.Register(nameof(BulletAlignment), typeof(VerticalAlignment), typeof(BulletDecorator),
            new PropertyMetadata(VerticalAlignment.Top, OnLayoutPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the bullet element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public UIElement? Bullet
    {
        get => (UIElement?)GetValue(BulletProperty);
        set => SetValue(BulletProperty, value);
    }

    /// <summary>
    /// Gets or sets the child element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public UIElement? Child
    {
        get => (UIElement?)GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical alignment of the bullet relative to the child.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public VerticalAlignment BulletAlignment
    {
        get => (VerticalAlignment)GetValue(BulletAlignmentProperty)!;
        set => SetValue(BulletAlignmentProperty, value);
    }

    #endregion

    #region Private Fields

    private const double BulletMargin = 4;

    #endregion

    #region Visual Children

    /// <inheritdoc />
    public override int VisualChildrenCount
    {
        get
        {
            var count = 0;
            if (Bullet != null) count++;
            if (Child != null) count++;
            return count;
        }
    }

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        if (index == 0)
        {
            return Bullet ?? Child;
        }
        if (index == 1 && Bullet != null && Child != null)
        {
            return Child;
        }
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var bulletSize = Size.Empty;
        var childSize = Size.Empty;

        if (Bullet != null)
        {
            Bullet.Measure(availableSize);
            bulletSize = Bullet.DesiredSize;
        }

        if (Child != null)
        {
            var childAvailable = new Size(
                Math.Max(0, availableSize.Width - bulletSize.Width - BulletMargin),
                availableSize.Height);
            Child.Measure(childAvailable);
            childSize = Child.DesiredSize;
        }

        return new Size(
            bulletSize.Width + BulletMargin + childSize.Width,
            Math.Max(bulletSize.Height, childSize.Height));
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var bulletSize = Bullet?.DesiredSize ?? Size.Empty;
        var childSize = Child?.DesiredSize ?? Size.Empty;

        if (Bullet != null)
        {
            double bulletY = BulletAlignment switch
            {
                VerticalAlignment.Top => 0,
                VerticalAlignment.Center => (finalSize.Height - bulletSize.Height) / 2,
                VerticalAlignment.Bottom => finalSize.Height - bulletSize.Height,
                _ => 0
            };

            Bullet.Arrange(new Rect(0, bulletY, bulletSize.Width, bulletSize.Height));
        }

        if (Child != null)
        {
            var childX = bulletSize.Width + BulletMargin;
            var childWidth = Math.Max(0, finalSize.Width - childX);
            Child.Arrange(new Rect(childX, 0, childWidth, finalSize.Height));
        }

        return finalSize;
    }

    #endregion

    #region Property Changed

    private static void OnBulletChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BulletDecorator decorator)
        {
            if (e.OldValue is UIElement oldBullet)
            {
                decorator.RemoveVisualChild(oldBullet);
            }

            if (e.NewValue is UIElement newBullet)
            {
                decorator.AddVisualChild(newBullet);
            }

            decorator.InvalidateMeasure();
        }
    }

    private static void OnChildChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BulletDecorator decorator)
        {
            if (e.OldValue is UIElement oldChild)
            {
                decorator.RemoveVisualChild(oldChild);
            }

            if (e.NewValue is UIElement newChild)
            {
                decorator.AddVisualChild(newChild);
            }

            decorator.InvalidateMeasure();
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BulletDecorator decorator)
        {
            decorator.InvalidateArrange();
        }
    }

    #endregion
}
