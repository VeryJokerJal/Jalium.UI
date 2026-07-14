using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Media.Animation;

public abstract class BooleanAnimationBase : AnimationTimeline
{
    protected BooleanAnimationBase() { }
    public new BooleanAnimationBase Clone() => (BooleanAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((bool)defaultOriginValue, (bool)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(bool); } }
    public bool GetCurrentValue(bool defaultOriginValue, bool defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract bool GetCurrentValueCore(bool defaultOriginValue, bool defaultDestinationValue, AnimationClock animationClock);
}

public abstract class ByteAnimationBase : AnimationTimeline
{
    protected ByteAnimationBase() { }
    public new ByteAnimationBase Clone() => (ByteAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((byte)defaultOriginValue, (byte)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(byte); } }
    public byte GetCurrentValue(byte defaultOriginValue, byte defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract byte GetCurrentValueCore(byte defaultOriginValue, byte defaultDestinationValue, AnimationClock animationClock);
}

public abstract class CharAnimationBase : AnimationTimeline
{
    protected CharAnimationBase() { }
    public new CharAnimationBase Clone() => (CharAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((char)defaultOriginValue, (char)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(char); } }
    public char GetCurrentValue(char defaultOriginValue, char defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract char GetCurrentValueCore(char defaultOriginValue, char defaultDestinationValue, AnimationClock animationClock);
}

public abstract class ColorAnimationBase : AnimationTimeline
{
    protected ColorAnimationBase() { }
    public new ColorAnimationBase Clone() => (ColorAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((Color)defaultOriginValue, (Color)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(Color); } }
    public Color GetCurrentValue(Color defaultOriginValue, Color defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract Color GetCurrentValueCore(Color defaultOriginValue, Color defaultDestinationValue, AnimationClock animationClock);
}

public abstract class DecimalAnimationBase : AnimationTimeline
{
    protected DecimalAnimationBase() { }
    public new DecimalAnimationBase Clone() => (DecimalAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((decimal)defaultOriginValue, (decimal)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(decimal); } }
    public decimal GetCurrentValue(decimal defaultOriginValue, decimal defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract decimal GetCurrentValueCore(decimal defaultOriginValue, decimal defaultDestinationValue, AnimationClock animationClock);
}

public abstract class DoubleAnimationBase : AnimationTimeline
{
    protected DoubleAnimationBase() { }
    public new DoubleAnimationBase Clone() => (DoubleAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((double)defaultOriginValue, (double)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(double); } }
    public double GetCurrentValue(double defaultOriginValue, double defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract double GetCurrentValueCore(double defaultOriginValue, double defaultDestinationValue, AnimationClock animationClock);
}

public abstract class Int16AnimationBase : AnimationTimeline
{
    protected Int16AnimationBase() { }
    public new Int16AnimationBase Clone() => (Int16AnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((short)defaultOriginValue, (short)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(short); } }
    public short GetCurrentValue(short defaultOriginValue, short defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract short GetCurrentValueCore(short defaultOriginValue, short defaultDestinationValue, AnimationClock animationClock);
}

public abstract class Int32AnimationBase : AnimationTimeline
{
    protected Int32AnimationBase() { }
    public new Int32AnimationBase Clone() => (Int32AnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((int)defaultOriginValue, (int)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(int); } }
    public int GetCurrentValue(int defaultOriginValue, int defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract int GetCurrentValueCore(int defaultOriginValue, int defaultDestinationValue, AnimationClock animationClock);
}

public abstract class Int64AnimationBase : AnimationTimeline
{
    protected Int64AnimationBase() { }
    public new Int64AnimationBase Clone() => (Int64AnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((long)defaultOriginValue, (long)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(long); } }
    public long GetCurrentValue(long defaultOriginValue, long defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract long GetCurrentValueCore(long defaultOriginValue, long defaultDestinationValue, AnimationClock animationClock);
}

public abstract class MatrixAnimationBase : AnimationTimeline
{
    protected MatrixAnimationBase() { }
    public new MatrixAnimationBase Clone() => (MatrixAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((Matrix)defaultOriginValue, (Matrix)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(Matrix); } }
    public Matrix GetCurrentValue(Matrix defaultOriginValue, Matrix defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract Matrix GetCurrentValueCore(Matrix defaultOriginValue, Matrix defaultDestinationValue, AnimationClock animationClock);
}

public abstract class ObjectAnimationBase : AnimationTimeline
{
    protected ObjectAnimationBase() { }
    public new ObjectAnimationBase Clone() => (ObjectAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(object); } }
    protected abstract object GetCurrentValueCore(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock);
}

public abstract class Point3DAnimationBase : AnimationTimeline
{
    protected Point3DAnimationBase() { }
    public new Point3DAnimationBase Clone() => (Point3DAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((Point3D)defaultOriginValue, (Point3D)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(Point3D); } }
    public Point3D GetCurrentValue(Point3D defaultOriginValue, Point3D defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract Point3D GetCurrentValueCore(Point3D defaultOriginValue, Point3D defaultDestinationValue, AnimationClock animationClock);
}

public abstract class PointAnimationBase : AnimationTimeline
{
    protected PointAnimationBase() { }
    public new PointAnimationBase Clone() => (PointAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((Point)defaultOriginValue, (Point)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(Point); } }
    public Point GetCurrentValue(Point defaultOriginValue, Point defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract Point GetCurrentValueCore(Point defaultOriginValue, Point defaultDestinationValue, AnimationClock animationClock);
}

public abstract class QuaternionAnimationBase : AnimationTimeline
{
    protected QuaternionAnimationBase() { }
    public new QuaternionAnimationBase Clone() => (QuaternionAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((Quaternion)defaultOriginValue, (Quaternion)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(Quaternion); } }
    public Quaternion GetCurrentValue(Quaternion defaultOriginValue, Quaternion defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract Quaternion GetCurrentValueCore(Quaternion defaultOriginValue, Quaternion defaultDestinationValue, AnimationClock animationClock);
}

public abstract class RectAnimationBase : AnimationTimeline
{
    protected RectAnimationBase() { }
    public new RectAnimationBase Clone() => (RectAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((Rect)defaultOriginValue, (Rect)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(Rect); } }
    public Rect GetCurrentValue(Rect defaultOriginValue, Rect defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract Rect GetCurrentValueCore(Rect defaultOriginValue, Rect defaultDestinationValue, AnimationClock animationClock);
}

public abstract class Rotation3DAnimationBase : AnimationTimeline
{
    protected Rotation3DAnimationBase() { }
    public new Rotation3DAnimationBase Clone() => (Rotation3DAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock) =>
        GetCurrentValue((Rotation3D)defaultOriginValue, (Rotation3D)defaultDestinationValue, animationClock);
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(Rotation3D); } }
    public Rotation3D GetCurrentValue(Rotation3D defaultOriginValue, Rotation3D defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract Rotation3D GetCurrentValueCore(Rotation3D defaultOriginValue, Rotation3D defaultDestinationValue, AnimationClock animationClock);
}

public abstract class SingleAnimationBase : AnimationTimeline
{
    protected SingleAnimationBase() { }
    public new SingleAnimationBase Clone() => (SingleAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((float)defaultOriginValue, (float)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(float); } }
    public float GetCurrentValue(float defaultOriginValue, float defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract float GetCurrentValueCore(float defaultOriginValue, float defaultDestinationValue, AnimationClock animationClock);
}

public abstract class SizeAnimationBase : AnimationTimeline
{
    protected SizeAnimationBase() { }
    public new SizeAnimationBase Clone() => (SizeAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((Size)defaultOriginValue, (Size)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(Size); } }
    public Size GetCurrentValue(Size defaultOriginValue, Size defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract Size GetCurrentValueCore(Size defaultOriginValue, Size defaultDestinationValue, AnimationClock animationClock);
}

public abstract class StringAnimationBase : AnimationTimeline
{
    protected StringAnimationBase() { }
    public new StringAnimationBase Clone() => (StringAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock) =>
        GetCurrentValue((string)defaultOriginValue, (string)defaultDestinationValue, animationClock);
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(string); } }
    public string GetCurrentValue(string defaultOriginValue, string defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract string GetCurrentValueCore(string defaultOriginValue, string defaultDestinationValue, AnimationClock animationClock);
}

public abstract class ThicknessAnimationBase : AnimationTimeline
{
    protected ThicknessAnimationBase() { }
    public new ThicknessAnimationBase Clone() => (ThicknessAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((Thickness)defaultOriginValue, (Thickness)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(Thickness); } }
    public Thickness GetCurrentValue(Thickness defaultOriginValue, Thickness defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract Thickness GetCurrentValueCore(Thickness defaultOriginValue, Thickness defaultDestinationValue, AnimationClock animationClock);
}

public abstract class Vector3DAnimationBase : AnimationTimeline
{
    protected Vector3DAnimationBase() { }
    public new Vector3DAnimationBase Clone() => (Vector3DAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((Vector3D)defaultOriginValue, (Vector3D)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(Vector3D); } }
    public Vector3D GetCurrentValue(Vector3D defaultOriginValue, Vector3D defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract Vector3D GetCurrentValueCore(Vector3D defaultOriginValue, Vector3D defaultDestinationValue, AnimationClock animationClock);
}

public abstract class VectorAnimationBase : AnimationTimeline
{
    protected VectorAnimationBase() { }
    public new VectorAnimationBase Clone() => (VectorAnimationBase)base.Clone();
    public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        return GetCurrentValue((Vector)defaultOriginValue, (Vector)defaultDestinationValue, animationClock);
    }
    public sealed override Type TargetPropertyType { get { ReadPreamble(); return typeof(Vector); } }
    public Vector GetCurrentValue(Vector defaultOriginValue, Vector defaultDestinationValue, AnimationClock animationClock)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(animationClock);
        return animationClock.CurrentState == ClockState.Stopped
            ? defaultDestinationValue
            : GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
    }
    protected abstract Vector GetCurrentValueCore(Vector defaultOriginValue, Vector defaultDestinationValue, AnimationClock animationClock);
}
