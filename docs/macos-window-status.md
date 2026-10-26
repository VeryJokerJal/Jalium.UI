# macOS Window 状态与剩余验收

此清单按当前 `Window`、`IPlatformWindow` 和 AppKit 桥接的公开能力整理，保留“完整补齐所有 macOS 行为，首先是 Window”的目标。源码接入、定向自动检查、真实界面操作和平台资格分别记录；某一套通过数量不能代替完整 Window 或 macOS 资格。

入口：[Window](../src/managed/Jalium.UI.Controls/Window.cs)、[平台契约](../src/managed/Jalium.UI.Controls/Platform/IPlatformWindow.cs)、[托管桥](../src/managed/Jalium.UI.Controls/Platform/NativePlatformWindow.cs)、[AppKit 实现](../src/native/jalium.native.platform/src/platform_apple.mm)。此前逐轮复现及测量见[行为记录](macos-window-behavior.md)。

## v329：Intel 与编译下限的原生 Window 门禁

在 arm64 主机指定 `CMAKE_OSX_ARCHITECTURES=x86_64` 后，父配置实际编译出 x86_64 Mach-O，却将 30 个映像放入 `osx-arm64` 负载目录。原有 RID 推断使用 `CMAKE_SYSTEM_PROCESSOR`，该值在这种 Apple 架构选择下仍为主机架构。现在优先采用明确的 Apple 目标架构；一个 .NET RID 只接收一个架构，混合架构须分别构建后再组合，错误显式 RID 继续拒绝。

父配置还拒绝正确的显式 `osx-x64`、接受错误的 `osx-arm64`，两项均已实际配置复现。修复后的旧错误缓存须重新配置到独立构建目录；其失败保护没有改写原有负载。

修复后，同一目标的 30 个映像写入 `osx-x64`，46 个原生 Window／剪贴板／线程检查均已构建并列入 CTest 清单。映像 CPU 与编译下限均检查到 x86_64／macOS 15.0。7 项真实 CMake 配置控制覆盖两种架构的自动和显式 RID、交叉错误 RID及混合架构拒绝。本地 arm64 对应路径也独立构建并运行 11 项相关原生合约。

本机为 macOS 27.0.1／arm64、Xcode 27.1，没有可用 Rosetta；本轮本地 x64 只证明构建及负载身份。验证器保留 `executed=false`、`passed=false`，不会把交叉构建写成 Intel 运行通过；真实 arm64 主机冒充 Intel 的运行请求在测试前被拒绝。

新增 [native Window 验证器](../eng/apple/verify-window-native.py) 与 [Apple CI](../.github/workflows/apple.yml) 中的两个 Intel 作业：

| 主机要求 | 工具链 | 必须检查的证据 |
| --- | --- | --- |
| macOS 15 / 原生 x86_64 | Xcode 26.3 | 实际主机 OS／CPU、无翻译、RID、薄 Mach-O CPU及 15.0 编译下限；完整 CTest 与 JUnit |
| macOS 26 / 原生 x86_64 | Xcode 26.6 | 相同的原生 Window 契约与完整性检查 |

作业使用当前完整的 Window／剪贴板／线程正则，不排除显示、焦点、输入或无障碍组。JUnit 必须与配置得到的完整 CTest 清单一一相同；缺失、重复、失败、跳过、未运行或计数不一致均失败。架构、主机、RID 和 JUnit 的 5 项正向／20 项负向控制用于防止错误放行。真实 Intel 运行结论须以 PR 对应提交的这两个作业终态及日志为准；跨编译和 ARM 回归不替代它们。

首次原生 Intel 结果（提交 `664c4e9e`）：macOS 15.7.9／Xcode 26.3 的 46 项全部通过；macOS 26 的 37 项通过、9 项失败或超时，集中在最小化完成及后续恢复。后者尚未取得资格。新增独立 [NSWindow 基线](../eng/apple/window-appkit-transitions.mm)，在加载 Jalium 之前核实原生和无边框窗口的开始／完成通知、最小化状态及恢复；日志保留 Dock 进程和通知线程，基线失败直接阻止运行门禁，不放宽原有合约。

