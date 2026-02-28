# Jalium.UI

Jalium.UI is a Windows-first, GPU-accelerated UI framework for .NET 10.
It combines a WPF-style object model, JALXAML markup, and a DirectX 12 renderer.

This repository contains the full framework stack: managed UI layers, native rendering backends,
build-time tooling, packaging projects, and tests.

## Project Status

- Active development (APIs can still evolve between minor versions)
- Primary target: Windows 10/11 x64
- Runtime target: .NET 10 (`net10.0-windows`)
- Rendering backend: DirectX 12

## Why Jalium.UI

- GPU-native rendering pipeline with native backend integration
- Familiar UI programming model (`DependencyObject`, `UIElement`, panels, templates, resources)
- JALXAML markup and runtime parser (`Jalium.UI.Markup.XamlReader`)
- Build-time tooling via NuGet (`Jalium.UI.Build`, `Jalium.UI.Xaml.SourceGenerator`)
- Broad control surface including layout, text/input, navigation, data, docking, ink, and web host controls

## Framework Composition

### Managed Packages

| Package | Responsibility |
| --- | --- |
| `Jalium.UI.Core` | dependency property system, visual tree, layout core, routed events, binding foundations |
| `Jalium.UI.Media` | brushes, geometry, drawing primitives, animation/media infrastructure |
| `Jalium.UI.Input` | mouse/keyboard/touch/stylus input abstractions and routing |
| `Jalium.UI.Interop` | managed/native bridge and runtime native dependency packaging |
| `Jalium.UI.Gpu` | GPU-side managed resource and render infrastructure |
| `Jalium.UI.Controls` | controls, panels, templates, windowing, themes |
| `Jalium.UI.Xaml` | JALXAML parse/load pipeline and markup services |
| `Jalium.UI.Build` | MSBuild tasks and build assets for JALXAML workflow |
| `Jalium.UI.Xaml.SourceGenerator` | Roslyn source generator for XAML/code-behind integration |
| `Jalium.UI` | metapackage that references the full framework stack |

### Native Modules

| Module | Responsibility |
| --- | --- |
| `jalium.native.core` | native core runtime layer |
| `jalium.native.d3d12` | DirectX 12 render target/backend |
| `jalium.native.browser` | browser/WebView native integration layer |

## Capability Overview

### Layout and Visual Tree

- Core panels: `Grid`, `StackPanel`, `Canvas`, `DockPanel`, `WrapPanel`, `UniformGrid`
- Virtualization and presenters: `VirtualizingStackPanel`, DataGrid presenters/panels, tab/tool panels
- Window-level layout host, overlay layer, title bar composition, chrome integration

### Controls

- Inputs: `Button`, `TextBox`, `PasswordBox`, `NumberBox`, `AutoCompleteBox`, `ComboBox`, `Slider`
- Data/navigation: `TreeView`, `NavigationView`, `TabControl`, `DataGrid`, menu/flyout family
- Rich scenarios: `InkCanvas`, `DocumentViewer`, `WebView`/`WebBrowser`, `TitleBar`, docking components

### Input Pipeline

- Pointer and keyboard routing
- Touch and stylus pathways with plugin-style stylus infrastructure
- Scroll and manipulation related control behaviors

### Markup and Tooling

- Runtime parsing: `Jalium.UI.Markup.XamlReader`
- Build integration through packaged MSBuild targets/tasks
- Source-generator package for compile-time assistance in JALXAML workflows

## Installation

### Recommended (metapackage)

```bash
dotnet add package Jalium.UI
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

```bash
# Build the framework (through metapackage dependency graph)
dotnet build src/packaging/Jalium.UI.csproj -c Release

# Run tests
dotnet test tests/Jalium.UI.Tests/Jalium.UI.Tests.csproj -c Release
```

## NuGet Packaging

```bash
# Pack metapackage + referenced packable projects
dotnet pack src/packaging/Jalium.UI.csproj -c Release -o artifacts/nuget
```

For per-project package output, run `dotnet pack` on each packable project under `src/managed/Jalium.UI.*` and `src/packaging`.

## Repository Layout

```text
Jalium.UI/
  src/
    managed/
      Jalium.UI/
      Jalium.UI.Core/
      Jalium.UI.Media/
      Jalium.UI.Input/
      Jalium.UI.Interop/
      Jalium.UI.Gpu/
      Jalium.UI.Controls/
      Jalium.UI.Xaml/
      Jalium.UI.Build/
      Jalium.UI.Xaml.SourceGenerator/
      Jalium.UI.Compiler/
    native/
      jalium.native.core/
      jalium.native.d3d12/
      jalium.native.browser/
    packaging/
      Jalium.UI.csproj
  tests/
    Jalium.UI.Tests/
```

## Visual Studio Extension Notes

The VSIX can be installed into either:

- Normal instance: `%LOCALAPPDATA%\\Microsoft\\VisualStudio\\18.0_<instanceId>\\Extensions`
- Experimental instance (`/rootsuffix Exp`): `%LOCALAPPDATA%\\Microsoft\\VisualStudio\\18.0_<instanceId>Exp\\Extensions`

If `.jalxaml` IntelliSense only shows raw XML suggestions, verify the extension is installed in the same instance you are using.

## Compatibility Notes

- Jalium.UI is not positioned as a drop-in WPF replacement yet.
- API names and behavior are intentionally close to familiar WPF concepts in many areas, but differences exist.
- Keep package versions aligned across all `Jalium.UI.*` packages.

## Contributing

Issues and pull requests are welcome. For large changes, include:

- motivation and design summary
- behavioral impact/risk
- validation steps (tests or manual verification)

## License

MIT. See [LICENSE](LICENSE).
