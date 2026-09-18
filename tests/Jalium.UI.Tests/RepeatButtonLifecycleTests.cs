using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class RepeatButtonLifecycleTests
{
    [Fact]
    public void DelayAndIntervalChanges_ReplaceTicks_AndDetachCancelsOldGeneration()
    {
        var parent = new Grid();
        var button = new RepeatButton { Delay = 60_000, Interval = 60_000 };
        int clicks = 0;
        button.Click += (_, _) => clicks++;
        try
        {
            for (int round = 0; round < 8; round++)
            {
                parent.Children.Add(button);
                button.SetIsPressed(true);
                var timer = Read<DispatcherTimer>(button, "_timer");
                var oldDelay = Generation(timer);
                button.Delay = 120_000 + round;
                Assert.Equal(TimeSpan.FromMilliseconds(120_000 + round), timer.Interval);
                InvokeTick(timer, oldDelay);
                Assert.Equal(round, clicks);

                InvokeTick(timer, Generation(timer));
                Assert.Equal(round + 1, clicks);
                Assert.Equal(TimeSpan.FromMilliseconds(button.Interval), timer.Interval);
                var oldInterval = Generation(timer);
                button.Interval = 120_100 + round;
                Assert.Equal(TimeSpan.FromMilliseconds(120_100 + round), timer.Interval);
                InvokeTick(timer, oldInterval);
                Assert.Equal(round + 1, clicks);

                var detachedGeneration = Generation(timer);
                parent.Children.Remove(button);
                Assert.False(button.IsPressed);
                Assert.False(timer.IsEnabled);
                InvokeTick(timer, detachedGeneration);
                Assert.Equal(round + 1, clicks);
            }
        }
        finally
        {
            parent.Children.Remove(button);
            button.CompleteInteractionForDetach();
        }
    }

    private static object Generation(DispatcherTimer timer) =>
        Read<object>(timer, "_activeGeneration");

    private static T Read<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void InvokeTick(DispatcherTimer timer, object generation) => typeof(DispatcherTimer)
        .GetMethod("OnTimerCallback", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(timer, [generation]);
}
