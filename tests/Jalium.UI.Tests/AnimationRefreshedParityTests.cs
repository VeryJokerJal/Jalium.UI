using System.Reflection;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Tests;

public sealed class AnimationRefreshedParityTests
{
    [Fact]
    public void ThreeDimensionalSimpleAnimationsExposeWpfConstructorsAndCloneShape()
    {
        (Type Type, Type ValueType)[] cases =
        [
            (typeof(Point3DAnimation), typeof(Point3D)),
            (typeof(QuaternionAnimation), typeof(Quaternion)),
            (typeof(Rotation3DAnimation), typeof(Rotation3D)),
            (typeof(Vector3DAnimation), typeof(Vector3D)),
        ];

        foreach ((Type type, Type valueType) in cases)
        {
            Assert.NotNull(type.GetConstructor([valueType, typeof(Duration), typeof(FillBehavior)]));
            Assert.NotNull(type.GetConstructor([valueType, valueType, typeof(Duration), typeof(FillBehavior)]));

            MethodInfo clone = type.GetMethod(
                nameof(Freezable.Clone),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!;
            Assert.Equal(type, clone.ReturnType);

            MethodInfo createInstanceCore = type.GetMethod(
                "CreateInstanceCore",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
            Assert.Equal(typeof(Freezable), createInstanceCore.ReturnType);
        }

        Assert.False(typeof(Vector3DAnimation).IsSealed);
    }

    [Fact]
    public void LegacyKeyFrameCollectionsDeclareThePublicCollectionProperties()
    {
        Type[] collectionTypes =
        [
            typeof(ColorKeyFrameCollection),
            typeof(ObjectKeyFrameCollection),
            typeof(Point3DKeyFrameCollection),
            typeof(PointKeyFrameCollection),
            typeof(QuaternionKeyFrameCollection),
            typeof(Rotation3DKeyFrameCollection),
            typeof(ThicknessKeyFrameCollection),
            typeof(Vector3DKeyFrameCollection),
        ];

        foreach (Type collectionType in collectionTypes)
        {
            foreach (string propertyName in new[] { "IsFixedSize", "IsReadOnly", "IsSynchronized", "SyncRoot" })
            {
                Assert.NotNull(collectionType.GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
            }
        }
    }

    [Fact]
    public void KeyFrameAnimationCloneCoresUseTheWpfParameterMetadata()
    {
        Type[] animationTypes =
        [
            typeof(BooleanAnimationUsingKeyFrames),
            typeof(CharAnimationUsingKeyFrames),
            typeof(ColorAnimationUsingKeyFrames),
            typeof(DoubleAnimationUsingKeyFrames),
            typeof(ObjectAnimationUsingKeyFrames),
            typeof(Point3DAnimationUsingKeyFrames),
            typeof(PointAnimationUsingKeyFrames),
            typeof(QuaternionAnimationUsingKeyFrames),
            typeof(Rotation3DAnimationUsingKeyFrames),
            typeof(StringAnimationUsingKeyFrames),
            typeof(ThicknessAnimationUsingKeyFrames),
            typeof(Vector3DAnimationUsingKeyFrames),
        ];

        foreach (Type animationType in animationTypes)
        {
            foreach (string methodName in new[] { "CloneCore", "CloneCurrentValueCore" })
            {
                MethodInfo method = animationType.GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
                Assert.Equal("sourceFreezable", Assert.Single(method.GetParameters()).Name);
            }
        }

        foreach (Type animationType in new[]
                 {
                     typeof(BooleanAnimationUsingKeyFrames),
                     typeof(CharAnimationUsingKeyFrames),
                     typeof(StringAnimationUsingKeyFrames),
                 })
        {
            MethodInfo method = animationType.GetMethod(
                "GetCurrentValueCore",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
            Assert.Equal(
                new[] { "defaultOriginValue", "defaultDestinationValue", "animationClock" },
                method.GetParameters().Select(static parameter => parameter.Name));
        }
    }

    [Fact]
    public void KeyTimeSplineAndTimelineCollectionHaveWpfTypeShape()
    {
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(KeyTimeType)));
        Assert.False(typeof(KeySpline).IsSealed);
        Assert.True(typeof(IFormattable).IsAssignableFrom(typeof(KeySpline)));
        Assert.True(typeof(TimelineCollection).IsSealed);
        Assert.DoesNotContain(typeof(IReadOnlyList<Timeline>), typeof(TimelineCollection).GetInterfaces());
        Assert.Empty(typeof(Storyboard).GetInterfaces().Except(typeof(ParallelTimeline).GetInterfaces()));
    }

    [Fact]
    public void StoryboardTriggerActionsLiveInAnimationAndExposeTheirContracts()
    {
        Type[] actionTypes =
        [
            typeof(BeginStoryboard),
            typeof(ControllableStoryboardAction),
            typeof(PauseStoryboard),
            typeof(RemoveStoryboard),
            typeof(ResumeStoryboard),
            typeof(SeekStoryboard),
            typeof(SetStoryboardSpeedRatio),
            typeof(SkipStoryboardToFill),
            typeof(StopStoryboard),
        ];

        Assert.All(actionTypes, static type => Assert.Equal("Jalium.UI.Media.Animation", type.Namespace));
        Assert.Equal(typeof(Storyboard), BeginStoryboard.StoryboardProperty.PropertyType);
        Assert.Equal(typeof(BeginStoryboard), BeginStoryboard.StoryboardProperty.OwnerType);

        var storyboard = new Storyboard();
        var begin = new BeginStoryboard { Storyboard = storyboard };
        Assert.Same(storyboard, begin.Storyboard);

        var seek = new SeekStoryboard();
        Assert.False(seek.ShouldSerializeOffset());
        seek.Offset = TimeSpan.FromMilliseconds(1);
        Assert.True(seek.ShouldSerializeOffset());

        ConstructorInfo constructor = Assert.Single(typeof(ControllableStoryboardAction).GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.True(constructor.IsAssembly);
    }
}
