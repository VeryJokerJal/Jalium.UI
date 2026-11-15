# Vello 0.10.0 全量重写（2026-08-30）

本次将 Jalium.UI 的 Vello 渲染引擎从"基于早期 Vello 的自定义混合移植"重写为
**上游 vello 0.10.0（2026-08-14 发布）的忠实移植**，覆盖 D3D12 与 Vulkan 两个后端。
上游源码参考：`vello-0.10.0`（linebender/vello，Apache-2.0 OR MIT OR Unlicense）。

## 一、旧实现与上游 0.10.0 的架构差距（分析结论）

| 维度 | 旧实现（"V2"） | 上游 0.10.0 / 新实现（"V3"） |
|---|---|---|
| 场景输入 | CPU 预消化数组（`PathSegment` 48B 预变换曲线、自定义 `DrawTag`/`PathInfo`/`PathDraw`） | 单一打包 u32 scene 流（pathtag/pathdata/drawtag/drawdata/transform/style 六个子流 + Config 中的 word 偏移） |
| pathtag/draw monoid | CPU 串行计算后上传 | GPU 前缀和（pathtag_reduce/reduce2/scan1/scan + draw_reduce/draw_leaf） |
| 曲线扁平化 | GPU（自定义输入格式）+ CPU 重复扁平化仅为算 bbox（丢弃） | GPU flatten 从 scene 流读 tag/monoid/transform/style，一次完成 |
| 描边 | CPU 全量加宽成三角形填充 | **GPU Euler 螺线描边扩展**（cap 标记段协议，miter/bevel/round + butt/square/round 全支持） |
| 变换 | CPU 逐点应用 | GPU 按 trans_ix 读取应用（含笔刷变换 swap 技巧） |
| clip 栈 | clip_reduce/clip_leaf 从不派发，CPU 栈重放上传 clipBbox | **真 GPU clip 管线**（含 0.9.0 #1637 非活跃 lane 修复），EndClip 正确回退父上下文 |
| 渐变 | 256 宽 ramp **buffer**；PTCL 内嵌 8 字参数；extend 恒 Pad | 512 宽 premul RGBA8 ramp **纹理**；draw_leaf 计算 info 流参数（两点圆锥径向全 kind：CIRCULAR/STRIP/FOCAL_ON_CIRCLE/CONE + SWAPPED）；**pad/repeat/reflect 全通** |
| PTCL | 渐变命令 8 字；无 blend_offset 头字 | 每 tile 首字 = blend spill 基址；渐变命令 3 字（index_mode + info_offset）；CMD_FILL 4 字 |
| 混合 | fine 恒 SrcOver（27 个常量是摆设） | 完整 Mix(16) x Compose(14) 表 + 亮度蒙版层 |
| bin | бин 上限 256（>256 未定义行为） | 0.10.0 #1700 块循环修复（256 bin 一块、aligned_n_bins 步长） |
| 图像 | encode 侧存根，atlas 从不绑定 | fine 完整支持（LOW/MEDIUM/HIGH 双三次 + 0.9.0 半像素修复 + BGRA + alpha_type）；atlas 接线留待后续（当前绑 1x1 dummy） |
| 溢出协议 | 部分 | 完整 failed 位协议 + `ptcl[0]=~0u` 毒标 + fine 早退 |

## 二、重写内容

### Shader（19 个逻辑 compute stage + 1 个 small-scan permutation，WGSL→HLSL 逐行移植）

`src/native/jalium.native.d3d12/shaders/vello_*.cs.hlsl` + `vello_shared.hlsli` + `vello_blend.hlsli`。
单一 HLSL 源双编译：fxc `/T cs_5_1`（DXBC，`gen_vello_bytecode.ps1` → `d3d12_vello_bytecode.h`）
和 dxc `-spirv -T cs_6_0 -fvk-use-dx-layout`（`gen_vello_spv.ps1` → `vulkan_vello_shaders.h`）。
**改任何 vello_*.hlsl 后必须两个生成器都重跑。**

派发序：pathtag_reduce → [pathtag_scan_small 或 pathtag_reduce2 → pathtag_scan1 → pathtag_scan] →
bbox_clear → flatten → draw_reduce → draw_leaf → [clip_reduce → clip_leaf] →
binning → tile_alloc → path_count_setup → path_count(indirect) → backdrop(dyn) →
coarse → path_tiling_setup → path_tiling(indirect) → fine(area)。

移植保真要点：
- flatten 保留 fma（HLSL mad）版私有 Transform、ESPC 常量逐字节一致、0.6.0 仿射容差修复、0.7.0 近平行 miter 修复；
- clip_leaf 保留 0.9.0 显式控制流（不用 select 物化越界索引）；
- tile_alloc 保留 storageBarrier 广播 workaround（`paths[ix|255].tiles`），故 **paths 缓冲必须按 align_up(n,256) 分配**；
- path_count/path_tiling 的保守光栅化与 y_edge/EPSILON 数值鲁棒逻辑逐行一致（顺带修正旧版 `fudge` 三目与上游 select 语义相反的潜伏 bug）。

### 共享编码器（`jalium.native.core/include/jalium_vello_encode.h`）

`vello_encoding` crate 的 C++ 移植，**D3D12 与 Vulkan 共用**（消灭了旧 D3D12 私有编码器副本，
也顺带消灭了旧版 `EncodeBeginClipRect` 的 `float commands[8]` 写 9 个元素的栈越界）。
包含：PathTag 状态机（含描边 cap 标记段、1e-12 零长过滤、隐式 MoveTo 提升）、
DrawTag 位打包、样式双字（f16 miter limit）、变换去重、笔刷变换 swap_last_path_tags、
即时渐变 ramp 解析（512 texel premul，帧内内容哈希去重，**免去上游 Patch/Resolver 机制**）、
CPU 虚线展开、SVG 弧→cubic、打包（pathtag 按 1024 字节零填充对齐）、
`BuildRenderInfo()`（WorkgroupCounts/BufferSizes 公式 + 88 字节 VelloConfig）。

### 后端

