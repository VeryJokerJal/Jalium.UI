# Render-thread design (JALIUM_RENDER_THREAD=1, default OFF)

Goal: move the GPU-blocking work (D3D12 BeginDraw frame-latency-waitable wait, EndDraw
Present, next-frame command-allocator fence wait) off the UI/message-pump thread onto a
dedicated render thread, so input + animation ticks are never blocked by the ~110 ms
windowed-iGPU DWM present. WPF model. Env-gated; default path byte-for-byte unchanged.

## Central constraint (verified)

Jalium renders **immediately during the visual-tree walk**: `Window.RenderFrame` walks the
tree with `Render(_drawingContext)` where `_drawingContext` is a `RenderTargetDrawingContext`
bound to the live swap-chain `RenderTarget`; every `Draw*/Fill*/Push*/Clear` is an immediate
P/Invoke into the open D3D12 command list (`RenderTargetDrawingContext.cs`). There is **no
standalone whole-frame display list**. The per-visual retained cache (`DrawingRecorder`→
`Drawing`→`DrawingReplayer`) replays into the SAME live context and `DrawingRecorder` proxies
inter-visual `Offset`/clip/opacity/transform (from `Visual.RenderChildVisualInline`) straight
to the live target — so it is NOT a whole-frame handoff unit.

So a render thread requires first building a **whole-frame recorder** that captures the entire
tree (including inter-visual pushes) as pure data, then replaying it on the render thread
between BeginDraw/EndDraw.

### Schema-gap risk
`DrawCommandKind` covers: DrawLine, DrawRectangle, DrawRoundedRectangle(+Corner), DrawEllipse,
DrawText, DrawGeometry, DrawImage, DrawBackdropEffect, PushTransform/Clip/Opacity, Pop,
PushEffect/PopEffect, DrawContentBorder, DrawPoints, DrawLines, DrawLiquidGlass.
RenderTargetDrawingContext has a WIDER surface (DrawVideoSurface, BlitInkLayer, per-corner /
aliased clips, bitmap scaling modes, HLSL shader effects, …). Content that maps onto the
generic schema records fine; anything that does not currently **Bypasses** the retained cache
(Perf tab "Bypass"). For a pixel-correct render-thread path the recorder must EITHER extend the
schema to cover the full surface OR set a "not fully recordable" flag and fall back to the
existing direct-render path for that frame.

## Architecture
- Handoff unit `FrameCapture`: whole-frame `Drawing` (immutable `DrawCommand[]`) + per-frame
  metadata (dirty rects, clip region, `fullInvalidation`, window bounds, `SimplifyGpuEffects`).
