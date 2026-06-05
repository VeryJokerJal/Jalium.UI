using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Jalium.UI.Interop;

namespace Jalium.UI.Controls;

/// <summary>
/// Kicks off render-context (D3D12 / Vulkan device + DXGI factory) creation on a
/// background thread the instant Jalium.UI.Controls is loaded.  The first render
/// context construction is dominated by ~200–400 ms of native device init that
/// does not depend on the HWND, so doing it eagerly in parallel with
/// <c>AppBuilder.Build</c>, the user's <c>Application</c> subclass construction,
/// <c>ThemeManager.Initialize</c>, and the user's <c>new MainWindow()</c> avoids
/// blocking the UI thread inside <c>EnsureHandle</c> → WM_SIZE →
/// <c>EnsureRenderTarget</c>.  By the time the UI thread reaches that point the
/// static <see cref="RenderContext.Current"/> is already populated and
/// <see cref="RenderContext.GetOrCreateCurrent"/> returns instantly.
/// Failures are swallowed silently — the synchronous UI-thread call site
/// retries through <see cref="RenderContext.GetOrCreateCurrent"/> normally.
/// </summary>
internal static class GpuPrewarmInitializer
{
    /// <summary>
    /// Stopwatch timestamp captured the moment Jalium.UI.Controls's module
    /// initializer fires.  Used by Window.Show's startup trace to split the
    /// "process start → window visible" interval into:
    ///   (a) process-start → module-load  — CLR boot + assembly JIT cost
    ///   (b) module-load   → app-ctor     — user Main + AppBuilder.Build
    ///   (c) app-ctor      → window-visible — covered by Application ctor
    ///                                        and Window.Show traces.
    /// </summary>
    public static long ModuleLoadTimestamp;

    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "The whole point is to start native GPU device init in parallel the instant the control library loads, before the consumer's Application/Window construction reaches the UI thread; an explicit init call would run too late to overlap. Failures are swallowed and retried on the synchronous UI-thread path.")]
    public static void Prewarm()
    {
        // Capture the module-load instant.  Stopwatch.GetTimestamp is monotonic
        // and matches the timestamps used elsewhere in the startup trace.
        ModuleLoadTimestamp = Stopwatch.GetTimestamp();

        _ = Task.Run(static () =>
        {
            try { _ = RenderContext.GetOrCreateCurrent(); }
            catch { /* UI thread will retry on Show */ }
        });
    }
}
