# Jalium.UI

A modern GPU-accelerated UI framework for .NET 10, featuring XAML-like markup (JALXAML) and DirectX 12 rendering.

## Features

- **GPU-Accelerated Rendering** - Built on DirectX 12 for high-performance graphics
- **JALXAML Markup** - Familiar XAML-like syntax for declarative UI design
- **WPF-Compatible API** - Similar control hierarchy and layout system
- **Source Generators** - Compile-time code generation for JALXAML files
- **Modern .NET** - Targets .NET 10 with latest C# features

## Installation

Install via NuGet:

```bash
dotnet add package Jalium.UI
```

Or install individual packages:

```bash
dotnet add package Jalium.UI.Core
dotnet add package Jalium.UI.Controls
dotnet add package Jalium.UI.Media
dotnet add package Jalium.UI.Xaml
```

## Quick Start

### 1. Create a Window

```csharp
using Jalium.UI;
using Jalium.UI.Controls;

var window = new Window
{
    Title = "Hello Jalium.UI",
    Width = 800,
    Height = 600
};

var button = new Button
{
    Content = "Click Me!",
    HorizontalAlignment = HorizontalAlignment.Center,
    VerticalAlignment = VerticalAlignment.Center
};

button.Click += (s, e) => MessageBox.Show("Hello, World!");

window.Content = button;
window.Show();
```

### 2. Using JALXAML

Create a `.jalxaml` file:

```xml
<Window xmlns="https://jalium.dev/ui"
        Title="My App"
        Width="800" Height="600">
    <StackPanel>
        <TextBlock Text="Welcome to Jalium.UI" FontSize="24" />
        <Button Content="Click Me" Click="OnButtonClick" />
    </StackPanel>
</Window>
```

## Architecture

```
Jalium.UI
├── Jalium.UI.Core          # DependencyObject, Visual, UIElement, Layout
├── Jalium.UI.Media         # Brush, Pen, Geometry, DrawingContext
├── Jalium.UI.Input         # Mouse, Keyboard, Touch handling
├── Jalium.UI.Controls      # Button, TextBox, Panel, Window, etc.
├── Jalium.UI.Xaml          # JALXAML parser and markup extensions
├── Jalium.UI.Interop       # Native P/Invoke layer + DirectX bindings
├── Jalium.UI.Gpu           # GPU resource management
└── Jalium.UI.Build         # MSBuild tasks for JALXAML compilation
```

## Available Controls

### Layout
- `StackPanel` - Linear layout (horizontal/vertical)
- `Grid` - Row/column-based layout
- `Canvas` - Absolute positioning
- `DockPanel` - Dock-based layout
- `WrapPanel` - Wrapping flow layout
- `UniformGrid` - Equal-sized grid cells

### Input
- `Button` / `RepeatButton` / `ToggleButton`
- `TextBox` / `PasswordBox` / `RichTextBox`
- `CheckBox` / `RadioButton`
- `ComboBox` / `ListBox`
- `Slider` / `ScrollBar`

### Display
- `TextBlock` / `Label`
- `Image`
- `Border`
- `ProgressBar`
- `ToolTip`

### Advanced
- `TabControl`
- `TreeView`
- `DataGrid`
- `Menu` / `ContextMenu`
- `InkCanvas`

## Requirements

- .NET 10.0 or later
- Windows 10/11 (x64)
- DirectX 12 compatible GPU

## Building from Source

```bash
git clone https://github.com/VeryJokerJal/Jalium-UI-Gallery.git
cd Jalium.UI
dotnet build -c Release
```

### Visual Studio Extension Instance Notes

The VSIX can be installed into either:

- Normal instance: `%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_<instanceId>\Extensions`
- Experimental instance (`/rootsuffix Exp`): `%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_<instanceId>Exp\Extensions`

If IntelliSense in `.jalxaml` only shows XML suggestions (for example `<![CDATA[`), the extension is usually installed only in the Exp instance while you are editing in the normal instance.

Use these scripts to install to the intended target:

```powershell
.\scripts\install-vsix-normal.ps1 -KillBlockingProcesses
.\scripts\install-vsix-exp.ps1 -KillBlockingProcesses
```

To refresh MEF cache for the normal instance:

```powershell
Rename-Item "$env:LOCALAPPDATA\Microsoft\VisualStudio\18.0_dc6d66ae\ComponentModelCache" `
            "ComponentModelCache.bak_$(Get-Date -Format yyyyMMddHHmmss)"
```

Note: the current `Jalium.UI.VisualStudio` extension does not yet provide a visual designer host (`ProvideEditorFactory`/designer surface). This release targets JALXAML language service and IntelliSense.

### Pack NuGet Packages

```bash
dotnet pack src/packaging/Jalium.UI.csproj -c Release -o ./packages
```

## Project Structure

```
Jalium.UI/
├── src/
│   ├── managed/           # C# projects
│   │   ├── Jalium.UI.Core/
│   │   ├── Jalium.UI.Controls/
│   │   ├── Jalium.UI.Media/
│   │   ├── Jalium.UI.Input/
│   │   ├── Jalium.UI.Interop/
│   │   ├── Jalium.UI.Xaml/
│   │   ├── Jalium.UI.Gpu/
│   │   ├── Jalium.UI.Build/
│   │   ├── Jalium.UI.Compiler/
│   │   └── Jalium.UI.Xaml.SourceGenerator/
│   ├── native/            # C++/DirectX native code
│   └── packaging/         # NuGet metapackage
├── samples/
│   └── Jalium.UI.Gallery/ # Demo application
├── tests/
│   └── Jalium.UI.Tests/   # Unit tests
└── packages/              # Generated NuGet packages
```

## License

MIT License - see [LICENSE](LICENSE) for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
