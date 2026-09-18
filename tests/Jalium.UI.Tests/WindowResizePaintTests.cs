using System.Reflection;
using System.Runtime.InteropServices;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class WindowResizePaintTests : IDisposable
{
    public WindowResizePaintTests() => RenderContextEmptyWindowTests.DrainAllContexts();
    public void Dispose() => RenderContextEmptyWindowTests.DrainAllContexts();

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void OsPaintAfterEmptyResize_ReusesCurrentPixels_WithoutReplayingTheTree()
    {
        var window = new Window { Width = 320, Height = 240 };
        try
        {
            window.Show();
            Assert.True(window.UsesAutomaticEmptySoftwareContext);
            SetSizing(window);
            window.ForceRenderFrame();
            var fullFrames = window.RenderPathCountersForTests.full;
            var presents = window.FrameHistory.TotalFrames;
            Assert.True(InvalidateRect(window.Handle, 0, false));

            _ = SendMessage(window.Handle, 0x000F, 0, 0);

            Assert.False(GetUpdateRect(window.Handle, 0, false));
            Assert.Equal(fullFrames, window.RenderPathCountersForTests.full);
            Assert.Equal(presents + 1, window.FrameHistory.TotalFrames);

            // An actual visual mutation must still render, even if the OS paint
            // arrives in the same resize operation.
            window.Background = Jalium.UI.Media.Brushes.Blue;
            Assert.True(InvalidateRect(window.Handle, 0, false));
            _ = SendMessage(window.Handle, 0x000F, 0, 0);
            window.ForceRenderFrame();
            Assert.True(window.RenderPathCountersForTests.full > fullFrames);
        }
        finally { window.Close(); }
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void FullSoftwareResizeFrame_ConsumesTheCoveredNativeUpdateRegion()
    {
        _ = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var window = new PaintWindow { Width = 300, Height = 220 };
        try
        {
            window.Show();
            SetSizing(window);
            Assert.True(SetWindowPos(window.Handle, 0, 0, 0, 420, 280, 0x0016));
            Assert.True(InvalidateRect(window.Handle, 0, false));
            window.RequestFullInvalidation();
            Assert.True(GetUpdateRect(window.Handle, 0, false));

            long before = window.FrameHistory.TotalFrames;
            window.ForceRenderFrame();

            Assert.Equal(RenderBackend.Software, window.RenderTarget!.Backend);
            Assert.True(window.FrameHistory.TotalFrames > before);
            Assert.False(GetUpdateRect(window.Handle, 0, false));
            long invalidationSequence = (long)typeof(Window)
                .GetField("_fullInvalidationSeq", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(window)!;
            // A stale WM_PAINT may already have been queued when the frame
            // clock covered its update region. It must not manufacture work.
            _ = SendMessage(window.Handle, 0x000F, 0, 0);
            Assert.Equal(invalidationSequence, (long)typeof(Window)
                .GetField("_fullInvalidationSeq", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(window)!);
        }
        finally { window.Close(); }
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void NewInvalidationDuringFullFrame_RemainsPendingAfterPresent()
    {
        _ = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var window = new PaintWindow { Width = 300, Height = 220 };
        try
        {
            window.Show();
            SetSizing(window);
            window.BeforePresent = () =>
            {
                window.RequestFullInvalidation();
                Assert.True(InvalidateRect(window.Handle, 0, false));
            };
            window.RequestFullInvalidation();
            window.ForceRenderFrame();

            Assert.True(GetUpdateRect(window.Handle, 0, false));
            window.BeforePresent = null;
            window.ForceRenderFrame();
            Assert.False(GetUpdateRect(window.Handle, 0, false));
        }
        finally { window.Close(); }
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void FailedFullFrame_DoesNotConsumeTheNativeUpdateRegion()
    {
        _ = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var window = new PaintWindow { Width = 300, Height = 220 };
        try
        {
            window.Show();
            SetSizing(window);
            window.BeforePresent = () => throw new InvalidOperationException("injected full-frame failure");
            Assert.True(InvalidateRect(window.Handle, 0, false));
            window.RequestFullInvalidation();

            Assert.Throws<InvalidOperationException>(() => window.ForceRenderFrame());

            Assert.True(GetUpdateRect(window.Handle, 0, false));
            window.BeforePresent = null;
        }
        finally { window.Close(); }
    }

    private static void SetSizing(Window window) => typeof(Window)
        .GetField("_isSizing", BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(window, true);

    private sealed class PaintWindow : Window
    {
        public Action? BeforePresent { get; set; }
        protected override void OnRender(RenderTarget renderTarget) => BeforePresent?.Invoke();
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(nint window, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUpdateRect(nint window, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
