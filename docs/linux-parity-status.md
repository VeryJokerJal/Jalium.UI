# Linux 桌面支持：差距矩阵与补齐计划

> 基于 2026-07-13 对整个仓库的系统化审计（8 个子系统并行审读 + 容器内真实构建/运行验证）。
> 验证环境：Ubuntu 20.04 glibc 基线容器（CI 同款）与 Alpine 3.24 musl 容器；weston headless（Wayland）与 Xvfb（X11）。

## 总体结论

1. **地基是真实的，不是空壳。** native linux-x64/musl-x64 全量构建通过（7 个 .so），
   一个真实的 code-only 应用在 X11 与 Wayland 双路径端到端运行成功：Vulkan(llvmpipe) 渲染、
   自绘 CSD 标题栏、fontconfig 文本（含 CJK fallback）、DispatcherTimer、xdotool 合成点击触发
   `Button.Click`、干净退出。X11 per-monitor DPI 缩放（Xft.dpi）真实生效。
2. **但存在六条"一碰即崩"的 Linux 崩溃链**，共同根因是两类反模式：无守卫地复用
   `Win32Methods`（user32/kernel32/imm32 P/Invoke），以及把 `Handle != 0` 当作"运行在
   Windows"的判据（Linux 上 Handle 是平台句柄，恒非零）。
3. **若干"声明支持、实际未完成/坏掉"的关键点**：带 Filter 的文件对话框因 GVariant 括号写反
   全废；通知 action 点击永远无效；打印只有探测无提交；托盘 NotifyIcon 纯空壳；
   SystemParameters 全部硬编码假数据；Wayland HiDPI 恒 1.0；主测试套锁死 net10.0-windows。
4. CI（linux.yml）自创建以来从未绿过，三个根因（NU1301 / gst-plugins-good / main-vs-master）
   已修复并在本地容器实证。

## 一、已确认崩溃链（P0/P1，Wave A）

| # | 症状（Linux 上） | 根因 | 证据 |
|---|---|---|---|
| A1 | 带 Filter 的 Open/Save/Folder 对话框全部瞬时失败 | GVariant 文本 `'filters': <[…>]` 括号顺序写反（应为 `<[…]>`），g_variant_parse 整体报错 | LinuxDesktopPortal.cs:256 |
| A2 | 任何 ScrollViewer 滚轮滚动 → DllNotFoundException | GetSystemWheelScrollLines 直接 P/Invoke user32!SystemParametersInfoW，无守卫 | ScrollViewer.cs:2798-2807 |
| A3 | 打开任何 Popup/ContextMenu/ToolTip → 静默失败（Dispatcher 吞异常） | OpenPopup→GetWorkingArea→MonitorFromWindow；MousePoint 定位→GetCursorPos/ScreenToClient；守卫误用 `Handle != 0` | Popup.cs:549,594-606,917-929,1101-1122 |
| A4 | IME 组合中移动光标 → DllNotFoundException（组合中断） | UpdateImeCompositionWindow 无守卫调 imm32 | Window.cs:10223-10235 |
| A5 | SystemCommands.ShowSystemMenu → 崩 | GetSystemMenu/TrackPopupMenuEx 无守卫，`Handle==0` 误判 | SystemCommands.cs:76-102 |
| A6 | DockItem 撕出/拖拽面板 → 崩 | GetCursorPos/GetWindowRect/SetWindowPos 裸调用 | DockItem.cs:308-614, DockManager.cs:114 |
| A7 | Terminal 控件放入视觉树 → 崩（AutoStartShell 默认 true） | ConPty 直调 kernel32，无 Unix PTY 后端 | Terminal.cs:583-595, ConPty.cs:261-280 |
| A8 | 光标形状全部错位（Arrow 显示成 IBeam、SizeWE 变隐藏…） | managed CursorType（28 值）直接强转为 native JaliumCursorShape（12 值），无翻译层 | WindowInputDispatcher.cs:178-190 vs jalium_platform.h:170-181 |
| A9 | 嵌套 PushFrame/DispatcherOperation.Wait 期间界面假死 | 非 Windows PushFrame 不泵平台事件 | Dispatcher.cs:775-813 |
| A10 | IsVirtualKeyDown 地雷（一旦跨平台路径复用即崩） | s_getKeyStateProvider 静态绑定裸 user32 GetKeyState | Window.cs:11473-11484 |

## 二、native 平台层缺口（Wave B）

ABI 级缺失（jalium_platform.h 无入口，Windows 由托管 Win32 interop 补足、Linux 无对应）：
- **显示器枚举**（bounds/workarea/scale/refresh/primary）→ X11: XRandR + _NET_WORKAREA；Wayland: wl_output(+xdg_output)
- **窗口 min/max 尺寸约束** → X11: WM_NORMAL_HINTS；Wayland: xdg_toplevel.set_min/max_size
- **交互式移动/缩放（DragMove）** → X11: _NET_WM_MOVERESIZE；Wayland: xdg_toplevel.move/resize
- **窗口图标** → X11: _NET_WM_ICON（Wayland 无标准，xdg-toplevel-icon-v1 可选）
- **运行时样式变更**（Topmost/Resizable 等 live 变更）
- **剪贴板非文本格式**（image/png、text/html）

