# Linux 桌面支持：实现与验证矩阵

> 审计日期：2026-07-14。本文只记录当前源码、测试脚本和本地验证能够支持的结论。
> “已实现”表示代码路径存在；“已验证”表示当前版本有对应的自动化或真实协议证据。
> 二者不会混写。

## 结论

Linux 后端已不再是只能开窗的样例：X11/Wayland、Vulkan/软件渲染、输入、剪贴板、
拖放、媒体、打印、无障碍、托盘/通知、系统设置、会话结束和文件关联均有实际实现。
当前已识别的软件实现缺口已经补齐。发布前剩余的是完成当前机器无法提供的 RID/硬件/
桌面环境 qualification，并明确承认 Wayland 协议与外部服务的边界。

## 当前验证快照

| 项目 | 当前结果 | 说明 |
| --- | --- | --- |
| Ubuntu 20.04 / `linux-x64` native | ✅ 已验证 | glibc 2.31 基线；当前 10/10 CTest 通过 |
| ELF 版本、导出、重定位 | ✅ 已验证 | 7 个 `.so` 通过 `GLIBC_2.31` / `GLIBCXX_3.4.28` 上限、导出闭包和 `ldd -r` |
| X11 软件 present | ✅ 已验证 | MIT-SHM 与强制 `XPutImage` 回退均有像素/增量更新测试 |
| Wayland 软件/Vulkan present | ✅ 已验证 | Weston 下 `wl_shm` 与 Vulkan surface smoke；WSL Weston 13 DnD 通过 |
| 真实媒体正路径 | ✅ 已验证 | H.264/AAC、本地、流式 Matroska HTTP 200/206、精确 seek、双音轨、字幕、GIF/APNG/动画 WebP |
| Print portal | ✅ 已验证 | `PreparePrint`、`Print` 和 Unix PDF FD 传递通过假 portal 协议测试 |
| StatusNotifier/通知 | ✅ 已验证 | 两套 watcher 注册、属性、点击/双击/菜单/滚轮、通知 action/close 通过 |
| logind / XSMP | ✅ 已验证 | delay inhibitor、`PrepareForShutdown`、logout cancellation 通过 |
| AT-SPI | ✅ 已验证 | 真实 accessibility bus D-Bus smoke 覆盖 Unicode Text/EditableText、Value/Action、焦点、属性、`ChildrenChanged` 与窗口生命周期 |
| portable managed suite | ✅ 已验证 | Xvfb/X11 下 3844 passed、1 skipped、0 failed；渲染后端保持自动选择，只排除下列 8 个 Windows-only 文件 |
| `linux-musl-x64` | ✅ 已验证 | native CTest 10/10、导出 7/7、无 `GLIBC_*`；静态聚合与三种 NuGet consumer 全过 |
| `linux-arm64` | ✅ QEMU 验证 | AArch64 native CTest 10/10、静态聚合 1/1、11 包及 self-contained/single-file/NativeAOT 全过；仍不等同物理 Arm qualification |
| `linux-musl-arm64` | ⏳ 待原生环境验证 | build/package/consumer/CI 路径已实现；当前机器没有 Arm64 musl SDK/runtime，不能用 glibc Arm 结果替代 |
| 四 RID NuGet + self-contained + trimmed single-file + NativeAOT | ⏳ 3/4 已验证 | `linux-x64`、`linux-musl-x64`、`linux-arm64` 已过；`linux-musl-arm64` 与最终 combined package 待原生 Arm CI |

发布文档不得把上述“待验证”项目改写成“已支持并验证”，除非当前提交已得到对应日志。

## 能力矩阵

