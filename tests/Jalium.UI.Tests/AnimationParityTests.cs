using System.Diagnostics;
using System.Reflection;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Tests;

public class AnimationCompositionPropertyParityTests
{
    private static readonly Type[] s_compositionTypes =
    [
        typeof(ByteAnimation),
        typeof(ColorAnimation),
        typeof(ColorAnimationUsingKeyFrames),
        typeof(DecimalAnimation),
        typeof(DoubleAnimation),
        typeof(DoubleAnimationUsingKeyFrames),
        typeof(Int16Animation),
        typeof(Int32Animation),
        typeof(Int64Animation),
        typeof(Point3DAnimation),
        typeof(Point3DAnimationUsingKeyFrames),
        typeof(PointAnimation),
        typeof(PointAnimationUsingKeyFrames),
        typeof(QuaternionAnimation),
        typeof(QuaternionAnimationUsingKeyFrames),
        typeof(RectAnimation),
        typeof(Rotation3DAnimation),
        typeof(Rotation3DAnimationUsingKeyFrames),
        typeof(SingleAnimation),
        typeof(SizeAnimation),
        typeof(ThicknessAnimation),
        typeof(ThicknessAnimationUsingKeyFrames),
        typeof(Vector3DAnimation),
        typeof(Vector3DAnimationUsingKeyFrames),
        typeof(VectorAnimation),
    ];

    [Fact]
    public void AnimationTimeline_DeclaresSharedCompositionDependencyPropertiesOnly()
    {
        const BindingFlags staticDeclared = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        const BindingFlags instanceDeclared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var additiveField = typeof(AnimationTimeline).GetField(nameof(AnimationTimeline.IsAdditiveProperty), staticDeclared);
        var cumulativeField = typeof(AnimationTimeline).GetField(nameof(AnimationTimeline.IsCumulativeProperty), staticDeclared);

        Assert.Same(AnimationTimeline.IsAdditiveProperty, additiveField?.GetValue(null));
        Assert.Same(AnimationTimeline.IsCumulativeProperty, cumulativeField?.GetValue(null));
        Assert.Equal(typeof(bool), AnimationTimeline.IsAdditiveProperty.PropertyType);
        Assert.Equal(typeof(bool), AnimationTimeline.IsCumulativeProperty.PropertyType);
        Assert.Equal(typeof(AnimationTimeline), AnimationTimeline.IsAdditiveProperty.OwnerType);
        Assert.Equal(typeof(AnimationTimeline), AnimationTimeline.IsCumulativeProperty.OwnerType);
        Assert.Equal(false, AnimationTimeline.IsAdditiveProperty.DefaultMetadata.DefaultValue);
        Assert.Equal(false, AnimationTimeline.IsCumulativeProperty.DefaultMetadata.DefaultValue);

        Assert.Null(typeof(AnimationTimeline).GetProperty("IsAdditive", instanceDeclared));
        Assert.Null(typeof(AnimationTimeline).GetProperty("IsCumulative", instanceDeclared));
        Assert.Null(typeof(TypedAnimationTimeline<double>).GetProperty("IsAdditive", instanceDeclared));
        Assert.Null(typeof(TypedAnimationTimeline<double>).GetProperty("IsCumulative", instanceDeclared));
    }

    [Fact]
    public void CsvCompositionTypes_DeclareReadWriteDpBackedWrappers()
    {
        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        foreach (var type in s_compositionTypes)
        {
            var additive = type.GetProperty("IsAdditive", declared);
            var cumulative = type.GetProperty("IsCumulative", declared);
            Assert.NotNull(additive);
            Assert.NotNull(cumulative);
            Assert.True(additive!.CanRead && additive.CanWrite, type.FullName);
            Assert.True(cumulative!.CanRead && cumulative.CanWrite, type.FullName);

            var animation = Assert.IsAssignableFrom<AnimationTimeline>(Activator.CreateInstance(type));
            additive.SetValue(animation, true);
            cumulative.SetValue(animation, true);
            Assert.Equal(true, animation.GetValue(AnimationTimeline.IsAdditiveProperty));
            Assert.Equal(true, animation.GetValue(AnimationTimeline.IsCumulativeProperty));
        }
    }

