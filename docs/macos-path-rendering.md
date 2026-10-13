# macOS Path 渲染

最大化图标由外框和内框组成，内框应挖空。此前轮廓桥接和绕向分类错误会把内框填满。
现在 Metal 对整个复合路径计算覆盖率，再应用画刷和透明度；孔洞、相交轮廓和描边交叉处
都不会因为逐个图形绘制而填错或叠加透明度。

## 实现范围

| 能力 | 行为 |
| --- | --- |
| 路径命令 | Move、Line、Cubic、Quadratic、旋转椭圆 Arc、Close；SVG/XAML 简写由共享解析器转换 |
| Geometry 入口 | Metal 的矩形、圆角矩形、椭圆和直线使用完整 Path 管线；Path.Data 保留椭圆与圆角弧段及原始 Geometry.Transform |
| 填充 | EvenOdd、Nonzero、嵌套孔洞与岛、相交轮廓、自交；开放 Figure 的填充隐式闭合 |
| 描边 | 每个 Figure 独立闭合；Miter/Bevel/Round、MiterLimit、Flat/Square/Round/Triangle；独立起点、终点与虚线端帽 |
| 虚线 | 奇数数组重复、正负相位、零长度圆点；闭合轮廓首尾连续时保留连接；未描边段使用 DashCap |
| Figure/Segment 属性 | IsFilled、IsClosed、IsStroked、IsSmoothJoin；曲线细分的内部连接保持平滑 |
| 画刷 | 实色、线性渐变、焦点椭圆径向渐变、ImageBrush 填充和描边；GPU 色标缓冲不再截断到 32 项，图片上传支持 CopyPixels 提供的 BitmapSource |
| 变换 | DPI、旋转、镜像、错切与非均匀缩放；GeometryGroup 子变换一起烘焙后执行填充规则；椭圆弧变换保留椭圆参数和扫向 |
| EdgeMode | Path 继承父级或使用局部 Aliased/Antialiased；绘制缓存记录并回放同一模式 |
| 裁剪 | 任意复合路径和描边轮廓的覆盖率裁剪；Geometry.Transform、孔洞、嵌套、图片、效果与缓存层 |
| 重绘 | 保留 dirty rectangle、效果捕获和 retained layer 的目标与裁剪状态 |

小路径和显式抗锯齿使用受可见边界限制的扫描线覆盖率；较大的默认抗锯齿路径使用
独立的 MSAA/stencil 覆盖纹理。超过 Stencil8 绕数容量的路径回退到整数绕数扫描线，
避免 256 次重叠后消失。Vello 的默认实色填充继续使用其原生曲线管线，渐变及扩展描边
使用共享覆盖与画刷计算。

