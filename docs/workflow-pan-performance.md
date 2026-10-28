# Workflow canvas performance changes

This change collects the retained framework optimizations from the VeloxDev
Workflow investigation, including the work preceding the protected stroke cache.
The temporary VeloxDev checkout and profiling traces are benchmark fixtures
outside this repository.

| Area | Retained behavior |
| --- | --- |
| Layout convergence | ScrollViewer requests corrective measurement when its arranged viewport changes, allowing stable smaller arrange slots to settle. |
| Frame capture | Coalesce superseded recording requests, retain ticket ownership, refresh stale captures, and anchor light-frame capture deadlines to the refresh grid. Preserve heavy-frame overlap, hidden-window dirty state, shutdown and timer-failure recovery. |
| Drawing lifetime | Explicit shared ownership protects visual caches, nested drawings, retries and worker replay. Large recordings use bounded, cleared command-buffer segments; abandoned captures release their ownership. |
| Managed allocation | Keep small effective-property snapshots inline, compact dependency-value entries, keep two properties inline, and allocate optional binding/animation/event state lazily. Collection storage retains its owner directly; single-parent Freezable notifications avoid temporary delegates. |
| Drawing inputs and text | Reuse immutable brush/pen inputs, skip unused whole-frame geometry bounds, borrow polygon coordinate storage, and retain local coordinates across canvas translations. Bound TextBox render-line reuse to 256 entries and text measurement results to 8,192 entries. CSS length-context equality avoids boxing. Guarded primitive constructors preserve normal mutation callbacks. |
| Dispatcher | Coalesce Windows wake notifications before entering the wake-resource gate. Queue publication and wake-resource teardown use separate gates while retaining input fairness and disposal guards. |
| Native geometry and batching | Reuse stroke contours and batch storage, preserve exact repeated-start handling, batch eligible polylines, and transport immutable coverage-mask runs through compact coverage/ellipse records. |
| Native caches and presentation | Protect expensive analytic masks from animated-key churn, recycle uniquely owned masks and LRU nodes, upload only dirty glyph rows, reuse bounded Vello output capacity, and reclaim idle resources after GPU completion. Apply engine requests at frame boundaries and retain guarded adaptive latency selection. |
| Diagnostics | FrameHistory includes capture/replay timestamps. Opt-in RenderPipelineTrace records bounded stage/ticket events and allocates no event ring when disabled. |

The property, event, collection and drawing changes retain layer precedence,
value identity, callback order, freezing, borrowed-mask immutability and painter
order. Their regression cases accompany the implementations.

## Storage and lifetime bounds

The primary analytic stroke cache retains 512 entries. Its secondary cache
admits masks containing at least 128 rectangles and retains at most 1,024 entries
and 32 MiB of rectangle-vector capacity. The primary and secondary may each
retain one uniquely owned allocation of at most 1 MiB for recycling. Idle batch
reclamation drops this scratch storage. Cache limits exclude container overhead
and allocations still retained by other owners; shared payloads must not be
counted twice when estimating total memory.

Impeller batch storage retains at most 16 batches and 8 MiB per engine. Vello's
free output pool retains the existing 64 MiB / 512-entry bounds. Existing GPU
fences continue to govern upload and resource retirement.

## Verification

The native checks run without a GPU:

```powershell
cmake -S tests/native/ScanlineRasterizer -B out/native-scanline
cmake --build out/native-scanline --config Debug
ctest --test-dir out/native-scanline -C Debug --output-on-failure
cmake --build out/native-scanline --config Release
ctest --test-dir out/native-scanline -C Release --output-on-failure
```

The seven executables cover scanline output, engine and frame-latency selection,
simple strokes, mask bounds, cache ownership/budgets and polyline flattening.
The standalone generic cache checks remain in `tests/native/StrokeMaskCache`.

On Windows x64, the combined branch also passed a full native Release build,
a D3D12 Debug build, and compilation of `Jalium.UI.Tests`. The selected managed
regression run contains 691 cases: 684 passed, seven Vulkan cases were skipped
because that backend was unavailable, and none failed. The worker-publication
fixture initializes its capture scheduler; the atlas-growth fixture observes
actual initial capacity rather than assuming a backend startup size.

Fresh native builds from the PR base and combined branch produced identical GPU
readback for 5,440 frame comparisons using the newly built managed framework:
2,880 DPI/width/alpha/transform/clip/opacity cases and 2,560 cache-churn frames.
In each 640-key churn replay, the second pass changed from 640 misses to 640
hits. Loaded native hashes were checked against the corresponding build.

These are finite correctness and cache-reuse checks on the combined PR binary.
Earlier performance measurements used their recorded workload and runtime
versions; they are not a new maximum-frame-rate measurement of this combined
branch. Capture timestamps describe scheduling age rather than display scanout.
