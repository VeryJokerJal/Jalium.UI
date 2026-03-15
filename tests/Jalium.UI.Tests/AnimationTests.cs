using Jalium.UI;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

public class AnimationTests
{
    #region Animation Value Precedence Tests

    [Fact]
    public void AnimatedValue_TakesPrecedence_OverLocalValue()
    {
        // Arrange
        var element = new TestElement();
        element.SetValue(TestElement.TestDoubleProperty, 0.5);

        // Act - Set animated value
        element.SetAnimatedValue(TestElement.TestDoubleProperty, 0.8, holdEndValue: true);

        // Assert - Animated value should be returned
        var value = (double)element.GetValue(TestElement.TestDoubleProperty)!;
        Assert.Equal(0.8, value);
    }

    [Fact]
    public void ClearAnimatedValue_WithHoldEnd_KeepsValue()
    {
        // Arrange
        var element = new TestElement();
        element.SetValue(TestElement.TestDoubleProperty, 0.5);
        element.SetAnimatedValue(TestElement.TestDoubleProperty, 1.0, holdEndValue: true);

        // Act
        element.ClearAnimatedValue(TestElement.TestDoubleProperty);

        // Assert - Value should be held at 1.0
        var value = (double)element.GetValue(TestElement.TestDoubleProperty)!;
        Assert.Equal(1.0, value);
    }

    [Fact]
    public void ClearAnimatedValue_WithStop_RestoresBaseValue()
    {
        // Arrange
        var element = new TestElement();
        element.SetValue(TestElement.TestDoubleProperty, 0.5);
        element.SetAnimatedValue(TestElement.TestDoubleProperty, 1.0, holdEndValue: false);

        // Act
        element.ClearAnimatedValue(TestElement.TestDoubleProperty);

        // Assert - Should restore to 0.5
        var value = (double)element.GetValue(TestElement.TestDoubleProperty)!;
        Assert.Equal(0.5, value);
    }

    [Fact]
    public void HasAnimatedValue_ReturnsTrue_WhenAnimated()
    {
        // Arrange
        var element = new TestElement();
        element.SetAnimatedValue(TestElement.TestDoubleProperty, 1.0, holdEndValue: true);

        // Assert
        Assert.True(element.HasAnimatedValue(TestElement.TestDoubleProperty));
    }

    [Fact]
    public void HasAnimatedValue_ReturnsFalse_WhenNotAnimated()
    {
        // Arrange
        var element = new TestElement();

        // Assert
        Assert.False(element.HasAnimatedValue(TestElement.TestDoubleProperty));
    }

    [Fact]
    public void GetAnimationBaseValue_ReturnsLocalValue_WhenAnimated()
    {
        // Arrange
        var element = new TestElement();
        element.SetValue(TestElement.TestDoubleProperty, 0.5);
        element.SetAnimatedValue(TestElement.TestDoubleProperty, 1.0, holdEndValue: true);

        // Act
        var baseValue = (double)element.GetAnimationBaseValue(TestElement.TestDoubleProperty)!;

        // Assert
        Assert.Equal(0.5, baseValue);
    }

    #endregion

    #region DoubleAnimation Tests

    [Fact]
    public void DoubleAnimation_HasCorrectTargetPropertyType()
    {
        // Arrange
        var animation = new DoubleAnimation();

        // Assert
        Assert.Equal(typeof(double), animation.TargetPropertyType);
    }

    [Fact]
    public void DoubleAnimation_FromTo_AreSetCorrectly()
    {
        // Arrange
        var animation = new DoubleAnimation
        {
            From = 0.0,
            To = 100.0,
            Duration = TimeSpan.FromSeconds(1)
        };

        // Assert
        Assert.Equal(0.0, animation.From);
        Assert.Equal(100.0, animation.To);
        Assert.Equal(TimeSpan.FromSeconds(1), animation.Duration.TimeSpan);
    }

    [Fact]
    public void DoubleAnimation_WithByValue_SetsValueCorrectly()
    {
        // Arrange
        var animation = new DoubleAnimation
        {
            From = 10.0,
            By = 50.0,
            Duration = TimeSpan.FromSeconds(1)
        };

        // Assert
        Assert.Equal(10.0, animation.From);
        Assert.Equal(50.0, animation.By);
    }