独立基线在同一 Intel 26 镜像复现：系统前台与应用 PID 相同，Dock 进程数为 0；标准和无边框 NSWindow 都收到 will-miniaturize、原生位变真，却在 8 秒内未收到 did-miniaturize。CI 现在先通过 launchd 启动内置 Dock 服务，再要求 Dock 就绪及两种原生窗口完成最小化／恢复。完整 46 项仍须全部实际运行通过；尚未取得新的 Intel 26 通过结果。另修正基本 Window 检查的四处等待：原生位与框架确认都达到预期后才断言，避免把较早的原生位当成完成通知。

启用 Dock 后，独立基线仍缺少完成通知，尚不能把原因归于 Dock 缺失。为区分测试事件循环与运行器行为，基线改为逐个 NSTimer 阶段驱动，阶段间返回 NSApplication 的外层主循环；共用启动协议增加可选异步完成入口，同步合约入口不变。Intel 26 仍须取得实际的两窗口基线及完整 CTest 通过记录。

这两个作业检查原生 Window，关闭不相关的运行时 shader 编译器依赖；原有生产 ALL、运行时编译器、SDK 和包验证不变。它们不证明 Intel SDK 包、渲染器、分发或物理输入已经完成。

运行器及工具链选择已核对 [GitHub 主机表](https://docs.github.com/en/actions/how-tos/write-workflows/choose-where-workflows-run/choose-the-runner-for-a-job)、[macOS 15 映像](https://github.com/actions/runner-images/blob/main/images/macos/macos-15-Readme.md)和 [macOS 26 映像](https://github.com/actions/runner-images/blob/main/images/macos/macos-26-Readme.md)。Apple 架构变量的含义见 [CMake 文档](https://cmake.org/cmake/help/latest/variable/CMAKE_OSX_ARCHITECTURES.html)。

## v330：SDK 目标架构、负载 CPU 和依赖路径

原有 SDK 在 `RuntimeIdentifier=osx-x64` 时仍选择 ARM 原生目录、ARM CMake 目录及 `runtimes/osx-arm64/native`。现在三个位置均由目标 RID 选择，并保留显式目标和原生目录覆盖。默认值跟随 .NET SDK 主机；原生构建、Metal 资源和 Gallery 入口共用架构选择，提供 x64 的普通／开发预设及明确的交叉构建模式。

导出符号并不足以证明目标 CPU。所有待复制的 dylib 现在须包含指定 CPU，核心／平台／Metal 的 ABI 符号也在同一切片中查询。已用真实通用 Mach-O 复现旧校验漏检：ARM 切片完整，而 x64 切片缺少接口时，合并查询仍放行；新校验正确拒绝。打包不执行 Build 的路径同样保留检查。

三份源码的架构／ABI／MSBuild 实际调用及平台别名回归各通过 29 项。PR、现有本地历史和冻结工作区分别完成包含运行时着色器编译器的 x64 原生负载；另在原生 ARM 主机完成没有显式 `CMAKE_OSX_ARCHITECTURES` 的真实 CMake 负载，核实默认架构完成流程。PR／本地历史的 x64 SDK 宿主和 NuGet 包、PR 的 ARM 对照宿主和包均已实际构建；包内 8 个动态库位于各自 RID，文件内容与所选负载一致。各 app 的原生文件 CPU 与深层签名检查通过。每个 app 实际校验 Resources 与 MonoBundle 中的 16 个文件，对应 8 个不同库。

本轮同时修复锁定 DXC 的版本文件生成路径：`TO_NATIVE_PATH` 给带空格路径插入转义后，Ninja 将其解释为错误目录；改为保留 CMake 原始路径。原失败构建中的两个版本目标已成功恢复，独立的带空格目录探针也生成两个有效版本文件。

ARM 对照宿主已实际运行完成 48/48 组、720/720 场景，另保持一个窗口经 CUA 查看了截图和无障碍树，标题、进度条及包含中文／emoji／Miii 的文本框均正常显示。这不是物理输入、IME、VoiceOver 或多屏资格。

本机工具链仍为 .NET 10.0.300／macOS workload 26.5.10280、Xcode 27.1，SDK 本地构建明确使用开发用的 `ValidateXcodeVersion=false`。这些结果证明本地构建及负载身份，不是锁定发布工具链或 Intel 执行通过；没有安装 Rosetta或把 x64 程序当作已运行。新增 Intel SDK CI 使用仓库锁定工具链，检查实际打包并运行现有 48 组／720 场景的 Window 通知矩阵，须以其提交及终态为准。

v329 的最新已发布提交 `15ff3ed2` 已取得 Intel 15 的外层 AppKit 基线 2/2、完整原生 Window 检查 46/46，零失败及跳过。Intel 26 的独立 NSWindow 基线仍缺少最小化完成通知，因此相关资格继续开放。SDK 新作业不替代这个失败门禁，也不关闭多 RID 发布包、NativeAOT、物理输入、VoiceOver、多屏和分发验收。

## v331：双架构 NuGet 与真实消费端

一个包现在按 `JaliumMacNativeOutputRoot` 收集两个完整 RID，应用运行时复制继续只选择自己的 RID。`JaliumMacRequireAllNativeRids=true` 强制两者齐全；单 RID 过滤、显式旧目录、部分输出和错误组合均有实际 `pack --no-build` 检查。每个 RID 要求全部 8 个动态库、离线 Metal 库及匹配的资源清单，并校验 CPU、关键 ABI、资源大小与 SHA-256。

真实 NuGet 消费端发现了仅检查压缩包时未暴露的问题：ARM 构建的 SDK 程序集嵌入了原生资源，x64 应用的 MonoBundle 虽然是 x64，Resources 中仍出现 ARM 动态库与 ARM Metal 清单。现在 SDK 源项目使用明确的发布目录元数据，NuGet 的 `buildTransitive` 资源目标按消费应用 RID 提供 Metal 文件；SDK 程序集不再携带构建 CPU 的资源。两个消费端各只有 8 个原生文件，全部属于其目标 CPU，资源清单 RID 及哈希、深层签名均通过。独立缓存、包源映射和包 SHA-512 回校证明实际使用的是本轮生成的包。

PR 与冻结工作区分别完成 ARM64／x64 原生负载，包含运行时着色器编译器。PR、本地历史和冻结工作区的校验器在两个 RID 分别通过 31 项架构／ABI／MSBuild／预设检查，共 186 项。另外 26 项实际打包及传输控制全部通过，覆盖单／双 RID、显式目录、缺少完成标记或模块／Metal、错误 CPU、来源版本、文件哈希、重复成员、路径穿越、符号链接及目标目录已存在。错误案例按预期失败，不产生可用包或目标目录。

`Jalium.UI.MacOS.PackageSmoke` 只通过 PackageReference 消费 SDK。新增组合包 CI 对两种 RID 实际恢复、构建并检查 app，且运行 ARM Window 启动。本地已执行这个 CI 消费端代码块，两种 app 都通过；ARM 实际运行 Metal Window，窗口句柄、Unicode 文本、运行架构和程序集加载位置检查通过，随后正常退出。CUA 实际截图及无障碍树显示中文标题、说明与“保留中文🙂 Miii”文本框，未见本次画面的文字裁切。此处仍为 `physicalInput=false`、`foregroundRequired=false`；没有重做物理输入或完整字体／前台矩阵。

跨作业原生传输要求 RID、配置、精确提交参数和完整文件哈希匹配，提取到临时目录后重新检查资源与 CPU／ABI，最后原子发布。它用于防止作业输入混用，不是签名或来源证明。组合包作业同时依赖 ARM 和 Intel 生产作业，不能借此绕过失败的 Window 门禁。

v330 的 [ARM 生产作业](https://github.com/VeryJokerJal/Jalium.UI/actions/runs/40280644649/job/121432953254)与 [Intel SDK 作业](https://github.com/VeryJokerJal/Jalium.UI/actions/runs/40280644649/job/121432953241)最终均因新增 `lipo` 参数顺序失败。已把输入文件放在 `-verify_arch` 前，并保留原始工具错误；[Apple cctools 的解析实现](https://github.com/apple-oss-distributions/cctools/blob/main/misc/lipo.c)会把该选项之后的所有参数当作架构。CPU／ABI 通过条件没有放宽。新提交在锁定 Xcode 26.6 的 CI 结果须单独记录，本地 Xcode 27.1 不能替代它。

本地仍使用 .NET 10.0.300／workload 26.5.10280，并明确设置开发用的 Xcode 版本覆盖。x64 仅完成构建、包消费与身份检查，没有执行。Intel 26 的独立 NSWindow 最小化完成通知门禁、完整 Intel SDK 执行、精确最低系统、NativeAOT、物理输入、VoiceOver、多屏／Spaces及签名公证分发继续开放。

## Window 能力与验收边界

下表的“已有证据”指仓库中的实现、测试及行为记录，不表示该行全部组合已经验收。未完成项继续作为需求保留。

| 需求组 | 已有实现或专项证据 | 完整验收仍需覆盖 |
| --- | --- | --- |
| 创建、取消、关闭、销毁及替代对象 | [创建寿命](../src/native/jalium.native.platform/tests/platform_apple_window_creation_lifetime_tests.mm)、[样式重入](../src/native/jalium.native.platform/tests/platform_apple_style_reentrancy_tests.mm)、托管 Closing/Closed | 系统操作、所属窗口与重开的完整前台组合 |
| Show、Hide、ShowActivated、首次绘制、重开 | Window 显示事务、visibility 与首次帧检查 | 每种标题栏／渲染器的前台及最后窗口重开组合 |
| 手动位置、居中、owner 居中和客户区原点 | [启动几何](../src/native/jalium.native.platform/tests/platform_apple_window_creation_geometry_tests.mm)、[启动流程](../src/native/jalium.native.platform/tests/platform_apple_window_startup_tests.mm)、坐标辅助测试 | 混合 DPI、负坐标多屏、显示器断开及用户连续拖动 |
| 尺寸、Min/Max、SizeToContent、RestoreBounds | 托管布局、[请求重入](../src/native/jalium.native.platform/tests/platform_apple_scalar_reentrancy_tests.mm)、原生几何／还原测试 | 约束、内容动态变化与真实跨屏拖动的组合 |
| 拖动、八边缩放及中断 | [缩放](../src/native/jalium.native.platform/tests/platform_apple_resize_drag_tests.mm)、Window 输入路由 | 物理拖动、状态切换、嵌套 owner 和多屏组合 |
| 最小化、还原、缩放／最大化、全屏 | 状态确认、恢复队列、转换失败、[全屏还原](../src/native/jalium.native.platform/tests/platform_apple_fullscreen_restore_tests.mm) | Spaces、多窗口、Dock、系统动画及不同显示布局 |
| Native／Custom 标题栏、扩展内容、按钮与双击 | 原生属性、标题栏生命周期／动作及焦点测试 | 其余双击系统偏好、前台连续切换及屏幕阅读器 |
| 标题、图标、置顶、透明度、禁用和鼠标穿透 | Window 属性、标量请求与 287 项样式检查 | 原生图标／Dock 显示、材质、失活及辅助功能组合 |
| Window 菜单、ShowInTaskbar 与轮换参与 | Window 菜单／system-menu、collectionBehavior 与 excludedFromWindowsMenu 一致性 | 系统轮换、Mission Control、Spaces 与物理菜单键盘操作 |
| Owner、OwnedWindows、ShowDialog、DialogResult | [owner](../src/native/jalium.native.platform/tests/platform_apple_window_owner_tests.mm)、托管模态及关闭策略 | 嵌套窗口、系统切换、焦点还原与取消异常的前台矩阵 |
| Activate、Key/Main、Activated/Deactivated | [键盘焦点](../src/native/jalium.native.platform/tests/platform_apple_keyboard_focus_tests.mm)、前台选择门及托管激活寿命 | 当前完整矩阵仍缺系统前台 PID、AppKit active 和目标 Key/Main 同时成立的证据 |
| 系统菜单、标题栏及编辑菜单命令 | menu-tracking、document-menu、sheet-keyboard、原生编辑命令记录 | 物理键盘／修饰键、真实 sheet 与跨应用路径 |
| 背景材质、主题和可访问性系统偏好 | SystemBackdrop、standalone appearance、系统通知与动态资源 | 系统偏好真实变更、减少透明度、失活合成和 VoiceOver |
| DPI、显示配置、刷新率及 Surface | display-configuration、原生坐标、GetSurface/GetMonitorRefreshRate | 不同刷新率／DPI 的实物多屏、热插拔与移动期间绘制 |
| 键盘、指针、滚轮、手势、平板及捕获 | input-state、cursor-ownership、gestures/tablets 与托管输入检查 | 不同键盘布局、物理功能键／侧键、触控板及平板设备矩阵 |
| 文本、IME、候选窗和焦点交接 | [文本输入寿命](../src/native/jalium.native.platform/tests/platform_apple_text_input_lifetime_tests.mm)、原生文本服务、历史真实中文及菜单验收 | 更广真实输入法、长文档、复杂格式及前台跨窗口组合 |
| Popup、ToolTip、菜单和文本编辑焦点 | popup-keyboard、托管 Popup 逻辑焦点／owner 与输入路由 | 真实失活／重开、层级、物理 Escape／修饰键及辅助功能组合 |
| 剪贴板、拖放、取消和数据所有权 | clipboard、[拖放寿命](../src/native/jalium.native.platform/tests/platform_apple_window_drag_tests.mm)、跨应用历史记录 | 物理修饰键、外部 promised data、多应用和设备组合 |
| Window AX 的角色、属性、写入、关系及通知 | [原生 Window AX](../src/native/jalium.native.platform/tests/platform_apple_window_accessibility_tests.mm)、托管桥和外部操作记录 | VoiceOver、外部通知订阅、完整可写属性及复杂内容导航 |
| 应用退出、会话结束及 Dock 重开 | 主应用类、Quit/SessionEnding 策略及 Dock ownership/motion 专项 | 用户实际 Dock、系统注销／关机取消、完整应用生命周期 |
| Metal／软件后端、首次帧和窗口外观读回 | 生产 ALL、SDK 两套 48 项矩阵、历史实际窗口与 97 张读回 | 物理 live-resize、跨屏、窗口遮挡／恢复及压力场景 |
| arm64／x64 与系统版本 | arm64 既有实机／CI；本轮 x64 构建、RID 修复及新增 Intel 门禁 | Intel 原生作业终态、完整 SDK/渲染/交互及确切最低系统资格 |
| SDK、NativeAOT、应用身份和分发 | AppKit 入口、导出、签名及打包校验 | SDK 路径、组合 RID 包及真实消费端本地检查已通过；锁定工具链 CI、完整 Intel SDK 执行、AOT、签名公证与安装分发继续开放 |

平台契约也需保留语义：`SetDesktopApplicationId` 和键盘移动／缩放能力在当前桥中属于 Linux 能力，macOS 对应应用身份来自 bundle；不能仅因返回 false/None 就认定 AppKit Window 缺陷。Dock 属于应用，ShowInTaskbar 在 macOS 控制窗口菜单／轮换／Mission Control 参与。移动平台的软键盘、方向和安全区不据此替代 macOS 文本输入验收。

## Window 之后的完整 macOS 范围

Window 完成需要上述需求及证据闭合，再继续核对控件与布局、普通／富文本与字体、设备输入与可访问性、渲染／图像／效果、媒体／浏览器、应用宿主和发布分发。各领域此前已有实现与定向检查，仍须按正常、边界、异常及反复操作验证，不能用 Window 或一次 CI 的通过替代全部平台资格。
