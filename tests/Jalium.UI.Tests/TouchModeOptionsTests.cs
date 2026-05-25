using Jalium.UI.Hosting;

namespace Jalium.UI.Tests;

public class TouchModeOptionsTests
{
    [Fact]
    public void Current_IsSingleton()
    {
        var a = TouchModeOptions.Current;
        var b = TouchModeOptions.Current;
        Assert.Same(a, b);
    }

    [Fact]
    public void Defaults_DisabledAnd40Dip()
    {
        // Reset to known defaults — the static singleton may have been mutated
        // by another test in the same process.
        TouchModeOptions.Current.Enabled = false;
        TouchModeOptions.Current.MinHitTargetSize = 40.0;

        Assert.False(TouchModeOptions.Current.Enabled);
        Assert.Equal(40.0, TouchModeOptions.Current.MinHitTargetSize);
    }

    [Fact]
    public void Enabled_RoundTrips()
    {
        TouchModeOptions.Current.Enabled = true;
        try
        {
            Assert.True(TouchModeOptions.Current.Enabled);
        }
        finally
        {
            TouchModeOptions.Current.Enabled = false;
        }
    }

    [Fact]
    public void MinHitTargetSize_RoundTripsAndClamps()
    {
        TouchModeOptions.Current.MinHitTargetSize = 56;
        Assert.Equal(56.0, TouchModeOptions.Current.MinHitTargetSize);

        TouchModeOptions.Current.MinHitTargetSize = 40.0;
    }
}