- **D3D12**（`d3d12_vello.h/.cpp`，5265 行→约 1240 行）：单根签名（root CBV b0 + SRV 表 t0-t4 + UAV 表 u0-u3），
  20 PSO（含 small/large scan 两个 permutation）预编译 DXBC，ExecuteIndirect x2，逐派发新建描述符堆并立即 park（#921 纪律原样保留：
  pendingRetiredResources_/Heaps_ → FlushVelloPaths DrainRetired；ForceNewOutputTexture 停靠语义不变）。
  公共 API 保持所有真实调用点兼容；删除全部死代码（CSS filter 图、mask、COLR、路径缓存、CPU 管线）。
- **Vulkan**（`vulkan_vello_compute.h/.cpp`）：20 stage/permutation 描述符表**按 spirv-dis 实测存活绑定**书写
  （shift b→N/t→N+16/u→N+48）；渐变 ramp 变为 sampled image（staging + CopyBufferToImage）；
  输出 storage image 从 RGBA32F 降为 **R8G8B8A8_UNORM**（配合 fine 的
  `[[vk::image_format("rgba8")]]`，该属性用 `-D JALIUM_SPIRV` 门控以免 fxc 报错）；
  帧槽/retire/事务性替换/AbandonDeviceResources 纪律原样保留。
  `Record` 签名改收 `VelloSceneEncoder&`（打包+配置按需推导）。

### 框架级修复：SpreadMethod ABI 打通

旧链路：managed 一直传 `extendMode`，但 C ABI（`jalium_api.h`/`brush.cpp`）签名没有该参数
——x64 调用约定下静默丢弃，导致全部渐变恒 Pad。修复：
- `jalium_brush_create_linear/radial_gradient` 增加 `uint32_t extendMode` 并转发到 backend
  （backend 虚接口与全部实现早已就绪）；
- managed `RenderTargetDrawingContext` 增加 `ToNativeExtendMode` 映射
  （**WPF 枚举序 Pad/Reflect/Repeat ↔ native/peniko 序 Pad/Repeat/Reflect 不同**，不可原样透传）。

## 三、与上游的既知分歧（均有意为之，代码中有注释）

1. fine 末端**保留 premultiplied 输出**（上游 un-premultiply）—— Jalium 两端合成均预乘约定。
2. 仅移植 `fine_area` 变体；MSAA8/16 permutation 未移植（上游 Area 同为完整模式）。
3. D3D12/Vulkan 已按上游阈值选择 `pathtag_scan_small`；Metal 暂仍走 large 链。
   large 路径的 reduce2/scan1 继续按 `align_up(path_tag_wgs,256)/256` 精确派发，
   pad 区垃圾经论证无害（见编码器头注释）。
4. 渐变 ramp 即时解析（无跨帧 RampCache epoch 缓存）；每帧上限 512 条。
5. 图像笔刷暂不路由 Vello（image 笔刷 EncodeFillPathBrush 返回 false → CPU 三角化回退，行为同旧版）。
6. `bump` 溢出 failed 位按上游协议整帧丢弃（上游 0.10.0 亦未实现重试；readback 自适应扩容为后续 TODO）。

## 四、验证

- fxc(cs_5_1) + dxc(spirv) 双编译 20/20 通过；native Debug/Release 全目标零错误。
- `Jalium.UI.Tests` 过滤 Gradient/Brush/RenderContext/Vello：180/180 通过。
- 视觉：`DesktopDemo` 新增 `VelloTestWindow`（`JALIUM_DEMO_WINDOW=vellotest` +
  `JALIUM_RENDERING_ENGINE=vello`，Program.cs 已接 env），静态特性矩阵
  （填充规则/多 figure 曲线/旋转变换/半透明叠加/三种 join/三种 cap/虚线/S 曲线描边/
  linear Pad+Repeat+Reflect/radial 同心+focal+Reflect 环纹/细线/极端细长三角）
  在 D3D12 与 Vulkan 上与 Impeller 参照一致，且 Repeat/Reflect 为新增正确行为。
- Vulkan validation layer：**零发现**（修复了 dxc 默认 Rgba32f 注解与 RGBA8 视图不匹配的 UB 警告）。
- PathPerfWindow（60 复杂图标 + 缩放动画）双后端 12s 压力稳定。

## 五、后续 TODO

- 图像 atlas 接线（fine 已完整支持 CMD_IMAGE；需把 Jalium bitmap 装入 atlas 纹理并 encode DrawImage）。
- bump 溢出 readback + 自适应缓冲扩容（上游同样是 TODO）。
- MSAA8/16 fine 变体（如需更高质量模式）。
- 上游 `sparse_strips`（vello_cpu/vello_hybrid）是下一代架构，独立于经典 compute 管线，
  未来若上游稳定可评估用于 Software 后端。

## 六、Vulkan painter-order 修复（B2 闭环，2026-08-30 当日追加）

重写后首次在 IDE（Jalium.One 用 Vello 引擎 + Vulkan 后端）真机运行暴露 z 序反转：
Vulkan 的 compute 模式把整帧 path 累进一个场景、帧末一次 composite，导致**同帧中
path 之后绘制的 rect/text 被反压到 path 之下**。症状：切走的设计器 tab 的工具栏
path 图标浮在新 tab 内容上持续残留（resize/PrintWindow 均在）；列表 hover/选中
高亮盖住行文字。D3D12 无此问题（FlushVelloIfNeeded 逐段 Dispatch）。

修复（对齐 D3D12 语义）：
- `VelloSceneEncoder::CutSubScene`（jalium_vello_encode.h）：把已编码内容 Finalize→
  Pack→BuildRenderInfo 后 **move** 成自包含 `VelloSubScene`（packed 流+ri+ramps），
  encoder 原地重开；engine 侧 `CutPendingToSubScene` 切段后重放 sticky scissor。
- `MaybeEmitEngineSpan`：compute Vello 不再被排除——每个非 path replay 命令录制前
  切段并发 `VelloSceneSpan`（新 replay kind，复用 engineBatchSpan.firstBatch 携带
  段索引）。
- `DrawReplayFrame`：`VelloSceneSpan` case 在流中原位 Record+Composite，
  capture-follow 与 EngineBatchSpan 完全一致（offscreen 区间内进离屏 CLEAR/LOAD 对，
  degraded 捕获/部分帧按同规则收 scissor）；帧末单场景 consume 删除。
