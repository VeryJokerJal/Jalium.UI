<div align="center">

# Jalium.UI Linux Demo

**可直接运行的 Linux 桌面交互示例**

X11 · Wayland · Vulkan · 软件渲染 · .NET 10

[Linux 指南](../../docs/linux.md) · [验证状态矩阵](../../docs/linux-parity-status.md) · [项目主页](../../README.zh-CN.md)

</div>

这个 code-only 示例用于验证 Jalium.UI 的真实 Linux 桌面路径，而不是静态控件截图。

| 领域 | 示例覆盖 |
| --- | --- |
| 窗口 | 自定义标题栏、`DragMove`、最小尺寸、居中启动 |
| 文本与输入 | CJK、IME、键盘、指针、输入控件 |
| 弹出层 | `ComboBox`、`ContextMenu`、`ToolTip`、自绘 `MessageBox` |
| 桌面集成 | 剪贴板、portal 文件对话框 |
| 渲染 | Vulkan 自动选择，以及 CPU 软件渲染回退 |

## 快速运行

先按 [Linux 指南](../../docs/linux.md#build-validate-and-package)安装编译工具、
开发包和运行时依赖，再在仓库根目录、匹配目标 RID 的 Linux 主机上执行。
Shell 脚本必须保持 LF 行尾；被 Windows checkout 转换为 CRLF 后，Bash 会拒绝执行。

```bash
# 首次运行或原生源码变更后，先构建完整载荷
bash eng/linux/build-native.sh linux-x64 Release

# 运行示例
dotnet run --project samples/Jalium.UI.LinuxDemo/Jalium.UI.LinuxDemo.csproj \
  -c Release
```

Arm64 或 musl 主机应把 `linux-x64` 替换为对应 RID。可发布 RID 与验证边界以
[Linux 状态矩阵](../../docs/linux-parity-status.md)为准。

## 运行时依赖

下面是 Ubuntu 24.04 的典型包名。标准 `jalium.native.platform` 载荷同时链接
X11 与 Wayland；即使运行时只选择其中一个窗口系统，也需要保留两套运行库。

```bash
sudo apt-get install \
  libx11-6 libxext6 libxrandr2 libxi6 libxcursor1 \
  libwayland-client0 libwayland-cursor0 libxkbcommon0 \
  libvulkan1 mesa-vulkan-drivers \
  fontconfig fonts-dejavu-core fonts-noto-cjk
```

示例中的 portal 文件对话框还需要会话总线、portal 服务和一个桌面后端：

```bash
sudo apt-get install \
  libglib2.0-0t64 xdg-desktop-portal xdg-desktop-portal-gtk
```

Ubuntu 22.04、Debian 12 等发行版使用 `libglib2.0-0`，而不是
`libglib2.0-0t64`。详细依赖与不同发行版说明见 [Linux 指南](../../docs/linux.md#runtime-dependencies)。

## 运行时选择

| 环境变量 | 可选值 | 默认行为 |
| --- | --- | --- |
| `JALIUM_WINDOW_SYSTEM` | `auto`、`wayland`、`x11` | `auto`：优先 Wayland，再尝试 X11 |
| `JALIUM_RENDER_BACKEND` | `auto`、`vulkan`、`software` | `auto`：优先 Vulkan，再使用软件渲染 |
| `JALIUM_DEMO_AUTOCLOSE_MS` | 正整数毫秒 | 未设置时保持窗口运行；适合 CI 自动退出 |

GPU 不可用或受限时，可以直接强制软件渲染：

```bash
JALIUM_RENDER_BACKEND=software \
  dotnet run --project samples/Jalium.UI.LinuxDemo/Jalium.UI.LinuxDemo.csproj \
  -c Release
```

## 无桌面环境运行

### X11 / Xvfb

安装 `xvfb` 后：

```bash
timeout 20s xvfb-run -a \
  env JALIUM_WINDOW_SYSTEM=x11 JALIUM_RENDER_BACKEND=software \
  JALIUM_DEMO_AUTOCLOSE_MS=5000 \
  dotnet run --project samples/Jalium.UI.LinuxDemo/Jalium.UI.LinuxDemo.csproj \
  -c Release
```

### Wayland / headless Weston

安装 `weston` 后，为 compositor 创建私有 runtime 目录并等待 socket 就绪：

```bash
runtime_dir="$(mktemp -d)"
chmod 700 "$runtime_dir"
socket="jalium-demo-$$"

XDG_RUNTIME_DIR="$runtime_dir" \
  weston --backend=headless-backend.so --socket="$socket" --idle-time=0 \
  >"$runtime_dir/weston.log" 2>&1 &
weston_pid=$!
trap 'kill "$weston_pid" 2>/dev/null || true; rm -rf "$runtime_dir"' EXIT

for _ in $(seq 1 50); do
  [ -S "$runtime_dir/$socket" ] && break
  sleep 0.1
done
test -S "$runtime_dir/$socket"

timeout 20s env XDG_RUNTIME_DIR="$runtime_dir" WAYLAND_DISPLAY="$socket" \
  JALIUM_WINDOW_SYSTEM=wayland JALIUM_RENDER_BACKEND=software \
  JALIUM_DEMO_AUTOCLOSE_MS=5000 \
  dotnet run --project samples/Jalium.UI.LinuxDemo/Jalium.UI.LinuxDemo.csproj \
  -c Release
```

> [!TIP]
> 如果窗口启动但文件对话框没有出现，请先确认 session bus、
> `xdg-desktop-portal` 服务和桌面专用 portal backend 都在运行；仅安装包并不等于
> 服务已经可用。