    [Fact]
    public void PathAnimations_UseSharedDp_AndMatrixDoesNotDeclareIsCumulative()
    {
        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        foreach (var type in new[] { typeof(DoubleAnimationUsingPath), typeof(PointAnimationUsingPath) })
        {
            var animation = Assert.IsAssignableFrom<AnimationTimeline>(Activator.CreateInstance(type));
            type.GetProperty("IsAdditive", declared)!.SetValue(animation, true);
            type.GetProperty("IsCumulative", declared)!.SetValue(animation, true);
            Assert.Equal(true, animation.GetValue(AnimationTimeline.IsAdditiveProperty));
            Assert.Equal(true, animation.GetValue(AnimationTimeline.IsCumulativeProperty));
        }

        var matrix = new MatrixAnimationUsingPath { IsAdditive = true };
        Assert.Equal(true, matrix.GetValue(AnimationTimeline.IsAdditiveProperty));
        Assert.NotNull(typeof(MatrixAnimationUsingPath).GetProperty("IsAdditive", declared));
        Assert.Null(typeof(MatrixAnimationUsingPath).GetProperty("IsCumulative", declared));
        Assert.Equal(typeof(bool), typeof(MatrixAnimationUsingPath).GetProperty(nameof(MatrixAnimationUsingPath.IsOffsetCumulative))!.PropertyType);
        Assert.Equal(typeof(bool), typeof(MatrixAnimationUsingPath).GetProperty(nameof(MatrixAnimationUsingPath.IsAngleCumulative))!.PropertyType);
    }
}

public class SimpleAnimationByParityTests
{
    private static readonly (Type Type, Type ValueType)[] s_byTypes =
    [
        (typeof(ByteAnimation), typeof(byte?)),
        (typeof(ColorAnimation), typeof(Color?)),
        (typeof(Int16Animation), typeof(short?)),
        (typeof(PointAnimation), typeof(Point?)),
        (typeof(RectAnimation), typeof(Rect?)),
        (typeof(SizeAnimation), typeof(Size?)),
        (typeof(ThicknessAnimation), typeof(Thickness?)),
        (typeof(VectorAnimation), typeof(Vector?)),
    ];

    [Fact]
    public void EightSimpleAnimations_DeclareByPropertyAndClrWrapper()
    {
        const BindingFlags instanceDeclared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        const BindingFlags staticDeclared = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var (type, valueType) in s_byTypes)
        {
            var property = type.GetProperty("By", instanceDeclared);
            var field = type.GetField("ByProperty", staticDeclared);
            var dependencyProperty = Assert.IsType<DependencyProperty>(field?.GetValue(null));

            Assert.NotNull(property);
            Assert.True(property!.CanRead && property.CanWrite, type.FullName);
            Assert.Equal(valueType, property.PropertyType);
            Assert.Equal(valueType, dependencyProperty.PropertyType);
            Assert.Equal(type, dependencyProperty.OwnerType);
        }
    }

    [Fact]
    public void ByParticipatesInBasicValueEvaluation()
    {
        var point = new PointAnimation
        {
            From = new Point(10, 20),
            By = new Point(4, 8),
            Duration = TimeSpan.FromSeconds(1),
        };
        var pointClock = MidpointClock(point);
        Assert.Equal(new Point(12, 24), point.GetCurrentValue(new Point(), new Point(), pointClock));

        var number = new ByteAnimation
        {
            From = 10,
            By = 20,
            Duration = TimeSpan.FromSeconds(1),
        };
        var numberClock = MidpointClock(number);
        Assert.Equal((byte)20, number.GetCurrentValue((byte)0, (byte)0, numberClock));
    }

    private static AnimationClock MidpointClock(AnimationTimeline timeline)
    {
        const long t0 = 1_000_000_000;
        var clock = new AnimationClock(timeline);
        clock.BeginAt(t0);
        clock.Tick(t0 + Stopwatch.Frequency / 2);
        return clock;
    }
}

public class FromToByCompositionParityTests
{
    private const long T0 = 1_000_000_000;