- `VelloComputePipeline::Record` 多次调用安全化：签名改收 `VelloSubScene`；
  描述符池扩容为 `kMaxRecordsPerFrame(24)×19` sets 且只在 PrepareFrameSlot reset
  （BuildDescriptorSets 内的 per-Record reset 移除——它会毁掉同帧前段的 sets）；
  host-visible 输入（config/scene/rampStaging）与 ramp image 同帧复用前
  `RetireInputsForReuse` 退休重建（前段的 GPU 读还在队列里，禁止原地改写）；
  共享 scratch 靠 leading barrier 串行；输出图单张串行复用
  （前段 composite 采样 → 本段 clear 之间 barrier 有序）。

验证：native Debug/Release + d3d12 双后端零错误编译；IDE 真机 Vulkan 下
"打开设计器 → 切 tab" 序列 PrintWindow 前后对比（修复前工具栏图标叠在 csproj
文本上，修复后干净）；Vello/Gradient/RenderContext 52/52、RenderTarget/Backend
126/126（顺带修正 PixelSnap 测试第 4 行期望值——m12=0.02 超过 scale-relative
旋转阈值 1e-3×scaleRef，managed/native 必须一致判为 rotated=true）。

已知残留 TODO：段数超过 24/帧时丢段（VK_LOG 一次）；每段 fine 仍全屏 tile 派发
（空 tile 早退，桌面 GPU 可忽略；移动端如需可按段 bbox 收窄 viewport）。

## 七、子场景性能修复（2026-08-30 当日追加，D3D12 + Vulkan）

Jalium.One（D3D12 + Vello）真机实测暴露重写后的严重性能回归：**4 fps**、GPU 显存 4.3 GB。
用 `JALIUM_VELLO_PERF=1`（新增的 opt-in 分段计时器，见 d3d12_vello.cpp）定位：

```
[VelloPerf] 4.0 fps | dispatch/frame=85.0 | cpu/frame=138ms
            (1620us/dispatch: encode=1 buffers=1215 desc=390 record=43)
            tags/frame=2131 drawobj/frame=273 fineWG/frame=471240
```

根因不是着色器，而是**每个子场景都按整个视口付费**：IDE 把图标与 rect/text 交错绘制，
`FlushVelloIfNeeded` 每帧切出 **85 个子场景**，而每个子场景都
①新建一张视口大小(1600×900=5.76MB)的输出纹理 + 4 个 upload committed resource，
②新建 312 描述符的堆并写 ~230 个描述符，
③把 `fine` 跑满整个视口（5544 tile）。
实际内容常常只是一个 24×24 的图标。

### 修复（三处，互相独立）

1. **按内容包围盒渲染**（共享编码器，双后端同时受益）
   `VelloSceneEncoder` 累积每个子场景的设备空间包围盒（控制点凸包 + 描边/模糊 padding，
   clip 不参与放大——它只会缩小可见范围），`Pack()` 产出 tile 对齐的 `VelloRenderRegion`
   并**把变换流整体重定基 -origin**（路径数据是局部空间，只有平移要动，故无需改 shader）。
   `BuildRenderInfo()` 的 target/tiles 随之变成区域尺寸，后端在 `origin` 处合成。
   → `fineWG/frame` 471240 → **673**。
2. **输出纹理池化 + 线性上传 arena**（D3D12）
   `ForceNewOutputTexture(frameIndex)` 把纹理交给该帧槽的 in-flight 列表，
   `RecycleFrameResources(frameIndex)`（BeginFrame 里、栅栏之后调用）归还到复用池；
   每个子场景的 scene/config/ramp/bump-zero 改为从**每帧一个 upload arena** 里 bump 分配。
   → `buffers` 1215µs → **77µs**。
3. **每帧一个描述符堆 + 跨子场景缓存**（D3D12）
   除 `fine` 外的 18 个 stage 在一帧内绑定的资源完全相同，其描述符块只在
   `resourceGeneration_`（任一资源重建时自增）变化时重写；`fine` 绑定每子场景的输出纹理，
   永远分配新槽位（**已录制的描述符绝不能就地改写**）。
   ★配套前提：所有视图的元素数改为描述**缓冲区完整容量**而非本子场景的逻辑尺寸，
   否则缓存必然失配（越界本就由 config 里的计数管）。
   → `desc` 390µs → **11µs**。

### 结果（Debug 构建 + D3D12 调试层开启）

| 指标 | 修复前 | 修复后 |
|---|---|---|
| fps | 4.0 | **33** |
| CPU/帧 | 138 ms | **9.3 ms** |
| µs/dispatch | 1620 | **113** |
| fine workgroup/帧 | 471,240 | **673** |
| GPU 显存 | 4.3 GB | 显著下降 |

### Vulkan 侧同步

共享编码器的重定基会让 Vulkan 一并偏移，故同步修改：
- `Record` 用 `sub.packed.region`，输出图像**只增不减**（每子场景改尺寸会退休仍被本帧
  早前子场景描述符引用的图像）；
- `CompositeVelloOutput` 新增 `dstRect` + `srcExtent`：视口设为"整张图像放在 region.origin"，
  scissor 收到 region —— uv 0..1 自然 1:1 落位，**无需改 shader**；
- ★`retireSlot` 由 `(frameIdx+1)%2` 改为 `frameIdx`。原设计假设"一帧一次 Record"，
  子场景模型下同帧早前子场景的命令还在未提交的命令缓冲里，退到另一槽会在其栅栏前销毁。
  → Vulkan validation 从 56 条 VUID 归零。

### 图标渲染

与 Impeller 逐像素比对：designer canvas **0% 差异**、代码编辑器 0.03%、图标条仅 ~1.7%
像素有边缘抗锯齿级差异（平均差值 1.5/765）。即着色本身正确；用户观察到的"图标不正常"
是 4 fps 下的掉帧/撕裂表现，随性能修复一并消失。

### 遗留

- Vulkan 仍有 20 条 `vkDestroyBuffer` 类 VUID 属另一条子场景改动的既有问题——已由上面的
  `retireSlot` 修正一并解决（复测归零）。
- 输出纹理池按精确尺寸匹配，尺寸种类极多时可能反复分配（上限 64 条）；如需可改成尺寸分桶。

## 八、IDE 实战第二轮：flush 风暴 + composite 变换污染（2026-08-30 当日追加）