行为缺陷（已有代码需修）：
- X11 `jalium_window_get_state` 恒返回 NORMAL（TODO stub，需查 _NET_WM_STATE）；X11 不派发 MOVE/STATE_CHANGED/ACTIVATE/DPI_CHANGED 事件
- Wayland dpiScale 恒 1.0（需 wl_output scale + wl_surface_set_buffer_scale；HiDPI 屏幕上模糊）
- Wayland set_cursor 是显式 no-op（需 wayland-cursor 主题 + wl_pointer_set_cursor）
- Wayland `jalium_input_get_cursor_pos` 把 surface-local 坐标当屏幕坐标返回
- Wayland 无 xdg_popup（Popup 外飞窗口在 Wayland 无定位手段，暂靠 overlay 模式）
- Wayland 无 zxdg_decoration 协商（GNOME 上无边框）
- 触摸输入两后端均缺（wl_touch / XInput2）；X11 无平滑滚轮
- CMake APPLE 分支引用不存在的 platform_macos.mm（配置必败）

## 三、渲染/文本缺口（Wave B/C）

- fontconfig 家族名 wcstombs 依赖 locale（非 UTF-8 locale 下非 ASCII 族名损坏）→ 显式 UTF-8 转换
- present 能力编译开关缺失时 EndDraw 静默成功（黑窗无错误信号）
- fontconfig 声明可选实为必需（无头则编译失败）
- X11 XPutImage 假设 24/32-depth；无 XShm；无增量 damage
- Vulkan WSI 无 XCB 分支（仅 Xlib）
- 字体 glyph fallback 仅一级（固定 Noto Sans CJK SC）；无 FcFontSort 逐字符链
- 无 LCD/ClearType 亚像素（恒灰度 AA，1/8px 亚像素定位已有）；无 COLR/CBDT 彩色 emoji
- Linux 运行时 HLSL 编译不可用（DXC Windows-only；内置效果不受影响，全部嵌入 SPIR-V）

## 四、多媒体缺口（Wave C）

- **架构缺陷：libjalium.native.media.so 硬链接 GStreamer**——未装 GStreamer 的用户机器上纯音频
  （WAV/FLAC/MP3/OGG，本应零依赖）也一并 DllNotFound。需改为运行时 dlopen/符号动态解析
- 媒体类型不匹配探测阻塞 15-30s（pad 永不链接，appsink 等满超时）
- 动画图像（GIF/APNG）恒回 1 帧（Windows WIC 有完整多帧）
- SoundPlayerAction 硬编码 winmm（Linux 静默无声，应走 AudioPlayer）
- 非 file URI 被 FileExists 挡死（http 流本可由 uridecodebin 播放）
- seek 仅 KEY_UNIT 精度；无 VAAPI/dma-buf 硬解零拷贝（CPU 双拷贝路径可用）
- 无字幕/多音轨 ABI；无麦克风采集；Balance 全平台空壳
- CI 无 H.264 插件且 smoke 正路径需手动传参 → 视频解码正路径从未被任何自动化验证

## 五、桌面集成/无障碍缺口（Wave C）

- 通知 action 回调 no-op + 无 GLib 泵 → 按钮点击/Activated/Dismissed 永不触发
- 无 org.freedesktop.portal.Settings → 无系统深浅色跟随、HighContrast 恒 false
- SystemParameters/SystemColors 全部硬编码 fallback（1920x1080、DoubleClickTime 500…）
- 打印：仅能力探测，无 portal PreparePrint/Print 提交 → 完全无法打印
- NotifyIcon 五个平台钩子全空（Windows 也没实现）；无 StatusNotifierItem
- AT-SPI 桥质量高（五接口+38 角色+事件），但缺 Value/EditableText/Table/Selection 接口
  （滑杆读数、AT 写文本、DataGrid 表格导航不可用）；Text 坐标类方法是近似值
- portal 对话框在 Wayland 无父窗口挂靠（需 xdg-foreign 导出）
- 拖出无拖拽图像、无 GiveFeedback/QueryContinueDrag
- Clipboard 仅纯文本
- 无 xdg-mime/.desktop 文件关联注册

## 六、构建/打包/CI/示例/文档缺口（Wave D）

