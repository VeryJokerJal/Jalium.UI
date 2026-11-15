# Linux Window 行为与验收

本轮起始于 2026-10-14，范围为 Jalium.UI 的 X11 与 Wayland 桌面 Window。
采用项目既有的自绘标题栏、主题、中文内容及键盘操作；窗口管理请求遵守
ICCCM/EWMH 与 xdg-shell，最终位置、层叠与激活由桌面环境决定。
依据：[EWMH 窗口属性](https://specifications.freedesktop.org/wm/latest/ar01s05.html)、
[xdg-shell 协议](https://raw.githubusercontent.com/wayland-mirror/wayland-protocols/main/stable/xdg-shell/xdg-shell.xml)。

最新进度见第十七批：修复触点取消重入与 v3 预编辑回调关闭窗口后的两处已释放内存访问，
以及旧预编辑、组合输入结束和托管提交在焦点/窗口变化后继续处理的问题。新增托管专项
18/18；Window 命名范围回归为 527 通过、2 跳过、0 失败。扩大相关范围为 764 通过、
2 跳过、1 失败；全功能当前快照为 6800 通过、86 跳过、25 失败。当前托管/测试源码
已整体同步，测试总数与上一批不同；失败清单和 IconSupportTests 编译排除均显式保留。
两种标题栏完成预编辑、提交、撤销、隐藏恢复与 380 × 360 焦点交互。
物理输入、完整桌面和剩余回调审计等边界仍未完成，不能宣称所有 LinuxOS 行为已补齐。

## 验收清单

| 范围 | 必须覆盖的行为 | 验证方式 |
| --- | --- | --- |
| 创建与显示 | 初始状态、激活偏好、隐藏/再显示、标题与图标、SourceInitialized 中关闭 | 原生协议测试、托管 host |
| 状态 | Normal/Minimized/Maximized/FullScreen 往返、重复操作、原始尺寸与 RestoreBounds、原生变更通知 | Openbox、Weston、绑定测试 |
| 几何 | Width/Height、Left/Top、Min/Max、固定窗口的程序缩放、SizeToContent、用户缩放取消自动尺寸、绑定保持 | 托管契约、真实 WM、截图 |
| 自绘边框 | 标题栏拖动、8 个边缘/角落缩放、双击最大化/还原、按钮与内容不误触拖动、ResizeMode | 输入路由契约、真实输入 |
| 窗口菜单 | Alt+Space 开关、可用项与焦点、键盘/鼠标启动移动缩放、Enter 确认与 Esc 取消、外部弹窗及关闭后输入 | 托管/原生契约、Mutter/Openbox、Wayland 回退菜单 |
| 所有权与模态 | Owner、关闭拥有者、嵌套模态、关闭取消、禁用拥有者输入、关闭后焦点恢复 | 生命周期与输入测试、host |
| 输入 | 鼠标抓取、Tab/Shift+Tab、Return/Escape、快捷键、焦点切换、IME 与组合文本 | 托管/原生输入测试、交互验证 |
| 显示器 | 工作区、启动位置、缩放通知、热插拔、跨屏恢复、整数与分数缩放 | XRandR、Wayland 协议、目标桌面验收 |
| 无障碍 | 窗口名称/角色、按钮名称、状态变更、可见焦点、AT-SPI 生命周期 | 托管语义、AT-SPI smoke、读屏验收 |
| 桌面集成 | app_id/WM_CLASS、桌面文件对应、拥有窗口及弹窗继承、动态允许操作 | X11 属性、Wayland wire、绑定与标题栏测试 |
| 异常与资源 | 非法参数、失效抓取、回调中关闭窗口、异常中结束模态、重复开关、事件循环与资源释放 | 原生契约、托管回归 |

## 本轮发现与实现

- Linux 自绘标题栏未接入原生移动与缩放；新增 Linux chrome 输入路由。
- 托管 Width/Height 与 SizeToContent 更新原生窗口仅限 macOS；扩展到 Linux。
- 原生位置和状态回调覆盖绑定；使用当前值更新保留绑定。
- Linux 可见性绑定未驱动原生显示；复用桌面显示生命周期，Hide/Close 同步可见性。
- Linux 模态只禁用直接拥有者；扩展桌面模态栈，保持嵌套会话与原始启用状态。
- Wayland 无激活 token 或键盘 seat 时模态关闭缺少逻辑焦点恢复；分别恢复托管目标与申请系统激活。
- 接受拥有者关闭后，Linux 子窗口仍可取消关闭；保持拥有关系的关闭契约，释放显式和隐式模态子窗口。
- XInput2 鼠标按压未记录根坐标；记录实际按压位置供 EWMH 移动/缩放使用。
- X11 初始状态使用只适用于已映射窗口的 ClientMessage；初次显示前发布属性和 ICCCM StateHint。
- X11 最小化恢复未重新映射；全屏/最大化切换残留互斥状态；补齐映射和状态清理。
- X11 原生 caption 能力未完整反映 ResizeMode 与按钮开关；发布完整 Motif 函数提示。
- Wayland 移动/缩放缺少有效按压校验，非法 edge 可能导致协议断开；校验窗口、按压和边缘。
- Wayland 状态请求抢先覆盖状态，抑制确认事件；普通状态由 compositor configure 发布。
- Wayland 恢复时零尺寸未选择正常尺寸，Hide/Show 丢失状态、owner 和尺寸约束；重建时恢复元数据。
- Wayland 非平铺、非用户缩放的正常还原可以选择保存的正常尺寸；外部还原更新请求缓存与恢复尺寸。
- 恢复尺寸随 DPI 更新；Wayland 图标缓存随角色重建重新提交（仍受可选协议限制）。
- Wayland paint/configure 与 X11 configure 回调中关闭窗口后继续使用窗口；回调后检查窗口存活。
- X11 按协议类型校验关闭消息，并处理外部 DestroyNotify；键盘/文本回调关闭时停止后续访问。

## 测试入口

```bash
JALIUM_NATIVE_BUILD_TESTS=1 bash eng/linux/build-native.sh linux-x64 Release
xvfb-run -a ctest --test-dir src/native/out/build/linux-x64 --output-on-failure
bash eng/linux/test-window-behavior.sh linux-x64 Release
xvfb-run -a dotnet test tests/Jalium.UI.Linux.Tests -c Release \
  --filter FullyQualifiedName~LinuxWindowBehaviorTests
```

`test-window-behavior.sh` 需要测试环境提供 Xvfb、Openbox、Weston 与 xprop。
它在真实窗口管理器和 compositor 中检查状态、恢复、隐藏再显示和回调销毁。
容器内的软件渲染证据与物理 GPU、真实输入设备和桌面环境验收分别记录。

可运行的中文验证程序位于 `tests/Jalium.UI.Linux.HostSmoke`，已加入 solution。
普通启动提供移动、缩放、全屏、模态、关闭取消和键盘验证；`--automated` 检查实际尺寸、
隐藏再显示、文本保留、模态启用状态、焦点恢复及关闭取消。

```bash
dotnet build tests/Jalium.UI.Linux.HostSmoke -c Release
JALIUM_WINDOW_TEST_HOST="$PWD/tests/Jalium.UI.Linux.HostSmoke/bin/Release/net10.0/Jalium.UI.Linux.HostSmoke.dll" \
  bash eng/linux/test-window-behavior.sh linux-x64 Release
```

## 2026-10-15 第一批终态记录

验证环境：Apple Silicon 上的 Docker/QEMU `linux-x64` Ubuntu 20.04、glibc 2.31，
Xvfb/Openbox 与 Weston 8；实际窗口使用软件渲染，未设置 `JALIUM_RENDER_THREAD=0`。

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 原生构建与 CTest | 13/13 通过 | `native-build-window.log`、`ctest-current.log` |
| X11/Openbox Window | 38 项通过 | `window-integration.log` |
| Wayland/Weston Window | 25 项通过 | `window-integration.log` |
| 原有 Wayland 输入/Popup/缩放 smoke | 通过 | `native-wayland-regression.log` |
| Linux Window 与 CSS 资源专项 | 52/52 通过（31 Window、21 CSS） | `managed-window-tests.log`、`test-results/linux-window.trx` |
| 相关便携托管回归 | 424 通过、2 跳过、0 失败 | `managed-window-regression.log`；包括按 OS 返回的其他平台用例，不等同 424 项 Linux 原生验收 |
| macOS 共享行为回归 | 216 通过、2 跳过、0 失败 | `macos-window-regression.log`；不含物理 AppKit/IME 验收 |
| 实际 Window host | X11、Wayland 各 10 项通过，正常退出 | `window-host-integration.log` |
| 外部 X11 输入与截图 | 标题栏移动、右下角缩放、380×360、Tab/Shift+Tab、Return、Alt+Space、Alt+F4 取消/再关闭 | `capture-window-ui.log`、`host-capture.log`、`screenshots/`；已查看全部截图 |
| AT-SPI | 真实 D-Bus 协议 smoke 通过 | `atspi-smoke.log`；窗口树、焦点、属性与生命周期等，未验证 Orca |
| ELF 导出、重定位、glibc 上限 | 7 个 `.so` 通过 | `native-exports.log`、`native-symbol-versions.log` |
| 完整功能回归尝试 | 5756 通过、85 跳过、20 失败 | `managed-functional-regression.log`；排除了页面性能基准及已有 CI 排除项，不能称为完整通过 |

工作区初始已有大量改动，保留了 HEAD、状态、二进制 patch、相关源文件与 SHA-256。
独立原生基线复现确认初始最大化、最小化还原、全屏到最大化均失败，修复后对应测试通过。

托管全工程原始构建受已存在的 `IconSupportTests.cs` 编译错误阻断；验证只在
`artifacts/` 中的临时 MSBuild 配置排除该文件，未修改或移除仓库测试。
Linux 测试工程补嵌入已有 CSS fixture 后，21 项资源测试通过。
整套运行还在页面替换基准中超过不活动超时；窗口基线的整套运行也出现同类基准超时。
上述失败和未完成项不被专项通过替代。
进一步排除基准并完成运行后，保存的窗口基线为 5710 通过、85 跳过、20 失败，
当前为 5756 通过、85 跳过、20 失败，失败用例集合完全相同。
当前增加的 46 个通过包括 25 个新增 Window 用例与单独补齐、验证的 21 个 CSS 资源用例。
此比较没有新增失败，但不能把现有 20 个失败改写为通过。
详情见 `functional-baseline-comparison.json`。

## 第二批：装饰、分数缩放与键盘

- `TitleBarStyle.Native` 保持应用请求和绑定。Wayland 没有服务端装饰时显示现有 Jalium 标题栏；
  装饰协商为服务端模式时移除应用标题栏，避免重复标题栏。模式在 `xdg_surface.configure` 确认后应用。
- 动态切换 Custom/Native、隐藏后再次显示、标题栏按钮开关及全屏隐藏/恢复沿用现有主题和布局。
- 接入 `wp_fractional_scale_v1` 与 `wp_viewporter`。渲染尺寸按逻辑尺寸乘缩放并四舍五入，
  分数缩放使用 buffer scale 1 和逻辑 viewport；缺少可选协议时保留整数输出缩放。
- 尺寸约束、正常恢复尺寸、弹窗定位、指针/触摸/笔/拖放坐标、IME 光标矩形和窗口菜单坐标
  使用同一缩放关系。整数光标主题选择向上取整的清晰度。
- 原生尺寸变化后的布局使当前聚焦控件进入可见区域，保留焦点，避免缩小窗口后必须再次 Tab。
- compositor 将快捷键交给应用时，Alt+F4 走可取消的 Close，Alt+Space 使用既有窗口菜单。
  菜单初始焦点、关闭后的焦点恢复和所属外部弹窗的键盘/文本作用域同时覆盖。
- 原生状态通知中的 `StateChanged` 回调可以再次请求状态变化，避免内部同步标记吞掉应用请求。

协议依据：
[xdg-decoration](https://raw.githubusercontent.com/wayland-mirror/wayland-protocols/main/unstable/xdg-decoration/xdg-decoration-unstable-v1.xml)、
[fractional-scale](https://raw.githubusercontent.com/wayland-mirror/wayland-protocols/main/staging/fractional-scale/fractional-scale-v1.xml)、
[viewporter](https://raw.githubusercontent.com/wayland-mirror/wayland-protocols/main/stable/viewporter/viewporter.xml)。

`--native-titlebar` 可运行 Native 模式的中文 host。测试构建若提供 libweston 开发文件，
会生成独立的 `test-modules/jalium-fractional-scale-test.so`；测试脚本在 Weston 中加载它，
通过真实 Wayland 协议发送 125%、150%、175% 的首选缩放。该模块仅用于测试，
不能把受控 compositor 的协议证据称为真实显示器或 GNOME/KDE 验收。
缺少开发文件时脚本明确报告该项不可用。

第二批终态与截图记录继续保存在 `artifacts/linux-window-20261014/`。

| 第二批检查 | 当前结果 | 证据 |
| --- | --- | --- |
| 原生 CTest | 13/13 通过 | `ctest-chrome-final.log` |
| 窗口、标题栏、输入路由及菜单专项 | 185/185 通过，其中 Linux Window 36 项 | `managed-window-menu.log`、`linux-window-menu.trx` |
| X11/Openbox 与 Wayland/Weston host | 7 次运行正常退出：默认模式各 13 项，Native 模式各 16 项 | `window-menu-final.log`；包含 125%、150%、175% 受控协议偏好 |
| 125% Native 实际输入与截图 | 标题栏移动、边缘缩放、380×360 保留可见焦点、菜单方向键/Enter/Escape、全屏恢复、Alt+F4 取消及再关闭 | `host-wayland-capture.log`、`capture-wayland-ui.log`、`screenshots/wayland-native-125-*.png`；已查看相关截图 |
| ELF ABI 与版本上限 | 7 个产品 `.so` 通过；GLIBC ≤ 2.31，GLIBCXX ≤ 3.4.28 | `native-exports-fractional.log`、`native-versions-fractional.log`；测试模块单独放置 |
| 较大范围功能回归 | 5608 通过、75 跳过、18 失败，没有新增失败 | `managed-functional-menu-final.log`、`linux-functional-menu-final.trx` |

较大范围运行遵循当前 CI 的图像用例排除项，并排除页面性能基准。
18 个失败均出现在已保存的基线中。ImageBrushDecode 被此次筛选排除，
WindowRenderScheduling 的弱引用失败本次没有复现；后者不能凭单次结果计作稳定修复。
详情见 `functional-chrome-final-comparison.json` 与 `functional-chrome.filter`。
原始全工程的 IconSupportTests 编译缺口仍使用前述临时排除配置处理。

构建过程曾出现 NuGet restore 等待和并发 Roslyn 退出 137；已终止任务内的滞留进程，
最终 host 使用单个 MSBuild worker 完成构建，测试均以终态日志为准。

## 协议边界与待验收项

- xdg-shell 不提供 toplevel 全局位置、强制 Topmost、任务栏可见性或整窗 opacity。
  `Left/Top` 只能作为本地请求元数据，不能称为 compositor 实际屏幕位置。
- Wayland 激活由 token 和 compositor 策略决定；初始不激活也无法获得跨 compositor 的保证。
- Wayland 最小化没有状态通知，也没有通用 unminimize 请求；请求状态与真实桌面观察要分开。
- 真实 GNOME/KDE 的分数缩放、Fedora/Arch、物理多显示器、输入法、触摸/笔和读屏验收仍须逐项补充。
- 桌面应用标识与能力协商已接入，当前证据见第三批记录；GNOME/KDE 的应用分组与任务栏表现仍须验收。
- 其他 LinuxOS 子系统沿用 `linux-parity-status.md` 的能力目录；其历史测试结果必须刷新，不能用于证明当前改动已经完整验收。

当前验证日志保存在 `artifacts/linux-window-20261014/`。完成记录只填写已取得终态的测试结果。

## 第三批：桌面标识、能力与新版 compositor

`Application.DesktopApplicationId` 和 `AppBuilderSettings.DesktopApplicationId` 可配置应用身份。
它应对应 `.desktop` 文件的 basename，省略 `.desktop` 后缀；在创建第一个 Linux 原生窗口前设置。
身份锁定后只能再次设置相同值。标题继续使用可读名称，机器标识与标题独立。
默认身份来自 host/入口程序集；无法直接使用的 Unicode 或过长名称使用稳定哈希，
避免不同中文名称都被替换成同一串下划线。

```csharp
var builder = AppBuilder.CreateBuilder(new AppBuilderSettings
{
    DesktopApplicationId = "org.jalium.Sample"
});
```

- Wayland `app_id` 在角色重建时重新提交，原生接口也允许映射后更新。
  X11 `WM_CLASS` 的 class 使用应用身份，instance 优先采用有效的 `RESOURCE_NAME`。
  X11 更新仅在 Withdrawn 状态接受；拥有窗口和原生弹窗继承身份。
- 携带上游 MIT 许可的 xdg-shell XML，旧发行版构建不再限制在早期协议表；
  实际绑定仍限制到已经实现的版本 6。
- `wm_capabilities` 在配对的 surface configure 确认后发布；未知条目忽略。
  X11 读取 `_NET_WM_ALLOWED_ACTIONS`，缺少该属性时保留旧 WM 的兼容行为。
- 标题栏、拖动/缩放和窗口菜单使用确认后的能力，保留应用请求的按钮属性。
  Wayland 不支持的状态请求约束到原生实际状态；能力恢复时仍可重新应用绑定值。
  X11 allowed-actions 约束用户操作，程序改变状态和固定窗口的显式尺寸更新继续可用。
- Weston 15 实际复现全屏尚未退出时最大化请求被忽略。现在先确认并绘制还原状态，
  再发最大化；新请求可以取消待执行动作。避免无关的 unset 请求和延迟配置覆盖正常恢复尺寸。
- 缺少原生窗口菜单能力时，标题栏右键使用既有 Jalium 菜单。打开即获得键盘焦点，
  关闭后恢复原焦点；实际方向键、Enter 与 Escape 已操作验证。
- 实际操作还复现了 Shift+Tab keysym 缺失，以及 text-input-v1 激活后丢弃未被输入法处理的字符键。
  已补映射并恢复硬件字符与重复输入路径，保留组合文本状态约束；按键/字符回调后检查窗口存活。

协议依据：
[ICCCM WM_CLASS](https://xorg.freedesktop.org/archive/current/doc/xorg-docs/icccm/icccm.html)、
[xdg-shell app_id 与能力](https://raw.githubusercontent.com/wayland-mirror/wayland-protocols/main/stable/xdg-shell/xdg-shell.xml)、
[input-method-v1 未处理按键转交](https://raw.githubusercontent.com/wayland-mirror/wayland-protocols/main/unstable/input-method/input-method-unstable-v1.xml)。

新的 `--keyboard-smoke` 从实际键盘完成 Tab、Shift+Tab 和字符输入，最后在字符回调中关闭窗口。
入口已接入 glibc CI 配置；本地执行结果不代替远端 CI 或物理设备验收。

```bash
JALIUM_WINDOW_TEST_HOST="/absolute/path/Jalium.UI.Linux.HostSmoke.dll" \
  bash eng/linux/test-window-keyboard.sh
```

第三批增加 Fedora 44 / Weston 15.0.1 的独立容器，继续使用软件渲染。
真实 wire 日志确认能力事件、配置确认顺序、应用标识以及隐藏重开后的标识保留。
能力缺失/恢复的各分支另用生产 listener 注入测试，不把注入当成 GNOME/KDE 的实际策略。

| 第三批检查 | 已取得的结果 | 证据 |
| --- | --- | --- |
| 身份与能力专项 | 49/49 通过，修复了初次运行的 3 个绑定恢复失败 | `linux-identity-capabilities-fixed.trx` |
| 窗口、标题栏与菜单专项 | 当前程序集 198/198 通过，其中 Linux Window 43、身份 6 项 | `linux-identity-focused-current.trx` |
| 原生 CTest、ELF ABI 与基线 | 13/13；7 个产品 `.so` 导出闭包通过，GLIBC ≤ 2.31，GLIBCXX ≤ 3.4.28 | `ctest-identity-keyboard.log`、`native-exports-identity-keyboard.log`、`native-versions-identity-keyboard.log` |
| Fedora/Weston 15 host | Custom 15 项、Native 18 项均通过且退出正常 | `fedora-host-custom.log`、`fedora-host-native.log`、`fedora-weston15-final.log` |
| Ubuntu/Openbox、Weston 8 host | 7 次运行均正常退出：Custom 各 15 项，Native 各 18 项；含 125%、150%、175% | `window-final.log` |
| 原有 Wayland 协议 smoke | 当前输入/弹窗/缩放 smoke 通过 | `native-wayland-regression-current.log` |
| 真实键盘输入 | X11/Openbox、Weston 8、Fedora/Weston 15 均通过；中文原文保留，ASCII 后缀提交一次，字符回调关闭正常退出 | `window-keyboard-current.log`、`fedora-keyboard-current.log` |
| Fedora 实际界面 | 标题栏移动、780×600/380×360、可见焦点、右键菜单方向键/Enter/Escape、文字输入、反向 Tab、全屏恢复、取消关闭与正常退出 | `fedora-ui-host.log`、`fedora-ui-wire.log`、`screenshots/fedora15-*.png`；已查看相应有效截图 |
| 扩大托管专项 | 300 通过、1 个已存在的 TextBox 高度失败 | `linux-identity-related.trx`；失败已在保存的基线中存在 |
| 较大范围功能回归 | 5620 通过、75 跳过、19 失败；没有新增失败 | `linux-functional-identity-final.trx`、`functional-identity-final-comparison.json` |

较大范围仍使用第二批的 CI 图像用例排除项和页面性能基准排除项，未称为全工程通过。
19 个失败都在保存的基线中出现；窗口弱引用释放失败本次重新复现，不能将上次未复现记作修复。
原始 IconSupportTests 编译问题仍仅通过任务临时配置排除，未修改仓库中的该测试。
真实中文输入法、组合文本用户旅程和读屏仍须另行验收，ASCII 输入与原文保留测试不替代这些项目。

测试环境曾因主机空间不足使 Docker 虚拟机停止；确认服务状态后恢复了环境，
保留容器和数据。一次窗口集成启动因 Xvfb display 不可用而没有运行用例，
该失败不计作产品通过。后续结果均按进程退出与终态日志记录。

## 第四批：身份优先级与渲染暂停

- host 的默认身份仅用于 Application 未明确配置且身份尚未锁定的情形。
  Application 构造函数、XAML 或预构造实例提供的身份保留；显式 builder 设置仍先于用户配置回调应用。
- xdg-shell v6 `suspended` 在配对 surface configure 确认后发布。
  原生 paint 分发和软件/Vulkan presentation readiness 同时暂停；托管 Window 移出全局可渲染计数。
  逻辑 Visibility、WindowState 和输入仍保持自身语义，暂停期间的无效区与内容更新保留。
- 恢复时重新加入帧调度、全量刷新并绘制新内容；Hide/Show 的新角色配置清除旧暂停状态。
  暂停期间的全屏退出到最大化请求等待恢复绘制；通知回调关闭窗口后停止访问。

依据：[xdg-shell suspended](https://raw.githubusercontent.com/wayland-mirror/wayland-protocols/main/stable/xdg-shell/xdg-shell.xml)。

| 第四批检查 | 已取得的结果 | 证据 |
| --- | --- | --- |
| 身份与 Linux Window 专项 | 53/53 通过（身份 9、Window 44） | `linux-window-suspension.trx` |
| 窗口、标题栏与菜单专项 | 202/202 通过 | `linux-window-state-focused.trx` |
| 原生 CTest、ABI 与基线 | 13/13，7 个产品 `.so` 通过；版本上限保持 2.31/3.4.28 | `ctest-suspension.log`、`native-exports-suspension.log`、`native-versions-suspension.log` |
| 真实 WM/compositor 中的窗口契约 | X11 47 项；Wayland 含暂停配置注入、恢复、重开与回调销毁均通过 | `native-window-suspension.log`、`fedora-window-suspension.log` |
| 普通 Host 回归 | Ubuntu 7 次、Fedora 2 次运行均正常退出，15/18 项检查全部通过 | `window-state-host.log`、`fedora-window-suspension.log` |
| 可见 Host 暂停探针 | 暂停时画面保留、更新文本待绘制，恢复后显示新文本，正常退出 | `suspension-ui-host.log`、`suspension-ui-wire.log`、`screenshots/suspension-*.png`；已查看三张状态截图 |
| 较大范围功能回归 | 5625 通过、75 跳过、18 个基线失败，无新增失败 | `linux-functional-window-state.trx`、`functional-window-state-comparison.json` |

探针通过显式 native-test 构建的生产 listener 注入配置，F9 暂停、F10 恢复，
使用 `--suspension-probe` 启动。wire 记录中暂停区间没有主应用 buffer attach，恢复后有两次提交。
这验证原生到托管的暂停链路，未证明 GNOME/KDE 在遮挡、锁屏或多屏情形下的实际策略。
本次窗口弱引用失败没有复现，仍按间歇基线问题保留。

本批构建前清理了任务内已经替代的三套旧构建缓存。原始基线、当前构建、源码快照、
日志、TRX 和截图保留；旧输出 DLL 校验记录见 `removed-obsolete-builds.json`。


## 第五批：Wayland v7 边缘约束与推荐尺寸

- xdg-shell 绑定上限提升到 v7，仍取 compositor、生成协议和实现版本三者的最小值。
  `constrained_left/right/top/bottom` 在配对 surface configure 确认后发布；重复配置不重复通知。
  新角色的首次配置会清除旧角色约束，通知回调同步关闭窗口后停止继续访问。
- 原生与托管分别保护缩放请求；受限角落只沿自由边缩放。
  托管标题栏、无边框窗口的边缘光标与实际请求使用相同的方向。
  约束改变时刷新静止指针的光标，同时保留 OverrideCursor、捕获、弹窗及不可调整窗口的语义。
- `configure_bounds` 以 surface 逻辑坐标缓存，在 Normal 状态由客户端选择尺寸时使用。
  非零 compositor 尺寸仍由 compositor 决定；显式最小尺寸优先，未知 bounds 不构成额外限制。
  其他状态的零宽或零高也逐维保留客户端选择，避免丢弃另一维的有效配置。

协议依据：[xdg-shell states 与 configure_bounds](https://raw.githubusercontent.com/wayland-mirror/wayland-protocols/main/stable/xdg-shell/xdg-shell.xml)。

| 第五批检查 | 已取得的结果 | 证据 |
| --- | --- | --- |
| 身份与 Linux Window 专项 | 55/55（身份 9、Window 46） | `linux-window-edges-vm.trx` |
| 窗口、菜单与输入回归 | 208/208；包含全部 16 种边缘掩码与 8 个边/角位置组合 | `linux-window-edges-focused-vm.trx` |
| 原生 CTest、ABI 与基线 | 13/13；7 个产品库通过；22 个 ELF 的版本上限保持 2.31/3.4.28 | `ctest-edges-vm.log`、`native-exports-edges-vm.log`、`native-versions-edges-vm.log` |
| X11/Openbox 与 Weston | 原生契约及 Ubuntu 7 次普通 Host 运行通过，含 125%/150%/175% | `native-window-edges.log`、`window-edge-host-vm.log` |
| Fedora/Weston 15 | 原生契约与 2 次普通 Host 运行通过 | `fedora-window-edges.log` |
| Mutter 50.5 | 123 项原生检查通过；实际绑定 xdg-shell v7；Custom/Native 两种托管 Host 各自正常完成 | `mutter-window-native.log`、`mutter-window-native-wire.log`、`mutter-window-vm.log`、`mutter-host-*.log` |
| 可见窗口交互 | 受限左边不缩放；左下角只改高至 720×600；解除约束后改宽至 780×600；推荐尺寸变为 380×360 | `constraints-ui-resumed-host.log`、`constraints-ui-resumed-wire.log`、`screenshots/constraints-*.png` |
| 键盘与焦点 | 窄窗口的当前按钮保持可见；Tab/Shift+Tab、Alt+Space/Escape、焦点恢复、取消关闭和再次关闭正常 | 同上；已查看本批 8 张状态截图，探针正常退出 |
| 较大范围功能回归 | 5626 通过、75 跳过、19 个已有基线失败，无新增失败 | `linux-functional-window-edges-vm.trx`、`functional-window-edges-comparison.json` |

可见窗口使用 `--constraints-probe`，F7/F8 注入边缘约束/恢复，F6 注入推荐尺寸，F5 记录光标。
这是生产 listener 到托管输入链路的受控验证；实际 Mutter v7 的协商、推荐尺寸、窗口状态和模态流程另有真实协议记录。
Mutter 使用虚拟显示和默认插件，不能代表完整 GNOME Shell 的交互、锁屏策略或物理设备验收。
KDE、Orca、多显示器及其他 RID 的物理/运行验证仍未完成。

较大范围回归中的窗口弱引用失败再次出现；单独运行该用例通过，仍归为间歇性基线问题，未记作修复。
原始 IconSupportTests 编译问题仍通过任务临时配置排除，未更改该测试。
Normal 自动选择尺寸在没有推荐 bounds 时的最小/最大约束，以及键盘窗口管理和剩余桌面能力，仍须继续核查。
本批通过结果不能替代这些项目，也不表示 Window 或所有 LinuxOS 行为已经补齐。

### 仓库迁移后的验证连续性

仓库移至 `/Volumes/My PSSD/source/Repos/Jalium.UI`，当前 HEAD 为 `d3fbf7f78a833d75f4196d6a507a24636217f4b2`。
原容器和 checkpoint 镜像保留；新 VM 容器使用源码及可执行文件快照，避免外接盘 bind mount 的实际写入失败。
快照为 3651 个文件、约 255.6 MiB，11 个本批源码文件的 hash 核验一致。
迁移后的专项、大范围回归、普通 Host、Mutter 及原生 gates 均以终态结果记录；迁移中断的运行没有计作完成。

迁移产生的 AppleDouble 副本已验证文件头并保留备份，源码数据未改写。
索引副本记录见 `migration-index-sidecars/manifest.json`，其余副本记录见 `migration-source-sidecars/manifest.json`。
构建快照和源码 hash 见 `migration-vm-snapshot.json`、`window-edge-current-sha256.json`；源码修改前快照仍在 `window-edge-baseline/`。


## 第六批：客户端尺寸约束与 KWin 验证

本批先补回归用例，在旧实现上复现 7 个失败，再修复生产实现。

- Normal 状态由客户端选择尺寸时，无 `configure_bounds` 也应用显式 Min/Max。
  每个轴独立合并推荐尺寸与显式范围；0 继续表示该方向没有额外限制。
- 尺寸提示和客户端尺寸选择共用物理到逻辑范围转换：Min 向上、Max 向下取整。
  小于一个逻辑像素的范围以取整后的 Min 保持有效，转换也限制在 int32 范围内。
- 还原时重新检查当前约束；固定窗口使用已保存的正常尺寸，避免被最大化缓冲区尺寸锁住。
- compositor 明确给出的非零尺寸保持其原有语义。依据：
  [xdg-shell 尺寸、最小和最大提示](https://raw.githubusercontent.com/wayland-mirror/wayland-protocols/main/stable/xdg-shell/xdg-shell.xml)。
- Host 的 Native/Custom 标题栏检查依据实际协商结果，覆盖客户端和服务端装饰，
  不再把所有 Wayland compositor 都假定为客户端装饰。普通界面样式没有改变。

| 第六批检查 | 结果 | 证据 |
| --- | --- | --- |
| 修复前复现 | 客户端 Min/Max、逐轴约束、还原和固定窗口共 7 个失败 | `native-window-constraints-red-final.log` |
| X11 与 Weston 原生、普通 Host | 当前实现通过，包含 125%/150%/175% 与两种标题栏模式 | `window-constraints-host.log`、`window-constraints-host-final.log` |
| 原生 CTest、ABI、版本基线 | 13/13；7 个产品库通过；22 个 ELF 保持 GLIBC 2.31 / GLIBCXX 3.4.28 上限 | `ctest-constraints.log`、`native-exports-constraints.log`、`native-versions-constraints.log` |
| 托管窗口、菜单和输入回归 | 208/208 通过 | `linux-window-constraints-focused.trx` |
| 较大范围功能回归 | 5626 通过、75 跳过、19 个已有基线失败，无新增失败 | `linux-functional-window-constraints.trx`、`functional-window-constraints-comparison.json` |
| Mutter 50.5 | 原生 142 项通过，Custom/Native Host 各 15/18 项通过 | `mutter-constraints-native.log`、`mutter-constraints-host-*.log` |
| KWin 6.7.5 + Breeze | 虚拟输出下原生 141 项通过，Custom/Native Host 各 15/18 项通过；协议确认装饰模式 2/1/2 切换 | `kwin-constraints-native.log`、`kwin-constraints-host-*.log`、`kwin-constraints-host-native-wire.log` |
| 可见 Min/Max 探针 | 无推荐尺寸时收敛为最大 480×360、最小 600×420，6 个检查通过；文字输入后恢复，焦点滚动可见 | `size-constraints-ui-host.log`、`screenshots/size-constraints-*.png`，已查看 5 张截图 |

可见尺寸探针通过 `--size-constraints-probe` 显式启用，F2/F3 分别驱动最大/最小约束，
验证原生客户端选择到托管布局的完整路径。独立 native 用例覆盖 compositor 非零尺寸、
窄范围取整、固定窗口与还原；探针没有替代这些检查。

### KWin 嵌套显示的验收边界

KWin 的正常画面已确认仅有一份 Breeze 服务端标题栏，Jalium 内容和中文编辑保持原样。
串行运行中观察并查看了最大化、还原、5 轮带文本焦点的最大化/还原、标题栏拖动
（位置移动 80×40）、服务端边框缩放到 780×600、关闭取消与再次关闭，最终 Host 正常退出。
对应记录：`kwin-control-ui-host.log`、`capture-kwin-control-repeat.log`、`screenshots/kwin-*.png`。

首次嵌套运行与另一虚拟 KWin 同时运行时，最大化后 KWin 自身进程发生 SIGSEGV。
后续串行复测没有再次复现；独立标准 Qt/kdialog 参考窗口也完成了 5 轮最大化/还原。
这些结果不足以确定最初崩溃的根因，不能记为已修复。
另有一次还原截图短暂缺失标题栏与部分画面，后续稳定画面正常，同样保留为显示路径问题。

- 原始崩溃：`capture-kwin-ui-first.log`、`kwin-ui-first-compositor.log`、`kwin-ui-first-wire.log`。
- 串行及参考窗口：`kwin-control-support.txt` 显示 X11WindowedBackend / QPainter；
  `screenshots/kwin-control-restored.png` 保留异常画面，`kwin-repeated-restored.png` 保留后续稳定画面。
- x64 模拟环境中的 GDB 无法读取寄存器，没有取得可用于归因的 backtrace；记录在 `kwin-gdb-ui-compositor.log`。
- 可见测试 helper 现已检查 compositor 存活状态，避免服务进程崩溃后仅因应用退出码为 0 而误记通过。

KWin 测试使用独立 D-Bus、虚拟或 Xvfb 嵌套输出及任务容器中的调度能力移除副本，
保留原二进制名称以使用 KWin 自带 QPA；未修改主机图形配置。
QPA 行为依据：[KWin 插件入口](https://raw.githubusercontent.com/KDE/kwin/master/src/plugins/qpa/main.cpp)。
本批不代表完整 Plasma/GNOME Shell、物理 GPU、多显示器、锁屏、Orca 或其他 RID 已验收。
键盘窗口管理、平铺/浮动几何、剩余桌面能力和上述嵌套显示问题继续保留在 Window 后续范围中。

本批源码修改前快照在 `window-constraints-baseline/`，回归及当前源码 hash 在
`window-constraints-source-sha256.json`。当前 native payload、Host 和 native 构建缓存已导回任务产物目录。


## 第七批：平铺恢复尺寸与 KWin 测试隔离

### 平铺窗口保留浮动恢复尺寸

Wayland 的平铺窗口在现有公开状态枚举中仍为 Normal。旧实现因此把平铺后的尺寸、
平铺缩放以及 DPI 更新保存成正常恢复尺寸，造成后续最大化还原和退出平铺时尺寸丢失。
现在原生窗口单独保存平铺状态，保存正常几何时跳过平铺窗口；重建角色时清除旧平铺状态。
合成器给出的平铺尺寸继续应用，返回浮动状态后更新下一次恢复尺寸。
显式改变隐藏窗口的尺寸时，撤销旧平铺标记并更新恢复尺寸，包括尺寸与当前缓冲区相同的请求。

协议参考：[xdg-shell 平铺状态和 configure 的逐轴客户端尺寸选择](https://raw.githubusercontent.com/wayland-mirror/wayland-protocols/main/stable/xdg-shell/xdg-shell.xml)。

新增独立窗口用例覆盖平铺、左右切换、交互缩放、最大化还原、逐轴零尺寸、程序改尺寸、
DPI 往返和 Hide/Show。用最终测试程序加载修复前产品库，复现 **11 个失败**；
使用当前产品库时，Weston 原生检查及 125%/150%/175% 全部通过，包含隐藏后显式改尺寸的边界。
旧库和 hash 保留在 `window-tiling-baseline-native/` 与 `window-tiling-baseline-native-sha256.json`，
红绿结果在 `native-window-tiling-red-final.log` 与 `window-tiling-retry-native.log`。

早期新增用例曾复用主测试窗口，残留配置导致 KWin 后续检查受影响；
已改成独立窗口并完成下列最终回归。该中间失败保留在 `kwin-tiling-fixture-failed/`。

| 第七批最终检查 | 结果 | 证据 |
| --- | --- | --- |
| 原生 CTest、ABI、版本基线 | 13/13；7 个产品库；22 个 ELF 保持 GLIBC 2.31 / GLIBCXX 3.4.28 上限 | `ctest-tiling-final.log`、`native-exports-tiling-final.log`、`native-versions-tiling-final.log` |
| X11/Openbox 与 Weston | 原生检查全部通过；7 次普通 Host 完成，包含 125%/150%/175% 和两种标题栏 | `window-tiling-retry-native.log`、`window-tiling-verified-host.log`、`window-tiling-verified-host/` |
| Mutter 50.5 | 当前原生检查通过；Custom/Native Host 各自正常完成 | `mutter-tiling-verified-native.log`、`mutter-tiling-verified-host-*.log`、`mutter-tiling-verified-final.log` |
| KWin 6.7.5 + Breeze | 当前原生检查通过；Custom/Native Host 各自正常完成；合成器在测试结束时仍存活 | `kwin-tiling-verified-final/`、`kwin-tiling-verified-final.log` |
| 实际平铺与还原 | Meta+Left/Right 得到 600×784；双击最大化后还原回 720×560 | `kwin-tiling-ui-host.log`、`kwin-tiling-ui-wire.log`、`tiling-ui/` |
| 实际键盘窗口管理 | Alt+F3 原生菜单移动 40×24；缩放至 760×584；Escape 返回文本焦点，输入 `_qa` 后恢复原文 | `tiling-ui/keyboard-caption-positions.json`、`tiling-ui/keyboard-contact-sheet.png`、`tiling-ui/kwin-keyboard-focus-restored.png` |

已查看本批平铺、最大化、还原、原生菜单、键盘移动/缩放及焦点截图；
可见 Host 正常关闭，合成器在关闭时仍存活。键盘窗口管理记录来自 KWin 的原生菜单，
不能替代其他合成器或 Jalium 回退菜单的 Move/Resize 验收。
最终产品库又完成一次实际平铺、最大化、还原及追加文字后恢复原文，三张当前截图已查看，
见 `tiling-ui/final-current-contact-sheet.png`、`kwin-tiling-final-ui-host.log` 与 `capture-kwin-tiling-final-ui.log`。
本批没有修改托管 Window 或其他控件；上一批 208 项托管专项及 19 个大范围基线失败的状态保持原记录，未声称重新执行。

原生夹具同时修正两处验证方式：注入配置的计数断言在配对确认后立即完成，避免把真实合成器
后续合法配置混入“重复配置”断言；`PRESENT_FAILED` 按现有软件 present smoke 和宿主语义延后重试。
首帧必须成功提交，销毁窗口前也必须在有界等待内完成重试，持续失败仍判为失败。
本次 Weston 四档运行均实际记录了暂时丢帧及后续恢复，见 `window-tiling-retry-native.log`。
早期失败保留在 `window-tiling-present-failed.log` 与 `kwin-tiling-contention-failed/`。
另有一次并行收尾运行出现 X11 的 3 秒映射等待超时；独立运行没有再次出现，原因仍待调查，
记录在 `window-tiling-contention-failed.log`。这次失败没有计入通过结果。

### 并发 KWin 崩溃的对照结果

第六批脚本把带调度能力的 KWin 程序复制到同一个临时路径，以便在容器运行。
第二个测试启动时会覆盖第一实例正在使用的程序副本。在当前 x64 模拟环境中，
共享路径对照再次立即触发可见 KWin 的 SIGSEGV，保留终态 139。

每次运行改用独立的 `runtime/bin/kwin_wayland` 副本后：

- 可见窗口完成 20 轮最大化/还原，另一套 KWin 同时完成原生及两种标题栏测试；
  可见窗口最后正常关闭，合成器仍存活。
- 41 份应用 CPU 帧缓冲与 21 张合成器截图的客户区没有出现此前的大面积黑块；
  21 张截图已逐张查看。串行对照另有 10 轮与 21 份 CPU 帧缓冲。
- 仓库新增 `eng/linux/test-window-kwin.sh`，使用独立 D-Bus、runtime 和程序副本，
  保留 QPA 要求的文件名，并在每项测试结束时检查合成器存活。
  两份仓库脚本并行运行均取得退出码 0。

共享路径复现与独立路径对照分别保存在
`presentation-investigation/shared-executable-repro/`、
`presentation-investigation/isolated-executable-retest/`；
仓库脚本并行终态在 `presentation-investigation/kwin-repo-parallel-{a,b}.log`。
此项归为测试脚本缺陷；KWin 的具体崩溃机制没有取得 backtrace。
第六批单次还原缺帧在后续对照中没有再次复现，其独立根因仍未确定。

Host 新增显式 `--presentation-probe`，在尺寸变化或 F12 后读取下一次自然绘制的帧缓冲，
不主动请求额外重绘。`JALIUM_WINDOW_PRESENTATION_OUTPUT` 指定 BGRA 与尺寸 JSON 的输出目录。
普通 Host 的界面和行为保持原样；诊断读回没有作为生产绘制修复。

### 继续验收的范围

平铺时 SizeToContent 的交互、回退菜单中的键盘移动/缩放、更多桌面能力及单次缺帧调查仍须继续。
一次 X11 映射超时的稳定性调查同样保留。
完整 GNOME/Plasma 会话、物理 GPU、多显示器、锁屏、Orca 和其他 RID 的实际运行仍未验收。
这些项目保持未完成，Window 及全部 LinuxOS 行为的目标继续进行。

本批前源码在 `window-tiling-baseline/`，最终源码与产品文件 hash 在
`window-tiling-final-sha256.json`。证据路径均相对于 `artifacts/linux-window-20261014/`。

## 第八批：平铺自动尺寸与尺寸通知

当前工作区已迁移到 `/Volumes/My PSSD/source/Repos/Jalium.UI`。
本批证据改存 `artifacts/linux-window-20261015-autosize/`；旧的主机
`artifacts/linux-window-20261014/` 目录目前不存在，前七批路径仅保留为历史记录。
容器中的部分旧结果已取回 `prior-evidence/`，不能视为恢复了全部旧产物。

### 已实现的行为

- Wayland 平铺状态在对应的 `xdg_surface.configure` 确认后发布。
  只改变平铺标记、没有改变尺寸或可缩放边缘时也通知托管窗口；重复配置不重复通知。
  查询空指针、隐藏窗口或 X11 时返回 false；旧版合成器没有平铺状态时也返回 false。
- 平铺时暂停 `SizeToContent` 的自动窗口请求，保留 Width/Height/WidthAndHeight 偏好与绑定。
  退出平铺后重新测量内容，包括退出平铺时尺寸和可缩放边缘都相同的情况。
  用户实际缩放仍切换为 Manual，并更新绑定源。
- 修复程序改尺寸和自动尺寸请求漏发 `Window.SizeChanged`：使用上一次原生接受的
  逻辑客户区尺寸比较，事件携带本次接受的尺寸。回声配置不重复发事件，纯 DPI 换算
  不制造逻辑尺寸变化，处理器中再次请求尺寸时仍保留正确的前后尺寸。
  `SizeChangedEventArgs` 仅增加内部构造函数，公开事件与普通元素布局事件的 API 保持一致。
- 原生 Hide/Show 用例增加“重新映射后实际提交帧”断言，避免只核验元数据尺寸。

依据：[xdg-shell 配置与平铺状态](https://raw.githubusercontent.com/wayland-mirror/wayland-protocols/main/stable/xdg-shell/xdg-shell.xml)、
[Window.SizeToContent 的尺寸事件与用户缩放约定](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.sizetocontent?view=windowsdesktop-9.0)。

### 本批最终验证

环境仍为 Apple Silicon 上的 Linux x64 模拟容器及软件渲染。
Host 的 `--autosize-probe` 是显式诊断选项，提供 F2/F3 增减内容、F4 切换模式、F6 检查绑定；
普通 Host 的流程不变。

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 平铺自动尺寸红绿 | 修复前 3 失败 / 2 通过；修复后 5/5 | `linux-window-autosize-pre-resume-red.trx`、`linux-window-autosize-green.trx` |
| 尺寸通知红绿 | 修复前 3 失败 / 5 通过；最终 LinuxWindowBehaviorTests 56/56 | `linux-size-events-red.trx`、`linux-window-events-final.trx` |
| 可比窗口与相邻专项 | 218/218 | `linux-window-size-events-focused.trx` |
| 原生 CTest / ABI / 符号版本 | 15/15；7 个库；24 个 ELF，GLIBC ≤ 2.31 / GLIBCXX ≤ 3.4.28 | `ctest-size-events-final.log`、`exports-size-events-final.log`、`versions-size-events-final.log` |
| X11/Openbox 与 Weston | 原生检查及 7 次普通 Host 完成；包含两种标题栏、125%/150%/175% | `window-size-events-final.log`、`window-size-events-final/` |
| KWin 6.7.5 + Breeze | 原生及 Custom/Native Host 完成，退出后合成器仍存活 | `kwin-size-events-final.log`、`kwin-size-events-final/` |
| Mutter 50.5 | 原生及 Custom/Native Host 完成 | `mutter-size-events-final.log`、`mutter-window-native.log`、`mutter-host-*.log` |
| KWin 实际自动尺寸交互 | 两种标题栏、三种模式；平铺内容改变均保持合成器尺寸，恢复后继续测量 | `autosize-ssd-contact-sheet.png`、`autosize-csd-contact-sheet.png`、`kwin-autosize-*-ui-host.log` |
| 实际键盘缩放 | 两种标题栏均切到 Manual，绑定保留；再增加内容不改变窗口尺寸 | `ssd-keyboard-screenshots/autosize-keyboard-resized.png`、`csd-screenshots/autosize-custom-resized.png`、Host 日志 |

已查看两套各 15 张状态截图的联系表、原生菜单、键盘缩放、文本焦点截图。
实际键盘操作使用 KWin 的原生菜单；编辑器追加 `_qa` 后恢复原文，按钮的 Tab/Return
最大化和还原也完成。两个可见 Host 都正常关闭，关闭时合成器仍存活。
这些结果不能替代 Jalium 回退菜单的键盘移动/缩放或其他桌面环境的实际交互。

### 大范围回归与限制

平铺修复后、尺寸事件修复前的完整执行自然结束于约 23 分钟：
**6014 通过、86 跳过、21 失败**，包含耗时的 `PageSwapCostBenchmarkTests`。
尺寸事件最终版本的功能回归只排除了该性能测量类：
**6078 通过、86 跳过、22 失败**；因此没有声称最终版本重新完成性能测量。
本批测试编译在容器中临时排除原有、不能编译的 `IconSupportTests.cs`，没有修改该仓库文件。

和取回的旧基线相比，最终结果仍包含原有 19 项失败，另有 ImageBrush 解码、WrapPanel
稳定态分配和 ComboBox 弹窗输入三项新增失败；后者在 Linux 直接调用 `user32.dll`。
OLE 窗口根释放检查在旧基线和最终运行失败，在事件修改前运行通过，结果存在波动。
源码同时包含其他任务的 CSS、渲染和控件改动，尚未将新增失败归因到 Window 修改。
失败详情与逐项比较见 `functional-comparison-final.json`；本批不宣称全套回归通过。

可见 Host 的复杂内容树在最大化后保留较宽的测量结果，移除新增内容也没有自动缩窄；
高度可随内容增减。该宽度行为在第九批定位并修复；本批旧截图本身不证明自动收缩通过。
早期 `xdotool key --clearmodifiers F4` 产生了 Alt 按键与合成器关闭请求，应用正常退出；
后续使用直接 XTest 键码完成交互。原始输入序列保留在 `ui-first-close/`，未作为产品快捷键修复。

原生及托管状态通知的源码边界见 `tested-source-sha256.json`、`tested-product-sha256.json`。
工作区仍有并行改动；编译快照之后的源码差异另存 `workspace-drift-after-tests.json`，
这些差异没有冒充已经测试。恢复时的 `resumed-baseline/` 也不是第八批开始前的完整干净基线。

X11 平铺识别、回退菜单的键盘移动/缩放、复杂内容自动收缩、旧的单次映射超时与缺帧调查
继续保留。完整 GNOME/Plasma 会话、物理 GPU、多显示器、锁屏、Orca 和其他 RID 仍未验收。
Window 及全部 LinuxOS 行为的目标继续进行。

## 第九批：文本框内容的自动宽度收缩

第八批真实交互发现的宽度问题来自 `TextBox.MeasureTextContent`：NoWrap 文本框在无限宽
测量时返回上次排列的视口宽度。最大化后的文本框因此把旧的 1200 宽度传回
`SizeToContent`，删除内容也无法收缩。现在这种测量返回文本本身的水平范围；
有限宽度测量、换行高度使用的排列宽度与布局收敛限制保持原有语义。
这是共用文本控件的依赖修复，本批运行证据来自 Linux，没有声明其他宿主已重新验收。

新增三个回归用例覆盖 TextBoxContentHost 的宽排列、文字增长与删除，以及两种
自动宽度模式下含模板文本框、DockPanel、ScrollViewer 的内容树。
用修复前文本框实现运行时 **3/3 失败**，返回旧宽度 900/1000；修复后 **3/3 通过**。
测试显式建立 `PART_ContentHost` 模板，避免测试未加载模板时绕过真实问题。

| 第九批检查 | 结果 | 证据（仍在第八批产物目录） |
| --- | --- | --- |
| 文本宽度红绿 | 3 失败 → 3 通过 | `linux-autosize-text-red-final.trx`、`linux-autosize-text-green.trx` |
| Window、文本输入/滚动/布局收敛 | 147/147 | `linux-autosize-text-controls.trx` |
| 可比窗口、菜单、输入等专项 | 256/256 | `linux-autosize-text-focused.trx` |
| 功能大范围（排除 PageSwapCostBenchmarkTests） | 6083 通过、86 跳过、20 失败 | `linux-functional-autosize-text.trx`、`functional-comparison-text-final.json` |
| X11/Openbox、Weston、125%/150%/175% | 原生检查及 7 次普通 Host 全部完成 | `window-autosize-text-final.log`、`window-autosize-text-final/` |
| KWin 与 Mutter | 各自的原生检查及 Custom/Native Host 完成 | `kwin-autosize-text-final.log`、`kwin-autosize-text-final/`、`mutter-autosize-text-final.log`、`text-final-mutter/` |
| 实际键盘输入 | X11/Wayland 的 Tab、Shift+Tab、追加字符保留中文并提交一次 | `keyboard-autosize-text-final.log`、`keyboard-autosize-text-final/` |
| KWin 实际自动尺寸 | Custom/Native 各三种模式、共 30 张状态截图已检查 | `autosize-text-ssd-contact-sheet.png`、`autosize-text-csd-contact-sheet.png`、`autosize-text-interaction-results.json` |

当前可见结果：自动宽度从 616 扩到 918，移除内容后回到 616；自动高度也随内容增长和收缩。
两种标题栏在平铺时均保持 600 宽的合成器几何，分别为 784/820 客户区高度。
退出平铺后恢复自动测量；所有状态检查中绑定和绑定源保持一致。
两个可见 Host 退出码均为 0，关闭时 KWin 仍存活。已查看两套联系表和最终普通尺寸原图。

功能回归相对第八批最终结果没有新增失败；ImageBrush 解码和 WrapPanel 分配这次通过，
但本批未修改这两个实现，因此只记录结果波动，不把它们归为本次修复成果。
剩余 20 项包括原基线失败和 Linux 中调用 user32.dll 的弹窗输入测试。
原有 IconSupportTests 的容器编译排除和未重跑性能测量的限制继续适用。
原生产品代码没有在本批改动，原生 CTest/ABI/符号版本使用第八批已经完成的结果。

本批源码和产品 hash 为 `tested-text-source-sha256.json` 与 `tested-text-product-sha256.json`，
编译快照之后的工作区变化为 `workspace-drift-after-text-tests.json`。
第八批关键测试源码另保存于 `tested-task-source/`，本批三份修改保存在 `text-autosize-tested-source/`。

后续 Window 工作仍包括 X11 的平铺扩展、回退菜单的键盘移动/缩放及现存稳定性调查。
已查阅 [EWMH 键盘移动/缩放](https://specifications.freedesktop.org/wm/latest/ar01s04.html)
和 compositor 实现：xdg-shell 虽允许由按键发起请求，当前
[KWin](https://raw.githubusercontent.com/KDE/kwin/master/src/input.cpp) 与
[Mutter](https://raw.githubusercontent.com/GNOME/mutter/main/src/wayland/meta-wayland-seat.c)
的移动抓取校验仍依赖指针、触摸或笔抓取。不能仅发送键盘 serial 就宣称操作已被接受。
完整 shell、物理 GPU、多屏/混合 DPI、Orca 和其他 RID 的实际验收继续保留。

## 2026-10-15 第十批：X11 平铺约束、装饰帧生命周期与最小化还原

本批产物为 `artifacts/linux-window-x11-constraints/`。X11 现在读取
`_GTK_EDGE_CONSTRAINTS` 的四组 tiled/resizable 位，将可缩放边同步给托管窗口，
并在接受 ConfigureNotify 几何前刷新平铺状态。平铺布局不再覆盖浮动还原尺寸，
自动尺寸也不会与窗口管理器争夺平铺几何。

查询顺序为客户端、由近到远的装饰祖先窗口。客户端的有效属性优先；属性必须是
单个 CARDINAL/32，拒绝长度、类型或格式不符的数据，忽略未来扩展的高位。
没有有效 GTK 提示时，以 EWMH 单轴最大化保守识别受 WM 管理的布局，
不从单轴状态猜测受限边。两轴最大化继续使用现有 Maximized 状态。
这与 [GTK 的属性约定](https://raw.githubusercontent.com/GNOME/gtk/main/gdk/x11/gdksurface-x11.c)
和 [Mutter 的 X11 装饰实现](https://raw.githubusercontent.com/GNOME/mutter/main/src/x11/window-x11.c) 对照。

实际 Openbox 回归发现：WM 可能在祖先查询和下一次属性读取之间销毁装饰帧，
默认 Xlib 错误处理会终止进程。确定性回归先排入帧属性事件、再重设父级并销毁帧，
修复前因 BadWindow 退出。现在仅在本次查询范围内处理 BadWindow；其他请求错误
仍转发原处理器，退出作用域后恢复处理器和显示锁，再调用应用回调。
测试同时覆盖旧错误转发、处理器恢复、重新挂接和回调同步销毁窗口。

Mutter 3.36.9 的真实 X11 回归还发现，最小化后的客户端可能仍处于 mapped 状态，
单独 XMapWindow 不足以请求还原。恢复一个可见的最小化窗口时，现在同时复用
标准 EWMH 激活请求；隐藏窗口仍不会因此显示。原先两次还原均失败，修复后通过，
随后扩展覆盖普通、最大化、全屏经最小化后进入不同目标状态及原始尺寸恢复。
激活请求的接受仍由 WM 决定，遵循 [EWMH 激活约定](https://specifications.freedesktop.org/wm/latest/ar01s03.html)。

| 第十批检查 | 结果 | 证据 |
| --- | --- | --- |
| GTK 约束红绿 | 34 通过、17 失败 → 初版 51/51 | `x11-edge-red-final.log`、`x11-edge-green.log` |
| 单轴最大化回退红绿 | 58 通过、3 失败 → 扩展后通过 | `x11-edge-partial-red.log`、`x11-edge-final.log` |
| 消失帧及错误处理回归 | 修复前 BadWindow 退出；最终 65/65 | `x11-edge-vanished-red.log`、`x11-edge-checked-final.log` |
| 最小化还原红绿 | 46 通过、2 失败 → 扩展后 60/60 | `minimize-baseline/native-red.log`、`test-mutter-x11-expanded-final.log` |
| X11/Openbox 与 Weston | 原生契约及 7 次普通 Host 完成 | `window-x11-edge-checked-final.log`、`window-x11-edge-checked-final/` |
| Fedora KWin 6.7.5 / Mutter 50.5 | 原生 Wayland 契约及各自 Custom/Native Host 完成 | `kwin-x11-edge-checked-final.log`、`mutter-wayland-x11-edge-checked-final.log`、`fedora-mutter-checked/` |

上表 Fedora 复核针对带查询保护的构建，早于随后仅作用于 X11 的最小化还原和缩放来源修复。
`pre-query-check-source/`、`pre-query-check-product/` 为加入查询保护前的中间快照，
`final-transfer/` 为当时传往 Fedora 的查询保护产品，不能当作第十一批最终产品。
最新源码、产品和截图边界见下一批记录。

## 2026-10-15 第十一批：桌面缩放来源与最小化后的实际画面

本批产物为 `artifacts/linux-window-x11-resize-origin/`。
Mutter 原生标题栏下，自动尺寸窗口原为 626 × 461；Alt+F8 后增长到 636 × 461，
随即被自动尺寸改回 626 × 461，绑定源仍为 WidthAndHeight。
原生事件记录显示 WM 的 NotifyGrab / NotifyUngrab，而旧实现只标记自绘边框发起的缩放。
红色实际交互日志为 `current-checked-native-resize-red-driver.log`，退出码为 1；
当时 Host 的绑定检查通过不能替代缩放行为验收。

现在跟踪 X11 的 WM 输入抓取，按已知程序尺寸请求匹配 ConfigureNotify，
保留连续、合并及尺寸约束后的程序请求来源；最大化、还原、平铺切换和 DPI 请求
继续按布局变化处理。抓取结束、普通焦点转移和隐藏时清除临时状态。
WM 未接管自绘缩放请求时，客户端收到的鼠标释放也会清除显式缩放标记，
覆盖 core 和 XI2 两条鼠标路径。事件回调同步关闭窗口的生命周期检查继续保留。

X11 不携带通用的“用户缩放原因”，因此这里使用输入抓取及已知请求识别，
没有把每个 ConfigureNotify 都当作用户操作。其他 WM 方言仍须实际验证。

| 第十一批检查 | 结果 | 证据 |
| --- | --- | --- |
| 缩放来源红绿 | 初版 34 通过、6 失败 → 40/40 | `x11-resize-origin-red.log`、`x11-resize-origin-green.log` |
| 未接管请求的释放红绿 | 42 通过、1 失败 → 43/43 | `x11-resize-release-red.log`、`x11-resize-release-green.log` |
| 最终原生 CTest | 17/17；其中约束命令 65/65、缩放来源命令 43/43 | `ctest-x11-release-final.log`、`ctest-x11-final-detail.log` |
| 最终 ABI / 符号版本 | 7 个库导出及 ldd -r 通过；24 个 ELF 保持 GLIBC ≤ 2.31 / GLIBCXX ≤ 3.4.28 | `exports-x11-release-final.log`、`versions-x11-release-final.log` |
| 最终 Mutter X11 | 60/60 原生检查及 Custom/Native 普通 Host 完成 | `mutter-x11-release-final.log`、`mutter-x11-final-contracts/` |
| Openbox / Weston 及分数缩放回归 | 原生检查及 7 次普通 Host 完成；早于最后的鼠标释放清理 | `window-x11-resize-final-diagnostic.log`、`window-x11-resize-final/` |
| 实际自动尺寸 / 原生与自绘缩放 | 最终构建两种标题栏各 15 个自动尺寸状态及一次缩放完成 | `mutter-x11-*-accepted-final-visible.log`、`accepted-interaction-results.json` |
| 实际最小化画面 | 最终构建两种标题栏各 Normal、Maximized、FullScreen 三条路径完成 | 同上；`accepted-minimize-restore-contact-sheet.png` |
| Openbox 原生菜单缩放 | 626 → 634，模式转为 Manual、绑定保留；早于最后的鼠标释放清理 | `openbox-native-resize-driver.log`、`openbox-x11-native-resize-final-result.png` |

最终可见结果中，Mutter 键盘缩放为 626 → 636，自绘平铺边缘拖拽为 640 → 702，
两者均保留新尺寸并切换 Manual，原双向绑定和绑定源仍一致。
三种自动尺寸模式在平铺内容增长时保持 WM 几何（Native 640 × 863，Custom 640 × 900），
退出平铺后恢复测量，删除内容后回到各自起始尺寸；自动宽高模式为 626 → 918 → 626。
最终两套可见 Host 均退出 0，退出时 WM 仍存活。

新增仅显式启用的 `--minimize-probe` / F5：在最小化期间修改编辑文本，
还原后检查原生状态、自动尺寸和绑定，再恢复原始文本。
六张还原截图均可见“最小化还原”新增中文，证明内容变化已呈现；
不能仅从 WindowState 属性或进程退出码推导画面已刷新。
已查看最终两套自动尺寸联系表、最小化联系表及对应原始截图。
脚本 `verify-interactions.py` 可重新核对日志状态并生成联系表。

最终五份本批修改源码与工作区一致，冻结副本位于 `tested-task-source/`，
原生平台库、测试程序和 Host 程序位于 `tested-product/`。
`tested-x11-final-source-sha256.json` 记录容器中 3177 份既有清单源码，
`tested-x11-final-product-sha256.json` 记录 125 份产品文件。
`workspace-drift-after-x11-tests.json` 记录该清单相对共享工作区的 80 处差异，
包括并行 CSS、渲染和媒体工作；本批没有把这些未进入构建快照的修改宣称为通过。
第九批 256/256 托管专项及大范围 20 项失败为当时快照，本批没有重新运行整个托管套件。
既有 IconSupportTests 容器编译排除继续适用，仓库源文件未被排除修改。

稳定性仍有记录：本批首次广泛回归入口返回 1 且日志为空，带执行跟踪的重跑通过，
原因未定位；原有单次映射超时及缺帧调查也没有由这次成功重跑而关闭。
Window 后续仍需补齐回退菜单的键盘移动/缩放，并完成完整 shell、物理 GPU、
多显示器混合 DPI、Orca 和其他 RID 的实际验收。LinuxOS 总目标继续进行。

## 2026-10-16 第十二批：窗口菜单、字符输入与 X11 抓取焦点

本批产物为 `artifacts/linux-window-keyboard-menu/`。X11 回退窗口菜单补充 Move / Resize，
通过 [EWMH 的键盘移动/缩放方向 10 / 9](https://specifications.freedesktop.org/wm/latest/ar01s04.html)
交给 WM，启动前释放原生弹窗的指针和键盘抓取。平台接口分别报告可用的键盘操作，
按 WM 公布的协议、允许操作、窗口状态、固定尺寸和可缩放边禁用菜单项。
旧原生库缺少新增 Linux 入口时，托管包装返回不可用。

菜单直接复用既有主题与语义。Move / Resize 启动前立即完成菜单关闭，随后在调度器中
重新检查窗口存活和能力，避免关闭动画继续占有抓取、重复调用以及过期菜单操作。
最大化与全屏菜单聚焦 Restore；全屏现在可以从回退菜单还原，且不会再次启用 Maximize。
状态操作明确使用当前值更新；绑定保持本来已有覆盖，不把原先通过的绑定行为算作新缺陷修复。

实际输入发现两个此前未被托管菜单检查覆盖的问题：

- Alt+Space 已被处理后，原生字符事件仍可能早于菜单焦点切换到达文本框，产生额外空格。
  现在仅消耗已处理 Space 命令对应的一次 U+0020，按键释放或后续按键会清理状态。
  覆盖重复按键、没有对应字符事件和之后正常输入；真实交互逐次检查中文文本未变。
- X11 的 NotifyGrab / NotifyUngrab 并不等同于输入焦点改变。旧实现把弹窗抓取报告为
  Window 失焦，外部菜单刚打开即被轻量关闭逻辑关闭。现在跳过临时抓取的激活通知，
  保留 WM 抓取的缩放来源识别；自有弹窗接管键盘时保留按键状态，由实际释放清除。
  原生测试同时核对 XGetInputFocus、真正焦点转移和按键经弹窗释放，避免只检查事件数量。

`ComboBoxPopupInputTests` 原先强制走 Win32 指针入口，在 Linux 加载 user32.dll 失败。
非 Windows 测试现在经 PopupWindow 的原生平台事件入口注入移动、按下和释放，
为仅安排布局而未显示的测试窗口提供平台生命周期桩，保留悬停、高亮、选择和关闭断言。
这项修改修复测试的跨平台入口，没有把它描述成新的生产 PopupWindow 修复。

| 第十二批检查 | 结果 | 证据（相对本批产物目录） |
| --- | --- | --- |
| 新菜单行为红绿 | 3 失败、2 通过 → 初版 9/9；扩展字符场景后 12/12 | `evidence/linux-window-menu-behavior-red.log`、`evidence/linux-window-menu-green.log`、`evidence/linux-window-menu-space-focus.log` |
| X11 抓取焦点红绿 | 47 通过、6 失败 → 53/53 | `evidence/keyboard-focus-red.log`、`evidence/keyboard-focus-green.log` |
| 弹窗按键释放红绿 | 55 通过、1 失败 → 最终 56/56 | `evidence/keyboard-held-red.log`、`evidence/keyboard-held-green.log` |
| 最终原生 CTest | 18/18 | `evidence/ctest-window-keyboard-menu-final.log` |
| 窗口菜单与 Popup 专项 | 194/194，0 跳过 | `evidence/menu-test-results/linux-window-menu-input-final.trx` |
| 扩大 Window、Focus、输入和菜单回归 | 691 通过、2 跳过、2 失败 | `evidence/menu-test-results/linux-window-menu-regression-final.trx` |
| 最终 Openbox / Weston 与分数缩放 | X11 60/60；Wayland 普通 188/188，125%、150%、175% 分别 187/187、189/189、189/189；7 次普通 Host 完成 | `evidence/window-menu-integration.log`、`evidence/window-menu-integration/` |
| 最终实际键盘输入 | X11 / Wayland 的 Tab、Shift+Tab、中文保留和字符提交完成 | `evidence/window-menu-keyboard-input.log` |
| ABI / 符号版本 | 7 库导出与 ldd -r；24 个 ELF 保持 GLIBC ≤ 2.31 / GLIBCXX ≤ 3.4.28 | `evidence/exports-window-menu-final.log`、`evidence/versions-window-menu-final.log` |

扩大回归的两项失败为 `ToolTipHoverTests.DisabledOwner_LeavingWindow_CancelsPendingToolTip`
和 `WindowRenderSchedulingTests.OleDropTarget_RevokeAfterNativeDestroy_ReleasesManagedWindowRoot`，
第九批全功能 TRX 中均为失败。`acceptance-summary.json` 保存本批计数及集合比较；
这里只能说明该扩大子集没有新增失败名称，不能据此推导整个套件已通过或只剩两项失败。
原有 IconSupportTests 的容器编译排除继续适用，仓库源文件未被排除修改。

Mutter 3.36.9、Openbox 各运行 Custom / Native 两套可见 Host，每套记录 28 个检查点。
覆盖菜单开关、Enter 与 Space 启动移动、Esc 取消移动/缩放、确认缩放转为 Manual、
最大化/全屏/最小化后还原、NoResize、360 × 320 窗口、实际外部弹窗、菜单后输入和鼠标
启动操作。Mutter 鼠标菜单缩放为 360 × 320 → 370 × 330，Openbox 为 368 × 328。
位置、尺寸、状态及自动尺寸绑定持续保留，文本除显式输入再删除的 a 外保持原值。
Openbox 第一次方向键选择缩放边，脚本需继续发送方向键才产生尺寸变化；相应早期
脚本失败保留在 `keyboard-menu-custom-openbox-input-final-*`，不作为产品红绿证据。

Wayland 的 Custom / Native 各记录 10 个可见检查点：不可用项被键盘导航跳过，
Alt+Space 再次按下和 Esc 均恢复文本焦点，固定尺寸菜单仅 Close 可用，最大化后可还原。
本批直接键盘 Move / Resize 仍返回不可用，菜单明确禁用；这是当前实现缺口，
不能从已测试 compositor 的抓取校验推导出整个 Wayland 协议永远禁止键盘操作。
当前截图包括 Weston 无服务端装饰时 Native 样式的自绘回退。

已查看菜单联系表以及普通、全屏、窄窗口、外部弹窗和 Wayland 原始截图。
`verify-menu-evidence.py` 可重新核对四套 X11、两套 Wayland 日志及 TRX，生成
`menu-acceptance-contact-sheet.png` 和 `acceptance-summary.json`。
语义继续使用既有 ContextMenu / MenuItem；本批没有完成 Orca 实际交互验收。

14 份任务相关源码与容器一致，冻结副本位于 `evidence/window-menu-tested-task-source/`。
`tested-menu-final-source-sha256.json` 记录 3178 份源码，
`tested-menu-final-product-sha256.json` 记录 309 份原生、Host 和测试产品；
关键产品副本位于 `evidence/window-menu-tested-product/`。
共享工作区与冻结源码的 84 处差异单列于 `workspace-drift-after-menu-tests.json`，
没有将并行 CSS、媒体和渲染修改宣称为本批通过。

验证期间 Docker 因宿主日志写入空间不足退出，诊断后恢复引擎和本任务 Ubuntu 容器，
随后重新运行最终检查；该中断独立于产品测试失败。本批未刷新 Fedora KWin / Mutter
的旧 Wayland 快照，也未重跑完整托管套件。
完整 shell、物理 GPU、多显示器混合 DPI、Orca、其他 RID、既有偶发超时与缺帧调查，
以及上述 Tooltip / OLE 失败继续保留；LinuxOS 总目标仍在进行。

## 2026-10-16 第十三批：禁用控件悬停、提示取消与窗口回收测试

本批产物为 `artifacts/linux-window-input-lifetime/`。修复前选取 Tooltip 与 OLE 断言的
15 项检查中，10 失败、5 通过。禁用控件的 `IsMouseOver` 按项目既有约定保持 false，
旧输入分发器却用此属性判断是否发送 MouseLeave，导致延迟提示未取消、已打开提示
未关闭，并在同一禁用父控件的子元素间移动时重复发送 MouseEnter。

输入分发器现在单独记录实际进入的元素，保留命中时的祖先链，按物理进入/离开配对
通知；禁用控件仍不会获得 hover 或 pressed 视觉状态。离开、子树移除与回调重入时，
按元素当前所属链完成清理，避免旧通知覆盖重新建立的悬停状态。
TooltipService 的显示入口同时尊重其 IsEnabled 和 ShowOnDisabled 设置，禁用祖先的
有效状态也参与判断。新增检查覆盖重复离开、禁用后重新启用再离开、离开回调中重入
以及子树移除；完整 Tooltip 悬停类现为 18/18。

OLE 断言单独运行两次均通过，扩大回归仍失败。控制性复现先激活 CSS，再创建并撤销
合成 OLE 状态，稳定重现窗口暂未回收；处理 CSS 调度队列后同一断言通过。
因此本批修正测试的前置条件，在检测 OLE 根引用前处理该临时 UI 工作，并补充核对
状态字典移除、Window 引用清空、GCHandle 释放和 COM 内存指针归零。
没有修改 OLE 生产代码，也没有将该测试修正称为 Linux 原生 OLE 修复。

| 第十三批检查 | 结果 | 证据（相对本批产物目录） |
| --- | --- | --- |
| 原始 Tooltip / OLE 选集 | 5 通过、10 失败 | `evidence/linux-window-input-lifetime-baseline.log`、对应 TRX |
| 首次悬停修复及禁用视觉状态 | 24/24 | `evidence/linux-window-hover-first-green.log` |
| CSS 前置条件下的 OLE 复现 | 18 通过、1 失败 → 最终窗口回归通过 | `evidence/linux-window-ole-css-controlled-red.log` |
| Window / Focus / 输入 / 菜单 / Tooltip 扩大回归 | 719 通过、2 跳过、0 失败 | `evidence/lifetime-test-results/linux-window-input-lifetime-final.trx` |
| 全功能回归，排除 PageSwapCostBenchmarkTests | 6109 通过、86 跳过、10 失败，共 6205 项 | `evidence/lifetime-test-results/linux-functional-window-hover-final.trx` |
| 图像解码与 WrapPanel 分配单独复核 | 各 1/1；全套中的失败继续保留 | `evidence/linux-window-hover-image-brush-isolated.log`、`evidence/linux-window-hover-wrap-allocation-isolated.log` |
| 实际窗口悬停 | Mutter / Openbox / Weston 各 Custom、Native 两套，共六套完成 | `evidence/window-hover-*-driver.log`、各自 JSON 与 PNG |

与第九批全功能 TRX 对比，原有清单中 12 项不再失败，包括前一批修正入口的 ComboBox
检查、本批 Tooltip 行为及 OLE 测试。图像解码和 WrapPanel 分配两项相对第九批重新
出现的失败，在第八批也有失败记录；本次单独通过不能关闭全套中的失败。
其余 8 项为主题尺寸/资源、模板边框和文本控件 API 形状检查。
`functional-comparison.json` 保存具体名称与消息，不把全功能结果写成全绿。
IconSupportTests 的原有容器编译排除继续适用，临时 targets 已保存于 `evidence/`，
仓库源文件未被排除修改；86 项跳过也不算作通过。

新增仅显式启用的 `--hover-probe`，沿用既有中文 Window Host、主题和文本框。
F7 输出实际悬停次数、提示状态、焦点及文本，F8 切换 360 × 320 窗口。
六套流程全部使用真实输入：短暂停留后离开不弹出提示；持续悬停可显示显式允许的
禁用说明；跨同一父控件的子元素不重启；移出窗口关闭；停用 TooltipService 不显示；
普通启用控件的提示可显示并关闭；窄窗口经实际滚轮滚动后继续显示和关闭。
全过程禁用控件 IsMouseOver 为 false，编辑器保留焦点与原始中文文本。

六个 Host 均正常退出，退出时各自 WM / compositor 仍存活。
Wayland 的 Native 样式为 Weston 未提供服务端装饰时的自绘回退。
已查看普通、窄窗口、显示及关闭后的原始截图和三套联系表；截图仅支持视觉范围，
状态、焦点和文本结论另外由实际输入日志支持。`verify-evidence.py` 可重新核对结果。
原生产品哈希与第十二批一致，本批未修改原生代码、未重复计入其 18/18 CTest。

6 份本批修改源码与容器一致。`tested-source-sha256.json` 记录 3179 份源码，
`tested-product-sha256.json` 记录 309 份原生、Host 和测试产品；
`window-hover-tested-source.tar.gz` 与 `window-hover-tested-products.tar.gz` 按清单冻结，
产品归档包含实际实现所在的 Jalium.UI.Managed.dll，以及 Host / 测试的依赖与原生产品。
Host 与测试加载的该托管实现 DLL 哈希一致。
共享工作区另有 84 份源码差异，记录于 `workspace-drift-after-tests.json`，未计入本批通过。

本批恢复了中断后处于停止状态的 Docker 引擎及本任务 Ubuntu 容器，再继续实际交互，
没有以容器启动代替测试结果。完整 shell、物理 GPU、多显示器混合 DPI、Orca、
其他 RID、Wayland 直接键盘移动/缩放和既有超时/缺帧调查仍待处理。
LinuxOS 总目标继续进行。

## 2026-10-16 第十四批：系统菜单输入归属与窗口生命周期

原生 Wayland 菜单此前直接读取进程全局的最后输入序号，可能取到其他窗口的输入。
现在按回调保存窗口、该次映射的身份和输入序号：嵌套回调结束后恢复外层输入；
同一 UI 线程排队的菜单可以继续使用本窗口最近一次用户动作，其他窗口和后台线程
不能借用该动作。隐藏、重新映射或禁用会使旧身份失效，关闭后也不能发送菜单请求。
键盘、鼠标、触摸与平板回调分别传入实际事件序号；触摸/平板的物理设备路径尚未验收。
原生和托管入口补上可见、启用、关闭及拆除状态检查，隐藏窗口不再响应 Alt+Space。

新增 `eng/linux/test-window-menu-input.sh`，并接入 `test-window-behavior.sh`。
它使用 Xvfb 内的嵌套 Weston X11 backend，提供 headless backend 不具备的实际 seat。
原生契约通过测试钩子注入触摸回调，验证多窗口、后台线程、回调重入、排队、
禁用/启用、隐藏/再显示和回调内销毁；这些断言证明请求归属，不证明物理触摸或菜单外观。

本批证据目录：`artifacts/linux-window-menu-input/`。

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 原生输入归属红绿 | 修复前 200 通过 / 8 失败；最终含菜单输入的 Wayland 契约 219 通过 / 0 失败 | `evidence/window-menu-input-red.log`、`evidence/window-menu-input-integration/menu-input/native.log` |
| 托管生命周期红绿 | 修复前 1 通过 / 4 失败；当前窗口扩大回归 724 通过 / 2 跳过 / 0 失败 | `evidence/menu-input-test-results/linux-window-menu-lifetime-red.trx`、`linux-window-menu-input-final.trx` |
| 原生集成与自动 Host | X11 60；Wayland 普通/分数缩放 188/187/189/189；另有含菜单输入契约 219；7 次 Host 均 0 失败 | `evidence/window-menu-input-integration.log` |
| 原生 CTest、导出与兼容性 | 18/18；7 个共享库导出与重定位通过；24 ELF 满足 GLIBC 2.31 / GLIBCXX 3.4.28 | `evidence/ctest-window-menu-input-refreshed.log`、`exports-window-menu-input-final.log`、`versions-window-menu-input-final.log` |
| 当前全功能回归 | 6115 通过、86 跳过、9 失败，共 6210；没有新增失败名称 | `evidence/menu-input-test-results/linux-functional-window-menu-input-final.trx`、`functional-comparison.json` |
| 实际 X11 菜单回归 | Mutter / Openbox × Custom / Native 共 4 套，每套 28 个状态检查点 | `evidence/keyboard-menu-*-menu-input-*-interaction-results.json` |
| 实际 Wayland 菜单与输入 | Custom / Native 各 11 个菜单状态检查点，另各 4 个原生请求诊断 | `evidence/window-menu-input-*-interaction-results.json`、`*-native-requests.json`、`*-wire.log` |

四套 X11 操作重新检查普通窗口、最大化、全屏、固定大小、360 × 320 窄窗口和外部弹窗。
移动/缩放的 Enter 确认、Esc 取消、Space 激活与鼠标菜单选择均成功；编辑内容和位置、
尺寸、状态绑定保留。操作结束后能输入和删除一个字符，窗口及 WM 均正常结束或继续存活。

Wayland 的 F2 同步请求、F3 排队请求和实际右键抬起均发出菜单请求，F4 后台线程请求
被拒绝。wire 逐项核对了目标 xdg_toplevel 与输入 focus surface，以及键盘 F2/F3 和
右键抬起的真实 serial；每套恰好 3 个请求，没有后台线程请求。菜单开关、禁用项跳过、
最大化/还原均保持中文原文、绑定与编辑焦点。Native 样式仍包含 Weston 缺少服务端装饰
时的自绘回退，不能据此认定 GNOME/KDE Wayland 原生装饰已验收。

已查看原始菜单、窄窗口外部弹窗、最终焦点截图及三套联系表。
依据最新 [Web Interface Guidelines](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md)
复核适用于桌面菜单的键盘路径、可见焦点、禁用状态和布局范围；HTML 专属条款不套用于
原生控件。屏幕阅读器和物理输入设备未测。本批没有变更既有中文内容、主题或控件布局。

全功能仍有 8 项既有主题尺寸/资源、模板边框及文本控件 API 形状失败，另有
ImageBrush 解码失败。WrapPanel 分配检查本次通过，但本批未修复该项，先前随全套状态
出现的失败仍保留调查。IconSupportTests 的容器临时编译排除继续记录于
`evidence/exclude-baseline-icon-tests.targets`，仓库测试源文件未因此修改。

首轮全功能误设 `JALIUM_RENDER_BACKEND=software`，12 项失败的诊断 TRX 单独保留为
`linux-functional-window-menu-input-forced-software-diagnostic.trx`；上表采用清除此覆盖后的
完整重跑结果。早先缺 seat、缺 DISPLAY 的诊断不计作产品失败；取消的重叠编译不计作
成功。最终托管串行 Rebuild 为 0 错误 / 40 警告，Host Rebuild 为 0 错误 / 0 警告。

`tested-source-sha256.json` 冻结 3181 份容器源码，`tested-product-sha256.json` 冻结
309 份原生、Host 与测试产品。两个 `window-menu-input-tested-*.tar.gz` 归档逐文件核对
全部哈希，包含实际实现 Jalium.UI.Managed.dll；Host 与测试加载的实现 DLL 一致。
冻结时 10 份本批关键文件与工作区一致。最终核对时 9 份仍逐文件一致；共享 Window.cs
新增一处物理指针修饰键映射修改，本轮菜单段保持一致，但该完整文件未重新构建或测试。
对应差异及源码保存在 `critical-drift-review.json`、`reviewed-concurrent-drift.patch` 和
`reviewed-workspace-source/`。最终核对快照共有 93 份工作区源码差异，未计入本批通过；
本批结果始终对应已冻结的源码和产品，不能替代整个最新工作区的集成验收。
`verify-evidence.py` 可重新核对原生/托管结果、实际操作、wire 序号与完整归档。

菜单请求被写出不等于合成器实际显示了菜单。[xdg-shell 的菜单请求说明](https://raw.githubusercontent.com/wayland-mirror/wayland-protocols/main/stable/xdg-shell/xdg-shell.xml)
允许合成器忽略请求；[Mutter 的请求处理](https://raw.githubusercontent.com/GNOME/mutter/main/src/wayland/meta-wayland-xdg-shell.c)
与 [seat 抓取校验](https://raw.githubusercontent.com/GNOME/mutter/main/src/wayland/meta-wayland-seat.c)
也说明不能仅凭一个键盘 serial 推断菜单或交互式移动已获接受。
Wayland 直接键盘移动/缩放仍为未实现项；完整 shell、物理 GPU、多显示器混合 DPI、
Orca、其他 RID 与既有超时/缺帧调查仍未关闭，LinuxOS 总目标继续进行。

## 2026-10-16 第十五批：Wayland 输入设备移除、取消与重连

Wayland seat 撤销键盘/鼠标能力时，旧实现只销毁客户端代理：键盘仍能触发重复、
Ctrl 等按压状态残留；窗口没有收到失焦/离开；旧按压仍能请求移动或系统菜单。
本批先复现 13 项原生失败，随后发现并复现另外两项输入法恢复失败，以及一项
按键回调内移除/重连后继续提交旧字符的失败。托管侧受控红色构建缺少取消事件接线，
两种标题栏的测试均残留按钮捕获。

现在撤销能力时按协议版本发送 release 或销毁旧代理，先从全局状态分离旧设备，再通知
窗口，避免回调重建的新代理被后续旧清理覆盖。键盘停止 repeat、清除按压和修饰键、
取消未提交输入及暂存文本操作，再发布失焦；重连后接收新的 keymap、焦点和按键。
输入代次同时使旧菜单授权和旧按键回调失效，防止回调内重连恢复同一窗口后继续提交
旧字符或重新启动旧 repeat。鼠标清除按压、光标序号和多击状态，并发送新的
`MouseCancel = 36`，复用托管捕获取消和离开路径清理按钮、hover、滚轮路由和鼠标状态。
取消通知直接清理状态，重连后的旧释放不会触发 Click。
既有事件编号和 72 字节事件结构保持不变。

依据：[Wayland seat capabilities 与 release 约定](https://raw.githubusercontent.com/wayland-mirror/wayland/main/protocol/wayland.xml)。
`eng/linux/test-window-menu-input.sh` 已加入 `--wayland-input-devices`，普通 Window 集成入口
也会运行该项。能力变化由测试钩子调用生产协议监听器，重建的是实际 Wayland 代理；
这些测试不能替代物理 USB 拔插或整个 seat global 被替换的验收。

本批证据目录：`artifacts/linux-window-input-devices/`。

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 原生移除/重连红绿 | 最初 215 通过 / 13 失败；最后 Weston 233 通过 / 0 失败 | `evidence/window-input-devices-red.log`、`window-input-devices-nested-green.log` |
| 输入法与按键回调重入红绿 | 输入法 228 通过 / 2 失败；回调重入 232 通过 / 1 失败，最终均通过 | `evidence/window-input-devices-ime-red.log`、`window-input-devices-nested-red.log` |
| 托管取消与扩大窗口回归 | 取消接线前 2 失败；最终 726 通过、2 跳过、0 失败，共 728 | `evidence/input-device-test-results/linux-window-mouse-cancel-red.trx`、`linux-window-input-devices-final.trx` |
| 当前原生集成与普通 Host | X11 60；Wayland 普通/分数缩放 188/187/189/189；菜单输入 219；设备输入 233；7 次 Host 均 0 失败 | `evidence/window-input-devices-probe-final-integration.log` |
| Fedora KWin 6.7.5 / Mutter 50.6 | 最终分别 235 / 237 项检查通过，结束时 compositor 仍存活 | `evidence/window-input-devices-fedora-kwin-nested-final*.log`、`window-input-devices-fedora-mutter-activated-devkit*.log` |
| 原生 CTest 与兼容性 | 18/18；7 个库导出与重定位通过；24 ELF 满足 GLIBC 2.31 / GLIBCXX 3.4.28 | `evidence/ctest-window-input-devices-final.log`、`exports-window-input-devices-final.log`、`versions-window-input-devices-final.log` |
| 实际 GUI 输入与取消 | Custom / Native 各 12 个快照；各 3 次键盘、1 次鼠标能力移除/重连 | `evidence/window-input-devices-*-interaction-results.json`、`*-host.log`、`*-wire.log` |
| 全功能回归 | 6116 通过、86 跳过、10 失败，共 6212 | `evidence/input-device-test-results/linux-functional-window-input-devices-final.trx`、`functional-comparison.json` |

原生检查计数包含实际 present 回调，受协议能力与帧次数影响；表中记录本次输出，
不把不同桌面的计数差解释为静态用例数差。

仅显式启用的 `--input-device-probe` 沿用既有界面与中文内容。实际键鼠输入先经 Xvfb /
Weston 到达应用，再用协议回调注入模拟能力丢失。记录证明：移除前 A 或 Ctrl 已处于
按压状态；移除期间不再重复、按压清除且文本稳定；重连后新按键和正常重复恢复。
未提交的“拼音未提交”可见于实际截图，键盘移除后取消，原文保留且之后可继续输入。
鼠标移除前按钮确实 pressed、hover 且持有 capture；移除后均清除，实际释放产生 0 次
Click，随后新点击只产生 1 次 Click，并把窗口改为 640 × 440；最后 Shift+Tab 回到编辑器。
wire 各有 3 次 keyboard release / 重建和 1 次 pointer release / 重建，无协议错误。

已查看未提交输入、鼠标取消、最终焦点的原始截图和完整联系表，核对 720 × 560 与
640 × 440 窗口的焦点和滚动视口。两套 Host 正常退出，退出时 Weston 仍存活。
Native 样式包含 Weston 不提供服务端装饰时的自绘回退。
依照最新 [Web Interface Guidelines](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md)
复核适用的键盘、焦点、交互取消和布局项；本批没有改动产品配色、字体或控件布局，
未进行物理输入、Orca 或数值化对比度验收。

Fedora 的纯虚拟/headless 后端没有所需键盘/鼠标能力，最初被测试前置条件拒绝，
没有将这些计作产品行为失败或通过。KWin 改用 X11 嵌套后验证完成；Mutter 安装本任务
容器内的 devkit 包，包管理器将 50.5 更新到 50.6，再用私有 D-Bus、Xvfb、PipeWire / 
WirePlumber 和 devkit 的实际输入完成验证。Weston 覆盖 text-input-v1，KWin / Mutter
覆盖 text-input-v3。传入 Fedora 的 24 份原生文件与冻结主环境产品逐项哈希一致。

全功能 10 项失败均有历史记录：主题尺寸/资源、模板边框和文本 API 形状 8 项，
ImageBrush 解码及 WrapPanel 分配各 1 项。WrapPanel 相对第十四批重新失败，
第八/十三批及第十四批强制软件渲染诊断也有失败记录；没有因一次通过而关闭该问题。
IconSupportTests 的临时容器编译排除继续单列，仓库测试源文件未因此修改。

最终托管框架与测试使用串行 Rebuild（0 错误 / 40 警告），最终 Host 探针 Rebuild 为
0 错误 / 0 警告，避免增量时间戳将受控红色版本误当作最新。最初的缺依赖编译、
移动请求错误码断言及控制脚本在键盘已移除时等待按键诊断等中间记录均保留，
没有计为最终通过。两个并行修饰键辅助依赖随本次构建同步，既有并行改动保留。

冻结归档含 3183 份源码与 309 份产品，完整包含实际实现 Jalium.UI.Managed.dll，
Host 与测试加载的实现 DLL 相同。冻结时 10 份本批修改文件及 2 份同步依赖与工作区
一致。最终核对时，Window.cs 和 WindowInputDispatcher.cs 又新增并行的命令目标
生命周期及鼠标到 pointer 释放抑制修改；本批取消事件和处理方法仍一致，这两个完整
文件尚未合入本批构建，记录于 `critical-drift-review.json`、差异与观察源码副本。
其余 10 份关键/依赖文件逐文件一致；最终观察快照的工作区差异单列，不能用本批结果
替代整个最新工作区的集成回归。两个 `window-input-devices-tested-*.tar.gz`
逐文件验证全部哈希；`verify-evidence.py` 可重查红绿、实际 GUI、wire 与归档。

后续仍需处理 seat global 移除/替换和剩余输入回调生命周期审计，继续调查既有
映射超时/缺帧，补齐直接 Wayland 键盘移动/缩放及其他 RID；物理 GPU、混合 DPI、
完整 shell、USB 拔插和 Orca 仍未验收。LinuxOS 总目标继续进行。

## 第十六批：Wayland seat 整体移除与替换（2026-10-16）

补上上一批确认的 `HandleRegistryRemove` 缺口：记录已公布的 seat，当前 seat 被撤销时
取消按键重复、修饰键、预编辑、鼠标按压/捕获和触点，释放旧数据设备、输入法、tablet
及 seat 对象；清理期间暂缓绑定新 seat，回调结束后选择仍存在的 seat。既能接受稍后
重新公布的 seat，也能接管已经公布的第二 seat；无关 seat 的移除不影响当前输入。
旧 seat 的菜单 serial 不会因重新绑定而复活，键盘映射与重复参数也不会跨 seat 残留。
单输入流 API 仍选择一个活动 seat，本批没有增加多个 seat 的独立并发输入 API。

新增 libweston 8 测试模块，使用真正的 `wl_global` / `global_remove` 和 compositor 输入
事件，不通过生产测试钩子伪造 seat registry 移除。模块允许重复移除、重新公布、双 seat
切换及回调内公布新 seat；键盘重复与两个同时存在的触点均经过协议。中文预编辑仍使用
既有输入法测试钩子启动，随后由真实 seat 移除取消；这不代表真实输入法候选窗口验收。
模块依赖 libweston 8 的导出 backend ABI，CMake 明确限制版本，其他版本会报告此夹具不可用。
正常 `test-window-behavior.sh` 已接入该检查，不会把缺少夹具记作通过。

| 检查 | 本批结果 | 证据（`artifacts/linux-window-seat-globals/`） |
| --- | --- | --- |
| 整体 seat 撤销红绿 | 前置条件修正后的旧实现 213 通过 / 14 失败；最终 237 通过 / 0 失败 | `evidence/window-seat-globals-red-final/`、`window-seat-globals-final/` |
| 旧协议对象释放 / 新对象创建 | 7 次 seat 绑定；6 次 seat、keyboard、pointer、touch、data-device release；7 次 text-input 创建 | `seat-protocol-resource-counts.json`、最终 `wire.log` |
| 托管强制重建与扩大回归 | 强制 Rebuild 0 错误；737 通过、2 跳过、0 失败，共 739 | `evidence/window-seat-globals-managed-rebuild.log`、`test-results/linux-window-seat-globals-final.trx` |
| 普通窗口完整入口 | 7 次 Host 均 0 失败；X11、普通/分数缩放 Wayland、菜单、设备与 seat 契约均通过 | `evidence/window-seat-globals-integration-final.log` |
| 最后加固的设备前置条件 | Weston 234、Fedora KWin 6.7.5 为 235、Mutter 50.6 为 238，均 0 失败 | `evidence/window-seat-globals-menu-final.log`、`window-seat-globals-fedora-*.log` |
| CTest、导出及兼容性 | 18/18；7 库导出/重定位通过；25 ELF 满足 GLIBC 2.31 / GLIBCXX 3.4.28 | `evidence/window-seat-globals-{ctest,exports,symbols}-final.log` |
| 可见窗口整体 seat 恢复 | 两种标题栏各 18 个状态快照，各 3 次真实 registry 移除/重建；Host 正常退出且 compositor 存活 | `evidence/window-seat-globals-ui-*-results.json`、`*-capture.log`、原始 PNG |
| 全功能回归 | 6116 通过、86 跳过、10 失败，共 6212；失败名称与上一批相同 | `evidence/test-results/linux-functional-window-seat-globals-final.trx`、`functional-comparison.json` |

原生计数包含实际 present 回调，不能把不同运行的计数差当成静态用例数差。
普通入口完成后，设备测试增加了启动焦点稳定前置条件；随后重跑对应的菜单/设备/seat
契约和 Fedora 验证。该项只改测试，生产原生库与托管实现未变。Mutter 的前次运行有两项
按键失败，wire 记录了测试输入后的启动焦点切换；另有 devkit viewer 尚未出现的截图。
测试现先等待 viewer 可见，再等待窗口焦点稳定，最后运行通过。前次日志保留于
`evidence/window-seat-globals-fedora-mutter-diagnostic/`，不以最后通过覆盖前次观察。

可见交互先证明 A 已按住且发生重复，移除整个 seat 后按键、重复和文本停止变化；新 seat
恢复后仍可继续输入。可见的“拼音未提交”在移除时取消，原文保留。按钮移除前确实处于
pressed/hover/captured，移除后清除；旧释放产生 0 次 Click，新点击恰好 1 次并改为
640 × 440，Shift+Tab 回到编辑器且文本不变。两套 wire 各有 3 次 seat/device release
和 4 次绑定/设备创建，无协议错误。已查看原始预编辑、按压取消、最终焦点截图及联系表，
覆盖 720 × 560 与 640 × 440；Native 样式在 Weston 没有服务端装饰时使用既有回退。
最初 GUI 夹具未同步指针全局坐标导致错误 leave，诊断保留在 `window-seat-globals-ui-diagnostic/`；
修正夹具坐标和 motion 后，用同一生产库重跑两种样式通过。
参照当前 [Web Interface Guidelines](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md)
复核适用的焦点、键盘、取消及布局项；本批沿用中文、主题和控件布局，没有 HTML/ARIA
或数值化对比度、Orca 的通过声明。registry 处理依据
[Wayland core protocol](https://raw.githubusercontent.com/wayland-mirror/wayland/main/protocol/wayland.xml)。

已把上一批未纳入构建的共享 `Window.cs`、`WindowInputDispatcher.cs` 改动，以及所需的
`CommandManager` / `RoutedCommand` 依赖合入隔离测试副本，强制重建并完成回归。
冻结归档包含 3185 个源码输入、310 个产品与 19 个重点/依赖文件；实际
`Jalium.UI.Managed.dll` 在 Host / Tests 中哈希一致，两个归档逐文件验证。
19 个重点文件与最终工作区一致；其他并行源码差异单列在 `workspace-source-comparison.json`，
全功能数字针对冻结副本，不代表尚未同步的整个工作区。`batch16-task-diff.patch` 显示本批
相对上批冻结源码的任务差异；`verify-evidence.py` 可复查产品、源码、TRX、wire 和 GUI 证据。

仍需复现并审计 v1/v3 输入法回调及逐窗口触点取消中的重入/关闭，检查 seat 清理时的排队
输入与其他 manager 变化。切换后的实际跨进程剪贴板/拖放和 tablet 尚未验收；对应对象
释放已有实现，不能据此宣称这些真实流程通过。直接 Wayland 键盘移动/缩放、既有映射
超时/缺帧、其他 RID、物理 GPU/USB、混合 DPI、完整桌面 shell 和 Orca 继续保留。

## 第十七批：输入法与触点回调生命周期（2026-10-16）

本批先复现上一批保留的输入回调风险，再修复。旧实现中，预编辑开始回调切换焦点、
隐藏或关闭窗口后，仍会继续投递旧预编辑；v1 会投递到新目标，v3 会访问已释放窗口。
旧组合状态还会阻止新焦点的普通字符输入。逐窗口触点取消在回调后继续使用活动集合
迭代器，重入销毁导致崩溃，回调中新建的同 ID 触点也可能被旧循环移除。
AddressSanitizer 分别确认了 `CancelWaylandTouchesForWindow` 和 v3
`JaliumPlatformWindow::DispatchEvent` 路径的 heap-use-after-free。

原生实现现在先分离待处理的 v3 事务/触点集合，再调用应用回调。输入法事务绑定窗口、
输入身份和会话修订号；焦点、隐藏、关闭或嵌套事务改变上下文后，旧事务停止继续投递。
组合状态在 start/end 回调前更新，避免外层回调清除新组合；leave 不在回调后继续改写
组合状态。窗口销毁入口增加重入保护，隐藏也会取消该窗口的触点并检查回调后的存活状态。
既有事件 ABI 保持 72 字节，新增注入接口仅存在于显式原生测试构建。

托管层也复现了提交串目标：组合结束回调把焦点移到另一编辑器后，旧文本仍被提交给它。
新增 Linux IME 生命周期检查，提交前后核对焦点/焦点意图修订、窗口与平台实例、渲染
生命周期、路由、可见/启用状态和当前输入目标；嵌套的新组合也不会接收旧提交。
切换编辑器时先撤销旧原生上下文，再公布新上下文，覆盖相同周边文本及回调重入的情况。

| 检查 | 本批结果 | 证据（`artifacts/linux-window-callback-lifetime/`） |
| --- | --- | --- |
| 首轮原生复现 | 6 个场景均失败；触点重入销毁退出 139 | `evidence/window-callback-lifetime-red/` |
| v1 修复与嵌套检查 | Weston 下 9 场景全部退出 0；ASAN 同样通过 | `evidence/window-callback-lifetime-native-final/`、`window-callback-lifetime-asan-green/` |
| v3 修复 | Mutter 50.6 下 12 场景全部退出 0，包括 delete/commit/preedit 同批处理、嵌套事务和 pending 后换焦点 | `evidence/window-callback-lifetime-mutter-*-wire.log`、`*-results.log` |
| 两处内存错误的红绿 | 旧版分别报告触点集合、v3 窗口 use-after-free；新版 v1 9 / v3 12 场景 ASAN 无报错 | `evidence/window-callback-lifetime-asan-red/`、`window-callback-lifetime-mutter-asan-red-*`、`*-asan-green-*` |
| 托管提交红绿 | 最初 14 项中 6 失败 / 8 通过；加入上下文切换检查后最终专项 18/18 | `evidence/test-results/managed-commit-lifetime-{red,green}.trx` |
| Window 命名范围 | 527 通过、2 跳过、0 失败，共 529 | `evidence/test-results/linux-window-only-callback-lifetime-final.trx` |
| 扩大相关范围 | 764 通过、2 跳过、1 失败，共 767 | `evidence/test-results/linux-window-callback-lifetime-final.trx` |
| 普通窗口入口 | 7 次普通 Host 均 0 失败；原生 X11、普通/分数缩放 Wayland、菜单、设备、seat 及 9 个生命周期场景通过 | `evidence/window-callback-lifetime-integration.log` |
| CTest、导出与兼容性 | 18/18；7 库导出/重定位通过；26 ELF 满足 GLIBC 2.31 / GLIBCXX 3.4.28 | `evidence/window-callback-lifetime-{ctest,exports,symbols}.log` |
| 无 Wayland SDK 路径 | 隔离 pkg-config 后原生平台及新测试程序编译通过；X11 平台 CTest 1/1 | `evidence/window-callback-lifetime-x11-only-*.log`、`pkg-config-x11-only.sh` |
| 实际可见输入 | Custom / Native 各 11 个状态快照，均正常关闭，compositor 仍存活 | `evidence/window-callback-lifetime-ui-*-results.json`、`*-capture.log`、原始 PNG |
| 全功能当前快照 | 6800 通过、86 跳过、25 失败，共 6911，正常后端选择 | `evidence/test-results/linux-functional-window-callback-lifetime-final.trx`、`functional-comparison.json` |

原生生命周期程序创建实际 Wayland 窗口/协议对象，通过测试钩子进入生产输入法及触点
监听器，验证回调和资源生命周期；该程序不承担窗口画面验收。可见 Host 另行验证真正的
compositor 键盘事件和实际 TextBox 行为；预编辑/提交由测试钩子触发，未验证真实输入法
候选窗口或物理设备。ASAN 设置为 `detect_leaks=0`，本批不声明通过泄漏检测。
Fedora ASAN 最初因缺少配套 core 库退出 127，补齐运行库后才形成上述红绿；最初日志
保留于 `evidence/window-callback-lifetime-mutter-asan-loader-diagnostic/`。

可见交互证明：开始回调切换编辑器后，旧预编辑未显示在新目标，普通键盘输入仍可用；
结束回调换焦点后，两编辑器均未收到旧提交，随后新输入进入新目标。正常中文提交恰好
一次，Ctrl+Z 一次撤销。预编辑开始时隐藏窗口后，定时恢复保留原文并可继续输入。
380 × 360 下新编辑器焦点完整可见，Tab / Shift+Tab 往返且无 Tab 字符写入。
已查看原始预编辑、窄窗口截图和 12 格联系表；沿用暗色主题、中文和现有控件，第二编辑器
及快捷键仅出现在显式 `--ime-lifetime-probe` 模式。参照当前
[Web Interface Guidelines](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md)
复核适用的键盘、焦点、状态反馈及滚动项，无 HTML/ARIA、数值对比度或 Orca 通过声明。

相关范围的 1 项失败为新增 `CssImageAnnotationTests` 在读取 StackPanel automation peer
时空引用。当前全功能快照比上一批增加了测试，仍有原 10 个失败名称，另有 15 个新失败
名称，涉及 CSS 图片/无障碍、线程优先级及重叠滚动条；这是不同源码快照的观察，不能
直接归因于本批修复。完整失败文本保留，未将这些失败排除以得到全绿。
不带排除的测试构建仍因 `IconSupportTests` 的 sealed / DrawIcon 接口不匹配失败；其源码
保留，构建采用单文件排除，实际 targets 和哈希一并保存。这一编译门槛仍需解决。

为统一共享输入依赖，本批同步了当前完整托管/测试源码，而非沿用上一批未同步的部分。
最终冻结 3270 个源文件/构建输入、311 个产品、31 个重点/依赖文件；Host 和 Tests 的
实际 `Jalium.UI.Managed.dll` 哈希一致，源码与产品归档逐文件核验。并行 CMake 修改位于
Apple 分支，已同步并重新配置，311 个产品字节未变。工作区其余并行源文件差异单列于
`workspace-source-comparison.json`；本批结果对应冻结副本，不代表整个工作区均已验收。
源码差异包含同步的并行修改，文件名已明确为 `batch17-baseline-diff-including-integrated-changes.patch`。
Docker 测试 VM 中途停止导致一次观察中断，恢复后重新运行形成最终结果，未将中断当作通过。

仍需检查取消回调中立即 hide/show、禁用后重新启用与新触点的归属，补齐 tablet/X11
对应重入路径、排队输入及 manager 变化、托管输入预览/上下文查询回调，以及跨进程剪贴板/
拖放的切换生命周期。直接 Wayland 键盘移动/缩放、既有映射/缺帧问题、其他 RID、物理
GPU/USB、混合 DPI、完整 shell 和 Orca 仍未完成；LinuxOS 总目标继续进行。
