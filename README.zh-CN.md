# Jalium.UI

[English](README.md) | **简体中文** | [日本語](README.ja.md) | [한국어](README.ko.md)

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/VeryJokerJal/Jalium.UI)

Jalium.UI 是一个面向 .NET 10 的 GPU 加速跨平台 UI 框架。
它融合了 WPF 风格的对象模型、带 Razor 语法扩展的 JALXAML 标记语言，
以及平台原生的渲染后端（DirectX 12、Vulkan、Metal、软件渲染）。

## 项目状态

- 积极开发中 —— v26.10.2-preview（小版本之间 API 仍可能演进）
- 主要目标平台：Windows 10/11 x64
- 跨平台：Android（arm64-v8a、x86_64）、Linux（Vulkan）、macOS（Metal）
- 运行时目标：.NET 10（`net10.0-windows`、`net10.0-android`、`net10.0`）
- 渲染：DirectX 12（Windows）、Vulkan（Linux/Android）、Metal（macOS）、软件渲染回退

## 为什么选择 Jalium.UI

- GPU 原生渲染管线，支持 ClearType 亚像素文本渲染
- 熟悉的编程模型（`DependencyObject`、`UIElement`、面板、模板、资源）
- 带 Razor 语法扩展的 JALXAML 标记语言（`@Path`、`@(expr)`、`@{ ... }`、`@if/@section/@RenderSection`）
- 丰富的控件库：80+ 个控件，包括 Charts、Ribbon、Docking、InkCanvas、WebView、Terminal、WindowsFormsHost
- 通过 NuGet 提供构建期工具链（`Jalium.UI.Build`、`Jalium.UI.Xaml.SourceGenerator`）
- 带自动化对等体（automation peer）的 UIA 无障碍支持
- 视觉特效：液态玻璃、背景模糊、亚克力（acrylic）、云母（mica）、过渡着色器、动画位图（GIF / APNG / 动画 WebP）
- 字素簇（grapheme-cluster）感知的文本编辑（UAX#29）—— emoji、ZWJ 序列、肤色修饰符、国旗永不会被拆分
- 自包含的 `Jalium.Extensions.*` 技术栈（Hosting / DI / Configuration / Options / Logging / Metrics）—— 不依赖 `Microsoft.Extensions.Hosting`
- 原生音频管线（miniaudio + dr_libs / minimp3），支持 WSOLA 保持音高的时间伸缩

## 框架组成

### 托管包

| 包 | 职责 |
| --- | --- |
| `Jalium.UI.Core` | 依赖属性系统、可视化树、布局、路由事件、绑定、动画 |
| `Jalium.UI.Media` | 画笔、几何图形、绘图图元、文本排版、图像处理、视觉特效 |
| `Jalium.UI.Input` | 鼠标、键盘、触摸、触控笔输入抽象与路由 |
| `Jalium.UI.Interop` | 托管/原生桥接、P/Invoke、运行时原生依赖打包 |
| `Jalium.UI.Gpu` | GPU 资源管理、渲染图、材质、着色器、后端抽象 |
| `Jalium.UI.Controls` | 控件、面板、模板、窗口化、主题、停靠、图表 |
| `Jalium.UI.Xaml` | JALXAML 解析/加载管线、Razor 语法支持、标记服务 |
| `Jalium.UI.Build` | 用于 JALXAML 编译工作流的 MSBuild 任务与构建资产 |
| `Jalium.UI.Xaml.SourceGenerator` | 用于 XAML/代码隐藏集成的 Roslyn 源生成器 |
| `Jalium.UI.Compiler` | 独立的 `jalxamlc.exe` 编译器工具 |
| `Jalium.UI` | 引用整个框架技术栈的元包（metapackage） |

### 原生模块

| 模块 | 平台 | 职责 |
| --- | --- | --- |
| `jalium.native.core` | 全部 | 原生核心运行时、后端注册表、上下文管理 |
| `jalium.native.d3d12` | Windows | DirectX 12 渲染目标与 Vello GPU 管线 |
| `jalium.native.vulkan` | Linux、Android | Vulkan 渲染后端 |
| `jalium.native.metal` | macOS | Metal 渲染后端 |
| `jalium.native.software` | 全部 | 基于 CPU 的软件渲染回退 |
| `jalium.native.platform` | 全部 | 平台抽象（窗口、输入、事件） |
| `jalium.native.text` | Linux、Android | 跨平台文本引擎（FreeType + HarfBuzz） |
| `jalium.native.browser` | Windows | WebView2 浏览器集成 |
| `jalium.native.media.core` | 全部 | 跨平台媒体 C ABI + 共享音频（miniaudio / dr_libs / minimp3 / stb_vorbis） |
| `jalium.native.media.windows` | Windows | Media Foundation 视频 / 摄像头 / AAC 解码器 + WIC 图像处理 |
| `jalium.native.aot` | 全部 | NativeAOT 聚合器（硬链接 media、text、各后端） |