| 子系统 | 已实现内容 | 自动化/协议证据 | 仍有边界 |
| --- | --- | --- | --- |
| X11 窗口 | 状态、激活、owner、尺寸约束、交互移动/缩放、图标、Topmost、Opacity、ShowInTaskbar、XRandR/EWMH | platform CTest、真实 Xvfb/sample | WM 可按策略拒绝 EWMH 请求 |
| Wayland 窗口 | `xdg_toplevel`、`xdg_popup`、整数 HiDPI、主题光标、decoration、foreign parent、可选 toplevel icon、原生 system menu 与 token-backed xdg activation | platform CTest、Weston sample/DnD | 无全局坐标、无 token 时不能强制激活、无 Topmost/整窗 Opacity/任务栏控制；无专用 fractional-scale 路径 |
| Vulkan | Xlib/Wayland WSI、LCD 双源混合、颜色 emoji、CPU video staging、受限 dma-buf import | Wayland Vulkan smoke、LCD CTest | 当前只有 llvmpipe/虚拟环境证据，无物理 GPU qualification |
| 软件渲染 | Wayland `wl_shm`；X11 MIT-SHM + `XPutImage` fallback；damage-scoped present | 软件 present CTest 和像素读取 | 性能仍取决于 compositor/X server |
| 文本 | 自研 OpenType shaping/raster、fontconfig fallback、CJK、LCD RGB coverage、COLR/CPAL、CBDT/CBLC | text/Vulkan/software 像素测试 | 并非所有 COLR-v1/SVG/sbix paint graph；目标机仍须安装字体 |
| 键盘/IME | XIM、xkb、Wayland text-input-v3/v1、repeat/state、组合文本 | native platform tests、sample 输入 | 无 text-input 协议时只能用 xkb commit |
| 鼠标/触摸/笔 | capture、光标、平滑滚轮、XInput2 touch/pen、Wayland `wl_touch` 与 tablet-v2（hover/contact/proximity、pressure/tilt/twist、工具/按钮） | synthetic native/managed tests | 无物理触摸/笔设备证据 |
| 剪贴板 | 文本、HTML、RTF、URI/file drop、PNG/bitmap、自定义 MIME | `xclip`、`wl-copy`/`wl-paste` 互操作 | 外部应用对自定义 MIME 的支持由对端决定 |
| 拖放 | Xdnd/Wayland data device、effects、feedback/cancel、drag image、URI/text/custom MIME | X11 与 Weston 真实交互脚本 | 组合器/目标程序可协商到不同 effect |
| Portal | FileChooser filters/cancel、OpenURI、Settings、X11/Wayland parent | 协议完整 fake portal + 模型测试；CI 配置真实服务 cancel smoke | session 必须运行可用的 portal backend；只有 service 包而没有 backend 不足以完成请求 |
| 打印 | 视觉/分页器转 PDF、PreparePrint、Unix FD Print | 假 portal 完整 FD 协议测试 | 无物理打印机证据；Linux `PrintQueue`/CUPS 队列管理未实现 |
| AT-SPI | Accessible/Application/Component/Action/Text/EditableText/Value/Selection/Table 和事件 | 模型测试 + 真实 accessibility bus D-Bus smoke | 未做 Orca 等全量互操作认证 |
| 托盘/通知 | freedesktop + KDE SNI、图标/tooltip/menu、activate/context/scroll、libnotify action/close | fake watcher + fake notification daemon | 需要宿主托盘 watcher；balloon 需要 libnotify 和通知服务 |
| 系统设置 | portal appearance、GNOME GSettings、KDE config、环境覆盖、monitor/workarea、power supply | managed parser/environment tests | 未知桌面会回退到保守默认值 |
| 会话结束 | logind delay inhibitor + shutdown signal；XSMP logout/cancel | 两个假服务端端到端 smoke | session 只会暴露适合自己的协议 |
| 文件关联 | per-user `.desktop`、shared MIME XML、`xdg-mime`、回滚/删除 | managed tests | 依赖 `xdg-utils` / shared-mime-info；不做系统级安装 |
| 媒体 | runtime-loaded GStreamer、H.264/AAC、HTTP、seek、多音轨、字幕、GIF/APNG/动画 WebP、camera/mic ABI | loader present/absent、真实生成媒体、HTTP 200→206、动画帧像素/时序、managed smoke | codec 插件和 WebP runtime 条件化；远程尾置 Cues Matroska 的打开后立即 seek 未 qualification；camera/mic 只有 no-device 证据 |
| 打包 | 四 RID 目录、version script、交叉工具链守卫、隔离 NuGet source mapping；`libjalium.native.aot.a` 媒体/音频聚合链接门禁；trimmed single-file 自解包门禁 | glibc x64、musl x64、glibc Arm64 的静态聚合、11 包及三种 consumer 已过；single-file 均证明干净提取并加载恰好 7 个 `.so` | `linux-musl-arm64` 与四 RID combined package 待原生 Arm CI；single-file 会提取共享 `.so`，不属于全静态打包 |

## 媒体 dma-buf 的准确边界

- `JALIUM_LINUX_MEDIA_DMABUF_EXPORT` 只在当前进程中的真实样本成功导出、且 Vulkan
  能导入单平面 packed-RGB dma-buf 后置位。
- 当前接受的 DRM 格式是 `AR24`、`XR24`、`AB24`、`XB24`。
- NV12、P010、`DMA_DRM` 和多平面描述符不会假装零拷贝；它们按同一时间戳重新打开
  CPU BGRA/RGBA 管线。
- Vulkan 尚无这批 YCbCr 格式所需的 immutable sampler 管线。
- 当前 WSL/CI 没有 `/dev/dri`，所以只证明能力位、拒绝/回退和资源生命周期安全，
  不证明真实 VAAPI → Vulkan 零拷贝。

## Wayland：协议限制，不是待补 Win32 仿真

