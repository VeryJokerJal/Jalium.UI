# Vulkan 原生验证

这些测试直接创建 Vulkan 上下文和 Linux 原生窗口，再检查 GPU 回读像素。
选择 Jalium Software 后端不能通过。测试数据和真实解码器/物理 GPU 的验收范围见下文。

## 本轮修正

### 稳定性与兼容

- 实例扩展、验证层、物理设备、设备扩展、表面格式、呈现模式和交换链图像的枚举，
  对 `VK_INCOMPLETE` 有限重试；列表缩短时丢弃尾部，失败时不发布部分结果。
- 用 `vkGetInstanceProcAddr` 查询版本入口，避免动态库直接依赖 Vulkan 1.1 的版本查询导出。
  根据加载器协商最高 1.2，保留 1.0/1.1 路径。设备功能取应用请求版本与物理设备版本的较小值，
  1.0 显式启用 `maintenance1`；见 [VkApplicationInfo](https://docs.vulkan.org/refpages/latest/refpages/source/VkApplicationInfo.html)。
- 优先共用绘制/呈现队列族，必要时创建独立队列并使用 concurrent 交换链共享。
  Vello compute 额外检查绘制队列的 compute 标志。
- 推送常量不足 240 字节时，图形常量改用 set 2 / binding 0 的动态 uniform buffer。
  set 0 纹理和 set 1 椭圆裁剪保持独立。缓冲按在途帧管理，fence 完成后才复用；
  扩容保留前面的绘制数据。分配失败丢弃未提交帧，并保留交换链恢复路径。
- 修正 `Initialize` 参数遮蔽 `ownerBackend` 成员导致的设备注销遗漏。
  关闭当前工厂使用的窗口后，重新选择仍存活且健康的设备 generation。

### 绘制、文字和特效

- 位图、导入视频和 ink 的负向缩放保留纹理坐标方向，支持水平/垂直镜像。
  位图转入 CPU 重放时也保留其仿射 quad。
- CPU 捕获/重放用顶点颜色重建渐变和 AA 三角形，遵守共享边的 top-left 规则；
  不再用近似纯色覆盖渐变。顶点已包含透明度，重放不会再次乘透明度栈。
- 回归检查位图更新前后的 COW 快照、straight-alpha 混合、文字覆盖率/着色/透明度、
  椭圆裁剪、颜色矩阵、模糊，以及 GPU 到 CPU 捕获的状态切换。
- CPU 重放恢复录制时的椭圆裁剪快照，预含透明度的文字不再重复应用透明度栈。
  最后的 CPU 画布合成使用点采样，保留已经按目标分辨率生成的像素。

### Linux 视频导入

- 直接导入 RGB、NV12 和 P010 dma-buf，支持同一分配中的多个平面，以及 disjoint 平面绑定。
  先查询格式/modifier/外部内存能力和专用分配要求；DRM modifier 的依赖按有效 API 版本启用。
- NV12/P010 使用 `VkSamplerYcbcrConversion` 和不可变 combined image sampler，
  传递 BT.601/709/2020 矩阵、full/studio range、chroma siting。
  pool 按驱动报告的实际 combined descriptor 数分配。
  约束见 [VkSamplerYcbcrConversionInfo](https://docs.vulkan.org/refpages/latest/refpages/source/VkSamplerYcbcrConversionInfo.html)。
- 没有 DRM modifier 扩展时，仅接受 modifier 0，且生产者偏移/行距必须与 Vulkan LINEAR 布局完全一致。
  不把非线性 modifier 当作线性像素布局。
- 通过外部队列所有权的 GENERAL → SHADER_READ → GENERAL 转移保留生产者像素。
  消费显式 acquire sync_file，或导出/合并各平面的隐式写栅栏；未完成的栅栏可转为 GPU semaphore wait。
  不支持时返回导入失败，交给现有 precise-PTS CPU 解码回退。
  参见 [Linux dma-buf 同步](https://docs.kernel.org/driver-api/dma-buf.html)。
- fd 使用 close-on-exec 的独立副本；提交 fence 完成前保留生产者样本。
  明确 device loss 可释放资源；无法证明 GPU 已停止时，连同生产者引用一起隔离，防止解码器复用在读内存。
- 普通帧直接采样，不上传视频像素。CPU 特效捕获、重放转入 CPU、其他窗口设备等情况，
  通过带 fence 的 GPU 回读生成缓存 BGRA，再复用现有位图绘制。
  回读等待有上限，异常提交不会提前释放仍在使用的分配。
- 最近邻和 pixelated 视频缩放通过 combined sampler 的 texel-center 采样实现。
- Linux 解码器允许协商 NV12/P010/DMA_DRM，保留 GstVideoMeta 的真实平面布局和颜色信息。
  不支持的格式、隔行/alternate-line chroma、HDR transfer 或无效元数据走 CPU 回退。
  这不代表完整 HDR tone mapping 或色彩管理已实现。

## 构建和运行

### Windows / Android 视频链路

Windows Vulkan 通过 `VK_KHR_external_memory_win32` 导入 D3D11 NT 共享句柄，查询格式和句柄的兼容内存类型，
使用专用分配，并保持外部队列的 GENERAL 布局与帧引用。支持 BGRA/BGRX 及带颜色元数据的 NV12/P010。
未完成写入的可复用纹理会被拒绝；NT 句柄使用独立副本，导入不会关闭解码器的句柄。
Media Foundation 的 GPU 拉帧不调用 CPU Lock/读回，仍做一次 GPU 内的不可变快照复制，避免解码池覆盖在途帧。
当前 MF 输出协商为 packed RGB；NV12/P010 也可通过外部描述符导入，完整 HDR tone mapping 不在本次实现内。

Android MediaCodec 输出到 PRIVATE AImageReader Surface，异步获取 AImage / AHardwareBuffer 和 acquire sync_file。
Vulkan 导入硬件缓冲区，使用驱动的 external format、转换组件、chroma siting，以及解码器提供的颜色矩阵/范围。
采样资源缓存区分外部格式和全部转换字段，描述符池按所需的多平面数量分配。
GPU 完成前保持 AImage 的 acquired 状态和 Reader 生命周期；仅持有 AHardwareBuffer 引用不能防止池复用。
关闭解码器、seek、EOF、导入拒绝和设备丢失均沿用同一引用协议。
不支持的裁剪/保护内容/HDR 走明确失败与 CPU 回退；seek 回退从对应时间戳重新解码。
API 26 接口使用运行时查找，API 24/25 库保持可加载并使用已有 CPU 路径。
接口与约束参考 [Android ImageReader](https://developer.android.com/ndk/reference/group/media#aimagereader_newwithusage)、
[AHardwareBuffer 格式](https://docs.vulkan.org/refpages/latest/refpages/source/VkAndroidHardwareBufferFormatPropertiesANDROID.html) 和
[Win32 内存导入](https://docs.vulkan.org/refpages/latest/refpages/source/VkImportMemoryWin32HandleInfoKHR.html)。

Windows 使用 Visual Studio / Vulkan SDK 配置；跨架构时须为目标选择对应的 loader import library：

```powershell
cmake -S src/native -B artifacts/vulkan-win -A x64 `
  -DJALIUM_BUILD_VULKAN=ON -DJALIUM_VULKAN_BUILD_TESTS=ON
cmake --build artifacts/vulkan-win --config Release --target jalium.native.vulkan jalium.native.media.windows jalium.native.vulkan.external-video.tests
ctest --test-dir artifacts/vulkan-win -C Release -R external-video --output-on-failure
```

Android 使用项目锁定的 NDK 27.2.12479018，以下两种 ABI 均须编译；最低 API 保持 24：

```bash
cmake -S src/native -B artifacts/vulkan-android-arm64 -G Ninja \
  -DCMAKE_TOOLCHAIN_FILE="$ANDROID_NDK_ROOT/build/cmake/android.toolchain.cmake" \
  -DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-24 \
  -DANDROID_STL=c++_shared -DANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=ON \
  -DCMAKE_BUILD_TYPE=Release -DJALIUM_VULKAN_BUILD_TESTS=ON \
  -DJALIUM_MEDIA_ANDROID_BUILD_TESTS=ON
cmake --build artifacts/vulkan-android-arm64 --parallel \
  --target jalium.native.vulkan jalium.native.media.android \
  jalium.native.vulkan.external-video.tests jalium.native.media.android.decoder.tests
# 使用另一构建目录并改为 -DANDROID_ABI=x86_64，再运行相同构建。
```

`external-video.tests` 与 `media.android.decoder.tests` 是带平台 API 测试替身的契约回归，可部署到设备运行；
它们不解码真实视频。物理设备验收还需实际播放 NV12/P010 内容、暂停/seek/关闭、拒绝导入后的恢复和资源计数。
在 Linux 主机上，可以用真实 NDK 声明测试 Android 解码器：

```bash
cmake -S src/native -B artifacts/vulkan-build \
  -DJALIUM_VULKAN_BUILD_TESTS=ON -DJALIUM_MEDIA_ANDROID_BUILD_TESTS=ON \
  -DJALIUM_ANDROID_NDK_ROOT="$ANDROID_NDK_ROOT"
cmake --build artifacts/vulkan-build --parallel \
  --target jalium.native.media.android.decoder.tests \
  jalium.native.vulkan.android-import.tests jalium.native.vulkan.win32-import.tests \
  jalium.native.vulkan.video-interop-shim
ctest --test-dir artifacts/vulkan-build -R 'android.decoder|android-import|win32-import' --output-on-failure
JALIUM_VIDEO_INTEROP_TEST_SHIM="$PWD/artifacts/vulkan-build/jalium.native.vulkan/libjalium.native.vulkan.video-interop-shim.so" \
  dotnet test tests/Jalium.UI.VideoInterop.Tests/Jalium.UI.VideoInterop.Tests.csproj \
  -p:JaliumBuildRoot="$PWD/artifacts/video-interop-managed"
```

测试替身放在构建目录，不应拷贝进运行时库包。托管测试单独启动进程，避免其 DllImportResolver 影响其他测试。

### Linux 绘制回归

在 Linux 仓库根目录执行，需要现有 CMake 3.28+、C++20、Vulkan/X11/fontconfig 等开发依赖。
`spirv-val` 提供规范校验；GStreamer 开发包存在时额外注册元数据测试，运行时媒体库仍动态加载 GStreamer。

```bash
cmake -S src/native -B artifacts/vulkan-build -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DJALIUM_NATIVE_RID=linux-x64 \
  -DJALIUM_NATIVE_OUTPUT_ROOT="$PWD/artifacts/vulkan-native" \
  -DJALIUM_VULKAN_BUILD_TESTS=ON
cmake --build artifacts/vulkan-build --parallel \
  --target jalium.native.vulkan.render.tests \
           jalium.native.vulkan.video-import.tests \
           jalium.native.vulkan.capabilities.tests \
           jalium.native.vulkan.lcd.tests
# 已安装 GStreamer 开发包时：
cmake --build artifacts/vulkan-build --target jalium.native.media.linux.dmabuf.tests

xvfb-run -a env \
  JALIUM_WINDOW_SYSTEM=x11 \
  JALIUM_VULKAN_FORCE_SOFTWARE=1 \
  JALIUM_ENABLE_GPU_VALIDATION=1 \
  ctest --test-dir artifacts/vulkan-build \
    -R '^jalium.native.(vulkan\.|media.linux.dmabuf)' --output-on-failure -V
```

`JALIUM_VULKAN_FORCE_SOFTWARE=1` 选择 CPU 类型的 **Vulkan 设备**，例如 lavapipe，
不切换到 Jalium Software。验证物理显卡时取消它。可用 `VK_DRIVER_FILES` 或旧加载器的
`VK_ICD_FILENAMES` 选择指定 ICD，避免加载容器中没有对应硬件的驱动。

Wayland 需要运行中的 compositor，例如 headless Weston。设置对应 `XDG_RUNTIME_DIR`、
`WAYLAND_DISPLAY`、`JALIUM_WINDOW_SYSTEM=wayland` 后运行同一组测试。
无 display 的渲染测试返回 77；指定 Wayland 时会检查实际窗口系统，不能静默回落 X11。

## 测试范围

| CTest 名称后缀 | 覆盖 |
| --- | --- |
| `lcd` | LCD 通道、帧生命周期、空闲回收门控 |
| `capabilities` | 版本、枚举竞争/错误/上限、队列、modifier 依赖、SPIR-V 边界 |
| `shader-constants` | 迁移 39 个内置模块，含 Windows glyph 和视频着色器 |
| `shader-constants-validation` | `spirv-val --target-env vulkan1.0` 校验实际迁移模块 |
| `video-import` | 模拟驱动检查 fd/颜色/平面/descriptor/lifetime；各阶段失败及 device loss/quarantine |
| `android-import.tests` | 主机模拟 AHB external/已知格式、转换缓存、同步、专用分配、失败清理与设备丢失 |
| `win32-import` | 主机模拟 NT 句柄复制/释放、NV12/P010/BGRX 及格式/内存/同步拒绝 |
| `media.android.decoder` | 使用真实 NDK 声明模拟 Surface 输出、引用/fence、seek、EOS、API 25 及 CPU 回退 |
| `render` | Impeller/Vello、形状、路径、600 个连续绘制、局部重绘、尺寸/VSync、纹理/文字/特效 |
| `render-incomplete` | 注入枚举 `VK_INCOMPLETE` 后运行相同渲染断言 |
| `render-graphics-only` | 隐藏 compute 并禁止 dispatch，验证 tessellation 回退 |
| `render-split-queues` | 模拟分离队列、共享模式和独立 present 句柄 |
| `render-vulkan10-loader` | 隐藏版本入口，用 1.0 实例和扩展运行回归 |
| `render-video-fd-fixture` | 24 组 NV12/P010 格式/布局/矩阵/范围；缩放、裁剪、特效及跨设备/窗口关闭 |
| `render-video-128` | 模拟 128 字节常量限制，运行同一视频像素检查 |
| `render-video-cpu-effects` | 强制 CPU 特效捕获，验证视频回读和绘制/文字的 CPU 重放 |
| `media.linux.dmabuf` | 真实 GStreamer 对象的 NV12/P010/DMA_DRM 导出元数据、偏移、fd 和错误清理 |

视频 fd 像素 fixture 明确限定为支持 YCbCr 的 lavapipe：使用 memfd 测试生产导入和采样代码，
不代表真实 dma-buf/VA-API 解码互操作。旧 lavapipe 不支持该功能时，这三项返回 77。
分离队列、旧加载器、128 字节限制也是模拟条件，不能替代对应的物理设备验收。

验证层报 `[Vulkan][validation][ERROR]` 会使渲染测试失败，API 返回成功也不能通过。
需从日志确认验证层实际附加；没有 `spirv-val` 时不会注册规范校验测试。

## 本机证据与边界

证据在仓库忽略目录 `artifacts/vulkan-20261015/`。环境为 macOS Apple Silicon 上的 Linux x64 容器：
Ubuntu 20.04 / llvmpipe LLVM 12（128 字节常量）和 Fedora 44 / Mesa 26.2.4 / LLVM 22.1.8（256 字节常量）。
保存了 X11/Wayland 日志、实际 GPU 回读图片、分配失败恢复，以及修复前的枚举、viewport、镜像和 CPU 渐变复现。

最终结果：Fedora X11/Wayland 各 14 项通过；Ubuntu X11/Wayland 各 11 项通过、3 项视频能力测试跳过，
均无失败或验证层错误。当前构建再次通过统一缓冲分配失败/恢复探针。
Vulkan C 导出列表保持一致，媒体/图形视频描述符的 216 字节 ABI 静态检查编译通过。
`verification.json` 保存了计数、日志位置及源码/运行库校验值。

未运行物理 NVIDIA/AMD/Intel GPU 的 VA-API/DRM modifier/显式 sync_file 整链路验收，
Windows/Android 的后续构建和契约测试证据在 `artifacts/vulkan-windows-android-20261015/`，
包含 Windows x64/ARM64（LLVM MinGW 交叉编译）与 Android arm64-v8a/x86_64（NDK r27c）产物。
本次追加验证：Fedora X11/Wayland 各 17/17；Ubuntu 各 14 项通过、3 项因旧驱动 YCbCr 能力缺失跳过。
四次运行均确认验证层附加，且没有验证层错误。托管视频交界测试 11/11，ABI/媒体控件定向测试 6/6。
Android 两种 ABI 的完整原生载荷及 libc++ 共各 8 个库，检查了 16 KB LOAD 对齐和新增 API 26 接口的运行时加载。
未运行 MSVC 构建或 Windows/Android 设备实际播放验收；Windows PE 回归程序在 Rosetta/Wine 初始化时超时。
主机模拟契约、Linux 像素回归与目标编译结果须分别解读，不能替代物理驱动的互操作或完整 HDR 验收。
全量 `Jalium.UI.Tests` 编译被工作区已有 `IconSupportTests` 与控件 API 不一致的问题阻断；
本次使用独立项目执行视频交界测试，并单独运行现有 ABI/媒体控件测试，没有修改这些无关控件或测试。
这些原生 fixture 不构成完整 Gallery 页面、硬件输入或辅助功能验收。
