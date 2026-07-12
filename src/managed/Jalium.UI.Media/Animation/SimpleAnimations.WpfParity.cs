namespace Jalium.UI.Media.Animation;

public sealed partial class DoubleAnimation
{
    public DoubleAnimation(double toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public DoubleAnimation(double fromValue, double toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new DoubleAnimation Clone() => (DoubleAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new DoubleAnimation();
}

public sealed partial class ColorAnimation
{
    public ColorAnimation(Color toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public ColorAnimation(Color fromValue, Color toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new ColorAnimation Clone() => (ColorAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new ColorAnimation();
}

public sealed partial class PointAnimation
{
    public PointAnimation() { }

    public PointAnimation(Point toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public PointAnimation(Point toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public PointAnimation(Point fromValue, Point toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    public PointAnimation(Point fromValue, Point toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new PointAnimation Clone() => (PointAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new PointAnimation();
}

public sealed partial class ThicknessAnimation
{
    public ThicknessAnimation() { }

    public ThicknessAnimation(Thickness toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    public ThicknessAnimation(Thickness toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public ThicknessAnimation(Thickness fromValue, Thickness toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    public ThicknessAnimation(Thickness fromValue, Thickness toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new ThicknessAnimation Clone() => (ThicknessAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new ThicknessAnimation();
}
