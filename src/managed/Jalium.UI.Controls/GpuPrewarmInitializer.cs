using System.Threading.Tasks;
using Jalium.UI.Diagnostics;
using Jalium.UI.Interop;

namespace Jalium.UI.Controls;

/// <summary>
/// Kicks off render-context (D3D12 / Vulkan device + DXGI factory) creation on a
/// background thread when the first <see cref="Application"/> is constructed. The first render
/// context construction is dominated by ~200–400 ms of native device init that
/// does not depend on the HWND, so doing it eagerly in parallel with the remaining
/// <see cref="Application"/> setup, <c>ThemeManager.Initialize</c>, and the user's
/// <c>new MainWindow()</c> avoids
/// blocking the UI thread inside <c>EnsureHandle</c> → WM_SIZE →
/// <c>EnsureRenderTarget</c>.  By the time the UI thread reaches that point the
/// static <see cref="RenderContext.Current"/> is already populated and
/// <see cref="RenderContext.GetOrCreateCurrent"/> returns instantly.
/// Failures are swallowed silently — the synchronous UI-thread call site
/// retries through <see cref="RenderContext.GetOrCreateCurrent"/> normally.
/// </summary>
internal static class GpuPrewarmInitializer
{
    public static void Prewarm()
    {
        _ = Task.Run(static () =>
        {
            try
            {
                using var prewarm = StartupDiagnostics.Begin(
                    "GpuPrewarm.CreateRenderContext",
                    blocksUiThread: false);
                _ = RenderContext.GetOrCreateCurrent();
            }
            catch { /* UI thread will retry on Show */ }
        });
    }
}
