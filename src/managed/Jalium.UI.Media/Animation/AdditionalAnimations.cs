namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a Rect property between two target values.
/// </summary>
public sealed class RectAnimation : AnimationTimeline<Rect>
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(Rect?), typeof(RectAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(Rect?), typeof(RectAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(RectAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public Rect? From
    {
        get => (Rect?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public Rect? To
    {
        get => (Rect?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public RectAnimation() { }

    public RectAnimation(Rect toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public RectAnimation(Rect fromValue, Rect toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override Rect GetCurrentValueCore(Rect defaultOriginValue, Rect defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;

        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var from = From ?? defaultOriginValue;
        var to = To ?? defaultDestinationValue;

        return new Rect(
            from.X + (to.X - from.X) * progress,
            from.Y + (to.Y - from.Y) * progress,
            from.Width + (to.Width - from.Width) * progress,
            from.Height + (to.Height - from.Height) * progress);
    }
}

/// <summary>
/// Animates the value of a Size property between two target values.
/// </summary>
public sealed class SizeAnimation : AnimationTimeline<Size>
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(Size?), typeof(SizeAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(Size?), typeof(SizeAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(SizeAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public Size? From
    {
        get => (Size?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public Size? To
    {
        get => (Size?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public SizeAnimation() { }

    public SizeAnimation(Size toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public SizeAnimation(Size fromValue, Size toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override Size GetCurrentValueCore(Size defaultOriginValue, Size defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;

        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var from = From ?? defaultOriginValue;
        var to = To ?? defaultDestinationValue;

        return new Size(
            from.Width + (to.Width - from.Width) * progress,
            from.Height + (to.Height - from.Height) * progress);
    }
}

/// <summary>
/// Animates the value of a Vector property between two target values.
/// </summary>
public sealed class VectorAnimation : AnimationTimeline<Vector>
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(Vector?), typeof(VectorAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(Vector?), typeof(VectorAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(VectorAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public Vector? From
    {
        get => (Vector?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public Vector? To
    {
        get => (Vector?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public VectorAnimation() { }

    public VectorAnimation(Vector toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public VectorAnimation(Vector fromValue, Vector toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override Vector GetCurrentValueCore(Vector defaultOriginValue, Vector defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;

        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var from = From ?? defaultOriginValue;
        var to = To ?? defaultDestinationValue;

        return new Vector(
            from.X + (to.X - from.X) * progress,
            from.Y + (to.Y - from.Y) * progress);
    }
}

/// <summary>
/// Animates the value of an Int32 property between two target values.
/// </summary>
public sealed class Int32Animation : AnimationTimeline<int>
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(int?), typeof(Int32Animation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(int?), typeof(Int32Animation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(int?), typeof(Int32Animation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(Int32Animation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public int? From
    {
        get => (int?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public int? To
    {
        get => (int?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public int? By
    {
        get => (int?)GetValue(ByProperty);
        set => SetValue(ByProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public Int32Animation() { }

    public Int32Animation(int toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public Int32Animation(int fromValue, int toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override int GetCurrentValueCore(int defaultOriginValue, int defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;

        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var from = From ?? defaultOriginValue;
        var to = To ?? (By.HasValue ? from + By.Value : defaultDestinationValue);

        return (int)Math.Round(from + (to - from) * progress);
    }
}

/// <summary>
/// Animates the value of a Byte property between two target values.
/// </summary>
public sealed class ByteAnimation : AnimationTimeline<byte>
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(byte?), typeof(ByteAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(byte?), typeof(ByteAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(ByteAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public byte? From
    {
        get => (byte?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public byte? To
    {
        get => (byte?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public ByteAnimation() { }

    public ByteAnimation(byte toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public ByteAnimation(byte fromValue, byte toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override byte GetCurrentValueCore(byte defaultOriginValue, byte defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;

        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var from = From ?? defaultOriginValue;
        var to = To ?? defaultDestinationValue;

        return (byte)Math.Round(from + (to - from) * progress);
    }
}

/// <summary>
/// Animates the value of a Decimal property between two target values.
/// </summary>
public sealed class DecimalAnimation : AnimationTimeline<decimal>
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(decimal?), typeof(DecimalAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(decimal?), typeof(DecimalAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(decimal?), typeof(DecimalAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(DecimalAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public decimal? From
    {
        get => (decimal?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public decimal? To
    {
        get => (decimal?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public decimal? By
    {
        get => (decimal?)GetValue(ByProperty);
        set => SetValue(ByProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public DecimalAnimation() { }

    public DecimalAnimation(decimal toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public DecimalAnimation(decimal fromValue, decimal toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override decimal GetCurrentValueCore(decimal defaultOriginValue, decimal defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = (decimal)animationClock.CurrentProgress;

        if (EasingFunction != null)
        {
            progress = (decimal)EasingFunction.Ease((double)progress);
        }

        var from = From ?? defaultOriginValue;
        var to = To ?? (By.HasValue ? from + By.Value : defaultDestinationValue);

        return from + (to - from) * progress;
    }
}

/// <summary>
/// Animates the value of a Single (float) property between two target values.
/// </summary>
public sealed class SingleAnimation : AnimationTimeline<float>
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(float?), typeof(SingleAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(float?), typeof(SingleAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(float?), typeof(SingleAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(SingleAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public float? From
    {
        get => (float?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public float? To
    {
        get => (float?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public float? By
    {
        get => (float?)GetValue(ByProperty);
        set => SetValue(ByProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public SingleAnimation() { }

    public SingleAnimation(float toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public SingleAnimation(float fromValue, float toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override float GetCurrentValueCore(float defaultOriginValue, float defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = (float)animationClock.CurrentProgress;

        if (EasingFunction != null)
        {
            progress = (float)EasingFunction.Ease(progress);
        }

        var from = From ?? defaultOriginValue;
        var to = To ?? (By.HasValue ? from + By.Value : defaultDestinationValue);

        return from + (to - from) * progress;
    }
}

/// <summary>
/// Animates the value of an Int16 property between two target values.
/// </summary>
public sealed class Int16Animation : AnimationTimeline<short>
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(short?), typeof(Int16Animation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(short?), typeof(Int16Animation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(Int16Animation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public short? From
    {
        get => (short?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public short? To
    {
        get => (short?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public Int16Animation() { }

    public Int16Animation(short toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public Int16Animation(short fromValue, short toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override short GetCurrentValueCore(short defaultOriginValue, short defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;

        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var from = From ?? defaultOriginValue;
        var to = To ?? defaultDestinationValue;

        return (short)Math.Round(from + (to - from) * progress);
    }
}

/// <summary>
/// Animates the value of an Int64 property between two target values.
/// </summary>
public sealed class Int64Animation : AnimationTimeline<long>
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(long?), typeof(Int64Animation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(long?), typeof(Int64Animation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(long?), typeof(Int64Animation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(Int64Animation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public long? From
    {
        get => (long?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public long? To
    {
        get => (long?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public long? By
    {
        get => (long?)GetValue(ByProperty);
        set => SetValue(ByProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public Int64Animation() { }

    public Int64Animation(long toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public Int64Animation(long fromValue, long toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override long GetCurrentValueCore(long defaultOriginValue, long defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;

        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var from = From ?? defaultOriginValue;
        var to = To ?? (By.HasValue ? from + By.Value : defaultDestinationValue);

        return (long)Math.Round(from + (to - from) * progress);
    }
}

/// <summary>
/// Animates the value of a CornerRadius property between two target values.
/// </summary>
public sealed class CornerRadiusAnimation : AnimationTimeline<CornerRadius>
{
    #region Dependency Properties

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(CornerRadius?), typeof(CornerRadiusAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(CornerRadius?), typeof(CornerRadiusAnimation),
            new PropertyMetadata(null));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(CornerRadiusAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public CornerRadius? From
    {
        get => (CornerRadius?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public CornerRadius? To
    {
        get => (CornerRadius?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    public CornerRadiusAnimation() { }

    public CornerRadiusAnimation(CornerRadius toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public CornerRadiusAnimation(CornerRadius fromValue, CornerRadius toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    protected override CornerRadius GetCurrentValueCore(CornerRadius defaultOriginValue, CornerRadius defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;

        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var from = From ?? defaultOriginValue;
        var to = To ?? defaultDestinationValue;

        return new CornerRadius(
            from.TopLeft + (to.TopLeft - from.TopLeft) * progress,
            from.TopRight + (to.TopRight - from.TopRight) * progress,
            from.BottomRight + (to.BottomRight - from.BottomRight) * progress,
            from.BottomLeft + (to.BottomLeft - from.BottomLeft) * progress);
    }
}
