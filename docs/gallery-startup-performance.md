# Gallery startup performance

This note records the 2026-07-15 startup investigation and optimization of
`samples/Jalium.UI.Gallery`. It intentionally distinguishes externally measured
time-to-window/time-to-response from diagnostic phase timings collected in a
separate, tracing-enabled run.

## Measurement protocol

- Configuration: `Release`, `net10.0-windows`.
- Machine: `JAL`, Windows `10.0.26300.0`, 32 logical processors.
- Six launches per build with a 10-second delay between runs.
- The timestamp is captured with QueryPerformanceCounter immediately before
  `Process.Start`.
- **Window visible** is the first poll for which the process has a non-zero main
  window handle and Win32 `IsWindowVisible` returns true.
- **Window responsive** is the first visible-window probe for which
  `SendMessageTimeout(WM_NULL, SMTO_ABORTIFHUNG, 25 ms)` succeeds.
- Poll interval: 2 ms. Diagnostics were disabled for the formal comparison.
- Run 1 is a **first-run/cold proxy** only. The machine was not restarted and the
  Windows standby/file-system cache was not cleared. Runs 2-6 are reported as the
  hot-start median and range.

The baseline and optimized runs used the same machine, OS, harness, build
configuration, polling interval, run count, and inter-run delay.

## Result

| Metric | First-run proxy, before | First-run proxy, after | Change | Hot median, before | Hot median, after | Change |
|---|---:|---:|---:|---:|---:|---:|
| Window visible | 2544.050 ms | 1041.357 ms | -59.1% | 2069.658 ms | 675.418 ms | -67.4% |
| Window responsive | 3061.729 ms | 1116.005 ms | -63.5% | 2519.325 ms | 789.675 ms | -68.7% |
| Visible-to-responsive gap | 517.679 ms | 74.648 ms | -85.6% | 487.173 ms | 109.623 ms | -77.5% |
| CPU at response | 2546.875 ms | 671.875 ms | -73.6% | 2468.750 ms | 750.000 ms | -69.6% |
| Working set at response | 212.863 MB | 146.004 MB | -31.4% | 213.270 MB | 147.664 MB | -30.8% |

Hot window-visible ranges were `1996.482-2103.789 ms` before and
`655.453-695.142 ms` after. Hot response ranges were `2514.174-2590.962 ms`
before and `756.751-804.765 ms` after. An earlier optimized validation series
contained a `1035.826 ms` response/`300.005 ms` visible-to-response outlier, so
the data still should not be generalized into a hard sub-second SLA.

## Original startup path and confirmed bottlenecks

The original synchronous path was:

`Main` -> render context -> Generic Host defaults/build -> `Application` and
theme -> construct all 16 Gallery sections -> host start -> `Window.Show` ->
native handle/surface -> full-tree layout and first render -> loaded/shown
handlers -> message pump.

The Gallery does not perform database migration, login, update checks, network
requests, plug-in scanning, or file-system discovery during startup. The delay
was dominated by UI-affine construction and rendering rather than those absent
subsystems.

Tracing-enabled runs confirmed the following material costs:

- Eager construction of the complete Gallery tree: approximately
  `1533-1944 ms`; editor controls alone cost approximately `370-390 ms` before
  they had a viewport.
- Default process-icon extraction/PNG conversion on the UI path: `275.889 ms`.
- Initial full-tree layout/render: approximately `495-524 ms` before deferral.
- Theme initialization: approximately `170-272 ms`.
- Generic Host start itself was only approximately `5-17 ms` and was not a
  primary bottleneck.

After optimization, a diagnostic run recorded `BuildMainWindow=85.866 ms`,
`InitialLayoutAndRender=92.293 ms`, asynchronous icon extraction `3.747 ms`, and
UI icon application `1.091 ms`. The largest remaining pre-visible phase was
`Window.RenderTargetCreate=268.820 ms`, followed by
`Application.ThemeInitialize=231.077 ms`.

## Architecture changes

- Added `Jalium.UI.Diagnostics.StartupDiagnostics`, including named start/end
  events, duration, UTC/process-relative timestamps, process/thread identity,
  UI-thread identity, and a UI-blocking annotation. It supports the
  `Jalium-UI-Startup` EventSource and opt-in structured text output.
- Instrumented the builder, host, application, GPU prewarm, window construction,
  native show, first render, loaded/shown handlers, and first input-priority
  callback.
- Kept the Generic Host lifecycle and DI behavior, but disabled unused default
  configuration/logging setup for the Gallery sample.
- Preserved D3D12/Vulkan selection and the Impeller rendering contract while
  allowing render-context prewarm to overlap application/theme construction.
- Made the initial Gallery tree a real, interactive page header and complete
  Buttons section. The other 15 sections are created after `Shown` at background
  dispatcher priority, in original order, with loading status, cancellation,
  per-section/card exception isolation, and input/render yields.
- Split the three heaviest deferred sections into card-sized UI-thread slices.
  In the measured trace, the longest remaining slice was `49.526 ms`.
- Deferred `EditControl` native font/layout initialization until it has a
  non-zero render viewport.
- Moved best-effort process-icon extraction off the UI thread and applies raw
  BGRA pixels on the dispatcher, avoiding synchronous PNG encoding/decoding.

The deferred pipeline completed 81 started/completed stage pairs with no failure
or cancellation milestones in the validation trace. It completed approximately
917 ms after the first-input-ready milestone, while yielding between sections
and heavy cards.

## Using startup diagnostics

Set either or both environment variables before launching an application:

```powershell
$env:JALIUM_STARTUP_TRACE = '1' # writes structured lines to stderr
$env:JALIUM_STARTUP_TRACE_FILE = 'D:\temp\jalium-startup.log'
dotnet run --project samples\Jalium.UI.Gallery -c Release
```

For EventPipe/ETW collection, enable the `Jalium-UI-Startup` EventSource. Text
file output is buffered and flushed at the main-window and deferred-completion
milestones. With no output consumer enabled, the instrumentation takes the
disabled fast path and performs no file I/O or timestamp formatting.

## Verification and remaining work

- Fresh Gallery Release build: 0 warnings, 0 errors.
- Focused new startup/editor/icon tests: 4 passed. The three affected test
  classes (`StartupDiagnosticsTests`, `EditControlTests`, and
  `TitleBarHitTestTests`), plus the window-lifetime regression, passed 122/122
  with no skips.
- The full `Jalium.UI.Tests` project ran 4070 tests: 4058 passed and 12 failed.
  Nine failures require the already-missing `Jalium.UI.DeviceLostHarness.exe`;
  one is an independently reproducible Vulkan DPI pixel mismatch in the dirty
  worktree; the remaining application-resource and arrow-geometry failures pass
  in isolation and are full-suite shared-state/order issues. None is in the
  focused startup regression set.
- UI Automation validation changed an initial Toggle before deferred completion,
  observed the deferred-completion milestone with no failure event, and changed
  the Toggle again afterward.
- `git diff --check`: passed. Existing unrelated worktree changes were preserved.

The next meaningful optimization targets are render-target creation and theme
resource initialization. They should be profiled independently before changing
backend lifetime or theme-loading semantics. A strict cold-start result still
requires a controlled reboot or an elevated, reproducible standby-cache purge.

Measurement limitations: `IsWindowVisible` is not proof that DWM has presented
the first frame; the WM_NULL probe proves a momentary message-pump response, not
that every feature is loaded; CPU time is process-wide; working set is sampled
at first response and therefore excludes much of the deferred steady-state UI;
and phase numbers come from a separate single run with tracing overhead.