### 平台包

| 包 | 目标 |
| --- | --- |
| `Jalium.UI.Desktop` | `net10.0-windows` —— 携带原生 DLL 的桌面发行版 |
| `Jalium.UI.Android` | `net10.0-android` —— 携带原生 .so 库的 Android 发行版 |

## 能力概览

### 布局与可视化树

- 核心面板：`Grid`、`StackPanel`、`Canvas`、`DockPanel`、`WrapPanel`、`UniformGrid`
- 虚拟化：`VirtualizingStackPanel`、DataGrid 呈现器/面板
- 停靠：`DockLayout`、`DockSplitPanel`、`DockTabPanel`、`Split`
- 窗口级布局宿主、覆盖层、标题栏组合、chrome 集成

### 控件

- **输入类**：`Button`、`TextBox`、`PasswordBox`、`NumberBox`、`AutoCompleteBox`、`ComboBox`、`Slider`、`CheckBox`、`RadioButton`
- **数据类**：`TreeView`、`DataGrid`、`TreeDataGrid`、`ListBox`、`ListView`
- **导航类**：`NavigationView`、`TabControl`、`Ribbon`、`CommandBar`、`MenuBar`
- **文档类**：`FlowDocumentViewer`、`FlowDocumentReader`、`FlowDocumentScrollViewer`、`Markdown`
- **图表类**：分类轴、日期时间轴、对数轴，并带图表图例
- **富功能类**：`InkCanvas`、`WebView`/`WebBrowser`、`EditControl`、`QRCode`（自托管编码器）、`TitleBar`、`Terminal`
- **互操作类**：`WindowsFormsHost`（在 `net10.0-windows` 上托管 `System.Windows.Forms` 控件）
- **打印类**：`PrintDialog`，由原生 Win32 平台层提供支持
- **通知类**：Toast 风格的通知系统

### 文本编辑

- 字素簇感知的光标、选择、分词与删除，覆盖 `TextBox`、
  `PasswordBox`、`EditControl`、`RichTextBox`、`TextBlock`、`Label`、`Markdown`、
  `Terminal` 和 `TextEffectPresenter` —— emoji（ZWJ / 肤色 / 国旗 /
  组合记号）永不会在簇中间被拆分（通过 `StringInfo.GetTextElementEnumerator` 实现 UAX#29）。
- 针对密码 / 只读字段的 IME 抑制，使输入法组合输入无法篡改
  受保护的编辑器状态。
- 支持 BOM 自动检测的多编码文件 IO（`TextBox`、`EditControl`、`Markdown`、
  `RichTextBox` 上的 `LoadFromFile` / `SaveToFile`）。通过 `ModuleInitializer`
  注册了 `CodePagesEncodingProvider`，因此 GBK / Shift-JIS / 任何非默认代码页
  都开箱即用。
- `Terminal` 新增 `Encoding` 属性和有状态的 `Decoder`，使输出字节
  按用户的终端编码解码，且不会拆分多字节序列。
- 与 WPF 对齐的 `TextBox.CharacterCasing` / `MinLines` / `MaxLines`、`ComboBox.StaysOpenOnEdit`。

### 文本渲染

- ClearType 亚像素文本渲染，采用双源混合（dual-source blending）。
- CPU 光栅化回退路径。
- 通过 FreeType + HarfBuzz 实现跨平台文本整形（shaping）（Linux/Android）。
- 按元素设置的 `TextOptions.{TextRenderingMode, TextFormattingMode, TextHintingMode}`
  可继承附加属性 —— 取值经由 `FormattedText` → 原生
  `JaliumTextFormat` 流转并抵达光栅器：
  - D3D12：`GlyphKey` 包含 `(aaMode, hintingMode)`；`RasterizeGlyph` 遵循 `key.mode`。
  - Vulkan / Windows：`LOGFONT.lfQuality` 在 bilevel（双色）/ smoothed（平滑）/ ClearType 之间切换；
    字体缓存 + 文本缓存 + GDI 字体池的键都包含 `fontQuality`。
- 进程级渲染模式覆盖 + 彩色 emoji 光栅化。

### 输入管线

- 带命中测试的指针与键盘路由
- 带手势识别的触摸与触控笔通路
- 滚动与操控（manipulation）事件处理

