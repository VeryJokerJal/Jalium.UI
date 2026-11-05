using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;

namespace Jalium.UI.Tests;

public sealed class DevToolsMeterMetadataTests
{
    [Fact]
    public void MeterMaximumMetadataRemainsCompatibleWhenProgressBarInitializesFirst()
    {
        Assert.Equal(100, new ProgressBar().Maximum);
        Assert.Equal(1, new DevToolsMeter().Maximum);
    }
}
