using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Jalium.UI.Controls.Platform;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSQuitReentrantClosingTests : MacOSGeometryTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuitStartedInsideClosingWaitsForCancelOrFailureAndCanBeRetried(bool fail)
    {
        var window = new DisplayedTestWindow();
        var other = new DisplayedTestWindow();
        var application = ApplicationFor(window, other);
        var replies = new List<bool>();
        using var request = new MacOSWindowCloseRequest(application, replies.Add);
        MacOSWindowCloseResult? initial = null;
        bool otherClosedDuringCallback = false;
        EventHandler<CancelEventArgs> callback = (_, args) =>
        {
            initial = request.Begin();
            otherClosedDuringCallback = other.IsCloseRequestedForPlatformTermination;
            args.Cancel = true;
            if (fail) throw new InvalidOperationException("Outer Closing failed after requesting quit");
        };
        window.Closing += callback;
        try
        {
            if (fail) Assert.Throws<InvalidOperationException>(window.Close);
            else window.Close();
            Assert.Equal(MacOSWindowCloseResult.Pending, initial);
            Assert.Equal(MacOSWindowCloseResult.Cancelled, request.Result);
            Assert.Equal([false], replies);
            Assert.False(otherClosedDuringCallback);
            Assert.False(window.IsCloseRequestedForPlatformTermination);
            Assert.False(other.IsCloseRequestedForPlatformTermination);
            window.Closing -= callback;
            using var retry = new MacOSWindowCloseRequest(application, _ => Assert.Fail("Unexpected deferred retry reply"));
            Assert.Equal(MacOSWindowCloseResult.Complete, retry.Begin());
        }
        finally { window.Closing -= callback; window.Close(); other.Close(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptedOuterCloseContinuesTheQuitAfterItsCallbackAndResourceRelease(bool deferred)
    {
        var window = new DisplayedTestWindow();
        var other = new DisplayedTestWindow();
        var application = ApplicationFor(window, other);
        var replies = new List<bool>();
        using var request = new MacOSWindowCloseRequest(application, replies.Add);
        MacOSWindowCloseResult? initial = null;
        bool otherClosedDuringCallback = false;
        window.Closing += (_, _) =>
        {
            initial = request.Begin();
            otherClosedDuringCallback = other.IsCloseRequestedForPlatformTermination;
        };
        SetRendering(window, deferred);
        try
        {
            window.Close();
            Assert.Equal(MacOSWindowCloseResult.Pending, initial);
            Assert.False(otherClosedDuringCallback);
            Assert.True(other.IsClosedForPlatformTermination);
            if (deferred)
            {
                Assert.Equal(MacOSWindowCloseResult.Pending, request.Result);
                Assert.Empty(replies);
                SetRendering(window, false);
                Finish(window);
                Finish(window);
            }
            Assert.True(window.IsClosedForPlatformTermination);
            Assert.Equal(MacOSWindowCloseResult.Complete, request.Result);
            Assert.Equal([true], replies);
        }
        finally { SetRendering(window, false); window.Close(); other.Close(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedWindowCanCancelTheQuitStartedInsideItsOwnClosing(bool inferredOwner)
    {
        var owner = new DisplayedTestWindow();
        var child = new DisplayedTestWindow();
        if (inferredOwner) SetField(child, "_modalOwner", owner);
        else child.Owner = owner;
        var application = ApplicationFor(owner, child);
        var replies = new List<bool>();
        using var request = new MacOSWindowCloseRequest(application, replies.Add);
        MacOSWindowCloseResult? initial = null;
        bool ownerClosedDuringCallback = false;
        EventHandler<CancelEventArgs> callback = (_, args) =>
        {
            initial = request.Begin();
            ownerClosedDuringCallback = owner.IsCloseRequestedForPlatformTermination;
            args.Cancel = true;
        };
        child.Closing += callback;
        try
        {
            child.Close();
            Assert.Equal(MacOSWindowCloseResult.Pending, initial);
            Assert.Equal(MacOSWindowCloseResult.Cancelled, request.Result);
            Assert.Equal([false], replies);
            Assert.False(ownerClosedDuringCallback);
            Assert.False(owner.IsCloseRequestedForPlatformTermination);
            Assert.False(child.IsCloseRequestedForPlatformTermination);
        }
        finally { child.Closing -= callback; SetField(child, "_modalOwner", null); child.Close(); owner.Close(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaterOuterCloseRejectsQuitAfterAnEarlierWindowAcceptedDeferredTeardown(bool fail)
    {
        var first = new DisplayedTestWindow();
        var later = new DisplayedTestWindow();
        var application = ApplicationFor(first, later);
        var replies = new List<bool>();
        using var request = new MacOSWindowCloseRequest(application, replies.Add);
        EventHandler<CancelEventArgs> callback = (_, args) =>
        {
            Assert.Equal(MacOSWindowCloseResult.Pending, request.Begin());
            args.Cancel = true;
            if (fail) throw new InvalidOperationException("Later outer Closing failed");
        };
        later.Closing += callback;
        SetRendering(first, true);
        try
        {
            if (fail) Assert.Throws<InvalidOperationException>(later.Close);
            else later.Close();
            Assert.Equal(MacOSWindowCloseResult.Cancelled, request.Result);
            Assert.Equal([false], replies);
            Assert.True(first.IsCloseRequestedForPlatformTermination);
            Assert.False(later.IsCloseRequestedForPlatformTermination);
            SetRendering(first, false);
            Finish(first);
            Finish(first);
            Assert.True(first.IsClosedForPlatformTermination);
            Assert.Equal(MacOSWindowCloseResult.Cancelled, request.Result);
            Assert.Equal([false], replies);
        }
        finally { later.Closing -= callback; SetRendering(first, false); Finish(first); first.Close(); later.Close(); }
    }

    private static Application ApplicationFor(params Window[] windows)
    {
        var application = (Application)RuntimeHelpers.GetUninitializedObject(typeof(Application));
        typeof(Application).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(application, new WindowCollection(() => windows.Where(window => !window.IsClosedForPlatformTermination).ToArray()));
        application.ShutdownMode = ShutdownMode.OnMainWindowClose;
        return application;
    }

    private static void SetField(Window window, string name, object? value) => typeof(Window)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
    private static void SetRendering(Window window, bool value) => SetField(window, "_renderState", value ? 1 << 1 : 0);
    private static void Finish(Window window) => typeof(Window)
        .GetMethod("CompletePendingManagedTeardown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
}
