using System.Runtime.InteropServices;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Tests;

/// <summary>
/// Smoke coverage for the Vulkan backend: resource creation plus one real
/// BeginDraw → draw calls → EndDraw frame against a hidden native window.
/// </summary>
/// <remarks>
/// <para>
/// Vulkan-only tests are gated by <see cref="RequiresBackendFactAttribute"/>
/// so a host without the backend reports them as SKIPPED instead of silently
/// passing (the old in-body <c>if (IsBackendAvailable(...) == 0) return;</c>
/// pattern recorded PASS and made Vulkan regressions invisible).
/// </para>
/// <para>
/// These tests create real native <see cref="RenderContext"/> instances, which
/// touch the process-wide <see cref="RenderContext.Current"/> static, so the
/// class joins the serialized "Application" collection — otherwise a parallel
/// class (e.g. <see cref="RenderContextBackendSwitchTests"/>, whose setup
/// disposes <c>RenderContext.Current</c>) could tear down a context these
/// tests are still using.
/// </para>
/// <para>
/// Note: the JALIUM_EXPERIMENTAL_VULKAN env toggle these tests used to set was
/// removed — the native gate (IsExperimentalVulkanEnabled) had no remaining
/// consumers. Availability is decided by IsBackendAvailable alone.
/// </para>
/// </remarks>
[Collection("Application")]
public sealed class VulkanBackendSmokeTests
{
    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void VulkanContext_CanCreateBasicResources_WhenExperimentalBackendAvailable()
    {
        using var context = new RenderContext(RenderBackend.Vulkan);
        Assert.Equal(RenderBackend.Vulkan, context.Backend);

        using var brush = context.CreateSolidBrush(1f, 0f, 0f, 1f);
        Assert.True(brush.IsValid);

        using var format = context.CreateTextFormat("Segoe UI", 14f);
        Assert.True(format.IsValid);

        var metrics = format.MeasureText("Jalium", 1000f, 1000f);
        Assert.True(metrics.LineHeight > 0f);
    }

    [Fact]
    public void BitmapImage_FromPixels_PreservesRawPixelMetadata()
    {
        var pixels = new byte[]
        {
            0x00, 0x00, 0xFF, 0xFF,
            0x00, 0xFF, 0x00, 0xFF
        };

        var image = BitmapImage.FromPixels(pixels, 2, 1);

        Assert.Equal(2d, image.Width);
        Assert.Equal(1d, image.Height);
        Assert.Equal(2, image.PixelWidth);
        Assert.Equal(1, image.PixelHeight);
        Assert.Equal(8, image.PixelStride);
        Assert.NotNull(image.RawPixelData);
        Assert.Equal(pixels, image.RawPixelData);
    }

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void VulkanContext_CanCreateBitmapFromRawPixels_WhenExperimentalBackendAvailable()
    {
        using var context = new RenderContext(RenderBackend.Vulkan);
        var pixels = new byte[]
        {
            0x00, 0x00, 0xFF, 0xFF,
            0x00, 0xFF, 0x00, 0xFF,
            0xFF, 0x00, 0x00, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF
        };

        using var bitmap = context.CreateBitmapFromPixels(pixels, 2, 2);

        Assert.True(bitmap.IsValid);
        Assert.Equal(2u, bitmap.Width);
        Assert.Equal(2u, bitmap.Height);
    }

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void VulkanContext_CanCreateBitmapFromEncodedBmp_WhenExperimentalBackendAvailable()
    {
        using var context = new RenderContext(RenderBackend.Vulkan);
        byte[] bmpBytes =
        [
            0x42, 0x4D, 0x3A, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x36, 0x00, 0x00, 0x00, 0x28, 0x00,
            0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00,
            0x00, 0x00, 0x01, 0x00, 0x20, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x13, 0x0B,
            0x00, 0x00, 0x13, 0x0B, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0xFF
        ];

        using var bitmap = context.CreateBitmap(bmpBytes);

        Assert.True(bitmap.IsValid);
        Assert.Equal(1u, bitmap.Width);
        Assert.Equal(1u, bitmap.Height);
    }

    /// <summary>
    /// Real rendering smoke: drives one full Vulkan frame — swapchain render
    /// target on a hidden native window, BeginDraw, Clear + rectangle fill /
    /// stroke + line, EndDraw (present) — and asserts every step reported
    /// JALIUM_OK. The resource-creation smokes above never open a drawing
    /// session, so before this test a Vulkan backend that could not render a
    /// single frame still passed the suite.
    /// </summary>
    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void VulkanRenderSmoke_BeginDrawEndDraw_Succeeds()
    {
        const int width = 256;
        const int height = 256;

        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(RenderBackend.Vulkan);
        // The RenderContext ctor silently falls back to Software when the
        // requested backend cannot materialize — assert we really got Vulkan.
        Assert.Equal(RenderBackend.Vulkan, context.Backend);

        // Ctor throws RenderPipelineException when native creation fails.
        using var renderTarget = context.CreateRenderTarget(window.Hwnd, width, height);
        Assert.True(renderTarget.IsValid);
        Assert.Equal(RenderBackend.Vulkan, renderTarget.Backend);

        using var fillBrush = context.CreateSolidBrush(0.9f, 0.2f, 0.1f);
        using var strokeBrush = context.CreateSolidBrush(0.1f, 0.4f, 0.9f);
        Assert.True(fillBrush.IsValid);
        Assert.True(strokeBrush.IsValid);

        // TryBeginDraw returns true only when native BeginDraw returned
        // JALIUM_OK (recoverable InvalidState → false, anything else throws),
        // so this asserts the begin return code.
        Assert.True(renderTarget.TryBeginDraw());
        Assert.True(renderTarget.IsDrawing);

        renderTarget.Clear(0.15f, 0.15f, 0.2f);
        renderTarget.FillRectangle(16f, 16f, 128f, 96f, fillBrush);
        renderTarget.DrawRectangle(24f, 24f, 96f, 64f, strokeBrush, strokeWidth: 2f);
        renderTarget.DrawLine(0f, 0f, width, height, strokeBrush, strokeWidth: 3f);

        // EndDraw submits and presents the frame; draw-call failures surface
        // through its native return code. Ok == JALIUM_OK.
        Assert.Equal(JaliumResult.Ok, renderTarget.TryEndDraw());
        Assert.False(renderTarget.IsDrawing);
    }
}

/// <summary>
/// A real (non-message-only) top-level Win32 window that is never shown, for
/// tests that need a valid HWND to back a swapchain surface. Message-only
/// windows (HWND_MESSAGE parent) cannot host a swapchain, so this creates an
/// invisible WS_POPUP window of the requested client size using the system
/// "Static" window class (no RegisterClass / WndProc needed).
/// </summary>
internal sealed partial class HiddenNativeWindow : IDisposable
{
    private const uint WS_POPUP = 0x8000_0000;

    private nint _hwnd;

    public HiddenNativeWindow(int width, int height)
    {
        _hwnd = CreateWindowExW(
            0, "Static", null, WS_POPUP,
            0, 0, width, height,
            nint.Zero, nint.Zero, nint.Zero, nint.Zero);
        if (_hwnd == nint.Zero)
        {
            throw new InvalidOperationException(
                $"CreateWindowExW failed (Win32 error {Marshal.GetLastPInvokeError()}).");
        }
    }

    public nint Hwnd => _hwnd;

    public void Dispose()
    {
        var hwnd = _hwnd;
        _hwnd = nint.Zero;
        if (hwnd != nint.Zero)
        {
            _ = DestroyWindow(hwnd);
        }
    }

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(
        uint exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);
}
