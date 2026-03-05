using System.Reflection;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class ProgressBarIndeterminateTests
{
    [Fact]
    public void IndeterminateAnimation_WhenNoWindowHost_DoesNotStartTimer()
    {
        var progressBar = new ProgressBar
        {
            IsIndeterminate = true
        };

        Assert.False(IsAnimationSubscribed(progressBar));
    }

    [Fact]
    public void IndeterminateAnimation_WhenAttachedToWindowHost_StartsTimer()
    {
        var progressBar = new ProgressBar
        {
            IsIndeterminate = true
        };

        var window = new Window
        {
            Content = progressBar
        };

        Dispatcher.GetForCurrentThread().ProcessQueue();

        Assert.True(IsAnimationSubscribed(progressBar));
    }

    private static bool IsAnimationSubscribed(ProgressBar progressBar)
    {
        var field = typeof(ProgressBar).GetField("_isAnimationSubscribed", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (bool)(field.GetValue(progressBar) ?? false);
    }
}
