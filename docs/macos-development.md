# macOS development and Gallery

The Apple Silicon development path uses an AppKit application host, the native
platform library and the Metal renderer. The standalone Gallery entry lives in
the sibling `Jalium.UI.Gallery` repository as `Jalium.UI.Gallery.MacOS`.

## Prerequisites

- Apple Silicon Mac; the projects declare macOS 15.0 as their deployment floor.
- Xcode with its command-line tools and Metal Toolchain component.
- .NET 10 SDK and the `macos` workload.
- CMake 3.28 or newer, Ninja and Python 3.9 or newer.

The toolchain versions are recorded in `eng/apple/toolchain.lock.json`. Install
the pinned SDK first, then its workload:

```bash
dotnet workload install macos --version 10.0.300.2
xcodebuild -downloadComponent MetalToolchain
```

The run script also finds ignored tools installed under `.tools/dotnet`,
`.tools/bin` and `.tools/cmake-*-macos-universal` in the framework checkout.
Dependencies remain local development tools and are not included in Git.

## Build and run

Keep the two repositories next to each other:

```text
Repos/
  Jalium.UI/
  Jalium.UI.Gallery/
```

From `Jalium.UI`, run:

```bash
bash eng/apple/run-gallery.sh Debug
```

This prepares the pinned static DXC/SPIRV-Cross compiler, generates the Metal
shader libraries, builds the native development payload, runs the native
GPU/AppKit tests, builds the managed `.app` and launches it. The compiler is
cached after its first build and is included in development builds by default.
Use `--build-only` to leave the app closed. `JALIUM_GALLERY_ROOT` can point to a
different Gallery checkout; `JALIUM_GALLERY_BUILD_ROOT` changes the isolated
managed output directory.

The Gallery entry permits a newer Xcode during local Debug development, so
after the native payload is built and `dotnet` is in `PATH`, it can also be run
from the Gallery checkout without additional build properties:

```bash
cd Jalium.UI.Gallery.MacOS
dotnet run
```

Release builds retain the .NET workload's Xcode version check. Set
`-p:ValidateXcodeVersion=true` to require that check in Debug as well.
The macOS entry package also checks the core/platform C entry points against
the current headers before building. If a managed/native mismatch is reported,
rebuild the native payload with `bash eng/apple/build-native.sh macos Debug --development`
(use `Release` for a Release app), then rebuild and restart the Gallery debugger.
Building individual native library/test targets invalidates the completion stamp.
Local Debug builds of the macOS entry package can now finish an existing
`src/native/out/build/apple-macos-dev-arm64` CMake build when that stamp is missing,
then validate the exports and collect the newly available native/Metal resources.
The cached source, output directory and configuration must match the requested
payload. Set `JaliumMacNativeBuildDirectory` for another configured build, or
`JaliumMacAutoCompleteNativePayload=false` to keep validation read-only.
This does not bootstrap the native toolchain or run its tests. Release and
design-time builds never auto-complete the native payload.
For an explicit local development experiment with another configuration:

```bash
bash eng/apple/run-gallery.sh Debug --allow-newer-xcode
```

The script's `--allow-newer-xcode` option passes `ValidateXcodeVersion=false`
only to that build. It is not a release qualification or a global SDK setting.
The current local verification used
macOS 27.0.1, Xcode 27.0, .NET SDK 10.0.300 and workload set 10.0.300.2 with this
override; the declared macOS 15.0 floor has not been tested on a macOS 15 host.

Output paths:

```text
src/native/bin/native/osx-arm64/Debug/
artifacts/macos-gallery/bin/Jalium.UI.Gallery.MacOS/Debug/net10.0-macos/osx-arm64/Jalium.UI.Gallery.MacOS.app
```

The app contains native dylibs and both `jalium_core.metallib` and
`jalium_vello.metallib`. Shader generation extracts the checked-in Vello SPIR-V,
preserves argument-buffer binding numbers and validates them against the Metal
runtime's binding tables. Gallery selects Impeller with 4× MSAA.

An AppKit entry point using `JaliumMacApplicationDelegate` sets `NSPrincipalClass`
to `JaliumMacApplication` in its Info.plist and calls
`JaliumMacApplication.Initialize()` before accessing `NSApplication.SharedApplication`.
Use this call in place of `NSApplication.Init()`; repeated calls return the same
application. The principal application contains recursive Quit calls while
Closing callbacks and delayed native-resource teardown are still running. The
delegate can then cancel, wait, or retry termination after the closing decision.
Gallery and the Window validation host use this entry point. An application with
its own principal class can derive that class from `JaliumMacApplication`.
For a project under a hidden directory, or with custom manifest discovery, set
`AppBundleManifest` to `$(MSBuildThisFileDirectory)Info.plist` explicitly. Verify
`NSPrincipalClass` in the compiled app's `Contents/Info.plist`; the source plist
alone does not prove that the SDK packaged it. The Window packaging scripts
reject an app whose compiled principal class is still `NSApplication`.