    [Fact]
    public void DoubleAnimation_EasingFunction_IsSet()
    {
        // Arrange
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var animation = new DoubleAnimation
        {
            From = 0.0,
            To = 100.0,
            EasingFunction = easing
        };

        // Assert
        Assert.Same(easing, animation.EasingFunction);
    }

    #endregion

    #region AnimationClock Tests

    [Fact]
    public void AnimationClock_Begin_SetsIsRunningTrue()
    {
        // Arrange
        var animation = new DoubleAnimation { Duration = TimeSpan.FromSeconds(1) };
        var clock = new AnimationClock(animation);

        // Act
        clock.Begin();

        // Assert
        Assert.True(clock.IsRunning);
    }

    [Fact]
    public void AnimationClock_Stop_SetsIsRunningFalse()
    {
        // Arrange
        var animation = new DoubleAnimation { Duration = TimeSpan.FromSeconds(1) };
        var clock = new AnimationClock(animation);
        clock.Begin();

        // Act
        clock.Stop();

        // Assert
        Assert.False(clock.IsRunning);
    }

    [Fact]
    public void AnimationClock_CurrentProgress_StartsAtZero()
    {
        // Arrange
        var animation = new DoubleAnimation { Duration = TimeSpan.FromSeconds(1) };
        var clock = new AnimationClock(animation);
        clock.Begin();

        // Assert
        Assert.Equal(0.0, clock.CurrentProgress);
    }

    #endregion

    #region IAnimationTimeline Interface Tests

    [Fact]
    public void AnimationTimeline_ImplementsIAnimationTimeline()
    {
        // Arrange
        var animation = new DoubleAnimation();

        // Assert
        Assert.IsAssignableFrom<IAnimationTimeline>(animation);
    }

    [Fact]
    public void AnimationTimeline_CreateClock_ReturnsValidClock()
    {
        // Arrange
        IAnimationTimeline animation = new DoubleAnimation();

        // Act
        var clock = animation.CreateClock();

        // Assert
        Assert.NotNull(clock);
        Assert.IsAssignableFrom<IAnimationClock>(clock);
    }

    [Fact]
    public void AnimationClock_ImplementsIAnimationClock()
    {
        // Arrange
        var animation = new DoubleAnimation();
        var clock = new AnimationClock(animation);

        // Assert
        Assert.IsAssignableFrom<IAnimationClock>(clock);
    }

    #endregion

    #region EasingFunction Tests

    [Fact]
    public void QuadraticEase_EaseIn_SquaresProgress()
    {
        // Arrange
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseIn };

        // Act & Assert
        Assert.Equal(0.0, easing.Ease(0.0));
        Assert.Equal(0.25, easing.Ease(0.5)); // 0.5^2 = 0.25
        Assert.Equal(1.0, easing.Ease(1.0));
    }

    [Fact]
    public void CubicEase_EaseIn_CubesProgress()
    {
        // Arrange
        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };

        // Act & Assert
        Assert.Equal(0.0, easing.Ease(0.0));
        Assert.Equal(0.125, easing.Ease(0.5)); // 0.5^3 = 0.125
        Assert.Equal(1.0, easing.Ease(1.0));
    }

    [Fact]
    public void LinearEase_ReturnsInputUnchanged()
    {
        // Arrange
        var easing = new LinearEase();

        // Act & Assert
        Assert.Equal(0.0, easing.Ease(0.0));
        Assert.Equal(0.5, easing.Ease(0.5));
        Assert.Equal(1.0, easing.Ease(1.0));
    }

    #endregion

    #region AnimationFactory Tests

    [Fact]
    public void AnimationFactory_CreatesDoubleAnimation()
    {
        // Act
        var animation = AnimationFactory.CreateAnimation(typeof(double), 0.0, 1.0, TimeSpan.FromSeconds(1));

        // Assert
        Assert.NotNull(animation);
        Assert.IsType<DoubleAnimation>(animation);
    }

    [Fact]
    public void AnimationFactory_CreatesColorAnimation()
    {
        // Act
        var animation = AnimationFactory.CreateAnimation(
            typeof(Color),
            Color.FromRgb(0, 0, 0),
            Color.FromRgb(255, 255, 255),
            TimeSpan.FromSeconds(1));

        // Assert
        Assert.NotNull(animation);
        Assert.IsType<ColorAnimation>(animation);
    }

    [Fact]
    public void AnimationFactory_CreatesRectAnimation()
    {
        var animation = AnimationFactory.CreateAnimation(
            typeof(Rect),
            new Rect(0, 0, 10, 10),
            new Rect(10, 10, 20, 20),
            TimeSpan.FromSeconds(1));

        Assert.NotNull(animation);
        Assert.IsType<RectAnimation>(animation);
    }

    [Fact]
    public void AnimationFactory_CreatesCornerRadiusAnimation()
    {
        var animation = AnimationFactory.CreateAnimation(
            typeof(CornerRadius),
            new CornerRadius(2),
            new CornerRadius(8),
            TimeSpan.FromSeconds(1));

        Assert.NotNull(animation);
        Assert.IsType<CornerRadiusAnimation>(animation);
    }

    [Fact]
    public void AnimationFactory_CreatesBrushAnimation_ForSolidColorBrushes()
    {
        var animation = AnimationFactory.CreateAnimation(
            typeof(Brush),
            new SolidColorBrush(Color.FromRgb(0, 0, 0)),
            new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            TimeSpan.FromSeconds(1));

        Assert.NotNull(animation);
        Assert.IsType<BrushAnimation>(animation);
    }

    [Fact]
    public void AnimationFactory_ReturnsNull_ForNonSolidBrushTransition()
    {
        var animation = AnimationFactory.CreateAnimation(
            typeof(Brush),
            new LinearGradientBrush(Color.Red, Color.Blue, 45),
            new SolidColorBrush(Color.White),
            TimeSpan.FromSeconds(1));

        Assert.Null(animation);
    }

    [Fact]
    public void BrushAnimation_InterpolatesNullToSolidBrush_WithTransparentSameHueStart()
    {
        var animation = new BrushAnimation
        {
            From = null,
            To = new SolidColorBrush(Color.FromArgb(255, 30, 60, 90)),
            Duration = TimeSpan.FromSeconds(1)
        };

        var result = (SolidColorBrush)((IAnimationTimeline)animation).GetCurrentValue(
            null!,
            animation.To!,
            CreateAnimationClock(animation, 0.5));

        Assert.Equal(Color.FromArgb(128, 30, 60, 90), result.Color);
    }

    [Fact]
    public void CreateTransitionAnimation_UsesStopFillBehavior()
    {
        var animation = AnimationFactory.CreateTransitionAnimation(
            typeof(double),
            0.0,
            1.0,
            TimeSpan.FromSeconds(1),
            TransitionTimingFunction.Recommended);

        Assert.NotNull(animation);
        Assert.Equal(AnimationFillBehavior.Stop, animation!.AnimationFillBehavior);
    }

    [Fact]
    public void CreateTransitionAnimation_RecommendedUsesEaseOutCubic()
    {
        var animation = Assert.IsType<DoubleAnimation>(AnimationFactory.CreateTransitionAnimation(
            typeof(double),
            0.0,
            1.0,
            TimeSpan.FromSeconds(1),
            TransitionTimingFunction.Recommended));

        var easing = Assert.IsType<CubicEase>(animation.EasingFunction);
        Assert.Equal(EasingMode.EaseOut, easing.EasingMode);
    }

    [Fact]
    public void AnimationFactory_ReturnsNull_ForUnsupportedType()
    {
        // Act
        var animation = AnimationFactory.CreateAnimation(typeof(string), "a", "b", TimeSpan.FromSeconds(1));

        // Assert
        Assert.Null(animation);
    }

    #endregion

    #region Test Helpers

    private class TestElement : UIElement
    {
        public static readonly DependencyProperty TestDoubleProperty =
            DependencyProperty.Register("TestDouble", typeof(double), typeof(TestElement),
                new PropertyMetadata(0.0));

        public double TestDouble
        {
            get => (double)(GetValue(TestDoubleProperty) ?? 0.0);
            set => SetValue(TestDoubleProperty, value);
        }
    }

    private static AnimationClock CreateAnimationClock(Timeline timeline, double progress)
    {
        var clock = new AnimationClock(timeline);
        clock.Begin();

        var progressField = typeof(AnimationClock).GetField(
            "_currentProgress",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(progressField);
        progressField!.SetValue(clock, progress);
        return clock;
    }

    #endregion
}