    public enum AnimationMode
    {
        Automatic,
        From,
        To,
        By,
        FromTo,
        FromBy,
        ToBy,
        FromToBy,
    }

    [Theory]
    [InlineData(AnimationMode.Automatic, 12.5)]
    [InlineData(AnimationMode.From, 8.0)]
    [InlineData(AnimationMode.To, 12.0)]
    [InlineData(AnimationMode.By, 12.0)]
    [InlineData(AnimationMode.FromTo, 6.0)]
    [InlineData(AnimationMode.FromBy, 6.0)]
    [InlineData(AnimationMode.ToBy, 12.0)]
    [InlineData(AnimationMode.FromToBy, 6.0)]
    public void DoubleAnimation_UsesWpfFromToByPrecedence(AnimationMode mode, double expected)
    {
        var animation = new DoubleAnimation { Duration = TimeSpan.FromSeconds(1) };
        switch (mode)
        {
            case AnimationMode.From:
                animation.From = 4;
                break;
            case AnimationMode.To:
                animation.To = 18;
                break;
            case AnimationMode.By:
                animation.By = 8;
                break;
            case AnimationMode.FromTo:
                animation.From = 4;
                animation.To = 12;
                break;
            case AnimationMode.FromBy:
                animation.From = 4;
                animation.By = 8;
                break;
            case AnimationMode.ToBy:
                animation.To = 18;
                animation.By = 100;
                break;
            case AnimationMode.FromToBy:
                animation.From = 4;
                animation.To = 12;
                animation.By = 100;
                break;
        }

        Assert.Equal(expected, Evaluate(animation, 10d, 20d, 0.25), 6);
    }

    [Fact]
    public void AdditiveFoundation_MatchesWpfForEachAnimationMode()
    {
        var fromTo = new DoubleAnimation
        {
            From = 4,
            To = 12,
            IsAdditive = true,
            Duration = TimeSpan.FromSeconds(1),
        };
        var fromBy = new DoubleAnimation
        {
            From = 4,
            By = 8,
            IsAdditive = true,
            Duration = TimeSpan.FromSeconds(1),
        };
        var by = new DoubleAnimation
        {
            By = 8,
            IsAdditive = true,
            Duration = TimeSpan.FromSeconds(1),
        };
        var to = new DoubleAnimation
        {
            To = 18,
            IsAdditive = true,
            Duration = TimeSpan.FromSeconds(1),
        };

        Assert.Equal(16, Evaluate(fromTo, 10d, 20d, 0.25), 6);
        Assert.Equal(16, Evaluate(fromBy, 10d, 20d, 0.25), 6);
        Assert.Equal(12, Evaluate(by, 10d, 20d, 0.25), 6);
        Assert.Equal(12, Evaluate(to, 10d, 20d, 0.25), 6);
    }

    [Fact]
    public void CumulativeFromTo_UsesDeltaAndCurrentIteration()
    {
        var animation = new DoubleAnimation
        {
            From = 4,
            To = 12,
            IsCumulative = true,
            RepeatBehavior = RepeatBehavior.Forever,
            Duration = TimeSpan.FromSeconds(1),
        };

        Assert.Equal(22, Evaluate(animation, 10d, 20d, 2.25), 6);

        animation.IsAdditive = true;
        Assert.Equal(32, Evaluate(animation, 10d, 20d, 2.25), 6);
    }

    [Fact]
    public void CumulativeBy_PreservesAutomaticOriginFoundation()
    {
        var animation = new DoubleAnimation
        {
            By = 8,
            IsCumulative = true,
            RepeatBehavior = RepeatBehavior.Forever,
            Duration = TimeSpan.FromSeconds(1),
        };

        Assert.Equal(28, Evaluate(animation, 10d, 20d, 2.25), 6);
    }

