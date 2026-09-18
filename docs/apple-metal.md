# Apple Metal backend

## Supported source targets

- Apple Silicon macOS 15 or later (`net10.0-macos`, `osx-arm64`).
- iOS/iPadOS 18 or later (`net10.0-ios`, device and arm64 simulator).
- tvOS 18 or later (`net10.0-tvos`, device and arm64 simulator).
- visionOS 2 native renderer/platform validation. There is intentionally no
  managed package until the official .NET Apple SDK exposes a visionOS TFM.

The Metal backend renders into a persistent GPU scene texture and presents it
through `CAMetalLayer`. CoreGraphics is limited to image/font decoding and the
explicit Software fallback; the Metal frame is never CPU-rasterized or uploaded
as a full BGRA bitmap.

## Build

The pinned toolchain is recorded in `eng/apple/toolchain.lock.json`.
`build-native.sh` first builds the pinned static DXC and SPIRV-Cross slice via
`prepare-shader-toolchain.sh`; an Apple package configure now fails instead of
silently shipping without dynamic `SourceHlsl` support.

```bash
eng/apple/build-native.sh macos Release
eng/apple/build-native.sh ios Release
eng/apple/build-native.sh iossimulator Release
eng/apple/build-native.sh tvos Release
eng/apple/build-native.sh tvossimulator Release
eng/apple/build-native.sh visionos Release
eng/apple/build-native.sh visionsimulator Release
eng/apple/package-xcframework.sh Release
```

Embedded targets produce static archives. `package-xcframework.sh` merges the
complete core/platform/Metal/software/text/media/browser set into
`src/native/artifacts/apple/JaliumNative.xcframework`. The iOS/tvOS entry
packages bind it with `NativeReference`, `ForceLoad`, and compile their P/Invoke
stubs against `__Internal`.

Vello shaders are generated from the same checked-in SPIR-V that Vulkan uses:

```text
canonical HLSL -> DXC SPIR-V -> SPIRV-Cross MSL -> xcrun metal -> metallib
```

`eng/apple/dependencies.lock.json` pins both compiler revisions.
The generator reflects every compiled SPIR-V stage/permutation and rejects resource-binding or
threadgroup-size drift before compiling `jalium_vello.metallib`. The embedded
core MSL is extracted byte-for-byte and compiled offline to
`jalium_core.metallib`; runtime source compilation is only a source-tree/debug
fallback.

## Runtime architecture

- Three command buffers may be in flight. Completion handlers publish GPU
  timing, surface errors, shared-event progress, and release frame-local
  upload/argument buffers.
- Render passes use a single-sample persistent scene plus 1/2/4/8x MSAA color
  and stencil attachments. Embedded targets use memoryless attachments; every
  pass resolves back to the persistent scene so nested captures and damage
  remain valid across encoder boundaries.
- Damage is applied as a Metal scissor against a persistent scene texture;
  `CAMetalDrawable` preservation is never assumed.
- Impeller uses the shared path flatten/stroke/triangulation algorithms.
- Vello uses the shared 0.10 scene encoder and the classic Metal compute graph.
- Effects, Vello scratch and retained layers allocate from `MTLHeap` pools;
  retained destruction and external video producer callbacks retire only after
  the relevant command buffer completes. Two-phase readback is the only normal
  path that copies a finished frame to CPU memory.
- CoreText supplies shaping/fallback/layout/hit-testing. Device-space text runs
  are cached as bounded Metal textures (including color Emoji) and invalidated
  by font/layout/transform/clip/color state.
- Video accepts BGRA, NV12/P010 CVPixelBuffer, IOSurface, and direct MTLTexture
  descriptors, with video/full-range and BT.601/709/2020 conversion. Decoder
  lifetime callbacks retain surfaces through GPU use.
- Composition pacing uses `CADisplayLink` at the active 60/120 Hz rate. AppKit
  exposes native drag source/target behavior; UIKit maps multiple UIWindowScene
  roots, coalesced/predicted Pencil samples, keyboard, safe-area and rotation.

## Validation

Run the source capability gate on any render interface change:

```bash
python3 tools/metal_parity_check.py
python3 tools/metal_parity_check.py --write-report
```

The Apple workflow additionally builds every device/simulator slice, compiles
all Vello stages, assembles the XCFramework, and builds the three entry packages.
Pixel, physical-device, 120 Hz, thermal and soak results are release gates; the
README platform table must remain conservative until that workflow and the
documented real-device matrix are green.