- ~~linux.yml push 触发分支 `main` 不存在（默认分支 master）~~ ✅ 已修
- ~~musl job 缺 gst-plugins-good~~ ✅ 已修；~~NU1301~~ ✅ 已修；~~Dockerfile dotnet 层静默失败~~ ✅ 已修
- **samples/ 零个 Linux 可运行示例**（8 个桌面示例全部 net10.0-windows + WinExe）
- 主测试套 tests/Jalium.UI.Tests 锁死 net10.0-windows；Linux 实际只执行 ~24 个 managed 用例
- Vulkan 渲染在 Linux 的测试执行数为 0（--wayland-vulkan-smoke 无调用方）
- eng/linux 三个真实端到端脚本（AT-SPI/剪贴板互操作/Wayland DnD）是孤儿，未接 CI
- 三个 Smoke 项目不在 slnx、无 runner；Media.LinuxSmoke 开箱即 DllNotFound（引用链无 Interop）
- musl job 不跑 managed 测试；无 NuGet linux RID 消费端验证（NuGetTest.Desktop 是 Windows-only）
- README 四语无 Jalium.UI.Linux 包/构建指引，且仍宣称 FreeType+HarfBuzz（已被自研引擎替换）；
  docs/linux.md 无任何入链；无发行版支持矩阵/故障排查文档
- 静态 AOT 聚合器 media 分支缺 Linux（jalium.native.aot 只有 WIN32/ANDROID）
- OS 早退式假绿测试（LinuxDragDropTests 在 Windows 上"通过"却什么都没验证）
- 共享 JaliumBuildRoot 并行构建 deps.json 竞争（需 -m:1）

## 实施波次与状态（2026-07-13 更新）

- ✅ **Wave A 完成**：全部 10 条崩溃链/毁灭性缺陷修复（含 filters GVariant、滚轮、Popup、
  imm32、SystemCommands、Dock、Unix PTY、光标枚举、PushFrame 泵、IsVirtualKeyDown）+
  GVariant 结构校验测试与 3 个真实 PTY 端到端测试；另修复 code-only 应用
  ContextMenuService 静态构造从不运行导致右键菜单全平台失效的问题。
- ✅ **Wave B 完成**：新平台 ABI（显示器枚举/min-max 约束/交互移动缩放/图标/topmost）
  三后端落地；X11 get_state 真查询 + MOVE/STATE_CHANGED 事件补发；Wayland HiDPI
  （wl_output + buffer scale + 坐标/尺寸物理像素化）；Wayland 主题光标；managed 接线
  （DragMove/Topmost live/尺寸约束/StartupLocation/Window.Icon/SystemParameters 真实
  屏幕与工作区数据）。
- 🟡 **Wave C 大部完成**：✅ 通知 action 激活与 Dismissed（GLib 泵）；✅ portal Settings
  深浅色跟随（ThemeMode.None 时启动读取+SettingChanged 订阅）；✅ 媒体无流探测 15s
  阻塞→0.03s 快速失败；✅ SoundPlayerAction 走框架音频栈；✅ AT-SPI Value/EditableText
  接口；✅ fontconfig locale 安全 UTF-8 转换。**未完成**（见下"后续跟进"）：GStreamer
  运行时 dlopen 化、portal Print 提交、剪贴板 image/png+text/html。
- 🟡 **Wave D 大部完成**：✅ samples/Jalium.UI.LinuxDemo（首个 Linux 示例，CI headless
  双路径运行）；✅ CI 接入孤儿脚本（剪贴板互操作/Wayland DnD）+ Vulkan-on-Wayland
  冒烟 + 示例构建运行；✅ README 四语 Linux 章节 + FreeType/HarfBuzz 措辞清除 +
  docs/linux.md 发行版矩阵与故障排查；✅ Linux 测试切片 40→55（PTY e2e、GVariant
  结构校验、AtSpiModelTests 首次在 Linux 执行）；✅ `.gitignore` 的 `samples/` 全忽略
  缺陷。**未完成**：NuGet linux RID 消费端验证项目、musl job 的 managed 测试、主套件
  net10.0 可移植化评估。
- ⏳ **Wave E（后续跟进，按优先序)**：
  1. libjalium.native.media.so 的 GStreamer 改运行时 dlopen（当前硬链接：未装 GStreamer
     的机器上纯音频也 DllNotFound——发布质量硬伤，改动面为 linux_media.cpp 全部 gst 调用）
  2. portal Print 的 PreparePrint/Print 提交（当前只有能力探测，Linux 无法打印）
  3. 剪贴板非文本格式（image/png、text/html 的 ABI + X11/Wayland 双实现）
  4. Wayland xdg_popup（菜单/下拉在 Wayland 目前走 overlay，无外飞窗口）
  5. 触摸输入（XInput2 touch / wl_touch → 既有 managed 触摸栈）
  6. X11 XShm 加速 present 与增量 damage；X11 ARGB 透明视觉
  7. LCD/ClearType 亚像素光栅（当前恒灰度 AA）；COLR/CBDT 彩色 emoji
  8. VAAPI dma-buf 硬解零拷贝；动画 GIF/APNG 多帧解码
  9. 托盘 StatusNotifierItem；xdg-decoration 协商；会话结束/设置变化事件
  10. 符号导出白名单（-fvisibility=hidden + version script）与交叉编译 preset

状态跟踪：本文件随实施持续更新；逐波提交、容器内验证后勾销。
