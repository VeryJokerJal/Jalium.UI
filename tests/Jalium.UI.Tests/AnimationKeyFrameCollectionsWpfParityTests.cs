using System.Collections;
using System.Reflection;
using Jalium.UI.Markup;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Tests;

public sealed class AnimationKeyFrameCollectionsWpfParityTests
{
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
}