## Validation

`eng/apple/build-native.sh macos Debug --development` runs the GPU/AppKit smoke
and rendering regression tests as part of the build. They check actual GPU readback at 96 and 192 DPI,
rectangle colors, English/CJK/emoji text, tight line boxes, glyph orientation,
the caret ABI, Retina window dimensions, keyboard focus, window positioning,
dispatcher wakes, IME commit, Backspace, the AppKit Paste responder action,
precise/coarse scroll units and UTF-16/HTML clipboard round-trips.

The rendering regressions cover PNG orientation through both the Metal and
Apple media decoders, BGRA/RGBA channels, straight-alpha bitmap uploads and
media decode, changing fractional damage bounds, unchanged partial redraws, text
cache reuse, rounded/excluded/deep/empty clip stacks, retained layers captured
during partial frames, and balanced AppKit cursor hide/unhide requests. Pixel
comparisons run at both DPI scales with 1× and 4× MSAA.

To run the isolated Retina text/clip benchmark after a development build:

```bash
JALIUM_METALLIB_DIR="$PWD/src/native/artifacts/apple/slices/osx-arm64/Debug" \
  src/native/bin/native/osx-arm64/Debug/jalium.native.metal.regression --benchmark
```

It reports GPU time, paced frame time and text-cache usage. Frame time includes
display synchronization; GPU time measures the rendering workload. Common
clip changes reuse an encoder, macOS keeps MSAA samples between passes, and
text-cache entries are independent of transient damage/parent clip rectangles.

The development entry also provides native Quit and Edit menus. Command
gestures feed Jalium's existing Control command gestures, so Command+K can
focus search and standard editing gestures use the same managed commands.

