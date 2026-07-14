using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Media.Animation;

/// <summary>Defines the common contract for key frames that animate strings.</summary>
public abstract class StringKeyFrame : Freezable, IKeyFrame
{
    public static readonly DependencyProperty ValueProperty = KeyFrameSupport.RegisterValue<string, StringKeyFrame>();
    public static readonly DependencyProperty KeyTimeProperty = KeyFrameSupport.RegisterKeyTime<StringKeyFrame>();

    protected StringKeyFrame() { }
    protected StringKeyFrame(string value) => Value = value;
    protected StringKeyFrame(string value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    public string Value { get => (string)GetValue(ValueProperty)!; set => SetValue(ValueProperty, value); }
    public KeyTime KeyTime { get => (KeyTime)(GetValue(KeyTimeProperty) ?? KeyTime.Uniform); set => SetValue(KeyTimeProperty, value); }
    object IKeyFrame.Value { get => Value; set => Value = (string)value; }
    public string InterpolateValue(string baseValue, double keyFrameProgress)
    {
        KeyFrameSupport.ValidateProgress(keyFrameProgress);
        return InterpolateValueCore(baseValue, keyFrameProgress);
    }
    protected abstract string InterpolateValueCore(string baseValue, double keyFrameProgress);
}

/// <summary>Defines the common contract for key frames that animate <see cref="Point3D"/> values.</summary>
public abstract class Point3DKeyFrame : Freezable, IKeyFrame
{
    public static readonly DependencyProperty ValueProperty = KeyFrameSupport.RegisterValue<Point3D, Point3DKeyFrame>();
    public static readonly DependencyProperty KeyTimeProperty = KeyFrameSupport.RegisterKeyTime<Point3DKeyFrame>();

    protected Point3DKeyFrame() { }
    protected Point3DKeyFrame(Point3D value) => Value = value;
    protected Point3DKeyFrame(Point3D value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    public Point3D Value { get => (Point3D)(GetValue(ValueProperty) ?? default(Point3D)); set => SetValue(ValueProperty, value); }
    public KeyTime KeyTime { get => (KeyTime)(GetValue(KeyTimeProperty) ?? KeyTime.Uniform); set => SetValue(KeyTimeProperty, value); }
    object IKeyFrame.Value { get => Value; set => Value = (Point3D)value; }
    public Point3D InterpolateValue(Point3D baseValue, double keyFrameProgress)
    {
        KeyFrameSupport.ValidateProgress(keyFrameProgress);
        return InterpolateValueCore(baseValue, keyFrameProgress);
    }
    protected abstract Point3D InterpolateValueCore(Point3D baseValue, double keyFrameProgress);
}

/// <summary>Defines the common contract for key frames that animate <see cref="Vector3D"/> values.</summary>
public abstract class Vector3DKeyFrame : Freezable, IKeyFrame
{
    public static readonly DependencyProperty ValueProperty = KeyFrameSupport.RegisterValue<Vector3D, Vector3DKeyFrame>();
    public static readonly DependencyProperty KeyTimeProperty = KeyFrameSupport.RegisterKeyTime<Vector3DKeyFrame>();

    protected Vector3DKeyFrame() { }
    protected Vector3DKeyFrame(Vector3D value) => Value = value;
    protected Vector3DKeyFrame(Vector3D value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    public Vector3D Value { get => (Vector3D)(GetValue(ValueProperty) ?? default(Vector3D)); set => SetValue(ValueProperty, value); }
    public KeyTime KeyTime { get => (KeyTime)(GetValue(KeyTimeProperty) ?? KeyTime.Uniform); set => SetValue(KeyTimeProperty, value); }
    object IKeyFrame.Value { get => Value; set => Value = (Vector3D)value; }
    public Vector3D InterpolateValue(Vector3D baseValue, double keyFrameProgress)
    {
        KeyFrameSupport.ValidateProgress(keyFrameProgress);
        return InterpolateValueCore(baseValue, keyFrameProgress);
    }
    protected abstract Vector3D InterpolateValueCore(Vector3D baseValue, double keyFrameProgress);
}

/// <summary>Defines the common contract for key frames that animate <see cref="Quaternion"/> values.</summary>
public abstract class QuaternionKeyFrame : Freezable, IKeyFrame
{
    public static readonly DependencyProperty ValueProperty = KeyFrameSupport.RegisterValue<Quaternion, QuaternionKeyFrame>();
    public static readonly DependencyProperty KeyTimeProperty = KeyFrameSupport.RegisterKeyTime<QuaternionKeyFrame>();

    protected QuaternionKeyFrame() { }
    protected QuaternionKeyFrame(Quaternion value) => Value = value;
    protected QuaternionKeyFrame(Quaternion value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    public Quaternion Value { get => (Quaternion)(GetValue(ValueProperty) ?? default(Quaternion)); set => SetValue(ValueProperty, value); }
    public KeyTime KeyTime { get => (KeyTime)(GetValue(KeyTimeProperty) ?? KeyTime.Uniform); set => SetValue(KeyTimeProperty, value); }
    object IKeyFrame.Value { get => Value; set => Value = (Quaternion)value; }
    public Quaternion InterpolateValue(Quaternion baseValue, double keyFrameProgress)
    {
        KeyFrameSupport.ValidateProgress(keyFrameProgress);
        return InterpolateValueCore(baseValue, keyFrameProgress);
    }
    protected abstract Quaternion InterpolateValueCore(Quaternion baseValue, double keyFrameProgress);
}

/// <summary>Defines the common contract for key frames that animate <see cref="Rotation3D"/> values.</summary>
public abstract class Rotation3DKeyFrame : Freezable, IKeyFrame
{
    public static readonly DependencyProperty ValueProperty = KeyFrameSupport.RegisterValue<Rotation3D, Rotation3DKeyFrame>();
    public static readonly DependencyProperty KeyTimeProperty = KeyFrameSupport.RegisterKeyTime<Rotation3DKeyFrame>();

    protected Rotation3DKeyFrame() { }
    protected Rotation3DKeyFrame(Rotation3D value) => Value = value;
    protected Rotation3DKeyFrame(Rotation3D value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    public Rotation3D Value { get => (Rotation3D)GetValue(ValueProperty)!; set => SetValue(ValueProperty, value); }
    public KeyTime KeyTime { get => (KeyTime)(GetValue(KeyTimeProperty) ?? KeyTime.Uniform); set => SetValue(KeyTimeProperty, value); }
    object IKeyFrame.Value { get => Value; set => Value = (Rotation3D)value; }
    public Rotation3D InterpolateValue(Rotation3D baseValue, double keyFrameProgress)
    {
        KeyFrameSupport.ValidateProgress(keyFrameProgress);
        return InterpolateValueCore(baseValue, keyFrameProgress);
    }
    protected abstract Rotation3D InterpolateValueCore(Rotation3D baseValue, double keyFrameProgress);
}