    [Fact]
    public void ScalarTypes_ShareAdditiveAndCumulativeSemantics()
    {
        var single = new SingleAnimation
        {
            From = 1,
            To = 5,
            IsAdditive = true,
            IsCumulative = true,
            RepeatBehavior = RepeatBehavior.Forever,
            Duration = TimeSpan.FromSeconds(1),
        };
        var decimalAnimation = new DecimalAnimation
        {
            From = 1,
            To = 5,
            IsAdditive = true,
            IsCumulative = true,
            RepeatBehavior = RepeatBehavior.Forever,
            Duration = TimeSpan.FromSeconds(1),
        };
        var thickness = new ThicknessAnimation
        {
            From = new Thickness(1),
            To = new Thickness(5),
            IsAdditive = true,
            IsCumulative = true,
            RepeatBehavior = RepeatBehavior.Forever,
            Duration = TimeSpan.FromSeconds(1),
        };

        Assert.Equal(17f, Evaluate(single, 10f, 20f, 1.5));
        Assert.Equal(17m, Evaluate(decimalAnimation, 10m, 20m, 1.5));
        Assert.Equal(new Thickness(17), Evaluate(thickness, new Thickness(10), default(Thickness), 1.5));
    }

    [Fact]
    public void TwoAndThreeDimensionalTypes_ShareComponentWiseComposition()
    {
        var point = new PointAnimation
        {
            From = new Point(1, 2),
            To = new Point(5, 10),
            IsAdditive = true,
            IsCumulative = true,
            RepeatBehavior = RepeatBehavior.Forever,
            Duration = TimeSpan.FromSeconds(1),
        };
        var vector = new VectorAnimation
        {
            From = new Vector(1, 2),
            To = new Vector(5, 10),
            IsAdditive = true,
            IsCumulative = true,
            RepeatBehavior = RepeatBehavior.Forever,
            Duration = TimeSpan.FromSeconds(1),
        };
        var point3D = new Point3DAnimation
        {
            From = new Point3D(1, 2, 3),
            To = new Point3D(5, 10, 15),
            IsAdditive = true,
            IsCumulative = true,
            RepeatBehavior = RepeatBehavior.Forever,
            Duration = TimeSpan.FromSeconds(1),
        };
        var vector3D = new Vector3DAnimation
        {
            From = new Vector3D(1, 2, 3),
            To = new Vector3D(5, 10, 15),
            IsAdditive = true,
            IsCumulative = true,
            RepeatBehavior = RepeatBehavior.Forever,
            Duration = TimeSpan.FromSeconds(1),
        };

        Assert.Equal(new Point(107, 214), Evaluate(point, new Point(100, 200), default(Point), 1.5));
        Assert.Equal(new Vector(107, 214), Evaluate(vector, new Vector(100, 200), default(Vector), 1.5));
        Assert.Equal(new Point3D(107, 214, 321), Evaluate(point3D, new Point3D(100, 200, 300), default(Point3D), 1.5));
        Assert.Equal(new Vector3D(107, 214, 321), Evaluate(vector3D, new Vector3D(100, 200, 300), default(Vector3D), 1.5));
    }

    [Fact]
    public void IntegerInterpolation_UsesWpfRoundingRules()
    {
        var positive = new Int32Animation { From = 0, To = 1, Duration = TimeSpan.FromSeconds(1) };
        var negative = new Int32Animation { From = 0, To = -1, Duration = TimeSpan.FromSeconds(1) };
        var shortAnimation = new Int16Animation { From = 0, To = 1, Duration = TimeSpan.FromSeconds(1) };
        var longAnimation = new Int64Animation { From = 0, To = -1, Duration = TimeSpan.FromSeconds(1) };
        var byteAnimation = new ByteAnimation { From = 10, To = 9, Duration = TimeSpan.FromSeconds(1) };

        Assert.Equal(1, Evaluate(positive, 0, 0, 0.5));
        Assert.Equal(-1, Evaluate(negative, 0, 0, 0.5));
        Assert.Equal((short)1, Evaluate(shortAnimation, (short)0, (short)0, 0.5));
        Assert.Equal(-1L, Evaluate(longAnimation, 0L, 0L, 0.5));
        Assert.Equal((byte)10, Evaluate(byteAnimation, (byte)0, (byte)0, 0.5));
    }

    [Fact]
    public void ByteByArithmetic_UsesWpfUncheckedAdditionAndInterpolation()
    {
        var animation = new ByteAnimation
        {
            From = 250,
            By = 10,
            Duration = TimeSpan.FromSeconds(1),
        };

        Assert.Equal((byte)5, Evaluate(animation, (byte)0, (byte)0, 1));
    }

