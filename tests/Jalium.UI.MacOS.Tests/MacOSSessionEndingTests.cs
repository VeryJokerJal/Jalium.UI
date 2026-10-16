using System.Reflection;
using System.Runtime.CompilerServices;
using Jalium.UI.MacOS;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSSessionEndingTests : MacOSGeometryTestBase
{
    [Theory]
    [InlineData(0x726c676fu, ReasonSessionEnding.Logoff)]
    [InlineData(0x73687574u, ReasonSessionEnding.Shutdown)]
    [InlineData(0x72657374u, ReasonSessionEnding.Shutdown)]
    public void RecognizesSessionManagerQuitReasons(uint value, ReasonSessionEnding expected)
    {
        Assert.True(MacOSSessionEndReason.TryGet(0x61657674, 0x71756974, value, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0x61657674u, 0x71756974u, null)]
    [InlineData(0x61657674u, 0x71756974u, 0u)]
    [InlineData(0x61657674u, 0x71756974u, 0x71756961u)] // Quit All
    [InlineData(0x61657674u, 0x71756974u, 0x6c6f676fu)] // logout command, not a quit reason
    [InlineData(0x61657674u, 0x71756974u, 0xffffffffu)]
    [InlineData(0u, 0x71756974u, 0x73687574u)]
    [InlineData(0x61657674u, 0u, 0x726c676fu)]
    public void OrdinaryOrUnrelatedEventsDoNotEndTheSession(uint eventClass, uint id, uint? reason)
        => Assert.False(MacOSSessionEndReason.TryGet(eventClass, id, reason, out _));

    [Theory]
    [InlineData(ReasonSessionEnding.Logoff)]
    [InlineData(ReasonSessionEnding.Shutdown)]
    public void NotifiesEveryLiveWindowBeforeTheApplication(ReasonSessionEnding reason)
    {
        var first = new DisplayedTestWindow();
        var hidden = new Window();
        var application = ApplicationFor(first, hidden);
        var order = new List<string>();
        first.SessionEnding += (_, e) => { Assert.Equal(reason, e.ReasonSessionEnding); order.Add("first"); };
        hidden.SessionEnding += (_, e) => { Assert.Equal(reason, e.ReasonSessionEnding); order.Add("hidden"); };
        application.SessionEnding += (_, e) => { Assert.Equal(reason, e.ReasonSessionEnding); order.Add("application"); };
        try
        {
            Assert.True(application.RaiseMacOSSessionEnding(reason));
            Assert.Equal(["first", "hidden", "application"], order);
        }
        finally { first.Close(); hidden.Close(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationFromEitherScopeSurvivesLaterScopesAndAllowsAFreshRequest(bool cancelApplication)
    {
        var first = new DisplayedTestWindow();
        var later = new DisplayedTestWindow();
        var application = ApplicationFor(first, later);
        bool cancel = true;
        int applicationEvents = 0, laterEvents = 0;
        first.SessionEnding += (_, e) => e.Cancel = cancel && !cancelApplication;
        later.SessionEnding += (_, e) => { laterEvents++; e.Cancel = false; };
        application.SessionEnding += (_, e) => { applicationEvents++; e.Cancel = cancel && cancelApplication; };
        try
        {
            Assert.False(application.RaiseMacOSSessionEnding(ReasonSessionEnding.Logoff));
            Assert.False(application.IsHandlingMacOSSessionEnding);
            Assert.False(first.IsCloseRequestedForPlatformTermination);
            Assert.False(later.IsCloseRequestedForPlatformTermination);
            cancel = false;
            Assert.True(application.RaiseMacOSSessionEnding(ReasonSessionEnding.Shutdown));
            Assert.Equal(2, applicationEvents);
            Assert.Equal(2, laterEvents);
        }
        finally { first.Close(); later.Close(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandlerExceptionsCancelWithoutPoisoningTheNextRequest(bool applicationHandler)
    {
        var window = new DisplayedTestWindow();
        var application = ApplicationFor(window);
        void Fail(object? sender, SessionEndingCancelEventArgs args) => throw new InvalidOperationException("session handler failed");
        if (applicationHandler) application.SessionEnding += Fail;
        else window.SessionEnding += Fail;
        try
        {
            Assert.False(application.RaiseMacOSSessionEnding(ReasonSessionEnding.Shutdown));
            Assert.False(application.IsHandlingMacOSSessionEnding);
            Assert.False(window.IsCloseRequestedForPlatformTermination);
            if (applicationHandler) application.SessionEnding -= Fail;
            else window.SessionEnding -= Fail;
            Assert.True(application.RaiseMacOSSessionEnding(ReasonSessionEnding.Shutdown));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void NestedRequestsReturnWithoutReenteringTheNotification()
    {
        var window = new DisplayedTestWindow();
        var application = ApplicationFor(window);
        int events = 0;
        window.SessionEnding += (_, _) =>
        {
            events++;
            Assert.True(application.IsHandlingMacOSSessionEnding);
            Assert.False(application.RaiseMacOSSessionEnding(ReasonSessionEnding.Shutdown));
        };
        try
        {
            Assert.True(application.RaiseMacOSSessionEnding(ReasonSessionEnding.Logoff));
            Assert.Equal(1, events);
            Assert.False(application.IsHandlingMacOSSessionEnding);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void AWindowClosedByAnEarlierHandlerIsNotNotifiedFromTheOldSnapshot()
    {
        var first = new DisplayedTestWindow();
        var later = new DisplayedTestWindow();
        var application = ApplicationFor(first, later);
        int laterEvents = 0, appEvents = 0;
        first.SessionEnding += (_, _) => later.Close();
        later.SessionEnding += (_, _) => laterEvents++;
        application.SessionEnding += (_, _) => appEvents++;
        try
        {
            Assert.True(application.RaiseMacOSSessionEnding(ReasonSessionEnding.Logoff));
            Assert.Equal(0, laterEvents);
            Assert.Equal(1, appEvents);
        }
        finally { first.Close(); later.Close(); }
    }

    [Theory]
    [InlineData(ShutdownMode.OnLastWindowClose, false)]
    [InlineData(ShutdownMode.OnExplicitShutdown, true)]
    public void SessionCallbacksPreserveTheirChosenPublicShutdownPolicy(ShutdownMode policy, bool cancel)
    {
        var window = new DisplayedTestWindow();
        var application = ApplicationFor(window);
        application.SessionEnding += (_, e) => { application.ShutdownMode = policy; e.Cancel = cancel; };
        try
        {
            Assert.Equal(!cancel, application.RaiseMacOSSessionEnding(ReasonSessionEnding.Logoff));
            Assert.Equal(policy, application.ShutdownMode);
        }
        finally { window.Close(); }
    }

    private static Application ApplicationFor(params Window[] windows)
    {
        var application = (Application)RuntimeHelpers.GetUninitializedObject(typeof(Application));
        typeof(Application).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(application, new WindowCollection(() => windows.Where(window => !window.IsClosedForPlatformTermination).ToArray()));
        application.ShutdownMode = ShutdownMode.OnMainWindowClose;
        return application;
    }
}
