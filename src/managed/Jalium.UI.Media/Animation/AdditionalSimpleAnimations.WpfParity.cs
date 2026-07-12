namespace Jalium.UI.Media.Animation;

public sealed partial class ByteAnimation
{
    public ByteAnimation(byte toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public ByteAnimation(byte fromValue, byte toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new ByteAnimation Clone() => (ByteAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new ByteAnimation();
}

public sealed partial class DecimalAnimation
{
    public DecimalAnimation(decimal toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public DecimalAnimation(decimal fromValue, decimal toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new DecimalAnimation Clone() => (DecimalAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new DecimalAnimation();
}


public sealed partial class Int16Animation
{
    public Int16Animation(short toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public Int16Animation(short fromValue, short toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new Int16Animation Clone() => (Int16Animation)base.Clone();

    protected override Freezable CreateInstanceCore() => new Int16Animation();
}


public sealed partial class Int32Animation
{
    public Int32Animation(int toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public Int32Animation(int fromValue, int toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new Int32Animation Clone() => (Int32Animation)base.Clone();

    protected override Freezable CreateInstanceCore() => new Int32Animation();
}


public sealed partial class Int64Animation
{
    public Int64Animation(long toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public Int64Animation(long fromValue, long toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new Int64Animation Clone() => (Int64Animation)base.Clone();

    protected override Freezable CreateInstanceCore() => new Int64Animation();
}


public sealed partial class SingleAnimation
{
    public SingleAnimation(float toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public SingleAnimation(float fromValue, float toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new SingleAnimation Clone() => (SingleAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new SingleAnimation();
}


public sealed partial class RectAnimation
{
    public RectAnimation(Rect toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public RectAnimation(Rect fromValue, Rect toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new RectAnimation Clone() => (RectAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new RectAnimation();
}


public sealed partial class SizeAnimation
{
    public SizeAnimation(Size toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public SizeAnimation(Size fromValue, Size toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new SizeAnimation Clone() => (SizeAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new SizeAnimation();
}


public sealed partial class VectorAnimation
{
    public VectorAnimation(Vector toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public VectorAnimation(Vector fromValue, Vector toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new VectorAnimation Clone() => (VectorAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new VectorAnimation();
}