    private static T Evaluate<T>(AnimationTimeline animation, T origin, T destination, double seconds)
    {
        var clock = new AnimationClock(animation);
        clock.BeginAt(T0);
        clock.Tick(T0 + (long)(seconds * Stopwatch.Frequency));
        return (T)animation.GetCurrentValue(origin!, destination!, clock);
    }
}

public class KeyFrameCompositionParityTests
{
    private const long T0 = 1_000_000_000;

    [Fact]
    public void EmptyKeyFrameCollection_ReturnsSuggestedDestination()
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(1),
        };

        Assert.Equal(20, Evaluate(animation, 10d, 20d, 0.5));
    }

    [Fact]
    public void FirstKeyFrameAfterZero_UsesOriginOrZeroAccordingToIsAdditive()
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(2),
        };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(20, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1))));

        Assert.Equal(15, Evaluate(animation, 10d, 20d, 0.5), 6);

        animation.IsAdditive = true;
        Assert.Equal(20, Evaluate(animation, 10d, 20d, 0.5), 6);
    }

    [Fact]
    public void CumulativeKeyFrames_AddFinalValueForEachCompletedIteration()
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(1),
            RepeatBehavior = RepeatBehavior.Forever,
            IsCumulative = true,
        };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(2, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(4, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1))));

        Assert.Equal(11, Evaluate(animation, 10d, 20d, 2.5), 6);

        animation.IsAdditive = true;
        Assert.Equal(21, Evaluate(animation, 10d, 20d, 2.5), 6);
    }

    [Fact]
    public void PointKeyFrames_UseSharedAdditiveAndCumulativeOperations()
    {
        var animation = new PointAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(1),
            RepeatBehavior = RepeatBehavior.Forever,
            IsAdditive = true,
            IsCumulative = true,
        };
        animation.KeyFrames.Add(new LinearPointKeyFrame(new Point(1, 2), KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearPointKeyFrame(new Point(3, 6), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1))));

        Assert.Equal(new Point(15, 30), Evaluate(animation, new Point(10, 20), default(Point), 1.5));
    }

    [Fact]
    public void ThicknessKeyFrames_UseSharedAdditiveAndCumulativeOperations()
    {
        var animation = new ThicknessAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(1),
            RepeatBehavior = RepeatBehavior.Forever,
            IsAdditive = true,
            IsCumulative = true,
        };
        animation.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(1), KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(3), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1))));

        Assert.Equal(new Thickness(15), Evaluate(animation, new Thickness(10), default(Thickness), 1.5));
    }

    [Fact]
    public void ThreeDimensionalKeyFrames_UseSharedCompositionOperations()
    {
        var point = new Point3DAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(1),
            RepeatBehavior = RepeatBehavior.Forever,
            IsAdditive = true,
            IsCumulative = true,
        };
        point.KeyFrames.Add(new LinearPoint3DKeyFrame(new Point3D(1, 2, 3), KeyTime.FromTimeSpan(TimeSpan.Zero)));
        point.KeyFrames.Add(new LinearPoint3DKeyFrame(new Point3D(3, 6, 9), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1))));

        var vector = new Vector3DAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(1),
            RepeatBehavior = RepeatBehavior.Forever,
            IsAdditive = true,
            IsCumulative = true,
        };
        vector.KeyFrames.Add(new LinearVector3DKeyFrame(new Vector3D(1, 2, 3), KeyTime.FromTimeSpan(TimeSpan.Zero)));
        vector.KeyFrames.Add(new LinearVector3DKeyFrame(new Vector3D(3, 6, 9), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1))));

        Assert.Equal(new Point3D(15, 30, 45), Evaluate(point, new Point3D(10, 20, 30), default(Point3D), 1.5));
        Assert.Equal(new Vector3D(15, 30, 45), Evaluate(vector, new Vector3D(10, 20, 30), default(Vector3D), 1.5));
    }

    private static T Evaluate<T>(AnimationTimeline animation, T origin, T destination, double seconds)
    {
        var clock = new AnimationClock(animation);
        clock.BeginAt(T0);
        clock.Tick(T0 + (long)(seconds * Stopwatch.Frequency));
        return (T)animation.GetCurrentValue(origin!, destination!, clock);
    }
}