第七节的修复把探针场景推到 33 fps，但用户实测大窗口 IDE 仍 13 fps（DevTools：
`FillPerCornerRoundedRectangle 177 次共 20.7ms，单次 117µs ≈ 一次 dispatch 成本`），
且解决方案树 / 选项卡的图标不渲染。两问题同根排查，最终四个修复：

### 1. flush 重叠门控（D3D12RenderTarget 层）

`FlushVelloIfNeeded` 原本在**每个**非 path 绘制前无条件切子场景。IDE 的图标与
rect/text 交错 → 大窗口 ~400 次 dispatch/帧。新增带界重载
`FlushVelloIfNeeded(x,y,w,h,siteTag)`：进来的绘制与 pending path 内容不相交时跳过
flush（画家序不受影响）。文本的界用 **DWrite 布局 metrics**（布局盒常是 10000 宽的
"无限"值，直接用会把整行都判为重叠）。

### 2. 逐图元盒列表（编码器）

单一合并 bbox 会让"图标+右侧文本"的行永远判重叠。encoder 维护
`primBoxes_[24]`（每个 Encode* 包装用 Begin/EndPrimitiveBox 括起），
`PendingHitsDeviceRect` 先粗测合并盒再逐盒精测；溢出降级为合并盒（保守）。

### 3. 重叠图元改道进子场景（关键提速）

真重叠的小图元（≤160dip 的 rect/圆角矩形/椭圆/多边形）不再触发 flush，而是
**编码进同一 vello 子场景**（`TryEncode{Rect,Ellipse,Polygon}IntoPendingVello`，
圆角/椭圆用 k=0.5522847498 贝塞尔，径向走 EncodeFill/StrokePathBrush 原生刷子路由）
——画家序在场景内天然正确，一次 dispatch 全包。这也顺带修正了 FillPolygon
"先无条件 flush 再 encode 进 vello"的旧反模式。

### 4. ★composite 环境状态污染（图标不渲染的真正根因）

`FlushVelloPaths` 的 composite `AddBitmap(region/dpi …)` 传的是**窗口绝对 DIP**，
但 AddBitmap 会把**当前变换/裁剪/不透明度**烘进 instance——而 flush 是由元素树
**任意深处**的绘制触发的：树行触发时环境变换带 ~(1030,300) 平移，composite 被
二次平移画出屏幕 → 该子场景内容（整行的 folder/文件图标）静默消失。
浅层（工具栏，变换≈identity）恰好显示，造成"部分图标丢失"的假象；
vellotest 全部在 identity 下 flush，从未暴露。
修复：composite 前暂存并清空 transformStack_/scissorStack_/currentOpacity_，
AddBitmap 后恢复（每条 path 的变换/裁剪/不透明度在 encode 时已进场景，
环境状态一概不得二次施加）。该 bug 自子场景化（第六节 B2）起就存在——
用户最初报"图标渲染还不正常"即为此。

### 结果（Jalium.One 项目视图探针，Debug + D3D12 调试层开启）

| 阶段 | fps | dispatch/帧 | CPU/帧 |
|---|---|---|---|
| 重写后初测 | 4.0 | 85 | 138 ms |
| 第七节三修 | 33 | 85 | 9.3 ms |
| +门控 | 33 | 78 | 9.0 ms |
| +改道 | 46 | 59 | 4.8 ms |
| +逐图元盒 | **64** | **23** | **0.68 ms** |

图标全数恢复（树 folder/文件、选项卡、启动按钮、工具栏后段），与 Impeller 视觉一致。
Vulkan validation 零发现；283/283 回归测试通过。

### 诊断设施（保留，env 门控）

`JALIUM_VELLO_PERF=1` 每秒统计行 + `[VelloGate]`（skip/hit/unbounded/reroute 计数与
site 分布）；`=2` 追加 `[GateHit]`（每次 flush 的触发绘制与 pending 盒）与
`[VD]`（每次 dispatch 的 region/path 数）；`=3` 追加 `[FP]/[SP]`（FillPath/StrokePath
的 vello 路由与 brush 类型）。各类每秒限额，常态零开销。

### 遗留

- 文本与图标**贴边**（间距 <0.5px pad）仍会 flush（~10 次/帧，成本已可忽略）。
- Vulkan 侧无 composite 污染问题（其 composite 走 render pass viewport，绝对坐标），
  但 flush 门控/改道尚未镜像到 Vulkan 的 MaybeEmitEngineSpan——如需可复用
  encoder 的 PendingHitsDeviceRect。

## 九、Vulkan 图标丢失双根因 + 优化镜像（2026-08-31 追加）

用户切到 Vulkan 后端后同样图标不渲染。排查（层假说/scissor/坐标空间逐一证伪，最终
[VkCut]/[VkSpan]/[VkLayer*] 取证锁定）发现是**两层容量上限的叠加**，与 D3D12 的
composite 变换污染完全无关：

1. **根因 A — Record 丢段**：`kMaxRecordsPerFrame=24`，而 IDE 项目视图一帧切
   **97 个子场景** → 每帧 73 段被 `Record` 拒绝（DROPPED），段里的图标整段消失。
   B2 落地时的注释就预告了这种降级，但没人想到真实 UI 会到 97。
2. **根因 B — composite 描述符池**：`velloCompositeDescPools` 每帧槽 **32 sets**
   （"enlarged (32-set)" 是按 cap=24 时代的余量），每个子场景 composite 分配一个
   set → 第 33 个起 `vkAllocateDescriptorSets` 失败 → `CompositeVelloOutput`
   **静默 return**。cap 提到 128 修掉根因 A 后，根因 B 顶上来继续丢后段内容；
   此前给门控/reroute 做的所有二分实验的"仍然丢"其实全是这个池 —— 一度误判
   门控有正确性问题。

修复：
- cap 24 → **128**（compute 描述符池随常量自动放大）；
- composite 池 32 → **kMaxRecordsPerFrame + 32**；
- **flush 门控镜像**：`RecordReplayCommand` 漏斗统一做重叠门控
  （`MaybeEmitEngineSpan(&command)` + `ReplayCommandDeviceBounds` 按 kind 提取
  bounds：SolidRect/customQuad/shadow 膨胀、Bitmap/InkLayer/ExternalVideo、
  TextRun 扫 glyph、VcTriangles 扫顶点；effect/capture 标记类无界 → 无条件切）；
