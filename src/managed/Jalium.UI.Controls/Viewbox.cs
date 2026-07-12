using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the direction that content is scaled.
/// </summary>
public enum StretchDirection
{
    /// <summary>
    /// The content scales upward only when it is smaller than the parent.
    /// If the content is larger, no scaling downward is performed.
    /// </summary>
    UpOnly,

    /// <summary>
    /// The content scales downward only when it is larger than the parent.
    /// If the content is smaller, no scaling upward is performed.
    /// </summary>
    DownOnly,

    /// <summary>
    /// The content scales to fit the parent according to the Stretch mode.
    /// </summary>
    Both
}

/// <summary>
/// Defines a content decorator that can stretch and scale a single child to fill the available space.
/// </summary>
[ContentProperty("Child")]
public class Viewbox : FrameworkElement
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.ViewboxAutomationPeer(this);
    }

    private FrameworkElement? _child;
    private ScaleTransform _scaleTransform = null!;
    // 内部 wrapper：Viewbox 的唯一 visual child 始终是它。
    // ScaleTransform 设在 wrapper 上而不是用户的 Child 上 —— 否则会覆盖 Child 自己声明的
    // RenderTransform（如 jalxaml 中给 Grid 设的 RotateTransform）。
    // wrapper.RenderTransformOrigin=(0,0) 让 ScaleTransform 绕左上角缩放，配合 Viewbox.ArrangeOverride
    // 把 wrapper Arrange 在 (0,0)，让缩放后的内容从 Viewbox 左上角铺开。
    private Border _wrapper = null!;

    public Viewbox()
    {
        ClipToBounds = true;
        _scaleTransform = new ScaleTransform();
        _wrapper = new Border
        {
            RenderTransformOrigin = new Point(0, 0),
            RenderTransform = _scaleTransform,
        };
        AddVisualChild(_wrapper);
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the Stretch dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(nameof(Stretch), typeof(Stretch), typeof(Viewbox),
            new PropertyMetadata(Stretch.Uniform, OnStretchPropertyChanged));

    /// <summary>
    /// Identifies the StretchDirection dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StretchDirectionProperty =
        DependencyProperty.Register(nameof(StretchDirection), typeof(StretchDirection), typeof(Viewbox),
            new PropertyMetadata(StretchDirection.Both, OnStretchPropertyChanged));

    /// <summary>
    /// Identifies the Child dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ChildProperty =
        DependencyProperty.Register(nameof(Child), typeof(UIElement), typeof(Viewbox),
            new PropertyMetadata(null, OnChildChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value that describes how the content should be stretched to fill the allocated space.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty)!;
        set => SetValue(StretchProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that determines how scaling is applied to the child.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public StretchDirection StretchDirection
    {
        get => (StretchDirection)GetValue(StretchDirectionProperty)!;
        set => SetValue(StretchDirectionProperty, value);
    }

    /// <summary>
    /// Gets or sets the single child of the Viewbox.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public UIElement? Child
    {
        get => (UIElement?)GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    #endregion

    #region Property Changed Handlers

    private static void OnStretchPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Viewbox viewbox)
        {
            viewbox.InvalidateMeasure();
        }
    }

    private static void OnChildChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Viewbox viewbox)
        {
            // wrapper 始终是 Viewbox 的 visual child；用户的 Child 放进 wrapper.Child。
            // 这样 Child.RenderTransform 是用户自己的，Viewbox 的 ScaleTransform 不会覆盖它。
            if (e.OldValue is FrameworkElement)
            {
                viewbox._wrapper.Child = null;
            }

            viewbox._child = e.NewValue as FrameworkElement;
            viewbox._wrapper.Child = viewbox._child;

            viewbox.InvalidateMeasure();
        }
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override int VisualChildrenCount => 1;

    /// <inheritdoc />
    protected override Visual? GetVisualChild(int index)
    {
        if (index != 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _wrapper;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_child == null)
            return default(Size);

        // 用 infinite 测 wrapper（→ 内层 Child）获取自然尺寸，再算 scale
        _wrapper.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var childSize = _wrapper.DesiredSize;

        if (childSize.Width == 0 || childSize.Height == 0)
            return default(Size);

        var scale = ComputeScaleFactor(availableSize, childSize, Stretch, StretchDirection);

        return new Size(childSize.Width * scale.Width, childSize.Height * scale.Height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_child == null)
            return finalSize;

        var childSize = _wrapper.DesiredSize;
        if (childSize.Width == 0 || childSize.Height == 0)
            return finalSize;

        var scale = ComputeScaleFactor(finalSize, childSize, Stretch, StretchDirection);

        _scaleTransform.ScaleX = scale.Width;
        _scaleTransform.ScaleY = scale.Height;

        // wrapper 以自然尺寸 Arrange 在 (0,0)；ScaleTransform 绕 (0,0) 缩放后铺满 finalSize
        _wrapper.Arrange(new Rect(0, 0, childSize.Width, childSize.Height));

        return finalSize;
    }

    private static Size ComputeScaleFactor(Size availableSize, Size contentSize, Stretch stretch, StretchDirection stretchDirection)
    {
        var scaleX = 1.0;
        var scaleY = 1.0;

        var isWidthInfinite = double.IsInfinity(availableSize.Width);
        var isHeightInfinite = double.IsInfinity(availableSize.Height);

        if (stretch != Stretch.None && (!isWidthInfinite || !isHeightInfinite))
        {
            scaleX = contentSize.Width > 0 ? availableSize.Width / contentSize.Width : 0;
            scaleY = contentSize.Height > 0 ? availableSize.Height / contentSize.Height : 0;

            if (isWidthInfinite)
            {
                scaleX = scaleY;
            }
            else if (isHeightInfinite)
            {
                scaleY = scaleX;
            }
            else
            {
                switch (stretch)
                {
                    case Stretch.Uniform:
                        // Use the smaller scale factor
                        var minScale = Math.Min(scaleX, scaleY);
                        scaleX = scaleY = minScale;
                        break;

                    case Stretch.UniformToFill:
                        // Use the larger scale factor
                        var maxScale = Math.Max(scaleX, scaleY);
                        scaleX = scaleY = maxScale;
                        break;

                    case Stretch.Fill:
                        // Use both scales (no aspect ratio preservation)
                        break;
                }
            }

            // Apply stretch direction constraints
            switch (stretchDirection)
            {
                case StretchDirection.UpOnly:
                    scaleX = Math.Max(1.0, scaleX);
                    scaleY = Math.Max(1.0, scaleY);
                    break;

                case StretchDirection.DownOnly:
                    scaleX = Math.Min(1.0, scaleX);
                    scaleY = Math.Min(1.0, scaleY);
                    break;
            }
        }

        // Guard against zero/negative scale (e.g. when child has zero DesiredSize)
        if (scaleX <= 0) scaleX = 1.0;
        if (scaleY <= 0) scaleY = 1.0;
        if (double.IsNaN(scaleX) || double.IsInfinity(scaleX)) scaleX = 1.0;
        if (double.IsNaN(scaleY) || double.IsInfinity(scaleY)) scaleY = 1.0;

        return new Size(scaleX, scaleY);
    }

    #endregion
}
