# Jalium.UI.LinuxDemo

Linux 桌面示例：一个 code-only 的 Jalium.UI 应用，覆盖窗口（自定义标题栏 +
`DragMove`）、文本（CJK + IME）、弹窗（ComboBox / ContextMenu / ToolTip）、
输入控件、剪贴板、portal 文件对话框与自绘 MessageBox。

## 运行

```bash
# 仓库根目录，先构建 native 载荷（一次即可）
bash eng/linux/build-native.sh linux-x64 Release

# 运行示例
cd samples/Jalium.UI.LinuxDemo
dotnet run -c Release
```

## 运行时依赖（Ubuntu/Debian 名称）

```bash
sudo apt install libx11-6 libxext6 libxrandr2 libxkbcommon0 \
                 libwayland-client0 libfontconfig1 libvulkan1 \
                 fonts-noto-cjk       # CJK 文本需要一款 CJK 字体
```

GPU 缺失或受限的环境（容器、CI）可用 Mesa 的软件 Vulkan（`mesa-vulkan-drivers`），
或强制软件渲染：

```bash
JALIUM_RENDER_BACKEND=software dotnet run
```

## 有用的环境变量

| 变量 | 取值 | 作用 |
|---|---|---|
| `JALIUM_WINDOW_SYSTEM` | `auto` / `wayland` / `x11` | 窗口系统选择（默认 auto：有 `WAYLAND_DISPLAY` 时优先 Wayland） |
| `JALIUM_RENDER_BACKEND` | `vulkan` / `software` | 渲染后端（默认 Vulkan，失败自动回退软件渲染） |

## Headless 运行（无桌面环境）

```bash
# X11 路径
xvfb-run -a dotnet run -c Release

# Wayland 路径
weston --backend=headless-backend.so --socket=demo --idle-time=0 &
WAYLAND_DISPLAY=demo JALIUM_WINDOW_SYSTEM=wayland dotnet run -c Release
```
