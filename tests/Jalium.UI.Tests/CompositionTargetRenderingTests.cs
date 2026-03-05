using System.Reflection;
using Jalium.UI;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CompositionTargetRenderingTests
{
    [Fact]
    public void Rendering_WhenOneSubscriberThrows_StillInvokesRemainingSubscribers()
    {
        int callCount = 0;
        EventHandler throwing = (_, _) => throw new InvalidOperationException("Injected test failure.");
        EventHandler healthy = (_, _) => callCount++;

        CompositionTarget.Rendering += throwing;
        CompositionTarget.Rendering += healthy;

        try
        {
            var raiseRendering = typeof(CompositionTarget).GetMethod(
                "RaiseRendering",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(raiseRendering);
            raiseRendering!.Invoke(null, null);
        }
        finally
        {
            CompositionTarget.Rendering -= healthy;
            CompositionTarget.Rendering -= throwing;
        }

        Assert.Equal(1, callCount);
    }
}