- **reroute 镜像**：`TryEncode{RoundedRect,Ellipse,Polyline}IntoPendingVello`
  （VelloReroutePreflight 统一先决条件：compute 模式、非 CPU 光栅、无
  transition/effect 捕获、≤160dip、逐图元盒重叠）；
- 逐图元盒：Vulkan 引擎 4 个 encode 包装加 Begin/EndPrimitiveBox 括号；
- **CPU 捕获逃逸修复**：FillPath/StrokePath/FillPolygon 的 engine 路由 gating
  增加 `!cpuRasterNeeded_` —— CPU 光栅帧（retained-layer CPU 回退等）里 path
  再进 vello 就会从 CPU 快照里消失（预存缺口）。
- 门控/reroute 均带 env 逃生阀：`JALIUM_VK_VELLO_SPAN_GATE=0` /
  `JALIUM_VK_VELLO_REROUTE=0` 关闭。

结果：Vulkan IDE 图标全数恢复（两次复跑稳定），DROPPED=0，打开项目帧切段
97 → 57（门控+reroute 生效；稳态静止帧 2 段）。validation 零发现；283/283。

诊断设施（env 门控，`JALIUM_VELLO_PERF=2` 起）：`[VkVello] subscenes/frame`、
`[VkCut]`（切段 region+触发命令 kind）、`[VkSpan]`（composite dst/offscreen/damage）、
`[VkSpan DROPPED]`、`[VkLayerEnd]/[VkLayerComp]`（层 blit/贴图）、level 3 加
`[VkSP]`（StrokePath 引擎路由）。

### 遗留

- Vulkan 打开项目帧仍 ~57 段（D3D12 同场景 23）：TextRun/杂类命令的门控界更保守，
  可继续收窄；静止帧 2 段已是地板。
- `subscenes/frame` 统计按"有 Record 的帧"平均，静止帧不计入 —— 读数时注意。

## 十、Vulkan 滚动掉帧：每段 2-3 次 vkAllocateMemory（2026-08-31 追加）

图标修复后用户实测滚动解决方案树仍 29 fps，DevTools 显示 **EndDraw 单项 47.26ms**
（各绘制调用只是录制 replay 命令、都在 µs 级；GPU 仅 3.8ms）——瓶颈在 EndDraw 内的
CPU 录制。滚动时每帧 ~46 个子场景，而 Vulkan 的 `Record` 每段：

- `RetireInputsForReuse` 退休 scene/config/rampStaging 三个 host buffer；
- `EnsureBuffer` 重建它们 = 每段 **2-3 次 vkCreateBuffer + vkAllocateMemory +
  vkBindBufferMemory + vkMapMemory**（≈100-300µs/次）；
- 19 组 descriptor set 分配 + 写入。

46 段 × 2-3 次设备内存分配 = **每帧上百次 vkAllocateMemory** —— 与 D3D12 第七节
"每段 committed resource" 完全同构的病，同构的药：

**每帧槽 upload arena**（`FrameArena`，1MB 起、不足翻倍增长）：scene | config |
ramp staging 全部 bump 分配（256 对齐，覆盖 minUniform/minStorage 对齐要求），
descriptor 写 `arena.buffer + offset/range`，ramp copy 用 `bufferOffset` 切片；
cursor 在 `PrepareFrameSlot`（本槽栅栏之后）归零；增长时旧 arena 退休进本槽
（帧内早段的 descriptor 仍引用它）。`RetireInputsForReuse` 只剩 ramp image 部分。

### 结果（Debug 构建实测，滚动探针 130 次滚轮）

| 指标 | 修复前 | 修复后 |
|---|---|---|
| Record CPU/段 | ~300-600µs（推算） | **25µs** |
| DrawReplayFrame/帧（滚动） | ~20-30ms（推算，DevTools EndDraw 47ms） | **0.4-0.8ms** |
| 滚动密集期重绘率 | 29 fps | **125-176 fps** |

（fps 读数波动大是事件驱动渲染的自然形态：静止期无重绘拉长统计窗口。）
正确性：IDE 图标完好、vellotest 矩阵正确、validation 零发现、283/283。

诊断追加：`[VkVello] ... rec=Nus`（每段 Record CPU 均值）与
`[VkFrame] drawReplay=N.NNms fps=N`（每 120 个 replay 帧）。

### 排查插曲（记录以免再踩）

探针脚本强杀实例后，另一场会话遗留的 `JaliumProbe` 进程持有单实例 mutex，
后续启动全部走"转发给主实例"分支静默退出（exit 0、无 startup 打点）——
表现酷似启动崩溃。遇到"秒退 code 0"先
`[System.Threading.Mutex]::TryOpenExisting("Jalium.One.IDE.mutex", ...)` 探测，
再杀残留进程。探针乱点还会把 Temp 里的临时解决方案写进 recent.json 首位。

## 十一、小尺寸 SymbolIcon 完整性与抗锯齿（2026-09-01）

Gallery 顶栏 `SymbolIcon Symbol="Website" Width="15" Height="15"` 暴露出另一类“图标
不完整”：U+EB41 的真实 Segoe MDL2 轮廓是圆环内三个小写 `w`，Jalium 的画面却只剩
圆环加一条横线，圆周也有明显阶梯。该控件不是 PathIcon；它经 `FormattedText` 进入
D3D12/Vulkan 各自的 glyph atlas，因此 Impeller/Vello 切换不会改变症状。

像素探针确认 Auto/Fixed/Animated 三种 hinting 在旧实现下完全相同：DirectWrite 直接按
最终 **15 ppem** 生成 alpha texture 时，三个 `w` 已被量化成单行；GPU 后续采样、对比度
增强或 Vello/Impeller 路由都无法恢复已经丢掉的覆盖。

修复（D3D12/Vulkan 镜像）：

- `IDWriteFontFace::IsSymbolFont()` 识别 symbol-font run；≤32 ppem 且非显式 Aliased 时，
  用 **2× grayscale strike** 光栅化；
- quad 通过 `strikeToFinalScale` 缩回真实尺寸，并由 smooth sampler 做 2×2 覆盖解析；
  普通文本继续 final-ppem + point sampler，显式 Aliased 语义也保持不变；
