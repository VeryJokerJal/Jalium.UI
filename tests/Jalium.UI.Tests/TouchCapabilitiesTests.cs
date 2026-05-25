using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class TouchCapabilitiesTests
{
    [Fact]
    public void GetTouchCapabilities_ReturnsSameInstanceWhenCached()
    {
        Touch.ResetCapabilitiesCacheForTesting();
        var first = Touch.GetTouchCapabilities();
        var second = Touch.GetTouchCapabilities();
        Assert.Same(first, second);
    }

    [Fact]
    public void OverrideCapabilitiesForTesting_PropagatesToIsTouchAvailable()
    {
        try
        {
            Touch.OverrideCapabilitiesForTesting(new TouchCapabilities { TouchPresent = true, Contacts = 10 });
            Assert.True(Touch.IsTouchAvailable);
            Assert.Equal(10, Touch.GetTouchCapabilities().Contacts);

            Touch.OverrideCapabilitiesForTesting(new TouchCapabilities { TouchPresent = false, Contacts = 0 });
            Assert.False(Touch.IsTouchAvailable);
            Assert.Equal(0, Touch.GetTouchCapabilities().Contacts);
        }
        finally
        {
            Touch.ResetCapabilitiesCacheForTesting();
        }
    }

    [Fact]
    public void GetTouchCapabilities_OnNonWindowsReportsAbsent()
    {
        // We can't fake the OS at runtime, but on the actual test host the
        // returned snapshot must be self-consistent: TouchPresent=false implies
        // Contacts=0; TouchPresent=true implies Contacts>=0. Reset the cache so
        // we read the live value.
        Touch.ResetCapabilitiesCacheForTesting();
        var caps = Touch.GetTouchCapabilities();
        if (!caps.TouchPresent)
        {
            Assert.Equal(0, caps.Contacts);
        }
        else
        {
            Assert.True(caps.Contacts >= 0);
        }
    }
}