以下行为不能用 core Wayland 正确伪造，当前实现会返回“不支持”：

1. 获取桌面全局光标坐标或设置顶层窗口绝对位置；
2. 没有 compositor 接受的 input serial / xdg-activation token 时强制抢焦点；
3. 设置 always-on-top；
4. 控制窗口是否出现在全局任务栏；
5. 设置整窗统一 opacity。

透明 surface 的逐像素 alpha、相对父窗口的 `xdg_popup`、compositor 授权的 move/resize
都已实现，不应与上述限制混为一谈。窗口图标、server-side decoration、portal parent
取决于 compositor 是否公布相应可选协议。

## 硬件与桌面环境尚无证明的项目

- 物理 NVIDIA/AMD/Intel Vulkan GPU 和多显示器混合 DPI；
- 有 `/dev/dri` 的 VAAPI/dma-buf 单平面真实导出，更不用说 NV12/P010 零拷贝；
- 物理触摸屏、XInput2 数位笔和 Wayland tablet-v2 笔设备；
- 真实摄像头、麦克风和权限门户；
- 真实打印机/CUPS 队列；当前只验证 portal PDF FD；
- Orca 等真实 Linux 辅助技术组合；当前以 AT-SPI D-Bus 合约为主；
- 所有桌面 shell 的 SNI、portal、session-manager 方言。

这些都是 qualification 缺口，不能从“API 存在”推导为“硬件已验证”。

## 有意排除的 Windows-only 测试文件

`tests/Jalium.UI.Linux.Tests` 自动包含主测试目录中的全部 C# 测试源，只有下列 8 个
文件被整文件排除：

1. `DeviceLost/DeviceRemovalInjectionTests.cs` — Win32 device-removal 注入 harness；
2. `EffectCaptureCullOverrideTests.cs` — Win32 hidden native window/GPU fixture；
3. `FileDialogComActivationTests.cs` — Windows COM common-dialog activation；
4. `SpellCheckerComActivationTests.cs` — Windows spell-check COM activation；
5. `UiaComWrappersTests.cs` — Windows UIA COM wrappers；
6. `VulkanEffectGpuRtSmokeTests.cs` — Win32 HWND GPU smoke fixture；
7. `VulkanRetainedLayerParityTests.cs` — Win32 HWND retained-render fixture；
8. `WindowTaskbarRelaunchTests.cs` — Windows taskbar AppUserModel/relaunch contract。

测试中的单个 Windows 分支仍可在 Linux 项目中存在，但必须用平台条件表达；不得再用
不断扩大的手工文件白名单掩盖可移植性问题。

## 明确未提供的 Linux 能力

- WebView2、WindowsFormsHost、JumpList、`TaskbarItemInfo` 等 Windows shell/COM 功能；
- Linux 对 CUPS 队列的枚举、暂停、恢复、作业管理；
- Wayland fractional-scale-v1 专用路径；
- NV12/P010/多平面 dma-buf 的 Vulkan YCbCr 零拷贝；
- Linux 运行时任意 HLSL 编译。内置效果使用预编译 SPIR-V，不受此限制；
- 每一种颜色字体格式/paint graph 的完整实现；
- Linux 全静态发布；trimmed single-file consumer 会从单一部署文件提取 7 个共享 `.so`，self-contained/NativeAOT consumer 则旁置部署它们；
- framebuffer/DRM/KMS 直出和无 compositor 桌面。

## 发布闭环清单

- [x] Ubuntu 20.04 `linux-x64` native 10/10 CTest
- [x] `linux-x64` GLIBC/GLIBCXX、导出闭包、`ldd -r`
- [x] X11/Wayland 软件与 Vulkan smoke
- [x] 真实 H.264/HTTP/multitrack/subtitle/GIF/APNG/动画 WebP
- [x] Print、StatusNotifier/notification、logind、XSMP 协议 smoke
- [x] AT-SPI Unicode/EditableText/`ChildrenChanged` 集成 smoke
- [x] portable managed suite 当前版本全绿（3844 passed、1 skipped）
- [x] 新鲜 `linux-musl-x64` native/package/三种 consumer 验证
- [x] `linux-arm64` native、静态聚合与三种 consumer（QEMU）
- [ ] `linux-musl-arm64` 原生运行与三种 consumer 证据
- [x] 已验证的三个 RID 的 `test-native-aot-static.sh` 静态聚合链接门禁全绿
- [ ] 四 RID 的 self-contained、trimmed single-file 与 NativeAOT consumer 全绿
- [ ] combined package 同时包含四 RID、每 RID 恰好 7 个 `.so`

只有上述发布门全部闭合后，才能把四 RID/single-file/AOT 的文档措辞从“workflow 已配置”提升为
“当前版本已验证”。
