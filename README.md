# Jalium.UI

**English** | [简体中文](README.zh-CN.md) | [日本語](README.ja.md) | [한국어](README.ko.md)

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/VeryJokerJal/Jalium.UI)

Jalium.UI is a GPU-accelerated, cross-platform UI framework for .NET 10.
It combines a WPF-style object model, JALXAML markup with Razor syntax extensions,
and platform-native rendering backends (DirectX 12, Vulkan, Metal, Software).

## Project Status

- Active development — v26.10.2-preview (APIs can still evolve between minor versions)
- Primary target: Windows 10/11 x64
- Cross-platform: Android (arm64-v8a, x86_64), Linux (Vulkan), macOS (Metal)
- Runtime target: .NET 10 (`net10.0-windows`, `net10.0-android`, `net10.0`)
- Rendering: DirectX 12 (Windows), Vulkan (Linux/Android), Metal (macOS), Software fallback

## Why Jalium.UI

- GPU-native rendering pipeline with ClearType sub-pixel text rendering
- Familiar programming model (`DependencyObject`, `UIElement`, panels, templates, resources)
- JALXAML markup with Razor syntax extensions (`@Path`, `@(expr)`, `@{ ... }`, `@if/@section/@RenderSection`)
- Rich control library: 80+ controls including Charts, Ribbon, Docking, InkCanvas, WebView, Terminal, WindowsFormsHost
- Build-time tooling via NuGet (`Jalium.UI.Build`, `Jalium.UI.Xaml.SourceGenerator`)
- UIA accessibility support with automation peers
- Visual effects: liquid glass, backdrop blur, acrylic, mica, transition shaders, animated bitmaps (GIF / APNG / animated WebP)
- Grapheme-cluster aware text editing (UAX#29) — emoji, ZWJ sequences, skin-tone modifiers, country flags never split
- Self-contained `Jalium.Extensions.*` stack (Hosting / DI / Configuration / Options / Logging / Metrics) — no `Microsoft.Extensions.Hosting` dependency
- Native audio pipeline (miniaudio + dr_libs / minimp3) with WSOLA pitch-preserving time-stretching

## Framework Composition

### Managed Packages

| Package | Responsibility |
| --- | --- |
| `Jalium.UI.Core` | Dependency property system, visual tree, layout, routed events, binding, animation |
| `Jalium.UI.Media` | Brushes, geometry, drawing primitives, text formatting, imaging, visual effects |
| `Jalium.UI.Input` | Mouse, keyboard, touch, stylus input abstractions and routing |
| `Jalium.UI.Interop` | Managed/native bridge, P/Invoke, runtime native dependency packaging |
| `Jalium.UI.Gpu` | GPU resource management, render graph, materials, shaders, backend abstraction |
| `Jalium.UI.Controls` | Controls, panels, templates, windowing, themes, docking, charts |
| `Jalium.UI.Xaml` | JALXAML parse/load pipeline, Razor syntax support, markup services |
| `Jalium.UI.Build` | MSBuild tasks and build assets for JALXAML compilation workflow |
| `Jalium.UI.Xaml.SourceGenerator` | Roslyn source generator for XAML/code-behind integration |
| `Jalium.UI.Compiler` | Standalone `jalxamlc.exe` compiler tool |
| `Jalium.UI` | Metapackage that references the full framework stack |

### Native Modules

| Module | Platforms | Responsibility |
| --- | --- | --- |
| `jalium.native.core` | All | Native core runtime, backend registry, context management |
| `jalium.native.d3d12` | Windows | DirectX 12 render target and Vello GPU pipeline |
| `jalium.native.vulkan` | Linux, Android | Vulkan render backend |
| `jalium.native.metal` | macOS | Metal render backend |
| `jalium.native.software` | All | CPU-based software rendering fallback |
| `jalium.native.platform` | All | Platform abstraction (window, input, events) |
| `jalium.native.text` | Linux, Android | Cross-platform text engine (FreeType + HarfBuzz) |
| `jalium.native.browser` | Windows | WebView2 browser integration |
| `jalium.native.media.core` | All | Cross-platform media C ABI + shared audio (miniaudio / dr_libs / minimp3 / stb_vorbis) |
| `jalium.native.media.windows` | Windows | Media Foundation video / camera / AAC decoder + WIC imaging |
| `jalium.native.aot` | All | NativeAOT aggregator (hard-links media, text, backends) |

### Platform Packages

| Package | Target |
| --- | --- |
| `Jalium.UI.Desktop` | `net10.0-windows` — Desktop distribution with native DLLs |
| `Jalium.UI.Android` | `net10.0-android` — Android distribution with native .so libraries |

## Capability Overview

### Layout and Visual Tree

- Core panels: `Grid`, `StackPanel`, `Canvas`, `DockPanel`, `WrapPanel`, `UniformGrid`
- Virtualization: `VirtualizingStackPanel`, DataGrid presenters/panels
- Docking: `DockLayout`, `DockSplitPanel`, `DockTabPanel`, `Split`
- Window-level layout host, overlay layer, title bar composition, chrome integration

### Controls

- **Input**: `Button`, `TextBox`, `PasswordBox`, `NumberBox`, `AutoCompleteBox`, `ComboBox`, `Slider`, `CheckBox`, `RadioButton`
- **Data**: `TreeView`, `DataGrid`, `TreeDataGrid`, `ListBox`, `ListView`
- **Navigation**: `NavigationView`, `TabControl`, `Ribbon`, `CommandBar`, `MenuBar`
- **Documents**: `FlowDocumentViewer`, `FlowDocumentReader`, `FlowDocumentScrollViewer`, `Markdown`
- **Charts**: Category, DateTime, Logarithmic axes with chart legend
- **Rich**: `InkCanvas`, `WebView`/`WebBrowser`, `EditControl`, `QRCode` (self-hosted encoder), `TitleBar`, `Terminal`
- **Interop**: `WindowsFormsHost` (host `System.Windows.Forms` controls on `net10.0-windows`)
- **Printing**: `PrintDialog` backed by a native Win32 platform layer
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
- Cross-platform text shaping via FreeType + HarfBuzz (Linux/Android).
- Per-element `TextOptions.{TextRenderingMode, TextFormattingMode, TextHintingMode}`
  inheritable attached properties — values flow through `FormattedText` → native
  `JaliumTextFormat` and reach the rasterizer:
  - D3D12: `GlyphKey` includes `(aaMode, hintingMode)`; `RasterizeGlyph` honours `key.mode`.
  - Vulkan / Windows: `LOGFONT.lfQuality` flips between bilevel / smoothed / ClearType;
    font cache + text cache + GDI font pool keys all include `fontQuality`.
- Process-wide rendering mode override + colour-emoji rasterization.

### Input Pipeline

- Pointer and keyboard routing with hit testing
- Touch and stylus pathways with gesture recognition
- Scroll and manipulation event handling

### GPU Rendering & Effects

- DirectX 12 with Vello GPU compute pipeline (path, clip, tile stages)
- Backdrop effects: blur, acrylic, mica, frosted glass
- Liquid glass with refraction, chromatic aberration
- Transition shaders and element effects (blur, drop shadow)
- Animated bitmaps: GIF, APNG, animated WebP
- Custom shader support via HLSL
- Bitmap downscale cache + virtualizing wrap panel for large image grids
- Unified path/bitmap telemetry C ABI surfaced in DevTools Perf tab

### Hosting / DI / Configuration

- Self-contained `Jalium.Extensions.*` stack lives inside `Jalium.UI.Controls`
  (no `Microsoft.Extensions.Hosting` package or any of its 18 transitive deps).
- Covers Hosting (`HostBuilder` / `Host` / `HostApplicationBuilder`),
  DependencyInjection (incl. keyed services + `ActivatorUtilities`),
  Configuration (Json / Xml / Ini / Memory / CommandLine / UserSecrets),
  Options (with `DataAnnotations` validation), Logging (`LoggerMessage`
  source-generator inclusive), Metrics, Caching, FileProviders,
  FileSystemGlobbing, ObjectPool, Primitives.
- Console support is intentionally not implemented.

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

- UIA automation peers for core and specialized controls
- Chart, DiffViewer, HexEditor, JsonTreeViewer, Map, PropertyGrid automation
- `Window.ResolveCursor` returns the standard arrow for disabled elements so
  hover state cannot be confused with enabled controls.

### Markup and Tooling

- Runtime parsing: `Jalium.UI.Markup.XamlReader`
- Build integration through packaged MSBuild targets/tasks
- Source generator for compile-time JALXAML code-behind
- Source generator compile-time lowering of Razor directives — see below.

## Razor Syntax in JALXAML

JALXAML supports Razor-style syntax as additive sugar on top of existing `{Binding ...}`:

- `@Path`
- `@(expr)`
- `@{ ... }`
- mixed text templates (for string/object targets)
- `@if(expr){<Element />}` block directives (with full `else if` / `else` chains)
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
<Window xmlns="https://jalium.dev/ui" Title="JALXAML Window" Width="800" Height="500">
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

- .NET 10 SDK (`net10.0-windows`)
- Visual Studio with C++ workload (for native modules)
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

# Build for Android
bash src/native/build-android-deps.sh  # FreeType + HarfBuzz
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
      Jalium.UI.Media/         # Brushes, geometry, drawing, text, imaging
      Jalium.UI.Input/         # Input abstractions and routing
      Jalium.UI.Interop/       # Native bridge and P/Invoke
      Jalium.UI.Gpu/           # GPU resources, render graph, shaders
      Jalium.UI.Controls/      # Controls, panels, themes, docking, charts
      Jalium.UI.Xaml/          # JALXAML parser and Razor support
      Jalium.UI.Build/         # MSBuild tasks for JALXAML compilation
      Jalium.UI.Xaml.SourceGenerator/  # Roslyn source generator
      Jalium.UI.Compiler/      # Standalone JALXAML compiler
    native/
      jalium.native.core/      # Native runtime core
      jalium.native.d3d12/     # DirectX 12 + Vello GPU backend
      jalium.native.vulkan/    # Vulkan backend
      jalium.native.metal/     # Metal backend (macOS)
      jalium.native.software/  # CPU software renderer
      jalium.native.platform/  # Platform abstraction layer
      jalium.native.text/      # FreeType + HarfBuzz text engine
      jalium.native.browser/   # WebView2 integration
    packaging/
      Jalium.UI/               # Main metapackage
      Jalium.UI.Desktop/       # Windows desktop package
      Jalium.UI.Android/       # Android package
  tests/
    Jalium.UI.Tests/           # xUnit test suite (70+ test classes)
    Jalium.UI.ShaderDemo/      # Shader effects demo
  docs/
    razor-syntax.md            # Razor syntax reference
    drawing-api.md             # Drawing API documentation
    manual-build-configuration.md  # Build configuration guide
```

## Documentation

| Document | Description |
| --- | --- |
| [`docs/razor-syntax.md`](docs/razor-syntax.md) | Razor syntax reference for JALXAML |
| [`docs/drawing-api.md`](docs/drawing-api.md) | Drawing API (DrawingContext, GPU effects, rendering) |
| [`docs/manual-build-configuration.md`](docs/manual-build-configuration.md) | Manual build configuration guide |

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