曲线容差根据变换的最大奇异值和 DPI 选择；几何缓存按比例分桶，使用桶的上界细分。
椭圆和椭圆弧按弓高误差细分；Vello 将弧转为三次曲线时也按变换后的误差选择段数。
负半径取绝对值、零半径退化为直线、起终点相同时省略弧；不足以连接两端的半径按比例
放大，与 [SVG 椭圆弧规则](https://www.w3.org/TR/SVG11/implnote.html#ArcImplementationNotes) 一致。
缓存最多 256 项，每项最多缓存 65536 个坐标。渐变缓冲缓存最多 128 项、4 MiB，
超大色标表仍可绘制但不进入缓存。闲置回收会释放上述缓存和路径覆盖纹理。

## 原生接口

`jalium_fill_path` / `jalium_stroke_path` 保持原有参数与命令编号。
Metal 支持扩展元数据 `[6,bits]`（下一段的描边和平滑标记）、`[7,start,end,dash]`
（当前 Figure 的端帽）和 `[8,mode]`（填充或裁剪的边缘模式）。填充忽略描边元数据。
具体布局见 `jalium_api.h`。

`jalium_push_path_clip` 和新增的 `jalium_push_stroke_path_clip` 成功后各对应一次
`jalium_pop_clip`。通过可选 provider 提供能力，不修改既有 RenderTarget 虚表；
不支持该接口的旧后端返回 NotSupported，managed 层保留原有回退。

## 验证与复现

2026-10-07 在 Apple Silicon macOS 上验证：

- `jalium.native.metal.paths`：1158 项 GPU 几何、画刷和捕获检查。包括 1x/1.5x/2x DPI、
  MSAA 1/4、Impeller/Vello、CoreGraphics 像素参考、覆盖率与颜色断言、超大画外路径、
  大半径椭圆弧、放大后的微小坐标弧、四种大小弧与扫向组合，以及退化和半径修正。
- Metal 的 fonts、smoke、regression、paths、effects 五个 CTest 目标通过。
- managed Path 专项：89 项通过，覆盖解析、布局、图标、描边透明度、EdgeMode 回放、
  仿射椭圆弧、复合变换、细分属性、退化弧、大椭圆与大圆角矩形的弓高误差，
  以及 primitive Path.Data 的曲线和变换保留。
- managed→Metal 集成：480 项 GPU 检查通过。将矩形、圆角矩形、椭圆和直线 Geometry
  与显式弧线路径逐像素比较，覆盖 Impeller/Vello、1x/2x DPI、三种 EdgeMode、
  仿射变换、半透明实色、渐变、图片填充和描边及虚线端帽；另断言 Aliased 覆盖率为二值、
  填充和描边实际产生可见像素，BitmapSource.Create 的 CopyPixels 上传与 BitmapImage 结果一致。
- Debug metallib 已从嵌入源码重新编译并同步到开发载荷，原生导出检查通过，独立 Gallery
  Debug 应用已构建。Path 测试在提供 `JALIUM_METALLIB_DIR` 时先检查预编译库的 ABI v5，
  避免旧库被源码回退掩盖；旧 ABI 库的负向检查已确认会立即失败。

测试源码：`src/native/jalium.native.metal/tests/metal_paths.mm`、
`tests/Jalium.UI.MacOS.Tests/MacOSPathRenderingTests.cs`、
`tests/Jalium.UI.MacOS.HostSmoke/PathRenderingChecks.cs`；结果与渲染 PNG 位于
`artifacts/macos-path-rendering-20261007/`。`native-tests.log`、`managed-tests-focused.log`
和 `path-managed-focused.trx` 是最终自动检查结果；`stale-shader-negative.log` 是故意
提供旧 shader 库时的拒绝记录。`managed-path-gpu.log` 记录 managed→Metal 检查结果，
`managed-gpu-paths/` 保存代表性 GPU 回读；`gallery-shader-verification.log` 确认应用打包的
core、Vello metallib 和 manifest 与测试使用的生成文件具有相同 SHA-256。

```sh
# 该入口会生成 shader、同步 metallib 和 manifest、构建路径测试并验证原生载荷。
PATH="$PWD/.tools/cmake-3.31.6-macos-universal/CMake.app/Contents/bin:$PATH" \
  bash eng/apple/build-native.sh macos Debug --development
.tools/dotnet/dotnet test tests/Jalium.UI.MacOS.Tests/Jalium.UI.MacOS.Tests.csproj \
  --filter 'FullyQualifiedName~MacOSPathRenderingTests|FullyQualifiedName~PathMarkupParserTests|FullyQualifiedName~PathShapeTests|FullyQualifiedName~PathIconTests|FullyQualifiedName~GeometryPathParityTests|FullyQualifiedName~ShapeStrokeDashArrayParityTests|FullyQualifiedName~SoftwareVectorRasterizerTests|FullyQualifiedName~PathStrokeOpacityEndToEndTests'

# 本机 Debug 宿主沿用 Gallery 对较新 Xcode 的开发验证设置。
.tools/dotnet/dotnet build tests/Jalium.UI.MacOS.HostSmoke/Jalium.UI.MacOS.HostSmoke.csproj \
  -c Debug -p:ValidateXcodeVersion=false \
  -p:JaliumBuildRoot="$PWD/artifacts/macos-path-rendering-20261007/host-build"
JALIUM_METALLIB_DIR="$PWD/src/native/artifacts/apple/slices/osx-arm64/Debug" \
  artifacts/macos-path-rendering-20261007/host-build/bin/Jalium.UI.MacOS.HostSmoke/Debug/net10.0-macos/osx-arm64/Jalium.UI.MacOS.HostSmoke.app/Contents/MacOS/Jalium.UI.MacOS.HostSmoke \
  --path-rendering
```

## 实际界面验收与边界

完整 Path 版 Gallery 已实际启动并查看截图，运行的是本次独立构建的应用。
两次锁屏中断均在用户解锁后解除。补齐 primitive Geometry 入口与 CopyPixels 图片上传的
最终构建已重新启动，复验了浅色与深色、最大化和还原、Shapes 路径图形、滚动、搜索与
Tab/Shift+Tab 焦点。Gallery 最后保留在浅色 Shapes 页面。

- 浅色和深色主题中，最大化图标中心保持镂空；点击后切换为镂空的双框还原图标，
  还原后恢复先前的窗口尺寸。
- Shapes 页面中的曲线心形、闭合箭头、开放勾线、多边形、折线、虚线与组合图形
  均已查看。滚动、主题切换和窗口调整后没有看到轮廓缺失、错误填充或残影。
- 最终构建实际截图覆盖 3000×1840 的常规窗口和 5120×2640 的最大化窗口
  （尺寸为像素）。较早一轮还查看了 2000×1540 的缩小窗口，以及平板、手机预览的选中态。
  最后一轮拖动角落没有得到可确认的尺寸变化，因此没有将该轮的小窗口复验声明为通过。
- ⌘K 能聚焦搜索框，搜索 Shapes 后可以打开示例；Tab 和 Shift+Tab 能在搜索框与
  导航项之间切换焦点。AX 树中可查看标题栏按钮、预览选中态及当前焦点。

上述界面截图已在本次会话中查看；磁盘上的 PNG 是 GPU 回读测试输出。
可访问性检查范围限于 AX 控件、选中态、调用和键盘焦点，没有运行 VoiceOver。

未验证 Intel macOS、Windows、Linux 和 VoiceOver。本次没有修改其他后端的 managed
扩展描边路由；共享几何辅助函数的其他平台运行结果仍需各平台验证。

探索性的完整 macOS managed 测试首轮还有文本分词和窗口文本导航用例失败：
71 项 `MacOSWordSegmentationTests`、3 项 `MacOSWindowTextNavigationTests`。
该轮新加测试的 3 个反射查找失败已修正，最终 Path 专项复测为 89/89；未将整套 managed
测试声明为通过，也未在此次 Path 工作中修改上述文本行为。
