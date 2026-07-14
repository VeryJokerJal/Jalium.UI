using System.Collections;

namespace Jalium.UI.Media.Animation;

internal static class KeyFrameCollectionDefaults
{
    internal static TCollection CreateFrozen<TCollection>() where TCollection : Freezable, new()
    {
        var collection = new TCollection();
        collection.Freeze();
        return collection;
    }
}

public partial class DoubleKeyFrameCollection : Freezable, IList
{
    private static readonly DoubleKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<DoubleKeyFrameCollection>();
    public static DoubleKeyFrameCollection Empty => s_empty;
    public new DoubleKeyFrameCollection Clone() => (DoubleKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new DoubleKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _storage.Freeze(isChecking);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); _storage.CopyFrom(((DoubleKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); _storage.CopyFrom(((DoubleKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable sourceFreezable) { base.GetAsFrozenCore(sourceFreezable); _storage.CopyFrom(((DoubleKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable) { base.GetCurrentValueAsFrozenCore(sourceFreezable); _storage.CopyFrom(((DoubleKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
}

public partial class ColorKeyFrameCollection : Freezable, IList
{
    private static readonly ColorKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<ColorKeyFrameCollection>();
    public static ColorKeyFrameCollection Empty => s_empty;
    public new ColorKeyFrameCollection Clone() => (ColorKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new ColorKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _storage.Freeze(isChecking);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); _storage.CopyFrom(((ColorKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); _storage.CopyFrom(((ColorKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable sourceFreezable) { base.GetAsFrozenCore(sourceFreezable); _storage.CopyFrom(((ColorKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable) { base.GetCurrentValueAsFrozenCore(sourceFreezable); _storage.CopyFrom(((ColorKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
}

public partial class PointKeyFrameCollection : Freezable, IList
{
    private static readonly PointKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<PointKeyFrameCollection>();
    public static PointKeyFrameCollection Empty => s_empty;
    public new PointKeyFrameCollection Clone() => (PointKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new PointKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _storage.Freeze(isChecking);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); _storage.CopyFrom(((PointKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); _storage.CopyFrom(((PointKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable sourceFreezable) { base.GetAsFrozenCore(sourceFreezable); _storage.CopyFrom(((PointKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable) { base.GetCurrentValueAsFrozenCore(sourceFreezable); _storage.CopyFrom(((PointKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
}

public partial class ThicknessKeyFrameCollection : Freezable, IList
{
    private static readonly ThicknessKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<ThicknessKeyFrameCollection>();
    public static ThicknessKeyFrameCollection Empty => s_empty;
    public new ThicknessKeyFrameCollection Clone() => (ThicknessKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new ThicknessKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _storage.Freeze(isChecking);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); _storage.CopyFrom(((ThicknessKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); _storage.CopyFrom(((ThicknessKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable sourceFreezable) { base.GetAsFrozenCore(sourceFreezable); _storage.CopyFrom(((ThicknessKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable) { base.GetCurrentValueAsFrozenCore(sourceFreezable); _storage.CopyFrom(((ThicknessKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
}

public partial class ObjectKeyFrameCollection : Freezable, IList
{
    private static readonly ObjectKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<ObjectKeyFrameCollection>();
    public static ObjectKeyFrameCollection Empty => s_empty;
    public new ObjectKeyFrameCollection Clone() => (ObjectKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new ObjectKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _storage.Freeze(isChecking);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); _storage.CopyFrom(((ObjectKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); _storage.CopyFrom(((ObjectKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable sourceFreezable) { base.GetAsFrozenCore(sourceFreezable); _storage.CopyFrom(((ObjectKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable) { base.GetCurrentValueAsFrozenCore(sourceFreezable); _storage.CopyFrom(((ObjectKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
}

public partial class StringKeyFrameCollection : Freezable, IList
{
    private static readonly StringKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<StringKeyFrameCollection>();
    public static StringKeyFrameCollection Empty => s_empty;
    public new StringKeyFrameCollection Clone() => (StringKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new StringKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _storage.Freeze(isChecking);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); _storage.CopyFrom(((StringKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); _storage.CopyFrom(((StringKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable sourceFreezable) { base.GetAsFrozenCore(sourceFreezable); _storage.CopyFrom(((StringKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable) { base.GetCurrentValueAsFrozenCore(sourceFreezable); _storage.CopyFrom(((StringKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
}

public partial class Point3DKeyFrameCollection : Freezable, IList
{
    private static readonly Point3DKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<Point3DKeyFrameCollection>();
    public static Point3DKeyFrameCollection Empty => s_empty;
    public new Point3DKeyFrameCollection Clone() => (Point3DKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new Point3DKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _storage.Freeze(isChecking);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); _storage.CopyFrom(((Point3DKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); _storage.CopyFrom(((Point3DKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable sourceFreezable) { base.GetAsFrozenCore(sourceFreezable); _storage.CopyFrom(((Point3DKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable) { base.GetCurrentValueAsFrozenCore(sourceFreezable); _storage.CopyFrom(((Point3DKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
}

public partial class Vector3DKeyFrameCollection : Freezable, IList
{
    private static readonly Vector3DKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<Vector3DKeyFrameCollection>();
    public static Vector3DKeyFrameCollection Empty => s_empty;
    public new Vector3DKeyFrameCollection Clone() => (Vector3DKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new Vector3DKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _storage.Freeze(isChecking);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); _storage.CopyFrom(((Vector3DKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); _storage.CopyFrom(((Vector3DKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable sourceFreezable) { base.GetAsFrozenCore(sourceFreezable); _storage.CopyFrom(((Vector3DKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable) { base.GetCurrentValueAsFrozenCore(sourceFreezable); _storage.CopyFrom(((Vector3DKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
}

public partial class QuaternionKeyFrameCollection : Freezable, IList
{
    private static readonly QuaternionKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<QuaternionKeyFrameCollection>();
    public static QuaternionKeyFrameCollection Empty => s_empty;
    public new QuaternionKeyFrameCollection Clone() => (QuaternionKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new QuaternionKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _storage.Freeze(isChecking);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); _storage.CopyFrom(((QuaternionKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); _storage.CopyFrom(((QuaternionKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable sourceFreezable) { base.GetAsFrozenCore(sourceFreezable); _storage.CopyFrom(((QuaternionKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable) { base.GetCurrentValueAsFrozenCore(sourceFreezable); _storage.CopyFrom(((QuaternionKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
}

public partial class Rotation3DKeyFrameCollection : Freezable, IList
{
    private static readonly Rotation3DKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<Rotation3DKeyFrameCollection>();
    public static Rotation3DKeyFrameCollection Empty => s_empty;
    public new Rotation3DKeyFrameCollection Clone() => (Rotation3DKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new Rotation3DKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _storage.Freeze(isChecking);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); _storage.CopyFrom(((Rotation3DKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); _storage.CopyFrom(((Rotation3DKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable sourceFreezable) { base.GetAsFrozenCore(sourceFreezable); _storage.CopyFrom(((Rotation3DKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable) { base.GetCurrentValueAsFrozenCore(sourceFreezable); _storage.CopyFrom(((Rotation3DKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
}
