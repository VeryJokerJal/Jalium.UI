# Jalium.UI Startup Allocation Probe

This project is a diagnostic companion to `Jalium.UI.MinimalWindowProbe`. It
keeps the same seven source-project references and the same `LowMemory`
NativeAOT settings, then records managed allocation and collection counters at
specific startup boundaries.

The probe is deliberately not a memory-budget result. Its event handlers,
`DispatcherTimer`, snapshots, marker strings, and console writes add managed
allocations of their own. Every entry marker includes
`diagnosticOnly=true samplingOverhead=true budgetProbe=false`; use the output to
identify a large startup phase, then confirm the budget with the uninstrumented
minimal probe and the prime external working-set sampler.

Each `## STARTUP_ALLOC` stage reports:

- `GC.GetTotalAllocatedBytes(false)` as `allocatedBytes`;
- `GC.GetTotalMemory(false)` as `heapBytes`;
- `GC.CollectionCount(0..2)` as `gen0`, `gen1`, and `gen2`;
- deltas from the preceding logical snapshot where that interval is isolated.

The application/theme snapshots are captured before their markers are emitted.
The window-construction and first-`Shown` snapshots are likewise captured before
their marker strings are formatted. The `stable_10s` interval necessarily
includes the three first-show markers, timer creation, and ten seconds of normal
visible-window activity. No stage calls `GC.Collect`, trims the working set,
enumerates processes/modules, or disables a framework feature.

The stages are `entry`, `application_before`, `application_after`,
`theme_verified`, `window_before`, `window_after`, `window_shown`, `stable_10s`,
and `run_return`. Theme verification requires at least three merged dictionaries
and the `typeof(Button)` resource without realizing the deferred style. After
the ten-second timer fires, the normal 800 x 600, `Content = null` window closes;
the default `OnLastWindowClose` path ends `Application.Run`, whose return code is
reported and returned. Early or exceptional exits emit a clear error marker and
return 1.

## Native startup layers

The same executable also contains two explicitly diagnostic-only startup modes.
Keeping them in the same NativeAOT image prevents the trimmer from producing a
smaller comparison program that happens to omit the normal Jalium path:

- `--native-shell-only` skips `Application` and `Window` construction at run
  time, registers a Win32 window class, and shows one ordinary 800 x 600
  `WS_OVERLAPPEDWINDOW` for ten seconds.
- `--application-native-shell` first constructs the normal `Application` and
  verifies the complete theme, including the `typeof(Button)` resource, then
  shows the identical native window without constructing a Jalium `Window`.

Both modes use an `UnmanagedCallersOnly` window procedure and a window-owned
`SetTimer`/`WM_TIMER`; they create no managed worker thread. Other command-line
arguments are ignored once either mode is selected, so measurement harness
arguments such as a one-window setting do not change this single-window
diagnostic. Supplying both mode switches is a usage error.

Their markers include `diagnosticOnly=true`, `frameworkWindow=false`, and
`acceptanceProbe=false`. They only separate process/runtime, `Application` and
theme, and native-shell startup costs. They are not evidence that a visible
Jalium framework window satisfies the 20 MB working-set budget.

## Publish

Publish from the repository root with dedicated absolute paths:

```powershell
$repoRoot = (Resolve-Path .).Path
$buildRoot = Join-Path $repoRoot 'artifacts\empty-window-memory\startup-allocation-layers-build'
$publishDir = Join-Path $repoRoot 'artifacts\empty-window-memory\startup-allocation-layers-aot'

dotnet publish .\tests\Jalium.UI.StartupAllocationProbe\Jalium.UI.StartupAllocationProbe.csproj `
  -p:PublishProfile=LowMemory `
  -p:JaliumBuildRoot="$buildRoot" `
  -o "$publishDir"
```

Publishing must not launch the executable. The prime measurement run owns the
visible UI and should combine these markers with its native-module profile to
locate the largest startup phase. The original
`startup-allocation-probe-aot` publication is retained as the frozen first
version and must not be overwritten by this layered build.
