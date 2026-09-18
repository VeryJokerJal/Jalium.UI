# Jalium.UI Minimal Window Probe

This project is a diagnostic host for separating the cost of a normal minimal
Jalium.UI application from the instrumentation and feature-test machinery in
`Jalium.UI.MemoryProbe`. It is intentionally a separate executable and must not
be presented as the old probe's baseline with the same input: the two programs
root and execute different host code.

The framework side stays complete. The project uses the same source-project
references as `Jalium.UI.MemoryProbe`, including Controls, Core, GPU, Media,
Interop, Xaml, and Desktop. `Application` construction follows the normal
framework path; `Application.Application()` initializes `ThemeManager`, which
loads the default control theme into application resources. The host does not
replace that path with a hand-built dictionary, remove Xaml, disable reflection
or globalization, or select a different title-bar backend.

`Program.cs` performs only the normal application work needed for the diagnostic:

- enter through an STA `Main` method;
- construct one `Application`;
- construct one visible 800 x 600 `Window` with the default title-bar behavior
  and `Content = null`;
- return the exit code from `Application.Run(window, args)` so closing the last
  window follows the framework's normal shutdown path.

It deliberately contains no console setup or markers, `Diagnostics.Process`
inspection, timers, forced collections, statistics, command-line scenario
parser, lifetime tracking, or feature-test tree. Adding those facilities would
turn the host back into an instrumented probe and obscure the difference this
project is meant to diagnose.

## Publish

Publish from the repository root with absolute, dedicated build and output
directories. The checked-in profile matches the MemoryProbe low-memory NativeAOT
settings: Release, `win-x64`, self-contained NativeAOT, size optimization, and
`IlcDehydrate=false`.

```powershell
$repoRoot = (Resolve-Path .).Path
$buildRoot = Join-Path $repoRoot 'artifacts\empty-window-memory\minimal-window-build'
$publishDir = Join-Path $repoRoot 'artifacts\empty-window-memory\minimal-window-aot'

dotnet publish .\tests\Jalium.UI.MinimalWindowProbe\Jalium.UI.MinimalWindowProbe.csproj `
  -p:PublishProfile=LowMemory `
  -p:JaliumBuildRoot="$buildRoot" `
  -o "$publishDir"
```

Do not launch the executable as part of the build. The prime measurement run
owns UI timing and the external 30-second working-set sampling. That run also
checks loaded native-module origins. Theme and feature behavior remain covered
by the full `Jalium.UI.MemoryProbe` regression scenarios; keeping those checks
outside this executable avoids adding their memory overhead to the minimal host.
