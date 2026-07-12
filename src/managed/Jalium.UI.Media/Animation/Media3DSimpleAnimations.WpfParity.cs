using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Media.Animation;

public sealed partial class Point3DAnimation
{
    public Point3DAnimation(Point3D toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public Point3DAnimation(Point3D fromValue, Point3D toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new Point3DAnimation Clone() => (Point3DAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new Point3DAnimation();
}

public sealed partial class QuaternionAnimation
{
    public QuaternionAnimation(Quaternion toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public QuaternionAnimation(Quaternion fromValue, Quaternion toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new QuaternionAnimation Clone() => (QuaternionAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new QuaternionAnimation();
}

public sealed partial class Rotation3DAnimation
{
    public Rotation3DAnimation(Rotation3D toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public Rotation3DAnimation(Rotation3D fromValue, Rotation3D toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new Rotation3DAnimation Clone() => (Rotation3DAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new Rotation3DAnimation();
}

public partial class Vector3DAnimation
{
    public Vector3DAnimation(Vector3D toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public Vector3DAnimation(Vector3D fromValue, Vector3D toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new Vector3DAnimation Clone() => (Vector3DAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new Vector3DAnimation();
}
