using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Jalium.UI.Controls.Platform;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSQuitShutdownPolicyTests : MacOSGeometryTestBase
{
    [Theory]
    [InlineData(ShutdownMode.OnLastWindowClose)]
    [InlineData(ShutdownMode.OnMainWindowClose)]
    [InlineData(ShutdownMode.OnExplicitShutdown)]
    public void ClosingReadsTheApplicationsPublicShutdownPolicy(ShutdownMode policy)
    {
        var window = new DisplayedTestWindow();
        var application = ApplicationFor(window);
        application.ShutdownMode = policy;
        ShutdownMode? observed = null;
        window.Closing += (_, _) => observed = application.ShutdownMode;
        using var request = new MacOSWindowCloseRequest(application, _ => Assert.Fail("Unexpected deferred reply"));

        Assert.Equal(MacOSWindowCloseResult.Complete, request.Begin());
        Assert.Equal(policy, observed);
        Assert.Equal(policy, application.ShutdownMode);
    }

    [Theory]
    [InlineData(ShutdownMode.OnLastWindowClose)]
    [InlineData(ShutdownMode.OnMainWindowClose)]
    [InlineData(ShutdownMode.OnExplicitShutdown)]
    public void AcceptedClosePreservesThePolicyChosenByItsCallback(ShutdownMode policy)
    {
        var window = new DisplayedTestWindow();
        var application = ApplicationFor(window);
        window.Closing += (_, _) => application.ShutdownMode = policy;
        using var request = new MacOSWindowCloseRequest(application, _ => Assert.Fail("Unexpected deferred reply"));

        Assert.Equal(MacOSWindowCloseResult.Complete, request.Begin());
        Assert.True(window.IsClosedForPlatformTermination);
        Assert.Equal(policy, application.ShutdownMode);
    }

    [Theory]
    [InlineData(ShutdownMode.OnLastWindowClose, false)]
    [InlineData(ShutdownMode.OnExplicitShutdown, false)]
    [InlineData(ShutdownMode.OnLastWindowClose, true)]
    [InlineData(ShutdownMode.OnExplicitShutdown, true)]
    public void CancelOrExceptionPreservesThePolicyChosenByItsCallback(ShutdownMode policy, bool fail)
    {
        var window = new DisplayedTestWindow();
        var application = ApplicationFor(window);
        EventHandler<CancelEventArgs> callback = (_, args) =>
        {
            application.ShutdownMode = policy;
            if (fail) throw new InvalidOperationException("Closing policy callback failed");
            args.Cancel = true;
        };
        window.Closing += callback;
        using var request = new MacOSWindowCloseRequest(application, _ => Assert.Fail("Unexpected deferred reply"));
        try
        {
            Assert.Equal(MacOSWindowCloseResult.Cancelled, request.Begin());
            Assert.False(window.IsCloseRequestedForPlatformTermination);
            Assert.Equal(policy, application.ShutdownMode);
        }
        finally { window.Closing -= callback; window.Close(); }
    }

    [Theory]
    [InlineData(ShutdownMode.OnLastWindowClose)]
    [InlineData(ShutdownMode.OnMainWindowClose)]
    [InlineData(ShutdownMode.OnExplicitShutdown)]
    public void DeferredTeardownPreservesTheLatestPolicy(ShutdownMode policy)
    {
        var window = new DisplayedTestWindow();
        var application = ApplicationFor(window);
        var replies = new List<bool>();
        using var request = new MacOSWindowCloseRequest(application, replies.Add);
        SetRendering(window, true);
        try
        {
            Assert.Equal(MacOSWindowCloseResult.Pending, request.Begin());
            application.ShutdownMode = policy;
            SetRendering(window, false);
            var finish = typeof(Window).GetMethod("CompletePendingManagedTeardown", BindingFlags.Instance | BindingFlags.NonPublic)!;
            finish.Invoke(window, null);
            finish.Invoke(window, null);
            Assert.Equal([true], replies);
            Assert.Equal(MacOSWindowCloseResult.Complete, request.Result);
            Assert.Equal(policy, application.ShutdownMode);
        }
        finally { SetRendering(window, false); window.Close(); }
    }

    [Fact]
    public void PolicyChangedBeforeBeginIsNotReplacedByTheConstructorSnapshot()
    {
        var window = new DisplayedTestWindow();
        var application = ApplicationFor(window);
        using var request = new MacOSWindowCloseRequest(application, _ => Assert.Fail("Unexpected deferred reply"));
        application.ShutdownMode = ShutdownMode.OnLastWindowClose;
        Assert.Equal(MacOSWindowCloseResult.Complete, request.Begin());
        Assert.Equal(ShutdownMode.OnLastWindowClose, application.ShutdownMode);
    }

    [Fact]
    public void BeginningACompletedRequestAgainDoesNotChangeThePolicyOrRecloseWindows()
    {
        var window = new DisplayedTestWindow();
        var application = ApplicationFor(window);
        int closing = 0;
        window.Closing += (_, _) => closing++;
        using var request = new MacOSWindowCloseRequest(application, _ => Assert.Fail("Unexpected deferred reply"));
        Assert.Equal(MacOSWindowCloseResult.Complete, request.Begin());
        Assert.Equal(MacOSWindowCloseResult.Complete, request.Begin());
        Assert.Equal(1, closing);
        Assert.Equal(ShutdownMode.OnMainWindowClose, application.ShutdownMode);
    }

    private static Application ApplicationFor(params Window[] windows)
    {
        var application = (Application)RuntimeHelpers.GetUninitializedObject(typeof(Application));
        typeof(Application).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(application, new WindowCollection(() => windows.Where(window => !window.IsClosedForPlatformTermination).ToArray()));
        application.ShutdownMode = ShutdownMode.OnMainWindowClose;
        return application;
    }

    private static void SetRendering(Window window, bool value) => typeof(Window)
        .GetField("_renderState", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value ? 1 << 1 : 0);
}