### GPU 渲染与特效

- 带 Vello GPU 计算管线（path、clip、tile 各阶段）的 DirectX 12
- 背景特效：模糊、亚克力、云母、磨砂玻璃
- 带折射、色散（chromatic aberration）的液态玻璃
- 过渡着色器与元素特效（模糊、投影）
- 动画位图：GIF、APNG、动画 WebP
- 通过 HLSL 支持自定义着色器
- 用于大型图片网格的位图降采样缓存 + 虚拟化 wrap 面板
- 在 DevTools 性能选项卡中呈现的统一 path/bitmap 遥测 C ABI

### 宿主 / DI / 配置

- 自包含的 `Jalium.Extensions.*` 技术栈位于 `Jalium.UI.Controls` 内部
  （不依赖 `Microsoft.Extensions.Hosting` 包及其 18 个传递依赖中的任何一个）。
- 涵盖 Hosting（`HostBuilder` / `Host` / `HostApplicationBuilder`）、
  DependencyInjection（含键控服务 + `ActivatorUtilities`）、
  Configuration（Json / Xml / Ini / Memory / CommandLine / UserSecrets）、
  Options（带 `DataAnnotations` 校验）、Logging（含 `LoggerMessage`
  源生成器）、Metrics、Caching、FileProviders、
  FileSystemGlobbing、ObjectPool、Primitives。
- Console 支持系有意未实现。

### 音频管线

- 原生 `MiniAudioDevice` 播放 + `NativeAudioDecoder`，涵盖 WAV / FLAC /
  MP3 / Vorbis（跨平台）和 AAC（Windows 上通过 Media Foundation）。
- 托管 `IAudioProcessor` 链，含用于保持音高时间伸缩的 `WsolaSpeedProcessor`。
- 音频编译单元（TU）会被编译进各平台对应的 `jalium.native.media.*` 库中，
  使符号落入托管 P/Invoke 所加载的同一个 DLL。

### 3D 动画类型

- `Point3DAnimation`、`Rotation3DAnimation`、`Size3DAnimation`、
  `Vector3DAnimation`，补全了 WPF 的动画类型集合。

### 无障碍

- 面向核心控件与专用控件的 UIA 自动化对等体
- Chart、DiffViewer、HexEditor、JsonTreeViewer、Map、PropertyGrid 的自动化
- `Window.ResolveCursor` 会为禁用元素返回标准箭头，
  使悬停状态不会与已启用的控件混淆。

### 标记与工具

- 运行时解析：`Jalium.UI.Markup.XamlReader`
- 通过打包的 MSBuild 目标/任务进行构建集成
- 用于编译期 JALXAML 代码隐藏的源生成器
- 源生成器对 Razor 指令的编译期降级（lowering）—— 见下文。

## JALXAML 中的 Razor 语法

JALXAML 在现有 `{Binding ...}` 之上支持 Razor 风格语法作为附加语法糖：

- `@Path`
- `@(expr)`
- `@{ ... }`
- 混合文本模板（用于字符串/对象目标）
- `@if(expr){<Element />}` 块指令（带完整的 `else if` / `else` 链）
- 用于模板化内容的 `@section`/`@RenderSection`
- 转义：`@@` 和 `\@`

绑定源的解析顺序为先 `DataContext`，然后回退到代码隐藏。

更新行为：

- 可观察源（`INotifyPropertyChanged` / 依赖属性）：实时更新。
- 不可观察的 CLR 源：在加载时进行一次性求值。

### 编译期降级

JALXAML 源生成器会在构建时降级以下内容，使热路径中
不存在运行时解析开销：

- `@if` / `@else if` / `@else` 链。
- `@section` / `@RenderSection`。
- 值表达式（`@Path`、`@(expr)`）。
- 经由 `SetCompiledBinding` 的 `{Binding ...}`（源生成器的 `SplitParameters`
  与运行时解析器保持逐行一致）。
- 自定义 xmlns 元素类型 —— 当某个 `.jalxaml` 使用通过 `XmlnsDefinition`
  暴露的控件库时，源生成器会在编译期解析出 CLR 类型，
  而非回退到运行时反射（有助于裁剪 / AOT）。

`Setter.Value` 系有意不降级。

有关语法细节与规则，请参阅 [`docs/razor-syntax.md`](docs/razor-syntax.md)。

## 安装

### 推荐方式（元包）

```bash
dotnet add package Jalium.UI
```

### 平台专用

```bash
# Windows 桌面
dotnet add package Jalium.UI.Desktop

# Android
dotnet add package Jalium.UI.Android
```

### 细粒度安装（进阶）

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

