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

## Runtime architecture

- Three command buffers may be in flight. Completion handlers publish GPU
  timing, surface errors, and release frame-local upload/argument buffers.
- Damage is applied as a Metal scissor against a persistent scene texture;
  `CAMetalDrawable` preservation is never assumed.
- Impeller uses the shared path flatten/stroke/triangulation algorithms.
- Vello uses the shared 0.10 scene encoder and a 19-stage Metal compute graph.
- Effects and retained layers use GPU textures; two-phase readback is the only
  normal path that copies a finished frame to CPU memory.
- Video accepts BGRA, NV12/P010 CVPixelBuffer, IOSurface, and direct MTLTexture
  descriptors. Decoder lifetime callbacks retain surfaces through GPU use.

## Validation

Run the source capability gate on any render interface change:

```bash
python3 tools/metal_parity_check.py
```

The Apple workflow additionally builds every device/simulator slice, compiles
all Vello stages, assembles the XCFramework, and builds the three entry packages.
Pixel, physical-device, 120 Hz, thermal and soak results are release gates; the
README platform table must remain conservative until that workflow and the
documented real-device matrix are green.
