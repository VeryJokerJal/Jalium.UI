using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class WindowEmptyStorageTests : IDisposable
{
    public WindowEmptyStorageTests() => RenderContextEmptyWindowTests.DrainAllContexts();

    public void Dispose() => RenderContextEmptyWindowTests.DrainAllContexts();

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void IdleWindow_CompactsFromItsNativeTimer_AndReadbackRestoresIdenticalPixels()
    {
        var window = new Window { Title = "Idle framebuffer round trip", Width = 800, Height = 600 };
        try
        {
            window.Show();
            window.ForceRenderFrame();
            var target = Assert.IsType<RenderTarget>(window.RenderTarget);
            Assert.Equal(RenderBackend.Software, target.Backend);
            byte[] before = ReadPixels(target);
            ulong denseBytes = QueryOwnedBytes(target);
            Assert.True(denseBytes >= 1024u * 1024u);

            // Dispatch the real HWND's WM_TIMER rather than directly calling
            // the compaction helper. The window's last successful present owns
            // the timer, and no application timer or forced GC is involved.
            var clock = Stopwatch.StartNew();
            while (QueryOwnedBytes(target) >= denseBytes && clock.Elapsed < TimeSpan.FromSeconds(3))
            {
                while (PeekMessage(out var message, window.Handle, 0x0113, 0x0113, 1))
                    _ = DispatchMessage(in message);
                Thread.Sleep(5);
            }

            ulong compactBytes = QueryOwnedBytes(target);
            Assert.True(denseBytes >= compactBytes + 1024u * 1024u,
                $"The idle timer did not reclaim one MiB: dense={denseBytes}, compact={compactBytes}.");
            Assert.True(window.IsVisible);
            Assert.Equal(before, ReadPixels(target));
            Assert.True(QueryOwnedBytes(target) >= (ulong)target.Width * (ulong)target.Height * 4u);
        }
        finally
        {
            window.Close();
        }
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void ClosingBeforeIdleTimer_ReleasesTheTargetWithoutWaitingForTheTimer()
    {
        var window = new Window { Width = 800, Height = 600 };
        try
        {
            window.Show();
            window.ForceRenderFrame();
            var target = Assert.IsType<RenderTarget>(window.RenderTarget);
            Assert.True(QueryOwnedBytes(target) > 0);

            window.Close();

            Assert.False(target.IsValid);
            Assert.Null(window.RenderTarget);
            Assert.False(window.UsesAutomaticEmptySoftwareContext);
        }
        finally
        {
            window.Close();
        }
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void PendingVisualUpdate_DoesNotCompactImmediatelyBeforeItsNextDraw()
    {
        var window = new Window { Width = 800, Height = 600 };
        try
        {
            window.Show();
            window.ForceRenderFrame();
            var target = Assert.IsType<RenderTarget>(window.RenderTarget);
            ulong denseBytes = QueryOwnedBytes(target);
            window.RequestFullInvalidation();
            window.InvalidateWindow();

            // Make the already-armed one-shot due while the next frame is still
            // banked. No dispatcher pumping or forced collection is necessary.
            typeof(Window).GetField("_emptyStorageDueTick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(window, 0L);
            typeof(Window).GetMethod("OnEmptyStorageTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, null);

            Assert.Equal(denseBytes, QueryOwnedBytes(target));
            Assert.Null(typeof(Window).GetField("_emptyStorageTarget", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(window));
        }
        finally { window.Close(); }
    }

    private static ulong QueryOwnedBytes(RenderTarget target)
    {
        Assert.Equal(0, NativeMethods.RenderTargetQueryMainFramebufferOwnedBytes(target.Handle, out ulong bytes));
        return bytes;
    }

    private static byte[] ReadPixels(RenderTarget target)
    {
        target.BeginDraw();
        Assert.Equal(JaliumResult.Ok, target.RequestReadback());
        target.EndDraw();
        byte[] pixels = new byte[checked(target.Width * target.Height * 4)];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, checked((uint)target.Width * 4u), out int width, out int height));
        Assert.Equal(target.Width, width);
        Assert.Equal(target.Height, height);
        return pixels;
    }

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG message, nint window, uint minimum, uint maximum, uint remove);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern nint DispatchMessage(in MSG message);
}
