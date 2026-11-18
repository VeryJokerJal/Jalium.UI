using System.Diagnostics;
using System.Reflection;
using Jalium.UI.Animation;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class AnimationContentTransitionRuntimeTests
{
    public static IEnumerable<object[]> Modes => Enum.GetValues<TransitionMode>().Select(mode => new object[] { mode });

    [Theory]
    [MemberData(nameof(Modes))]
    public void EveryTransitionMode_AdvancesAndCompletesOnTheAnimationTimebase(TransitionMode mode)
    {
        ContentTransition transition = Assert.IsAssignableFrom<ContentTransition>(TransitioningContentControl.CreateTransitionFromMode(mode));
        transition.Duration = TimeSpan.FromSeconds(1);
        TransitionHost host = new TransitioningContentControl();
        DispatcherTimer? timer = null;
        int completed = 0;
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => timer = transition.Run(host, null, new Border(), new Size(200, 100), () => completed++));
            Assert.NotNull(timer);
            AnimationEngineTests.RunInsideFrame(start + Stopwatch.Frequency / 4, _ => Tick(timer!));
            Assert.True(timer!.IsEnabled);
            Assert.Equal(0, completed);
            AssertFinite(host);
            AnimationEngineTests.RunInsideFrame(start + Stopwatch.Frequency * 5 / 4, _ => Tick(timer));
            Assert.Equal(1, completed);
            Assert.False(timer.IsEnabled);
            Assert.Equal(1d, host.NewContentOpacity, 5);
            Assert.Equal(-1, host.GpuShaderMode);
            Assert.Null(host.ActiveParticles);
            AssertFinite(host);
        }
        finally
        {
            timer?.Stop();
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void ParticleTransitions_HaveTheSameMotionAtDifferentFrameRates()
    {
        foreach (ContentTransition transition in new ContentTransition[]
            { new FallingTilesTransition(), new MatrixRainTransition(), new ParticleDissolveTransition(), new ShatterTransition() })
        {
            var slow = SampleParticles(transition, 10);
            var fast = SampleParticles(transition, 60);
            Assert.Equal(slow.Length, fast.Length);
            for (int i = 0; i < slow.Length; i++)
            {
                Assert.Equal(slow[i].X, fast[i].X, 4);
                Assert.Equal(slow[i].Y, fast[i].Y, 4);
                Assert.Equal(slow[i].Rotation, fast[i].Rotation, 4);
            }
        }
    }

    private static TransitionParticle[] SampleParticles(ContentTransition transition, int frameCount)
    {
        transition.Duration = TimeSpan.FromSeconds(1);
        TransitionHost host = new TransitioningContentControl();
        DispatcherTimer? timer = null;
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => timer = transition.Run(host, null, null, new Size(200, 800), () => { }));
            for (int i = 1; i <= frameCount; i++)
            {
                long timestamp = start + (long)(Stopwatch.Frequency * .5 * i / frameCount);
                AnimationEngineTests.RunInsideFrame(timestamp, _ => Tick(timer!));
            }
            return host.ActiveParticles!.ToArray();
        }
        finally { timer?.Stop(); }
    }

    [Fact]
    public void ContentChangesBeforeLayout_DoNotStartInvisibleTransitions()
    {
        var control = new TransitioningContentControl { Transition = new CrossfadeTransition(), Content = new Border() };
        control.Content = new Border();
        try
        {
            Assert.False(IsTransitioning(control));
            Assert.Null(ActiveTimer(control));
            Assert.Equal(1d, ((TransitionHost)control).NewContentOpacity);
        }
        finally { ActiveTimer(control)?.Stop(); }
    }

    [Fact]
    public void AFailedTransitionFrame_RestoresTheCurrentContentAndStopsItsTimer()
    {
        var control = new TransitioningContentControl
        {
            Transition = new CrossfadeTransition { EasingFunction = new ThrowingEasing() },
            Content = new Border()
        };
        control.Measure(new Size(200, 100));
        control.Arrange(new Rect(0, 0, 200, 100));
        control.Content = new Border();
        var timer = Assert.IsType<DispatcherTimer>(ActiveTimer(control));
        try
        {
            Tick(timer);
            Assert.False(timer.IsEnabled);
            Assert.False(IsTransitioning(control));
            Assert.Equal(1d, ((TransitionHost)control).NewContentOpacity);
        }
        finally { timer.Stop(); }
    }

    private static void Tick(DispatcherTimer timer) =>
        ((EventHandler?)typeof(DispatcherTimer).GetField("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(timer))?.Invoke(timer, EventArgs.Empty);

    private static DispatcherTimer? ActiveTimer(TransitioningContentControl control) =>
        (DispatcherTimer?)typeof(TransitioningContentControl).GetField("_activeTransition", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control);

    private static bool IsTransitioning(TransitioningContentControl control) =>
        (bool)typeof(TransitioningContentControl).GetField("_isTransitioning", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;

    private static void AssertFinite(TransitionHost host)
    {
        Assert.True(double.IsFinite(host.NewContentOpacity));
        Assert.True(double.IsFinite(host.OverlayOpacity));
        foreach (var transform in new[] { host.NewContentTransform, host.OverlayTransform, host.Overlay2Transform })
        {
            if (transform == null) continue;
            var matrix = transform.Value;
            Assert.True(double.IsFinite(matrix.M11) && double.IsFinite(matrix.M12) &&
                double.IsFinite(matrix.M21) && double.IsFinite(matrix.M22) &&
                double.IsFinite(matrix.OffsetX) && double.IsFinite(matrix.OffsetY));
        }
    }

    private sealed class ThrowingEasing : IEasingFunction
    {
        public double Ease(double normalizedTime) => throw new InvalidOperationException("Injected transition failure.");
    }
}
