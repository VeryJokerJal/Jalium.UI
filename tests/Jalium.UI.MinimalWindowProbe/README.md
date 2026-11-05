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

## 2026-09-19 内存测量补充

该程序创建一个 800×600、`Content = null` 的可见窗口，并验证完整桌面主题已加载。使用默认标题栏和默认渲染后端。采样由外部进程完成。

## 低内存发布

使用现有 `LowMemory` NativeAOT 配置发布；普通 `dotnet build -c Release` 是另一种运行方式，不能套用这里的 AOT 测量结果。

在仓库根目录运行：

```powershell
$buildRoot = Join-Path ([IO.Path]::GetTempPath()) 'Jalium-MinimalWindow-AOT'
$payload = Join-Path (Get-Location) 'artifacts/minimal-window-aot'
dotnet publish tests/Jalium.UI.MinimalWindowProbe/Jalium.UI.MinimalWindowProbe.csproj `
    -c Release -r win-x64 -p:PublishProfile=LowMemory `
    "-p:JaliumBuildRoot=$buildRoot" -o $payload
```

`JaliumBuildRoot` 必须使用绝对路径，避免生成文件落进共享源码目录。发布需要已安装的 Windows NativeAOT 编译工具链。

2026-09-19 的本地测量中，源码声明版本为 26.11.0，而恢复源仅提供 26.10.9 的构建工具包，因此额外使用了 `-p:Version=26.10.9 -p:AssemblyVersion=26.10.9.0 -p:FileVersion=26.10.9.0`。这是该次构建的版本覆盖，不是修改仓库版本文件；依赖可用时不需要沿用。该次测量复用了现有原生 DLL，没有重新编译原生源码。

## 空闲 Private Bytes

```powershell
./tools/measure-window-memory.ps1 `
    -ProbePath "$payload/Jalium.UI.MinimalWindowProbe.exe" -MinimalApplication `
    -Runs 3 -StabilizationSeconds 5 -MeasurementSeconds 10 -IntervalMilliseconds 250 `
    -OutputDirectory ./artifacts/minimal-window-idle-results
```

查看 `aggregate.csv` 中的 `PrivateMemoryMeanMiB` 和 `PrivateMemoryMaximumMiB`。旧脚本的 `WorkingSetBelowBudget` 检查的是 Working Set，不能用它判断 Private Bytes 是否达到 10 MiB。MiB = 1,048,576 字节。

## 位置和尺寸变化

无需全局鼠标输入的程序化测试：

```powershell
./tools/measure-window-bounds-memory.ps1 `
    -ProbePath "$payload/Jalium.UI.MinimalWindowProbe.exe" `
    -OutputDirectory ./artifacts/minimal-window-bounds-results -ActiveSeconds 5
```

该脚本只对自身启动的窗口使用 `SetWindowPos`，不改变前台焦点，不发送全局鼠标输入，不伪造进入或退出拖拽消息。它测量正常空闲、位置变化、尺寸变化及恢复空闲，并输出原始采样、阶段平均值/采样峰值、原生模块来源和退出校验。它不等同于鼠标拖拽测试。

真实鼠标测试入口也支持同一个最小宿主：

```powershell
./tools/measure-window-drag-stress.ps1 `
    -ProbePath "$payload/Jalium.UI.MinimalWindowProbe.exe" -MinimalApplication `
    -IncludeCaptionDrag -WarmupSeconds 5 -PhaseSeconds 5 `
    -OutputDirectory ./artifacts/minimal-window-mouse-results
```

真实鼠标脚本会操作桌面光标，只向经检查的测试窗口发送按键事件。首次优化时执行被自动安全检查拦截；后续两次实际启动也因未取得前台焦点而在输入前中止。新 AOT 产物目前只有程序化移动/缩放结果，不能据此声称真实鼠标拖拽已通过。

所有测试关闭工作集裁剪，不主动调用 `GC.Collect`，不改变主题或隐藏窗口来压低测量值。输出目录须选择未使用的新目录。

## 首次 AOT 测量的范围

2026-09-19 的 AOT 空闲三轮测量：Private Bytes 平均 8.607 MiB，采样峰值 8.727 MiB，共 123 个稳定期采样。程序化移动平均 8.715 MiB，程序化连续缩放为 12.403 MiB；缩放阶段尚未达到约 10 MiB 的目标。

详见仓库中的 `evidence/private-bytes-10mib-20260919/report.md`。这些结果只验证空窗口，不代表所有控件、动态 XAML、Razor 脚本或反射用法的 AOT 兼容性。发布日志保留了现有 AOT/裁剪警告。

## PrivateMemory 配置

后续优化将 Software 帧缓冲扩容余量从 50% 降为 12.5%，保留 64 KiB 对齐和容量复用。使用这个改动须先重新构建原生 DLL，不能继续引用旧的 Software DLL。完整原生构建命令见 `docs/empty-window-memory.md`。

新增的 `PrivateMemory` 配置在 `LowMemory` 基础上设置 `System.GC.Gen0MaxBudget=524288`，即 512 KiB 的 Gen0 分配预算。它让临时对象更频繁地由运行时自动回收，不是托管堆上限，也没有主动调用 `GC.Collect`。高分配量应用需另外评估 GC 暂停及吞吐量，不应把空窗结果推广到复杂场景。

```powershell
# 使用绝对路径；$nativeRoot 指向刚完成构建的原生输出根（其下包含 win-x64/Release）。
dotnet publish tests/Jalium.UI.MinimalWindowProbe/Jalium.UI.MinimalWindowProbe.csproj `
    -c Release -r win-x64 -p:PublishProfile=PrivateMemory `
    "-p:JaliumBuildRoot=$buildRoot" "-p:JaliumNativeOutputRoot=$nativeRoot" -o $payload
```

参数烘焙进 AOT 产物，启动不需要设置环境变量。可用 `-p:JaliumGen0MaxBudgetBytes=0` 恢复运行时默认预算，或指定其他字节数。普通 `LowMemory` 发布配置保持原来的 GC 策略。

后续原始数据、正式产物、构建限制及失败测量记录保存在 `evidence/private-bytes-resize-20260919/report.md`。