Live Gallery verification covered search, Inputs navigation, Chinese/emoji
paste, Command+A replacement, single Backspace deletion, window maximization,
restoration, edge resizing, scrolling and closing the last window. Scroll
conversion uses the point/line distinction in
[AppKit's scroll-delta API](https://developer.apple.com/documentation/appkit/nsevent/hasprecisescrollingdeltas).

## Metal shader effects

Development and normal macOS builds both include the static runtime compiler:

```bash
bash eng/apple/build-native.sh macos Debug --development
```

Custom `PixelShader.SourceHlsl` uses the cross-platform `main` pixel shader
contract: captured content at `t0`, sampler at `s0`, and constants at `b0`.
Metal compiles HLSL to SPIR-V with DXC, then to MSL with SPIRV-Cross, and caches
generated MSL per compiler ABI and device. Missing constants are zero-filled,
including reflected buffers larger than Metal's inline constant limit.
DirectX DXBC bytecode remains Windows-specific; use `SourceHlsl` on macOS.
Invalid shaders preserve the captured content and report
`PixelShader.InvalidPixelShaderEncountered` once until the shader changes or
recovers. `jalium_render_target_get_last_shader_effect_result` exposes the
corresponding native diagnostic.

Metal implements Gaussian and Box element blur, drop/inner shadows, outer
glow, emboss, color matrices
and ordered matrix chains, custom HLSL, CSS spread/elliptical/alpha/text-shadow
layers, Gaussian/box/frosted backdrop materials, liquid glass with refraction,
spectral dispersion, pointer light and neighbor fusion, ten transition modes,
moving glow ribbons, head/tail transitions and expanding ripples. Visible
backdrop or liquid-glass scenes repaint in full to avoid feedback from the
previous frame's filtered pixels; ordinary effects retain partial redraw.

The native `jalium.native.metal.effects` test performs GPU pixel readback at
1x/2x DPI and 1x/4x MSAA. It checks shader compilation/errors, local UVs,
fractional/rotated input, small and large constant buffers, brush HLSL, deep
clipping, opacity, shadows, matrices, nested capture, partial redraw, glass,
backdrop kernels, all transition endpoints and animated highlights. Separate
checks exercise standalone CSS shadow layers and captured transition fades
under inherited opacity and both analytical and deep stencil clipping. The
eleven suites run in four DPI/MSAA combinations (44 suite runs), including
translated blur/shadow/glass output on larger targets to check compact texture
origins, multipass halos and physical texture borders. To also write
rendered evidence:

```bash
src/native/bin/native/osx-arm64/Debug/jalium.native.metal.effects \
  "$PWD/artifacts/macos-shader-effects"
```

The portable managed effect, diagnostic and backdrop replay tests also run on
macOS despite the test project's Windows TFM:

```bash
dotnet test tests/Jalium.UI.Tests/Jalium.UI.Tests.csproj -c Debug \
  -p:EnableWindowsTargeting=true \
  -p:JaliumBuildRoot="$PWD/artifacts/macos-shader-managed-tests-windows-tfm" \
  --filter 'FullyQualifiedName~ShaderEffectTests|FullyQualifiedName~MediaEffectsParityTests|FullyQualifiedName~MetalBackdropReplayTests'
```

Manual Gallery checks require an unlocked Mac. Validate the Shader Effects
page's blur, shadow and HLSL sliders with pointer input and Home/End/arrow keys,
switch the Box blur checkbox, then inspect the keyboard focus ring and the
narrow preview layout. The displayed XAML/C# examples include actual effect
attachments and the portable HLSL/constant callback contract.

Live Apple Silicon verification covered desktop, 760-pixel tablet and
420-pixel phone previews, blur radius 0–50, Gaussian/Box switching, shadow
depth 0–30, and HLSL inversion from original input to full inversion. Home,
End, arrow keys, Tab/Shift+Tab, Space and the visible focus rings were checked
in Gallery. AppKit navigation keys use their physical key identity because
removing Function/NumericPad during text translation changes them into ASCII
control characters; native key-down/key-up regression checks cover this path.

For an explicit compiler-free macOS experiment, set
`JALIUM_METAL_BUILTINS_ONLY=1` when calling the native build script. Custom
HLSL is unavailable in that configuration; the full shader test is omitted.

### Effect performance

Blur dispatch and its two intermediate textures cover the region consumed by
the effect instead of the entire window. Each separable pass retains the halo
needed by the next pass; shadow tails, frosted jitter, physical texture borders
and glass refraction/dispersion are included. Gaussian and Box weights are
computed once per effect, and adjacent weighted taps use one linear texture
sample. Frosted blur retains individual jittered taps. The offline core shader
ABI is v3, so older metallibs cannot be loaded with the new parameter layout.

For a reproducible native benchmark at the Gallery's 3000 × 1840 physical
pixels, 2x DPI and 4x MSAA:

```bash
JALIUM_METALLIB_DIR="$PWD/src/native/artifacts/apple/slices/osx-arm64/Debug" \
  src/native/bin/native/osx-arm64/Debug/jalium.native.metal.effects \
  --benchmark "$PWD/artifacts/macos-effect-performance/native"
```

The benchmark warms up three frames and measures twelve completed frames per
case. GPU readback runs only afterward to save evidence. `frame_ms` includes
native drawing, submission and the explicit completion wait; `gpu_ms` is the
completed command buffer's GPU interval. This diagnostic barrier is not used
by normal rendering. The effect region is 200 × 100 logical pixels, with a
300 × 200 capture for element effects.

The macOS Gallery also has an opt-in measurement mode:

```bash
JALIUM_GALLERY_EFFECT_PERF_LOG="$PWD/artifacts/macos-effect-performance/gallery.csv" \
  artifacts/macos-gallery/bin/Jalium.UI.Gallery.MacOS/Debug/net10.0-macos/osx-arm64/Jalium.UI.Gallery.MacOS.app/Contents/MacOS/Jalium.UI.Gallery.MacOS
```

It requests full redraws with a 16 ms dispatcher timer, and records the main
window's actual frame count, elapsed time, frame-history duration, latest GPU
interval and texture allocation. `submitted_fps` measures whole-frame
submission throughput, not the display's presented frame rate. `frame_ms`
can include GPU pacing, and overlapping queued frames can increase the GPU
interval; use the serialized native benchmark to compare GPU workloads.
Unset the variable for normal idle-aware rendering. Normal launches do not
create the measurement timer or write CSV files.

On Apple M6, macOS 27.0.1, Debug/Impeller/4x MSAA, the Shader Effects desktop
page with Gaussian radius 50 improved from 8.45 to 66.11 submitted frames/s
under continuous full redraw. Median reported texture allocation fell from
750.71 to 492.15 MiB. In the native benchmark, Gaussian radius 50 improved from
265.55 to 13.92 ms/frame, and liquid glass from 23.10 to 10.69 ms/frame. These
are measurements of the stated scenes, not a frame-rate guarantee for every
effect size or device. Eleven before/after full-window readbacks differed by
at most 2/255 per channel. Actual Gallery checks covered Gaussian/Box switching,
blur Home/Right/End values, shadow offsets, HLSL inversion endpoints and visible
keyboard focus. F3/F12 native key regression checks also preserve function-key
identity so the Gallery's F12 performance tools can open.

## Remaining scope

Intel macOS, NativeAOT, signed/notarized distribution, multi-monitor DPI
transitions, real IME candidate-window placement, VoiceOver, media/browser
integration and the complete Gallery control matrix still need separate
qualification. These are independent of the verified Apple Silicon shader
effects path.

On a fresh machine, workload set 10.0.300.2 can fail while reading missing
Xamarin SDK preferences. A local workaround used during validation creates
`~/Library/Preferences/Xamarin/Settings.plist` with an `AppleSdkRoot` string
pointing to the selected Xcode bundle, only when no existing SDK settings file
is present. The underlying issue is tracked in
[dotnet/macios #25476](https://github.com/dotnet/macios/issues/25476).