- UI thread per frame: Win32 input, CompositionTarget ticks, `UpdateLayout`, dirty-region
  compute (all pure CPU), then RECORD the tree into a new `FrameRecorder`, `Commit()` →
  `FrameCapture`, publish to the channel, return to GetMessage. Clear `_dirtyElements` only
  after a successful publish (mirror today's clear-after-TryBeginDraw).
- Render thread ("Jalium.Render"): wait on frame-latency waitable → `TryBeginDraw` (allocator
  fence wait) → Clear + `DrawingReplayer.Replay(capture.Drawing, _renderThreadDrawingContext)`
  + overlay → `TryEndDraw` (Present + DComp Commit) → loop. Owns ALL native RT calls.

## Handoff + back-pressure
Single-slot mailbox, latest-frame-wins, 1-deep in-flight gate. `_pendingFrame` under
`_channelLock`; `AutoResetEvent _frameAvailable`; `volatile bool _renderBusy`. Producer overwrites
the pending capture if not yet picked up (coalesce); UI NEVER blocks on present. If render thread
busy, UI skips recording this tick and sets `RenderFlag_Requested` (existing reschedule) — bounds
memory to 1 pending + 1 in-flight, returns to pump immediately (avoids the v5 synchronous-block
anti-pattern). No lock ever held across a native call.

## Thread-safety
Render thread exclusively owns: the `RenderTarget` + `RenderTargetDrawingContext`, all P/Invoke
draw calls, TryBeginDraw/TryEndDraw/Resize/Dispose, the frame-latency waitable (single consumer),
command list/allocators/fence, glyph atlas, lazy bitmap GPU upload. UI thread must STOP touching
`_drawingContext`/`RenderTarget` once it publishes — the render thread now creates + maintains
(`SimplifyGpuEffects`, `DrainPendingRetainedLayers`) and trims (`TrimCacheIfNeeded`) `_drawingContext`
inside `PresentCaptureOnRenderThread`; the UI thread creates it only on the inline path, AFTER the
publish gate. `_isDrawing` is `volatile` (read/written across the drain barrier). Bitmap graveyard
`RetireGpuResource` is already mutex-guarded.

CORRECTION (verified against the code): `Brush`/`Pen`/`Transform` are NOT `Freezable` in this
codebase — only `Geometry` has a (shallow) `Freeze()`/`Clone()`. So unsafe live refs are
SNAPSHOTTED BY VALUE at record time (`DrawInputSnapshotter`, whole-frame path only), not frozen:
gradient `Brush` → memoised deep copy keyed by `GradientBrush.ComputeContentHash` (keeps the native
brush identity cache hitting across unchanged frames); `Transform` → `new MatrixTransform(t.Value)`;
gradient-brush `Pen` → value copy; `WriteableBitmap`/video image → trip a direct-render fallback
(per-frame-mutating, can't be value-copied cheaply). `SolidColorBrush`/simple `Pen`/`FormattedText`
are already value-copied by `DrawingObjectPool`, so they pass through untouched. Unfrozen `Geometry`
is left as-is (per-OnRender temporaries don't escape the walk; long-lived animated geometry should
be `Freeze()`d, the WPF convention).

## Lifecycle (drain/pause protocol)
`RequestRenderThreadIdle()`: set `_renderThreadPause`, signal `_frameAvailable`, wait
`_renderThreadIdle` — render thread finishes any in-flight BeginDraw..EndDraw, parks. Route ALL
of these through it BEFORE touching the RT: resize (`TryResizeRenderTarget`/`RenderTarget.Resize`),
device-lost recovery (`TryRecoverFromRenderPipelineFailure` — marshal from render thread to UI via
dispatcher), WebView composition swap (`EnsureCompositionRenderTargetForEmbeddedContent` — sync
WM_PAINT hazard), shutdown. Shutdown order = stop flag → signal → Join → THEN RenderTarget.Dispose
(waitable/fence handles closed there). DComp is thread-affine → RT+DComp creation must happen ON
the render thread under the gate. Re-acquire the NEW waitable after any RT recreate.

## Increments (each build + verify before next)
1. [DONE] `FrameCapture` (inline holder in Window.cs) + `DrawingRecorder.BindWholeFrame` record-
   whole-tree-then-replay on the SAME UI thread. Pixel-identity mechanized: `WholeFrameRecorderTests`
   asserts the replayed DrawCommand stream equals a direct Render walk (value-token + push/pop
   balance + SetOffset round-trip), plus a reflection inventory test that fails if a new
   `RenderTargetDrawingContext` override of a `DrawingContext` virtual ships without a recordable
   command.
1a. [DONE] Schema-gap guard. The recorder never sees RTDC-only methods reached via
   `is RenderTargetDrawingContext` casts (WebView windowless punch, video, ink-layer blit,
   transitions, …). Call sites flip a thread-static via `DrawingContext.MarkCurrentFrameUnrecordable`;
   `Drawing.FullyRecordable` carries it; `RenderTreeOrCapture` falls back to direct render for that
   frame; the render-thread path latches `_rtActive=false` (`StopRenderThread` + reschedule inline).
2. [DONE] Render thread (`Jalium.Render`) does TryBeginDraw+Replay+TryEndDraw; UI records + publishes
   (latest-frame-wins mailbox + `_rtBusy` back-pressure). Thread-safety fixes landed: the render
   thread exclusively owns `_drawingContext` (was a CRASH-level UI/render race — `TrimCacheIfNeeded`
   destroying native brushes vs `Replay` reading them); drain (`RequestRenderThreadIdle` now returns
   success) wraps resize / composition swap / device-lost recovery; pacer ⇄ render-thread are
   mutually exclusive on the waitable; `_isDrawing` is volatile. RUNTIME verification (deadlock-free,
   pixel A/B, input latency) still requires a real GPU — not mechanizable headlessly.
3. [DONE] Freeze-clone via `DrawInputSnapshotter` (snapshot-by-value — see Thread-safety correction).
   Pool stays single-producer (UI thread does CreateFrameRecorder/FinishRecord; render thread only
   Replays, read-only), so the existing single lock suffices; no per-thread pool needed.

STATUS (2026-06): increments 1 / 1a / 2 / 3 are code-complete + headless-tested; `JALIUM_RENDER_THREAD`
stays DEFAULT-OFF (opt-in) until the runtime gates are demonstrated on the target GPU. Default-on
gating criteria: (1) equality tests green; (2) schema-gap fallback proven; (3) freeze-clone
mutation-isolation green; (4) pool single-producer confirmed; (5) real-GPU deadlock-free through
publish/drain/resize/device-loss/shutdown; (6) pixel A/B vs default-OFF; (7) measured input latency
improved with FPS unchanged. Criteria 1–4 are mechanized (green); 5–7 need the target hardware.

## Adversarial risks → mitigations
- N+1 recorded into objects replayed for N → FrameCapture owns its DrawCommand[] copy + freeze-clone
  unsafe refs; mailbox swap is pointer-only under lock.
- Two waiters on auto-reset waitable (v5 trap) → only render thread waits; mutually exclusive with
  JALIUM_ENABLE_FRAME_PACER.
- EndDraw on disposed/resized RT (use-after-free) → RequestRenderThreadIdle drain before any RT
  Dispose/Resize/null.
- Deadlock (UI drains while render waits frame) → pause flag checked on wake → render parks.
- Closed-handle wait on shutdown → Stop→Join→Dispose ordering.
- `[ThreadStatic] _isDrawing/_drawTextDepth` split → BeginDraw/Replay/EndDraw all on the one render
  thread; UI never enters Draw*.
- Glyph-atlas mid-frame reset → atlas grow/reset at BeginFrame on render thread; rasterize follows
  replay thread (single-threaded as required).

Key files: `Jalium.UI.Controls/Window.cs` (gate, render-thread loop, channel, lifecycle drains);
NEW `Jalium.UI.Media/Rendering/FrameRecorder.cs`, `FrameCapture.cs`; `MediaRenderCacheHost.cs`
(thread-aware pool); `DrawingRecorder.cs` (freeze-clone). No native changes for increments 1–2.

Reference: derived from a 5-agent design workflow (2026-05-30). See project memory
`project_d3d12_vsync_contradiction_v6` for the latency root-cause this addresses.