- `CachedGlyphRun` 保存 `requiresSmoothSampling`，保证第二帧 instance-cache hit 不会退回
  point sampler；混合字体 layout 只把当前 text batch 升为 smooth；
- `SymbolIcon`/`FontIcon` 把继承的 TextRenderingMode/TextFormattingMode/TextHintingMode
  写入 `FormattedText`，显式应用级策略现在能真正到达 native atlas。

回归：

- U+EB41 在 96 DPI 的中部有效覆盖由 1 行恢复为 **3 行**，圆环边缘产生连续灰度覆盖；
- 96/144/192 DPI 均验证内部轮廓完整，无高 DPI 裁切或采样模式回退；
- D3D12/Vulkan × Impeller/Vello 四组合输出 PNG 字节一致；
- 新增 `GpuSymbolGlyphRenderingTests`（四组合、第二帧 cache-hit 捕获）和
  `GpuSmallIconPathRenderingTests`（15px compound cubic ring，覆盖曲线 AA/EvenOdd，四组合）；
- 既有 `GpuTextDpiRenderingTests` 与聚焦图标套件全部通过，Gallery Debug 构建 0 警告。

## 十二、Vulkan Impeller 小尺寸开放描边阶梯（2026-09-01）

Gallery `DesktopBackButton` 继续暴露一条与字体无关的路径：箭头来自两段开放 `Path`
（`M18,4 L8,12 L18,20 M8,12 H22`，`StrokeThickness=1.8`，18×18 Uniform stretch）。
像素级复现与同尺寸 Chromium SVG 对比：

| 渲染器 | 部分覆盖像素 |
|---|---:|
| Vulkan Impeller（修复前） | 38 |
| Chromium/Edge | 72 |
| Vulkan/D3D12 Vello | 78 |
| D3D12 Impeller | 84 |

根因在 `ImpellerVulkanEngine::EncodeStrokePath`：`preferAnalyticStroke` 已正确识别
icon/control 尺寸并绕过本地 feather mesh，但 legacy tail 的 `UseStencilPath()` 仍优先于
该判定，把 expanded stroke 送进 4× MSAA stencil；于是所谓 analytic 请求最终仍只有少量
离散覆盖等级。D3D12 同场景已经真正进入 `RasterizePathToRects`，所以没有该退化。

修复：合并 `analytic || preferAnalyticStroke` 为 `useAnalyticCoverage`；该条件为真时明确
跳过 Vulkan stencil，使用共享 scanline `PixelRect` 覆盖。大型默认 artwork 继续保留 GPU
stencil 路由，性能策略不变。修复后 Vulkan Impeller 为 84 个部分覆盖像素，与 D3D12
Impeller 的覆盖分布一致，并达到/超过 Chromium 基准。

新增 `GpuSmallIconStrokeRenderingTests`：按 Gallery 的真实 stretch 矩阵、stroke 参数和
4× Path MSAA 设置，在 D3D12/Vulkan × Impeller/Vello 四组合中要求 ≥70 个部分覆盖像素，
同时验证可见面积、实心核心和水平 shaft；相关 stroke/stencil/effect 回归 57/57 通过。

## 十三、直线 Path 的 DrawPolygon 绕过 Vello（2026-09-01）

真实 Gallery 复测发现 Vello 箭头仍是纯二值，而低层 `StrokePath` 回归已是平滑覆盖。
两张截图的原始像素统计给出决定性证据：Vello 图只有背景+箭头 **2 种颜色**，正常图有
**36 个灰度等级**。当前进程加载的 D3D12 DLL 哈希与最新构建一致，排除了旧文件。

根因在更上层：`RenderTargetDrawingContext.DrawPathGeometry` 会把没有曲线的每个
`PathFigure` 专门化为 `DrawPolygon`。返回箭头因此不是一次 `StrokePath`，而是 chevron
和 shaft 两次 `DrawPolygon`：

- D3D12 的 `TryEncodePolygonIntoPendingVello` 要求 encoder 已有工作且新图元与 pending
  bbox 相交；首个/独立 icon 永远不满足，直接落到无 AA 的 triangle/polyline fallback；
- Vulkan 的 solid `DrawPolygon` 同样绕过 active engine，仅 gradient 才回到 `StrokePath`；
- 原回归直接调用 `StrokePath`，因此没有覆盖 UI 的真实专门化分支。

修复：

1. D3D12 小型 polygon Vello 路由允许开启新 scene，不再要求 pending overlap；
2. D3D12 `FillPolygon` 在 Vello 活跃时对 solid/gradient 一视同仁；
3. Vulkan `DrawPolygon` 的 solid/gradient 都重表达为 line-command stream 后交给
   `StrokePath`，复用 active engine、capture fallback、clip sync 与 AA 策略；
4. `GpuSmallIconStrokeRenderingTests` 改为真实的两次 `DrawPolygon`，修复前
   D3D12/Vello、Vulkan/Vello、Vulkan/Impeller 均稳定得到 0 个部分覆盖像素；修复后
   四组合全部 ≥70；`GpuSmallIconPathRenderingTests` 追加真实 `FillPolygon` 四组合。

验证：UI 等价 stroke 4/4、直线 fill + cubic path 8/8，且 Vello compute 诊断出现
`[VkCut]/[VkSpan]`，证明 Vulkan 用例确实经过 compute sub-scene 而非 legacy fallback。

## 十四、小场景 scan 快路 + D3D12 barrier batching（2026-09-03）

前几轮已经把全屏 fine、每段 committed resource 和描述符堆风暴消掉；剩余稳态场景却仍
对每个很小的子场景固定执行 large pathtag 链。MillionScroll 实测每帧 3 个子场景、合计
仅约 113 个 path-tag 字节；Jalium.One 滚动时也几乎全是几十字节的小图标，而上游只有
`path_tag_wgs > 256`（即 tag 流超过约 256 KiB）才需要 reduce2/scan1。

### 修复

1. 移植上游 `pathtag_scan.wgsl` 的 `small` permutation 到
   `vello_pathtag_scan_small.cs.hlsl`，D3D12/Vulkan 在阈值内从
   `reduce → reduce2 → scan1 → large-scan` 改为 `reduce → small-scan`；大场景仍走原链。
   `JALIUM_VELLO_SMALL_SCAN=0` 可在两个后端强制旧链做 A/B/逃生。