## 快速上手（C#）

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

## 快速上手（JALXAML 运行时解析）

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

## 从源码构建

### 前置条件

- .NET 10 SDK（`net10.0-windows`）
- 安装了 C++ 工作负载的 Visual Studio（用于原生模块）
- Vulkan SDK（可选，用于 Vulkan 后端）
- Android NDK（可选，用于 Android 构建）

### 构建

```bash
# 构建完整框架
dotnet build src/packaging/Jalium.UI/Jalium.UI.csproj -c Release

# 运行测试
dotnet test tests/Jalium.UI.Tests/Jalium.UI.Tests.csproj -c Release

# 构建原生模块（在 VS 开发人员命令提示符中）
msbuild src/native/Jalium.Native.sln /m /p:Configuration=Release /p:Platform=x64

# 为 Android 构建
bash src/native/build-android-deps.sh  # FreeType + HarfBuzz
bash src/native/build-android.sh       # 原生库
```

### NuGet 打包

```bash
dotnet pack src/packaging/Jalium.UI/Jalium.UI.csproj -c Release -o artifacts/nuget
```

有关详细的构建配置，请参阅 [`docs/manual-build-configuration.md`](docs/manual-build-configuration.md)。

## 仓库结构

```text
Jalium.UI/
  src/
    managed/
      Jalium.UI.Core/          # 依赖属性系统、可视化树、布局
      Jalium.UI.Media/         # 画笔、几何图形、绘图、文本、图像处理
      Jalium.UI.Input/         # 输入抽象与路由
      Jalium.UI.Interop/       # 原生桥接与 P/Invoke
      Jalium.UI.Gpu/           # GPU 资源、渲染图、着色器
      Jalium.UI.Controls/      # 控件、面板、主题、停靠、图表
      Jalium.UI.Xaml/          # JALXAML 解析器与 Razor 支持
      Jalium.UI.Build/         # 用于 JALXAML 编译的 MSBuild 任务
      Jalium.UI.Xaml.SourceGenerator/  # Roslyn 源生成器
      Jalium.UI.Compiler/      # 独立的 JALXAML 编译器
    native/
      jalium.native.core/      # 原生运行时核心
      jalium.native.d3d12/     # DirectX 12 + Vello GPU 后端
      jalium.native.vulkan/    # Vulkan 后端
      jalium.native.metal/     # Metal 后端（macOS）
      jalium.native.software/  # CPU 软件渲染器
      jalium.native.platform/  # 平台抽象层
      jalium.native.text/      # FreeType + HarfBuzz 文本引擎
      jalium.native.browser/   # WebView2 集成
    packaging/
      Jalium.UI/               # 主元包
      Jalium.UI.Desktop/       # Windows 桌面包
      Jalium.UI.Android/       # Android 包
  tests/
    Jalium.UI.Tests/           # xUnit 测试套件（70+ 个测试类）
    Jalium.UI.ShaderDemo/      # 着色器特效演示
  docs/
    razor-syntax.md            # Razor 语法参考
    drawing-api.md             # 绘图 API 文档
    manual-build-configuration.md  # 构建配置指南
```

## 文档

| 文档 | 描述 |
| --- | --- |
| [`docs/razor-syntax.md`](docs/razor-syntax.md) | JALXAML 的 Razor 语法参考 |
| [`docs/drawing-api.md`](docs/drawing-api.md) | 绘图 API（DrawingContext、GPU 特效、渲染） |
| [`docs/manual-build-configuration.md`](docs/manual-build-configuration.md) | 手动构建配置指南 |

## Visual Studio 扩展说明

该 VSIX 可安装到以下任一位置：

- 普通实例：`%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_<instanceId>\Extensions`
- 实验性实例（`/rootsuffix Exp`）：`%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_<instanceId>Exp\Extensions`

如果 `.jalxaml` 的 IntelliSense 只显示原始 XML 建议，请确认该扩展已安装在你正在使用的同一个实例中。

## 兼容性说明

- Jalium.UI 目前尚未定位为可直接替换 WPF 的方案（drop-in replacement）。
- API 名称与行为有意贴近大家熟悉的 WPF 概念，但仍存在差异。
- 请保持所有 `Jalium.UI.*` 包的版本一致。

## 贡献

欢迎提交 Issue 和 Pull Request。对于较大的改动，请包含：

- 动机与设计概述
- 行为影响/风险
- 验证步骤（测试或手动验证）

## 社区

如需讨论、提问或获取社区支持，可加入 QQ 群：

**QQ: 1079778999**

## 许可证

MIT。参见 [LICENSE](LICENSE)。