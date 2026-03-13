namespace Jalium.UI.Tests;

public class ApplicationDpiAwarenessTests
{
    [Fact]
    public void TryEnablePerMonitorDpiAwareness_WhenPerMonitorV2Succeeds_ShouldNotUseFallbacks()
    {
        bool shcoreCalled = false;
        bool legacyCalled = false;

        var result = Application.TryEnablePerMonitorDpiAwareness(
            setProcessDpiAwarenessContext: _ => true,
            getLastError: () => 0,
            setProcessDpiAwareness: _ =>
            {
                shcoreCalled = true;
                return 0;
            },
            setProcessDpiAware: () =>
            {
                legacyCalled = true;
                return true;
            });

        Assert.True(result);
        Assert.False(shcoreCalled);
        Assert.False(legacyCalled);
    }

    [Fact]
    public void TryEnablePerMonitorDpiAwareness_WhenContextApiDenied_ShouldTreatManifestAsSuccess()
    {
        bool shcoreCalled = false;
        bool legacyCalled = false;

        var result = Application.TryEnablePerMonitorDpiAwareness(
            setProcessDpiAwarenessContext: _ => false,
            getLastError: () => 5,
            setProcessDpiAwareness: _ =>
            {
                shcoreCalled = true;
                return 0;
            },
            setProcessDpiAware: () =>
            {
                legacyCalled = true;
                return true;
            });

        Assert.True(result);
        Assert.False(shcoreCalled);
        Assert.False(legacyCalled);
    }

    [Fact]
    public void TryEnablePerMonitorDpiAwareness_WhenContextApiFails_ShouldFallbackToShcore()
    {
        bool legacyCalled = false;

        var result = Application.TryEnablePerMonitorDpiAwareness(
            setProcessDpiAwarenessContext: _ => false,
            getLastError: () => 87,
            setProcessDpiAwareness: _ => 0,
            setProcessDpiAware: () =>
            {
                legacyCalled = true;
                return true;
            });

        Assert.True(result);
        Assert.False(legacyCalled);
    }
}