2. D3D12 把 22 个 scratch buffer 的同源/同目标 transition 合成一次
   `ResourceBarrier(N, ...)` 调用（状态语义不变），减少每子场景 42 次驱动入口；
   `JALIUM_VELLO_BATCH_BARRIERS=0` 可恢复逐条提交。
3. `JALIUM_VELLO_PERF` 增加 `scan/frame=C/S/L`，可直接证明真实 workload 走了
   CPU/small/large 哪个分支；
   Vello Dispatch 前补 `GpuTimingCategory::Path`，不再把 compute 时间错误归到前一批 SDF。
4. MillionScroll 的 JSON 增加 GPU total/path/SDF/text/bitmap/other 分位数；默认渲染线程
   的 EndDraw worker 补发硬件 timestamp（此前只有 inline 路径发布，默认模式恒 0 样本）。
5. Jalium.One 保持 Vello 为默认，同时支持 `JALIUM_RENDERING_ENGINE=impeller` A/B；
   `JALIUM_FRAME_PERF=1` 每秒输出实际完成帧数，不需要打开 DevTools。

### 结果

Release、相同二进制、MillionScroll 1,000,000 行、120 px/tick、5 s 预热 + 15 s 采样，
small-scan 开/关各两轮的均值如下（FPS 被 16 ms 驱动定时器封顶，故看帧耗时）：

| 指标 | large scan | small scan | 变化 |
|---|---:|---:|---:|
| frame p50 | 4.030 ms | **3.688 ms** | **-8.5%** |
| frame p95 | 5.364 ms | **4.812 ms** | **-10.3%** |
| render p50 | 1.203 ms | **1.089 ms** | **-9.5%** |
| present p50 | 1.505 ms | **1.373 ms** | **-8.8%** |
| GPU total p50（inline timestamp A/B） | 0.321 ms | **0.309 ms** | **-3.5%** |

barrier batching 的反向顺序干净配对中，frame p50 4.374 → **4.157 ms**、render p50
1.254 → **1.158 ms**、present p50 1.767 → **1.534 ms**；另一配对的关闭组受系统抖动
污染而剔除，不用它夸大收益。

真实 Jalium.One（1600×900、打开本仓 `.slnx`、12 s 加载预热 + 同一 12 s 定向滚轮
+ 2 s 收尾；完成帧为整次进程累计，CPU 为滚轮开始后的 14 s 区间）：

| 路径 | 完成帧 | 滚动段进程 CPU |
|---|---:|---:|
| Vello，small/barrier 都关闭 | 2,275 | 2.422 s |
| Vello，默认优化 | **2,300** | **2.062 s** |
| Impeller | 2,328 | 2.031 s |

即本轮让 Vello 的 CPU 消耗下降约 **14.9%**、完成帧增加约 **1.1%**；优化后相对
Impeller 仅少约 **1.2%** 帧、CPU 多约 **1.5%**，该真实滚动场景已基本追平。

### 验证

- fxc/DXBC 与 dxc/SPIR-V 20/20 编译；新 SPIR-V 经 `spirv-val`，反射绑定
  `{0,16,17,48}`、workgroup `256×1×1` 与上游一致；
- native Release/Debug 全目标构建通过；D3D12/Vulkan 的 small 与强制 large 分支均执行；
- Vello/Gradient/小图标像素回归 55/55；
- 扩展 RenderTarget/Backend/Rendering 回归 272/272；
- Vulkan + `VK_LAYER_KHRONOS_validation` 自动滚动 8 s，validation warning/error **0**。

经典 Vello compute 的 GPU p50 在小场景仍高于 Impeller；下一节继续用按 stage timestamp
拆解剩余固定 dispatch，而不是再猜 CPU 侧资源分配。

## 十五、按 stage GPU timestamp + tiny-scene CPU pathtag scan（2026-09-03）

新增 `JALIUM_VELLO_STAGE_PERF=1`：D3D12 Vello 懒创建独立 timestamp query heap，
每个实际执行的 stage 后写一个 query；结果 Resolve 到当前 frame slot，只有该槽 fence
完成、被下一帧复用时才 Map。常态不开启时不创建 query/readback 资源，仅保留一次 false
分支。该探针也在 Debug + D3D12 调试层下跑过资源生命周期回归。

MillionScroll（3 subscene/frame）的稳定区间：

- small scan：stage 合计约 **0.30–0.34 ms/frame**；
- 强制 large scan：约 **0.41–0.47 ms/frame**，直接证明第十四节不是 CPU 假优化；
- 单段热点并不集中：`flatten`/`coarse` 各约 14–16 µs，`fine` 约 10–12 µs，
  small-scan 约 7–8 µs，其余多为 2–7 µs。结论是小场景主要受多次 dispatch 的固定延迟支配。

上游 CPU shader 已提供 pathtag monoid 算法。D3D12 因此新增更短的 tiny-scene 路径：

1. 当 `path_tag_wgs <= 4`（tag 流 ≤4 KiB、monoid 输出 ≤20 KiB）时，CPU 对 packed tag
   word 做完全相同的 exclusive prefix scan；
2. 结果从既有 frame upload arena 分配并一次 CopyBufferRegion 到 tag-monoid buffer；
3. GPU 跳过 `pathtag_reduce + pathtag_scan_small` 两个 stage；5–20 KiB 的拷贝换掉两次
   dispatch/UAV barrier。超过 4 WG 仍走 GPU small scan，超过 256 WG 仍走 large scan；
4. 该路径保留为 opt-in 实验，`JALIUM_VELLO_CPU_TAG_SCAN=1` 开启；真实可见窗口的
   高密度 path/text 交错负载出现回退反馈后，不再把隐藏窗口基准的收益外推为默认策略；
   `JALIUM_VELLO_SMALL_SCAN=0` 优先级更高，会强制整个旧 large 链以便回归。

### A/B 结果

MillionScroll、同二进制、15 s × 两轮反向顺序均值：

