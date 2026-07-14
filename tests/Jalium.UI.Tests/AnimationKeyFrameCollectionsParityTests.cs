using System.Collections;
using System.Reflection;
using Jalium.UI.Markup;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Tests;

public sealed class AnimationKeyFrameCollectionsParityTests
{
    private static readonly (Type KeyFrame, Type Collection)[] s_wpfFamilies =
    [
        (typeof(BooleanKeyFrame), typeof(BooleanKeyFrameCollection)),
        (typeof(ByteKeyFrame), typeof(ByteKeyFrameCollection)),
        (typeof(CharKeyFrame), typeof(CharKeyFrameCollection)),
        (typeof(ColorKeyFrame), typeof(ColorKeyFrameCollection)),
        (typeof(DecimalKeyFrame), typeof(DecimalKeyFrameCollection)),
        (typeof(DoubleKeyFrame), typeof(DoubleKeyFrameCollection)),
        (typeof(Int16KeyFrame), typeof(Int16KeyFrameCollection)),
        (typeof(Int32KeyFrame), typeof(Int32KeyFrameCollection)),
        (typeof(Int64KeyFrame), typeof(Int64KeyFrameCollection)),
        (typeof(MatrixKeyFrame), typeof(MatrixKeyFrameCollection)),
        (typeof(ObjectKeyFrame), typeof(ObjectKeyFrameCollection)),
        (typeof(Point3DKeyFrame), typeof(Point3DKeyFrameCollection)),
        (typeof(PointKeyFrame), typeof(PointKeyFrameCollection)),
        (typeof(QuaternionKeyFrame), typeof(QuaternionKeyFrameCollection)),
        (typeof(RectKeyFrame), typeof(RectKeyFrameCollection)),
        (typeof(Rotation3DKeyFrame), typeof(Rotation3DKeyFrameCollection)),
        (typeof(SingleKeyFrame), typeof(SingleKeyFrameCollection)),
        (typeof(SizeKeyFrame), typeof(SizeKeyFrameCollection)),
        (typeof(StringKeyFrame), typeof(StringKeyFrameCollection)),
        (typeof(ThicknessKeyFrame), typeof(ThicknessKeyFrameCollection)),
        (typeof(Vector3DKeyFrame), typeof(Vector3DKeyFrameCollection)),
        (typeof(VectorKeyFrame), typeof(VectorKeyFrameCollection)),
    ];

