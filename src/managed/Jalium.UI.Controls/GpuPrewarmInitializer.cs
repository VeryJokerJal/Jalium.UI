using System.Threading.Tasks;
using Jalium.UI.Diagnostics;
using Jalium.UI.Interop;

namespace Jalium.UI.Controls;

/// <summary>
/// Optionally loads the preferred GPU backend and creates its lightweight native
/// context on a background thread once a caller has established real GPU demand.
/// Device, queue and swap-chain creation still occurs at RenderTarget creation;
/// this method must therefore not be scheduled merely because an Application was
/// constructed. In particular, doing so would defeat the empty-window Software
/// path by publishing a GPU Current before the first Window is classified.
/// Failures are swallowed — the synchronous target-creation path retains its
/// normal diagnostics and fallback behavior.
/// </summary>
internal static class GpuPrewarmInitializer
{
    private static int s_started;

    public static void PrewarmForGpuDemand()
    {
        if (Interlocked.Exchange(ref s_started, 1) != 0)
        {
            return;
        }

        _ = Task.Run(static () =>
        {
            try
            {
                using var prewarm = StartupDiagnostics.Begin(
                    "GpuPrewarm.CreateBackendContext",
                    blocksUiThread: false);
                _ = RenderContext.GetOrCreateCurrent();
            }
            catch { /* UI thread will retry on Show */ }
        });
    }
}
