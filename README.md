# Jalium.UI

**English** | [简体中文](README.zh-CN.md) | [日本語](README.ja.md) | [한국어](README.ko.md)

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/VeryJokerJal/Jalium.UI)

Jalium.UI is a GPU-accelerated, cross-platform UI framework for .NET 10.
It combines a WPF-style object model, JALXAML markup with Razor syntax extensions,
and platform-native rendering backends (DirectX 12, Vulkan, Metal, Software).

## Project Status

- Active development — v26.10.6 (APIs can still evolve between minor versions)
- Primary target: Windows 10/11 (x64, ARM64)
- Cross-platform: Android (arm64-v8a, x86_64), Linux (X11/Wayland; Vulkan or software), macOS (Metal)
- Runtime target: .NET 10 (`net10.0-windows`, `net10.0-android`, `net10.0`)
- Rendering: DirectX 12 (Windows), Vulkan (Linux/Android), Metal (macOS), Software fallback

## Why Jalium.UI

- GPU-native rendering pipeline with ClearType sub-pixel text rendering
- Familiar programming model (`DependencyObject`, `UIElement`, panels, templates, resources)
- JALXAML markup with Razor syntax extensions (`@Path`, `@(expr)`, `@{ ... }`, `@if`/`@for`/`@foreach`, `@section`/`@RenderSection`)
- Rich control library: 100+ controls including Charts, Ribbon, Docking, InkCanvas, WebView, Terminal, MediaElement, MapView, PropertyGrid, WindowsFormsHost
- Developer experience: JALXAML hot reload (live visual-tree patching) plus an opt-in built-in DevTools inspector and debug HUD
- Build-time tooling via NuGet (`Jalium.UI.Build`, `Jalium.UI.Xaml.SourceGenerator`)
- First-class Generic Host integration — `AppBuilder` implements `IHostApplicationBuilder` (`Microsoft.Extensions.Hosting`) for DI, configuration, options, logging and metrics, plus Jalium MVVM view/view-model wiring
- Accessibility automation peers with Windows UIA and Linux AT-SPI bridges
- Visual effects: liquid glass, backdrop blur, acrylic, mica, transition shaders, animated bitmaps (GIF / APNG / animated WebP)
- Native GPU video via `MediaElement` / `NativeVideoSurface` (D3D12 / Vulkan surfaces, staged rollout)
- Full multi-touch input — manipulation with physical inertia, gesture recognition, real-time stylus preview
- Grapheme-cluster aware text editing (UAX#29) — emoji, ZWJ sequences, skin-tone modifiers, country flags never split
- Native audio pipeline (miniaudio + dr_libs / minimp3) with WSOLA pitch-preserving time-stretching

## Framework Composition

### Managed Packages

| Package | Responsibility |
| --- | --- |
| `Jalium.UI.Core` | Dependency property system, visual tree, layout, routed events, binding, animation |
| `Jalium.UI.Media` | Brushes, geometry, drawing primitives, text formatting, imaging, video, visual effects |
| `Jalium.UI.Input` | Mouse, keyboard, touch, stylus input abstractions and routing |
| `Jalium.UI.Interop` | Managed/native bridge, P/Invoke, runtime native dependency packaging |
| `Jalium.UI.Gpu` | GPU resource management, render graph, materials, shaders, backend abstraction |
| `Jalium.UI.Controls` | Controls, panels, templates, windowing, themes, docking, charts, hosting |
| `Jalium.UI.Xaml` | JALXAML parse/load pipeline, Razor syntax support, hot reload, markup services |
| `Jalium.UI.Build` | MSBuild tasks and build assets for JALXAML compilation workflow |
| `Jalium.UI.Xaml.SourceGenerator` | Roslyn source generator for XAML/code-behind integration |
| `Jalium.UI` | Metapackage that references the full framework stack |

> `Jalium.UI.Compiler` builds the standalone `jalxamlc` compiler executable. It is a
> build tool consumed from source, not a NuGet package (`dotnet add package` does not apply).

### Native Modules

| Module | Platforms | Responsibility |
| --- | --- | --- |
| `jalium.native.core` | All | Native core runtime, backend registry, context management |
| `jalium.native.d3d12` | Windows | DirectX 12 render target and Vello GPU pipeline |
| `jalium.native.vulkan` | Linux, Android | Vulkan render backend |
| `jalium.native.metal` | macOS | Metal render backend |
| `jalium.native.software` | All | CPU-based software rendering fallback |
| `jalium.native.platform` | All | Platform abstraction (window, input, events) |
| `jalium.native.text` | Linux, Android, macOS | Self-hosted text engine (sfnt/cmap/glyf/CFF + OT shaper; fontconfig for discovery on Linux) |
| `jalium.native.browser` | Windows | WebView2 browser integration |
| `jalium.native.media.core` | All | Cross-platform media C ABI + shared audio (miniaudio / dr_libs / minimp3 / stb_vorbis) |
| `jalium.native.media.windows` | Windows | Media Foundation video / camera / AAC decoder + WIC imaging |
| `jalium.native.media.android` | Android | Android image / video / camera decoders + YUV SIMD (NEON) via the NDK |
| `jalium.native.aot` | All | NativeAOT aggregator (hard-links media, text, backends) |

### Platform Packages

| Package | Target |
| --- | --- |
| `Jalium.UI.Desktop` | `net10.0-windows` — Desktop distribution with per-RID native DLLs (win-x64 / win-arm64) |
| `Jalium.UI.Android` | `net10.0-android` — Android distribution with native .so libraries |
| `Jalium.UI.Linux` | `net10.0` — Linux desktop distribution (Wayland/X11, Vulkan + software rendering; linux-x64 / arm64 / musl RIDs) |

## Capability Overview

### Layout and Visual Tree

- Core panels: `Grid`, `StackPanel`, `Canvas`, `DockPanel`, `WrapPanel`, `UniformGrid`
- Virtualization: `VirtualizingStackPanel`, `VirtualizingWrapPanel`, DataGrid presenters/panels
- Docking: `DockLayout`, `DockSplitPanel`, `DockTabPanel`, `Split`
- Window-level layout host, overlay layer, title bar composition, chrome integration

### Controls

- **Input**: `Button`, `TextBox`, `PasswordBox`, `NumberBox`, `AutoCompleteBox`, `ComboBox`, `Slider`, `RangeSlider`, `CheckBox`, `RadioButton`, `ToggleSwitch`, `SplitButton`
- **Pickers**: `ColorPicker`, `DatePicker`, `TimePicker`, `Calendar`
- **Data**: `TreeView`, `DataGrid`, `TreeDataGrid`, `ListBox`, `ListView`, `PropertyGrid`, `JsonTreeViewer`
- **Navigation & bars**: `NavigationView`, `TabControl`, `Ribbon`, `CommandBar`, `MenuBar`, `ToolBar`, `StatusBar`, `GroupBox`, `Expander`, `InfoBar`
- **Documents**: `DocumentViewer`, `FlowDocumentReader`, `FlowDocumentScrollViewer`, `FlowDocumentPageViewer`, `Markdown`
- **Charts**: `BarChart`, `LineChart`, `PieChart`, `ScatterPlot`, `CandlestickChart`, `GaugeChart`, `Heatmap`, `GanttChart`, `NetworkGraph`, `SankeyDiagram`, `TreeMap`, `Sparkline`, plus `FlowchartDiagram` / `MermaidDiagram` — Category / DateTime / Logarithmic / Numeric axes, legend and tooltip
- **Media**: `MediaElement`, `CameraView`
- **Maps**: `MapView`, `MiniMap`, `GeographicHeatmap`
- **Rich**: `InkCanvas`, `WebView`/`WebBrowser`, `EditControl`, `RichTextBox`, `QRCode` (self-hosted encoder), `TitleBar`, `Terminal`, `SwipeControl`
- **Developer tools**: `DiffViewer`, `HexEditor`
- **Interop**: `WindowsFormsHost` (host `System.Windows.Forms` controls on `net10.0-windows`)
- **Printing**: `PrintDialog` backed by Win32 on Windows and PDF + xdg-desktop-portal on Linux
- **Notifications**: Toast-style notification system

### Text Editing

- Grapheme-cluster aware caret, selection, word break and delete across `TextBox`,
  `PasswordBox`, `EditControl`, `RichTextBox`, `TextBlock`, `Label`, `Markdown`,
  `Terminal` and `TextEffectPresenter` — emoji (ZWJ / skin-tone / country flags /
  combining marks) are never split mid-cluster (UAX#29 via `StringInfo.GetTextElementEnumerator`).
- IME suppression for password / read-only fields so composing input cannot mutate
  protected editor state.
- Multi-encoding file IO with BOM auto-detection (`LoadFromFile` / `SaveToFile` on
  `TextBox`, `EditControl`, `Markdown`, `RichTextBox`). `CodePagesEncodingProvider`
  registered via a `ModuleInitializer` so GBK / Shift-JIS / any non-default code
  page works out of the box.
- `Terminal` adds an `Encoding` property and stateful `Decoder` so output bytes are
  decoded in the user's terminal encoding without splitting multi-byte sequences.
- WPF-aligned `TextBox.CharacterCasing` / `MinLines` / `MaxLines`, `ComboBox.StaysOpenOnEdit`.

### Text Rendering

- ClearType sub-pixel text rendering with dual-source blending.
- CPU rasterization fallback path.
- Cross-platform text shaping via the self-hosted OpenType engine (Linux/Android) — no FreeType/HarfBuzz runtime dependency; fontconfig is used for font discovery only.
- Per-element `TextOptions.{TextRenderingMode, TextFormattingMode, TextHintingMode}`
  inheritable attached properties — values flow through `FormattedText` → native
  `JaliumTextFormat` and reach the rasterizer:
  - D3D12: `GlyphKey` includes `(aaMode, hintingMode)`; `RasterizeGlyph` honours `key.mode`.
  - Vulkan / Windows: `LOGFONT.lfQuality` flips between bilevel / smoothed / ClearType;
    font cache + text cache + GDI font pool keys all include `fontQuality`.
- Process-wide rendering mode override + colour-emoji rasterization.

### Input Pipeline

- Pointer and keyboard routing with hit testing
- Full multi-touch manipulation (pan / pinch / rotate) with physical inertia
- `GestureRecognizer` (tap / swipe / pinch) wired natively across the control catalog
- Real-time stylus (RTS) background ink preview
- Scroll and manipulation event handling

### GPU Rendering & Effects

- DirectX 12 with Vello GPU compute pipeline (path, clip, tile stages)
- Backdrop effects: blur, acrylic, mica, frosted glass
- Liquid glass with refraction, chromatic aberration
- Transition shaders and element effects (blur, drop shadow)
- Animated bitmaps: GIF, APNG, animated WebP
- Native GPU video surface (`NativeVideoSurface`): CPU BGRA8 staging plus import of
  external GPU resources (D3D11 shared texture / Vulkan external `VkImage` / Android
  `AHardwareBuffer` / Apple `IOSurface`) — API surface in place, per-backend upload
  paths being filled in incrementally.
- Custom shader support via HLSL
- Bitmap downscale cache + virtualizing wrap panel for large image grids
- Unified path/bitmap telemetry C ABI surfaced in DevTools Perf tab

### Hosting / DI / Configuration

- Built on the standard `Microsoft.Extensions.Hosting` stack (10.0.3). `AppBuilder`
  implements `IHostApplicationBuilder` (wrapping `HostApplicationBuilder`), so the
  familiar `IServiceCollection`, `IConfiguration`, `ILoggingBuilder`,
  `IHostEnvironment` and `Meter` surfaces are available during app startup.
- Jalium-specific glue lives in `Jalium.UI.Hosting`: MVVM view/view-model
  registration (`AddView<TView, TViewModel>`, `AddViewsAndViewModels`),
  `ConfigureJalium` options binding to `JaliumRuntimeOptions`, frame-time metrics
  (`UseJaliumMetrics` / `JaliumMeter`), and opt-in developer tools
  (`UseDevTools` / `UseDebugHud`).
- Trim/AOT-safe configuration binding via `EnableConfigurationBindingGenerator`;
  reflection-based view discovery overloads are annotated `[RequiresUnreferencedCode]`.

### Audio Pipeline

- Native `MiniAudioDevice` playback + `NativeAudioDecoder` covering WAV / FLAC /
  MP3 / Vorbis (cross-platform) and AAC (Windows via Media Foundation).
- Managed `IAudioProcessor` chain with `WsolaSpeedProcessor` for pitch-preserving
  time-stretching.
- Audio TUs are compiled into each per-platform `jalium.native.media.*` library
  so symbols land in the same DLL the managed P/Invoke loads.

### 3D Animation Types

- `Point3DAnimation`, `Rotation3DAnimation`, `Size3DAnimation`,
  `Vector3DAnimation` complete the WPF animation type set.

### Accessibility

- Automation peers for core and specialized controls, exported through Windows UIA and Linux AT-SPI
- Chart, DiffViewer, HexEditor, JsonTreeViewer, Map, PropertyGrid automation
- `Window.ResolveCursor` returns the standard arrow for disabled elements so
  hover state cannot be confused with enabled controls.

### Developer Tools

- Opt-in DevTools window (`app.UseDevTools()`, F12 / Ctrl+Shift+C element picker):
  visual / logical / flat tree inspector with virtualization and inline property
  editing, plus Layout, Events, Bindings, Resources, Perf, UIA, Tools
  (ruler / colour picker / overdraw & dirty-region overlays / screenshot export)
  and a live REPL tab.
- On-screen debug HUD (`app.UseDebugHud()`, F3): frame timings, dirty regions and
  backend info.
- Both are off by default, so they never ship in release apps unless explicitly enabled.

### Markup and Tooling

- Runtime parsing: `Jalium.UI.Markup.XamlReader`
- Build integration through packaged MSBuild targets/tasks
- Source generator for compile-time JALXAML code-behind
- Source generator compile-time lowering of Razor directives — see below.
- JALXAML hot reload: `HotReloadRuntime` / `HotReloadAgent` patch the live visual
  tree in-process over a named pipe (auto-started via the `JALIUM_HOTRELOAD_PIPE`
  environment variable set by the IDE); a standalone file watcher lives at
  `tools/Jalium.UI.HotReload.Watcher`.

## Razor Syntax in JALXAML

JALXAML supports Razor-style syntax as additive sugar on top of existing `{Binding ...}`:

- `@Path`
- `@(expr)`
- `@{ ... }`
- `@*...*@` comments
- mixed text templates (for string/object targets)
- `@if(expr){<Element />}` block directives (with full `else if` / `else` chains)
- statement / control-flow directives: `@for`, `@foreach`, `@while`, `@switch`,
  `@using`, `@lock` (expanded at parse time)
- `@section`/`@RenderSection` for templated content
- escapes: `@@` and `\@`

Binding source resolution is `DataContext` first, then code-behind fallback.

Update behavior:

- Observable source (`INotifyPropertyChanged` / dependency property): real-time updates.
- Non-observable CLR source: one-time evaluation at load.

### Compile-time lowering

The JALXAML source generator lowers the following at build time so there is no
runtime parsing cost in the hot path:

- `@if` / `@else if` / `@else` chains.
- `@section` / `@RenderSection`.
- Value expressions (`@Path`, `@(expr)`).
- `{Binding ...}` via `SetCompiledBinding` (SG `SplitParameters` is kept
  line-for-line consistent with the runtime parser).
- Custom-xmlns element types — when a `.jalxaml` uses a controls library exposed
  through `XmlnsDefinition`, the SG resolves the CLR type at compile time
  instead of falling back to runtime reflection (helps trimming / AOT).

`Setter.Value` is intentionally NOT lowered.

For syntax details and rules, see [`docs/razor-syntax.md`](docs/razor-syntax.md).

## Installation

### Recommended (metapackage)

```bash
dotnet add package Jalium.UI
```

### Platform-specific

```bash
# Windows Desktop
dotnet add package Jalium.UI.Desktop

# Android
dotnet add package Jalium.UI.Android

# Linux desktop
dotnet add package Jalium.UI.Linux
```

### Granular install (advanced)

```bash
dotnet add package Jalium.UI.Core
dotnet add package Jalium.UI.Media
dotnet add package Jalium.UI.Input
dotnet add package Jalium.UI.Interop
dotnet add package Jalium.UI.Gpu
dotnet add package Jalium.UI.Controls
dotnet add package Jalium.UI.Xaml
dotnet add package Jalium.UI.Build
dotnet add package Jalium.UI.Xaml.SourceGenerator
```

## Quick Start (C#)

```csharp
using Jalium.UI.Controls;

var app = new Application();

var window = new Window
{
    Title = "Hello Jalium.UI",
    Width = 960,
    Height = 640,
    Content = new StackPanel
    {
        Margin = new Thickness(24),
        Children =
        {
            new TextBlock { Text = "Jalium.UI", FontSize = 28 },
            new TextBlock { Text = "GPU-accelerated .NET UI framework", Margin = new Thickness(0, 8, 0, 16) },
            new Button { Content = "Start" }
        }
    }
};

app.Run(window);
```

## Quick Start (JALXAML runtime parse)

```csharp
using Jalium.UI.Controls;
using Jalium.UI.Markup;

var app = new Application();

var xaml = """
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Title="JALXAML Window" Width="800" Height="500">
  <Grid>
    <StackPanel Margin="20">
      <TextBlock Text="Hello from JALXAML" FontSize="24"/>
      <Button Content="Click" Margin="0,12,0,0"/>
    </StackPanel>
  </Grid>
</Window>
""";

var window = (Window)XamlReader.Parse(xaml);
app.Run(window);
```

## Build From Source

### Prerequisites

- .NET 10 SDK
- Visual Studio with C++ workload (for Windows native modules)
- CMake, Ninja, Clang/GCC, and the X11/Wayland development packages (for Linux native modules; see the [Linux guide](docs/linux.md))
- Vulkan SDK (optional, for Vulkan backend)
- Android NDK (optional, for Android builds)

### Build

```bash
# Build the full framework
dotnet build src/packaging/Jalium.UI/Jalium.UI.csproj -c Release

# Run tests
dotnet test tests/Jalium.UI.Tests/Jalium.UI.Tests.csproj -c Release

# Build native modules (in VS Developer Command Prompt)
msbuild src/native/Jalium.Native.sln /m /p:Configuration=Release /p:Platform=x64

# Build native modules for Linux (glibc or musl host)
bash eng/linux/build-native.sh linux-x64 Release

# Build for Android
bash src/native/build-android-deps.sh  # Android native dependencies
bash src/native/build-android.sh       # Native libraries
```

### NuGet Packaging

```bash
dotnet pack src/packaging/Jalium.UI/Jalium.UI.csproj -c Release -o artifacts/nuget
```

For detailed build configuration, see [`docs/manual-build-configuration.md`](docs/manual-build-configuration.md).

## Repository Layout

```text
Jalium.UI/
  src/
    managed/
      Jalium.UI.Core/          # Dependency property system, visual tree, layout
      Jalium.UI.Media/         # Brushes, geometry, drawing, text, imaging, video
      Jalium.UI.Input/         # Input abstractions and routing
      Jalium.UI.Interop/       # Native bridge and P/Invoke
      Jalium.UI.Gpu/           # GPU resources, render graph, shaders
      Jalium.UI.Controls/      # Controls, panels, themes, docking, charts, hosting
      Jalium.UI.Xaml/          # JALXAML parser, Razor support, hot reload
      Jalium.UI.Build/         # MSBuild tasks for JALXAML compilation
      Jalium.UI.Xaml.SourceGenerator/  # Roslyn source generator
      Jalium.UI.Compiler/      # Standalone jalxamlc compiler (executable)
    native/
      jalium.native.core/      # Native runtime core, backend registry
      jalium.native.d3d12/     # DirectX 12 + Vello GPU backend
      jalium.native.vulkan/    # Vulkan backend
      jalium.native.metal/     # Metal backend (macOS)
      jalium.native.software/  # CPU software renderer
      jalium.native.platform/  # Platform abstraction layer
      jalium.native.text/      # Self-hosted text engine (non-Windows)
      jalium.native.browser/   # WebView2 integration
      jalium.native.media.core/     # Cross-platform media C ABI + shared audio
      jalium.native.media.windows/  # Media Foundation video / camera + WIC
      jalium.native.media.android/  # Android media decoders + YUV SIMD
      jalium.native.aot/       # NativeAOT aggregator (hard-links media/text/backends)
    packaging/
      Jalium.UI/               # Main metapackage
      Jalium.UI.Desktop/       # Windows desktop package (win-x64 / win-arm64)
      Jalium.UI.Android/       # Android package
      Jalium.UI.Linux/         # Linux desktop package (linux-x64/arm64, musl)
  samples/                     # Gallery, DesktopDemo, AndroidDemo, HostingDemo,
                               #   MillionScroll, AotWindowDemo, BorderlessDemo,
                               #   TransparentBackdropDemo, LinuxDemo
  tools/
    Jalium.UI.HotReload.Watcher/  # Standalone JALXAML hot-reload file watcher
  tests/
    Jalium.UI.Tests/           # xUnit test suite (70+ test classes)
    Jalium.UI.ShaderDemo/      # Shader effects demo
    Jalium.UI.ParityHarness/   # Backend parity harness
    Jalium.UI.DeviceLostHarness/  # Device-lost recovery harness
    Jalium.UI.NuGetTest.Desktop/  # Packaged-desktop smoke test
    Jalium.UI.NuGetTest.Android/  # Packaged-android smoke test
  docs/                        # razor-syntax, drawing-api, manual-build-configuration,
                               #   render-thread-design, present-pacing-design,
                               #   shell-drag-drop (+ design/ and reference/)
```

## Documentation

| Document | Description |
| --- | --- |
| [`docs/razor-syntax.md`](docs/razor-syntax.md) | Razor syntax reference for JALXAML |
| [`docs/drawing-api.md`](docs/drawing-api.md) | Drawing API (DrawingContext, GPU effects, rendering) |
| [`docs/manual-build-configuration.md`](docs/manual-build-configuration.md) | Manual build configuration guide |
| [`docs/linux.md`](docs/linux.md) | Linux desktop guide (runtime dependencies, window systems, packaging) |
| [`docs/linux-parity-status.md`](docs/linux-parity-status.md) | Verified Linux support matrix, evidence, and remaining boundaries |
| [`docs/render-thread-design.md`](docs/render-thread-design.md) | Render thread architecture |
| [`docs/present-pacing-design.md`](docs/present-pacing-design.md) | Present pacing / frame scheduling |
| [`docs/shell-drag-drop.md`](docs/shell-drag-drop.md) | Shell drag & drop integration |

## Visual Studio Extension Notes

The VSIX can be installed into either:

- Normal instance: `%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_<instanceId>\Extensions`
- Experimental instance (`/rootsuffix Exp`): `%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_<instanceId>Exp\Extensions`

If `.jalxaml` IntelliSense only shows raw XML suggestions, verify the extension is installed in the same instance you are using.

## Compatibility Notes

- Jalium.UI is not positioned as a drop-in WPF replacement yet.
- API names and behavior are intentionally close to familiar WPF concepts, but differences exist.
- Keep package versions aligned across all `Jalium.UI.*` packages.

## Contributing

Issues and pull requests are welcome. For large changes, include:

- Motivation and design summary
- Behavioral impact/risk
- Validation steps (tests or manual verification)

## Community

For discussions, questions, or community support, you can join the QQ group:

**QQ: 1079778999**

## License

MIT. See [LICENSE](LICENSE).
