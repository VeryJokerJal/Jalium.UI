using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Media.Animation;

/// <summary>Defines the common contract for key frames that animate strings.</summary>
public abstract class StringKeyFrame : KeyFrame<string>
{
    public new static readonly DependencyProperty ValueProperty = KeyFrame<string>.ValueProperty;
    public new static readonly DependencyProperty KeyTimeProperty = KeyFrame<string>.KeyTimeProperty;

    protected StringKeyFrame() { }
    protected StringKeyFrame(string value) => Value = value;
    protected StringKeyFrame(string value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    public new string Value { get => base.Value; set => base.Value = value; }
    public new KeyTime KeyTime { get => base.KeyTime; set => base.KeyTime = value; }
    public new string InterpolateValue(string baseValue, double keyFrameProgress) =>
        base.InterpolateValue(baseValue, keyFrameProgress);

    protected abstract override string InterpolateValueCore(string baseValue, double keyFrameProgress);
}

/// <summary>Defines the common contract for key frames that animate <see cref="Point3D"/> values.</summary>
public abstract class Point3DKeyFrame : KeyFrame<Point3D>
{
    public new static readonly DependencyProperty ValueProperty = KeyFrame<Point3D>.ValueProperty;
    public new static readonly DependencyProperty KeyTimeProperty = KeyFrame<Point3D>.KeyTimeProperty;

    protected Point3DKeyFrame() { }
    protected Point3DKeyFrame(Point3D value) => Value = value;
    protected Point3DKeyFrame(Point3D value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    public new Point3D Value { get => base.Value; set => base.Value = value; }
    public new KeyTime KeyTime { get => base.KeyTime; set => base.KeyTime = value; }
    public new Point3D InterpolateValue(Point3D baseValue, double keyFrameProgress) =>
        base.InterpolateValue(baseValue, keyFrameProgress);

    protected abstract override Point3D InterpolateValueCore(Point3D baseValue, double keyFrameProgress);
}

/// <summary>Defines the common contract for key frames that animate <see cref="Vector3D"/> values.</summary>
public abstract class Vector3DKeyFrame : KeyFrame<Vector3D>
{
    public new static readonly DependencyProperty ValueProperty = KeyFrame<Vector3D>.ValueProperty;
    public new static readonly DependencyProperty KeyTimeProperty = KeyFrame<Vector3D>.KeyTimeProperty;

    protected Vector3DKeyFrame() { }
    protected Vector3DKeyFrame(Vector3D value) => Value = value;
    protected Vector3DKeyFrame(Vector3D value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    public new Vector3D Value { get => base.Value; set => base.Value = value; }
    public new KeyTime KeyTime { get => base.KeyTime; set => base.KeyTime = value; }
    public new Vector3D InterpolateValue(Vector3D baseValue, double keyFrameProgress) =>
        base.InterpolateValue(baseValue, keyFrameProgress);

    protected abstract override Vector3D InterpolateValueCore(Vector3D baseValue, double keyFrameProgress);
}

/// <summary>Defines the common contract for key frames that animate <see cref="Quaternion"/> values.</summary>
public abstract class QuaternionKeyFrame : KeyFrame<Quaternion>
{
    public new static readonly DependencyProperty ValueProperty = KeyFrame<Quaternion>.ValueProperty;
    public new static readonly DependencyProperty KeyTimeProperty = KeyFrame<Quaternion>.KeyTimeProperty;

    protected QuaternionKeyFrame() { }
    protected QuaternionKeyFrame(Quaternion value) => Value = value;
    protected QuaternionKeyFrame(Quaternion value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    public new Quaternion Value { get => base.Value; set => base.Value = value; }
    public new KeyTime KeyTime { get => base.KeyTime; set => base.KeyTime = value; }
    public new Quaternion InterpolateValue(Quaternion baseValue, double keyFrameProgress) =>
        base.InterpolateValue(baseValue, keyFrameProgress);

    protected abstract override Quaternion InterpolateValueCore(Quaternion baseValue, double keyFrameProgress);
}

/// <summary>Defines the common contract for key frames that animate <see cref="Rotation3D"/> values.</summary>
public abstract class Rotation3DKeyFrame : KeyFrame<Rotation3D>
{
    public new static readonly DependencyProperty ValueProperty = KeyFrame<Rotation3D>.ValueProperty;
    public new static readonly DependencyProperty KeyTimeProperty = KeyFrame<Rotation3D>.KeyTimeProperty;

    protected Rotation3DKeyFrame() { }
    protected Rotation3DKeyFrame(Rotation3D value) => Value = value;
    protected Rotation3DKeyFrame(Rotation3D value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    public new Rotation3D Value { get => base.Value; set => base.Value = value; }
    public new KeyTime KeyTime { get => base.KeyTime; set => base.KeyTime = value; }
    public new Rotation3D InterpolateValue(Rotation3D baseValue, double keyFrameProgress) =>
        base.InterpolateValue(baseValue, keyFrameProgress);

    protected abstract override Rotation3D InterpolateValueCore(Rotation3D baseValue, double keyFrameProgress);
}