    [Fact]
    public void AllWpfKeyFrameFamiliesHaveCanonicalDirectBasesInterfacesConstructorsAndDependencyPropertyOwners()
    {
        Type[] expectedCollectionInterfaces = [typeof(ICollection), typeof(IEnumerable), typeof(IList)];

        foreach ((Type keyFrame, Type collection) in s_wpfFamilies)
        {
            Assert.True(keyFrame.IsAbstract);
            Assert.False(keyFrame.IsSealed);
            Assert.Equal(typeof(Freezable), keyFrame.BaseType);
            Assert.Equal([typeof(IKeyFrame)], keyFrame.GetInterfaces());

            ConstructorInfo[] constructors = keyFrame.GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.Equal(3, constructors.Length);
            Assert.All(constructors, constructor => Assert.True(constructor.IsFamily));

            foreach (string fieldName in new[] { "ValueProperty", "KeyTimeProperty" })
            {
                FieldInfo field = keyFrame.GetField(
                    fieldName,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!;
                Assert.NotNull(field);
                Assert.Equal(keyFrame, ((DependencyProperty)field.GetValue(null)!).OwnerType);
            }

            Assert.Null(keyFrame.GetProperty(
                "TypedValue",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            Assert.Null(keyFrame.GetMethod(
                "OnFreezableChildPropertyChanged",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));

            Assert.False(collection.IsAbstract);
            Assert.False(collection.IsSealed);
            Assert.Equal(typeof(Freezable), collection.BaseType);
            Assert.Equal(
                expectedCollectionInterfaces.OrderBy(type => type.FullName),
                collection.GetInterfaces().OrderBy(type => type.FullName));
            Assert.DoesNotContain(collection.GetInterfaces(), type => type.IsGenericType);
        }
    }

    [Fact]
    public void NonWpfGenericKeyFrameHelpersAreNotExported()
    {
        HashSet<string> removedNames =
        [
            "Jalium.UI.Media.Animation.KeyFrame`1",
            "Jalium.UI.Media.Animation.KeyFrameAnimationTimeline`1",
            "Jalium.UI.Media.Animation.KeyFrameCollection`1",
            "Jalium.UI.Media.Animation.KeyFrameCollectionBase`1",
        ];

        Assert.DoesNotContain(
            typeof(IKeyFrame).Assembly.GetExportedTypes(),
            type => type.FullName is { } fullName && removedNames.Contains(fullName));
    }

    [Fact]
    public void InterpolateValueInvokesTheVirtualCoreAtBothEndpoints()
    {
        var keyFrame = new EndpointProbeDoubleKeyFrame();

        Assert.Equal(100d, keyFrame.InterpolateValue(7d, 0d));
        Assert.Equal(101d, keyFrame.InterpolateValue(7d, 1d));
        Assert.Equal(2, keyFrame.CallCount);
    }

    [Fact]
    public void ExplicitIListUsesWpfCastAndFrozenMutationSemantics()
    {
        var collection = new DoubleKeyFrameCollection();
        IList list = collection;

        Assert.Throws<InvalidCastException>(() => list.Add("not a key frame"));
        Assert.Throws<InvalidCastException>(() => list.Contains("not a key frame"));
        Assert.Throws<ArgumentNullException>(() => list.Add(null));

        collection.Freeze();
        Assert.Throws<InvalidOperationException>(() => list.Remove(null));
    }

    [Fact]
    public void NamedKeyFrameBasesAndEasingDependencyPropertiesMatchWpfShape()
    {
        Assert.True(typeof(DoubleKeyFrame).IsAbstract);
        Assert.True(typeof(StringKeyFrame).IsAbstract);
        Assert.True(typeof(Rotation3DKeyFrame).IsAbstract);
        Assert.Equal(typeof(DoubleKeyFrame), typeof(LinearDoubleKeyFrame).BaseType);
        Assert.Equal(typeof(StringKeyFrame), typeof(DiscreteStringKeyFrame).BaseType);
        Assert.Equal(typeof(Vector3DKeyFrame), typeof(SplineVector3DKeyFrame).BaseType);
        Assert.False(typeof(SplineRotation3DKeyFrame).IsSealed);
        Assert.NotNull(typeof(EasingDoubleKeyFrame).GetField(
            nameof(EasingDoubleKeyFrame.EasingFunctionProperty),
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.NotNull(typeof(EasingQuaternionKeyFrame).GetField(
            nameof(EasingQuaternionKeyFrame.UseShortestPathProperty),
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
    }

    [Fact]
    public void TypedCollectionsAreFreezableCloneableAndExposeNonGenericIList()
    {
        var collection = new DoubleKeyFrameCollection();
        var frame = new LinearDoubleKeyFrame(4, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1)));

        Assert.Equal(0, collection.Add(frame));
        Assert.Single(collection);
        Assert.Same(frame, collection[0]);
        Assert.IsAssignableFrom<IList>(collection);
        Assert.False(collection.IsReadOnly);
        Assert.False(collection.IsFixedSize);

        DoubleKeyFrameCollection clone = collection.Clone();
        Assert.NotSame(collection, clone);
        Assert.NotSame(collection[0], clone[0]);
        Assert.Equal(4, clone[0].Value);

        collection.Freeze();
        Assert.True(collection.IsFrozen);
        Assert.True(frame.IsFrozen);
        Assert.True(DoubleKeyFrameCollection.Empty.IsFrozen);
        Assert.Same(DoubleKeyFrameCollection.Empty, DoubleKeyFrameCollection.Empty);
    }

    [Fact]
    public void UsingKeyFramesSupportsReplacementMarkupCloneAndRecursiveFreeze()
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        var replacement = new DoubleKeyFrameCollection();
        animation.KeyFrames = replacement;

        Assert.Same(replacement, animation.KeyFrames);
        Assert.False(animation.ShouldSerializeKeyFrames());
        ((IAddChild)animation).AddChild(new LinearDoubleKeyFrame(8));
        Assert.True(animation.ShouldSerializeKeyFrames());
        Assert.Throws<InvalidOperationException>(() => ((IAddChild)animation).AddText("text"));

        var interfaceView = (IKeyFrameAnimation)animation;
        Assert.Same(replacement, interfaceView.KeyFrames);

        DoubleAnimationUsingKeyFrames clone = animation.Clone();
        Assert.NotSame(animation.KeyFrames, clone.KeyFrames);
        Assert.NotSame(animation.KeyFrames[0], clone.KeyFrames[0]);

        animation.Freeze();
        Assert.True(animation.KeyFrames.IsFrozen);
        Assert.True(animation.KeyFrames[0].IsFrozen);
    }

    [Fact]
    public void StringAndThicknessKeyFramesExposeTheMissingWpfContracts()
    {
        var strings = new StringAnimationUsingKeyFrames();
        strings.KeyFrames.Add(new DiscreteStringKeyFrame("done", KeyTime.Paced));
        Assert.IsType<StringKeyFrameCollection>(strings.KeyFrames);
        Assert.Equal("done", strings.KeyFrames[0].Value);

        var easing = new PowerEase();
        var thickness = new EasingThicknessKeyFrame(new Thickness(2), KeyTime.Uniform, easing);
        Assert.Same(easing, thickness.EasingFunction);
        Assert.Same(EasingThicknessKeyFrame.EasingFunctionProperty,
            typeof(EasingThicknessKeyFrame).GetField(nameof(EasingThicknessKeyFrame.EasingFunctionProperty))!.GetValue(null));
    }

    private sealed class EndpointProbeDoubleKeyFrame : DoubleKeyFrame
    {
        internal int CallCount { get; private set; }

        protected override double InterpolateValueCore(double baseValue, double keyFrameProgress)
        {
            CallCount++;
            return 100d + keyFrameProgress;
        }

        protected override Freezable CreateInstanceCore() => new EndpointProbeDoubleKeyFrame();
    }
}