| 指标 | GPU small scan | CPU tag scan | 变化 |
|---|---:|---:|---:|
| frame p50 | 2.826 ms | **2.735 ms** | **-3.2%** |
| frame p95 | 4.253 ms | **3.981 ms** | **-6.4%** |
| render p50 | 0.758 ms | **0.733 ms** | **-3.2%** |
| present p50 | 1.085 ms | **1.039 ms** | **-4.3%** |
| Vello CPU/dispatch | 48 µs | **约 42 µs** | **约 -12.5%** |

GPU total p50 两组均约 1.42 ms（差 <1%，噪声级），符合“省的是 command submission
固定成本而非 shader 吞吐”的判断。3-WG PathPerf 的 12 s 进程 CPU 3.266 → **3.062 s**
（-6.2%），证明阈值不只对单-WG 图标有效。

真实 Jalium.One 隐藏窗口同一滚动序列在 opt-in 下变为 **2,314 帧 / 1.859 s CPU**：相对本轮最初旧链
的 2,275 / 2.422 s，完成帧 +1.7%、CPU -23.2%；相对 Impeller 的 2,328 / 2.031 s，
只少约 0.6% 帧且进程 CPU 低约 8.5%。

验证：Release 广泛渲染回归 272/272；opt-in CPU scan、默认 GPU small scan、强制 large scan
均分别执行；Debug + D3D12 调试层 + stage profiler 55/55；Vulkan validation 仍为零发现。

## 十六、密集交错绘制：单工作组归约与纹理池容量（2026-09-05）

继续对照本地 `vello-0.10.0/vello_shaders/shader/pathtag_scan.wgsl`、
`draw_leaf.wgsl` 和 `fine.wgsl`，针对实际 UI 常见的 path/text 交错场景优化，
保留 Vello compute 渲染和原来的覆盖精度。

### 改动

1. **单工作组不再执行无用 reduce**（D3D12/Vulkan）：small pathtag scan 的
   第 0 个工作组只需要单位元前缀，完全不读 `reduced`；当 tag 流 ≤1024 字节时
   省掉 `pathtag_reduce`。`draw_leaf` 同理，draw object ≤256 时省掉 `draw_reduce`。
   多工作组仍正常归约；`JALIUM_VELLO_SMALL_SCAN=0` 仍保留完整 large pathtag 链。
2. **着色器跳过第 0 组的零前缀扫描**：两个 leaf/scan 着色器使用工作组一致的
   `wg_id.x != 0` 分支，跳过全零共享内存扫描及同步；其他工作组仍读取前序归约。
3. **取消每段输出清屏**（D3D12/Vulkan）：fine 本来就覆盖区域内所有像素，包括
   空 tile。为保持溢出语义，`ptcl[0] == ~0u` 分支改为写透明像素再返回，
   不依赖提前清屏。Vulkan 的输出图可能大于当前区域，取消整图 clear 也避免为
   前面大子场景的尺寸反复付费；合成仍限制在当前区域内。
4. **D3D12 输出池按实际分配字节限额**：原池最多 64 张，74 段/帧的固定负载中
   至少 10 张/帧必须重新创建，无法进入零分配稳态。改为最多 **64 MiB / 512 张**，
   字节数使用 `GetResourceAllocationInfo`，包含显存对齐开销。回收仍在帧槽 fence
   完成后进行；池满时让返回的新尺寸替换旧尺寸，避免 resize 后持续 miss。
   `JALIUM_VELLO_PERF` 增加 `output/frame=Nnew/Mreused`，用于确认是否真正复用。

### A/B 证据与适用范围

1600×900 固定渲染探针：367 个混合 fill/stroke path/polygon、456 次文本绘制，
每 5 个路径插入一次有重叠的文本，稳定 **74 个 Vello 子场景/帧**。
同一份 managed 探针，替换本轮修改前/后的 Release native DLL；90 帧预热、
240 帧采样，按 old → new → new → old 顺序执行，计时阶段关闭 profiler。
下表是两轮分位数的平均值，所有采样帧均成功。

| 指标 | 修改前 | 修改后 | 变化 |
|---|---:|---:|---:|
| CPU 绘制记录 p50 | 5.955 ms | 2.130 ms | -64.2% |
| CPU 绘制记录 p95 | 6.697 ms | 2.953 ms | -55.9% |
| 探针帧耗时 p50 | 12.818 ms | 6.827 ms | -46.7% |
| 探针帧耗时 p95 | 13.851 ms | 8.042 ms | -41.9% |
| GPU total p50 | 4.583 ms | 4.217 ms | -8.0% |
| Vello dispatch/帧 | 74 | 74 | 相同工作量 |

这是隐藏 HWND 的固定 GPU 渲染负载，用于隔离管线开销；**不能把耗时倒数当作
实际可见项目 FPS，也不能据此声称已消除用户报告的全部 20–40 FPS 差距**。
CPU 降幅明显大于 GPU 降幅，与消除纹理池溢出后持续分配的方向一致。
独立 profiler 运行确认稳态为 `output/frame=0.0new/74.0reused`，
`reduce` 和 `drawR` 的 GPU stage 样本均为零，证明上述工作确实被移除。

### 回归入口

- `VelloWorkgroupRenderingTests`：D3D12/Vulkan 均覆盖 1、64、128、255、256、257、
  512 个对象，交替实心/EvenOdd 孔洞、颜色和变换；大场景后再次渲染小场景，
  检查前缀、空像素和复用输出。
- 原生 `vello_fine_output_tests`：直接运行已嵌入的 fine DXBC，预置脏纹理，分别
  输入空 tile 和失败毒标；逐 RGBA 通道验证目标区域全部变透明，区域外保持原值。
  配置 `-DJALIUM_D3D12_BUILD_TESTS=ON`，构建 `vello_fine_output_tests` 后运行
  `ctest --test-dir build_x64_vs18 -C Release -R vello_fine_output --output-on-failure`。

验证结果：D3D12/Vulkan 的 Release、Debug 构建通过；DXBC/SPIR-V 各 20 个
stage/permutation 重新生成；SPIR-V 验证与全部
反射绑定/workgroup 检查通过；Release 扩展渲染回归 **408/408**，强制 large scan
分支的新增像素回归 **2/2**，原生 fine 输出测试 **1/1**（同时检查 D3D12 debug
layer 错误）。测试目录 DLL 哈希与本轮构建产物一致。
