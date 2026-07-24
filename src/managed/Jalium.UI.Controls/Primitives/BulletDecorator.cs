using System.ComponentModel;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a layout control that aligns a bullet and content.
/// </summary>
public class BulletDecorator : Decorator
{
    #region Dependency Properties

    /// <summary>Identifies the brush used to paint the decorator background.</summary>
    public static readonly DependencyProperty BackgroundProperty =
        Panel.BackgroundProperty.AddOwner(
            typeof(BulletDecorator),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the bullet element.
    /// </summary>
    [DefaultValue(null)]
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public UIElement? Bullet
    {
        get => _bullet;
        set
        {
            if (ReferenceEquals(_bullet, value))
            {
                return;
            }

            if (value != null && ReferenceEquals(value, Child))
            {
                throw new InvalidOperationException("The bullet and child must be different elements.");
            }

            UIElement? oldBullet = _bullet;
            _bullet = null;
            if (oldBullet != null)
            {
                RemoveVisualChild(oldBullet);
                RemoveLogicalChild(oldBullet);
            }

            _bullet = value;
            if (value != null)
            {
                UIElement? child = Child;
                try
                {
                    AddLogicalChild(value);
                    if (child != null)
                    {
                        RemoveVisualChild(child);
                    }

                    AddVisualChild(value);
                    if (child != null)
                    {
                        AddVisualChild(child);
                    }
                }
                catch
                {
                    _bullet = null;
                    RemoveVisualChild(value);
                    RemoveLogicalChild(value);
                    if (child != null && child.VisualParent == null)
                    {
                        AddVisualChild(child);
                    }

                    throw;
                }
            }

            InvalidateMeasure();
        }
    }

    /// <summary>Gets or sets the brush used to paint the decorator background.</summary>
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    #endregion

    private UIElement? _bullet;

    #region Visual Children

    /// <inheritdoc />
    protected internal override System.Collections.IEnumerator LogicalChildren
    {
        get
        {
            if (_bullet == null)
            {
                return base.LogicalChildren;
            }

            return Child == null
                ? new object[] { _bullet }.GetEnumerator()
                : new object[] { _bullet, Child }.GetEnumerator();
        }
    }

    /// <inheritdoc />
    protected override int VisualChildrenCount
    {
        get
        {
            var count = 0;
            if (_bullet != null) count++;
            if (Child != null) count++;
            return count;
        }
    }

    /// <inheritdoc />
    protected override Visual? GetVisualChild(int index)
    {
        if (index == 0)
        {
            return _bullet ?? Child;
        }
        if (index == 1 && _bullet != null && Child != null)
        {
            return Child;
        }
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (Background != null)
        {
            drawingContext.DrawRectangle(Background, null, new Rect(RenderSize));
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var bulletSize = default(Size);
        var childSize = default(Size);

        if (_bullet != null)
        {
            _bullet.Measure(availableSize);
            bulletSize = _bullet.DesiredSize;
        }

        if (Child != null)
        {
            var childAvailable = new Size(
                Math.Max(0, availableSize.Width - bulletSize.Width),
                availableSize.Height);
            Child.Measure(childAvailable);
            childSize = Child.DesiredSize;
        }

        return new Size(
            bulletSize.Width + childSize.Width,
            Math.Max(bulletSize.Height, childSize.Height));
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        UIElement? bullet = _bullet;
        UIElement? child = Child;
        double bulletOffsetY = 0;
        Size bulletSize = default;

        if (bullet != null)
        {
            bullet.Arrange(new Rect(bullet.DesiredSize));
            bulletSize = bullet.RenderSize;
        }

        if (child != null)
        {
            Size childSize = finalSize;
            if (bullet != null)
            {
                childSize.Width = Math.Max(child.DesiredSize.Width, finalSize.Width - bullet.DesiredSize.Width);
                childSize.Height = Math.Max(child.DesiredSize.Height, finalSize.Height);
            }

            child.Arrange(new Rect(bulletSize.Width, 0, childSize.Width, childSize.Height));
            bulletOffsetY = Math.Max(0, GetFirstLineHeight(child) * 0.5 - bulletSize.Height * 0.5);
        }

        if (bullet != null && bulletOffsetY > double.Epsilon)
        {
            bullet.Arrange(new Rect(0, bulletOffsetY, bullet.DesiredSize.Width, bullet.DesiredSize.Height));
        }

        return finalSize;
    }

    #endregion

    private static double GetFirstLineHeight(UIElement element)
    {
        TextBlock? text = FindText(element);
        if (text == null)
        {
            return element.RenderSize.Height;
        }

        double fontSize = text.FontSize > 0 ? text.FontSize : 14;
        double naturalLineHeight = TextMeasurement.GetLineHeight(
            text.FontFamily.Source,
            fontSize,
            text.FontWeight.ToOpenTypeWeight(),
            text.FontStyle.ToOpenTypeStyle());
        double lineHeight = double.IsNaN(text.LineHeight)
            ? naturalLineHeight
            : text.LineStackingStrategy == LineStackingStrategy.MaxHeight
                ? Math.Max(naturalLineHeight, text.LineHeight)
                : Math.Max(0, text.LineHeight);
        Point offset = text.TransformToAncestor(element);
        return lineHeight + offset.Y * 2;
    }

    private static TextBlock? FindText(Visual root)
    {
        if (root is TextBlock text)
        {
            return text;
        }

        if (root is not ContentPresenter && root is not AccessText)
        {
            return null;
        }

        return VisualTreeHelper.GetChildrenCount(root) == 1
            ? VisualTreeHelper.GetChild(root, 0) switch
            {
                TextBlock childText => childText,
                AccessText accessText when VisualTreeHelper.GetChildrenCount(accessText) == 1 =>
                    VisualTreeHelper.GetChild(accessText, 0) as TextBlock,
                _ => null,
            }
            : null;
    }
}