public class AnimationClockParityTests
{
    private const long T0 = 1_000_000_000;

    private static long Ticks(double seconds) => (long)(seconds * Stopwatch.Frequency);

    private static AnimationClock CreateClock(
        RepeatBehavior repeatBehavior,
        bool autoReverse = false,
        FillBehavior fillBehavior = FillBehavior.HoldEnd,
        TimeSpan? beginTime = null)
    {
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromSeconds(1),
            RepeatBehavior = repeatBehavior,
            AutoReverse = autoReverse,
            FillBehavior = fillBehavior,
            BeginTime = beginTime ?? TimeSpan.Zero,
        };

        var clock = new AnimationClock(animation);
        clock.BeginAt(T0);
        return clock;
    }

    [Fact]
    public void MultiIterationFrameJump_PreservesRemainderAndIteration()
    {
        var clock = CreateClock(RepeatBehavior.Forever);

        clock.Tick(T0 + Ticks(2.25));

        Assert.Equal(3, clock.CurrentIteration);
        Assert.Equal(TimeSpan.FromSeconds(0.25), clock.CurrentTime);
        Assert.Equal(0.25, clock.CurrentProgress, 6);
    }

    [Fact]
    public void NonTerminalExactBoundary_StartsTheNextIteration()
    {
        var clock = CreateClock(RepeatBehavior.Forever);

        clock.Tick(T0 + Ticks(2));

        Assert.Equal(3, clock.CurrentIteration);
        Assert.Equal(TimeSpan.Zero, clock.CurrentTime);
        Assert.Equal(0, clock.CurrentProgress);
    }

    [Fact]
    public void FractionalRepeatCount_HoldsAtItsFractionalTerminalProgress()
    {
        var clock = CreateClock(new RepeatBehavior(2.5));

        clock.Tick(T0 + Ticks(3));

        Assert.Equal(3, clock.CurrentIteration);
        Assert.Equal(TimeSpan.FromSeconds(0.5), clock.CurrentTime);
        Assert.Equal(0.5, clock.CurrentProgress, 6);
        Assert.True(((IAnimationClock)clock).IsCompleted);
    }

    [Fact]
    public void RepeatDuration_HoldsAtItsFractionalTerminalProgress()
    {
        var clock = CreateClock(new RepeatBehavior(TimeSpan.FromSeconds(2.5)));

        clock.Tick(T0 + Ticks(10));

        Assert.Equal(3, clock.CurrentIteration);
        Assert.Equal(0.5, clock.CurrentProgress, 6);
        Assert.True(((IAnimationClock)clock).IsCompleted);
    }

    [Fact]
    public void AutoReverse_UsesAForwardAndReverseLegPerIteration()
    {
        var clock = CreateClock(new RepeatBehavior(2), autoReverse: true);

        clock.Tick(T0 + Ticks(1.25));
        Assert.Equal(1, clock.CurrentIteration);
        Assert.Equal(0.75, clock.CurrentProgress, 6);

        clock.Tick(T0 + Ticks(2.25));
        Assert.Equal(2, clock.CurrentIteration);
        Assert.Equal(0.25, clock.CurrentProgress, 6);

        clock.Tick(T0 + Ticks(5));
        Assert.Equal(2, clock.CurrentIteration);
        Assert.Equal(0, clock.CurrentProgress, 6);
        Assert.True(((IAnimationClock)clock).IsCompleted);
    }

    [Fact]
    public void BeginTime_BeforeActivationHasNoIterationOrCurrentTime()
    {
        var clock = CreateClock(new RepeatBehavior(1), beginTime: TimeSpan.FromSeconds(1));

        clock.Tick(T0 + Ticks(0.5));

        Assert.Null(clock.CurrentIteration);
        Assert.Null(clock.CurrentTime);
        Assert.Equal(0, clock.CurrentProgress);
    }
}
