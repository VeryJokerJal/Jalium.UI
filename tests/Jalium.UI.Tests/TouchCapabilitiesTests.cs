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
    public void GetTouchCapabilities_ReturnsSelfConsistentSnapshot()
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

    [Fact]
    public void LinuxCapabilityQuery_RefreshesAndReusesUnchangedSnapshot()
    {
        var calls = 0;
        try
        {
            Touch.OverrideLinuxCapabilitiesQueryForTesting(() =>
            {
                calls++;
                return calls switch
                {
                    1 => (0, 0, 0),
                    _ => (0, 1, 10),
                };
            });

            var absent = Touch.GetTouchCapabilities();
            var present = Touch.GetTouchCapabilities();
            var unchanged = Touch.GetTouchCapabilities();

            Assert.False(absent.TouchPresent);
            Assert.True(present.TouchPresent);
            Assert.Equal(10, present.Contacts);
            Assert.Same(present, unchanged);
            Assert.Equal(3, calls);
        }
        finally
        {
            Touch.ResetCapabilitiesCacheForTesting();
        }
    }

    [Fact]
    public void LinuxCapabilityQuery_MissingNativeLibraryFailsClosed()
    {
        try
        {
            Touch.OverrideLinuxCapabilitiesQueryForTesting(
                () => throw new DllNotFoundException());

            var capabilities = Touch.GetTouchCapabilities();

            Assert.False(capabilities.TouchPresent);
            Assert.Equal(0, capabilities.Contacts);
        }
        finally
        {
            Touch.ResetCapabilitiesCacheForTesting();
        }
    }
}
