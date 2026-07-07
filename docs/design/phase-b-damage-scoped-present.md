# Phase B 终版设计文档：Native Damage-Scoped Present（GPU 工作量正比于 damage 而非窗口面积）

> 版本：v2（吸收对抗式审查）| 分支：`feat/devtools-tree-reveal-and-render-fixes` | 后端：D3D12（`jalium.native.d3d12`）
> 目标读者：native 渲染管线维护者 + managed 侧 `Window.cs` 失效记账维护者
> 本文所有 API/字段/行号均已对照当前代码核实（含审查提出的断言，逐条 Read 验证），未臆造。
> 本文档可直接作为实现依据。

---

## 审查后修订摘要（v1 → v2 因对抗式审查而改动的点）

以下每条对应审查中的一个 blocker/major，标注设计如何吸收。**未解决即标注为"已知风险 + 缓解 + 门控"。**

| # | 审查发现（严重度） | v1 的错误/缺口 | v2 的应对 | 关联章节 |
|---|---|---|---|---|
| R-A | **render-thread 路径下 Phase B 完全失效**（blocker，集成回归） | v1 从未提及 render thread。而 iGPU 上 HWND 窗恰恰走 render thread，它每帧 `SetFullInvalidation` → `frameDamageScoped_` 恒 false → 三处改动全短路，**在目标场景零收益** | **决定性重构**：新增 §0.5。Phase B 首版明确 **`EnableRenderThread=false` 前提**，并给出"damage-scoped inline 为何在 iGPU 上能替代 render-thread-full"的论证与验收；render-thread 的 dirty 管线化列为 Phase B-RT 后续独立子项（当前 `PublishFrameToRenderThread` 无 dirty 通道，已核实 Window.cs:7267/7303） | §0.5, §1.1, §6.2 |
| R-B | **PSS blit 用 `AddBitmap` 是 alpha-blend 非拷贝**（blocker，正确性） | v1 §2.C.3 说"整张 PSS blit opacity=1"，但 `CompositeRetainedLayer`→`AddBitmap`（已核实 :4862）走 bitmap PSO = `SrcBlend=ONE/DestBlend=INV_SRC_ALPHA`（已核实 :765-772）= premult over-blend → blend-over-stale，透明窗越叠越暗/重影 | **改用 `CopyResource`（同格式 R8G8B8A8_UNORM，逐字节 identical）为首选**；若需 quad 路径则用已存在的 `copyBlendPSO_`（`SrcBlend=ONE/DestBlend=ZERO`，已核实 :824-835），**绝不复用 AddBitmap**。透明窗强制 CopyResource 保 straight-alpha | §2.C.3, §2.C.6 |
| R-C | **B2 收益被高估：managed 侧已有 cull + bbox scissor**（blocker + major，正确性/回归） | v1 §0/§2.B 说"每 present 重发所有 batch 覆盖全窗"。实际 partial 帧 `PushDirtyRegionClip`（已核实 Window.cs:8223）已 `PushClipBounds`（Visual.ShouldRenderChild cull，bbox 外元素不 emit）+ `PushClipAliased`（native scissor，已核实 RenderTargetDrawingContext.cs:3089-3097）→ 几乎不存在 bbox 外还在光栅的 batch。B2 对单 bbox 近乎 no-op | **重新定位 B2**：它优化的是"单 bbox clip 无法表达的 L 形/散点 damage 膨胀"，即把 native root scissor 从**单 bbox 改为多 dirty rect 逐 batch 求交**。验收基准场景改为**两个相距很远的小 dirty rect**（caret + 远处 badge），否则斜率法测不出差异。ClearBackground/PushDirtyRegionClip 同步改多矩形 | §0.3, §2.B, §1.2 |
| R-D | **PSS render-to-PSS 与 CaptureSnapshot 时间语义冲突**（blocker，正确性） | v1 §5 把 CaptureSnapshot 统一推迟到 B4，但 §2.C.3 一旦 render-to-PSS，同帧 CaptureSnapshot 的 source 仍是 back buffer（此时是上帧/空）→ backdrop 采到错帧 | **B3 与 B4 不可拆分**：render-to-PSS 模式下 CaptureSnapshot 的 source 立即改为 PSS。且**含 backdrop（DrawBackdropFilter/DrawLiquidGlass）的帧强制 full**（`frameDamageScoped_=false`），PSS 全窗重画后再采样 | §2.C.3, §5 |
| R-E | **B2 剔除破坏 CaptureSnapshot"back buffer 到快照点完整"不变量**（blocker，正确性） | v1 §2.B.3 剔除判据仅按 `batch.scissor∩dirty`，未考虑被保留的 backdrop batch 会采样被剔除 batch 的输出区域 → 玻璃折射缺块。且 CaptureSnapshot 的 flush 内 `inOffscreenCapture_/inRetainedCapture_` 均 false，v1 的 guard 挡不住 | **含 CaptureSnapshot / backdrop consumer 的帧整帧禁用 B2 剔除**（不止 capture 内）。检测本帧存在 snapshot/backdrop batch（或 snapshot-requested flag）时，`frameDamageScoped_=false` 整帧回退 | §2.B.2, §2.B.3, §5 |
| R-F | **B2 + scoped clear + `!hasScissor` 半透明 batch → 非脏区叠加变暗**（major，正确性） | v1 §2.B.1 说 `!hasScissor` batch "保守保留"每帧全窗重发，同时 §2.A 只清脏区 → 半透明全窗 batch blend 到"只清脏区、非脏区是上帧"的目标 → 变暗/重影 | **对 `!hasScissor` 的 batch，scoped 帧强制套 dirty-union scissor**（`RSSetScissorRects` = 多 dirty rect），使其只画脏区、不碰非脏区。与 B2 的 `IntersectsAnyDirtyRect` 复用同一组 dirty rect。绝不允许"保留全窗 batch 重发"+"只清脏区"并存 | §2.B.1, §2.A |
| R-G | **"两帧并集 blit"优化 + 删 RC2/RC4 ring 矛盾**（major，正确性） | v1 §2.C.4 推荐"两帧并集 blit"省采样，但 §3/§6 又说 PSS 上线后删 ring。两帧并集 blit 重新引入 N-buffer 陷阱，删 ring 后无补偿 | **写死二选一**：① PSS 走**全窗 CopyResource** → alternate buffer 每帧拿完整帧 → 可删 ring；② 两帧并集 blit → **必须保留 ring 或等价 native 多帧并集记账**。首版选 ①（正确优先）。ring 删除条件写作 `DirtyHistoryCount>=swapBufferCount-1` 的等价 native 断言，不静默删 | §2.C.4, §3, §6.2 |
| R-H | **PSS 自身重建（EnsureSize）未被 `_fullInvalidation` 覆盖 → 露垃圾**（major，正确性） | v1 §2.0 `frameDamageScoped_ = env && !fullInvalidation && !dirty.empty()` 未含 PSS 自身状态。PSS 因尺寸/DPI 变而 EnsureSize 重建新纹理（COMMON 未清）时 managed `_fullInvalidation` 未必 true → scoped 只画脏区 → 新 PSS 非脏区是垃圾 | 新增 `pssNeedsFullRepaint_` 标志，任何导致 PSS 纹理新建的路径都置位；`frameDamageScoped_` 额外 `&& !pssNeedsFullRepaint_`。`EnsureSize` 须能报告"是否发生重建" | §2.C.2, §4 |
| R-I | **scoped clear 对透明/半透明窗漏清 + 破坏 punch-through**（major，正确性） | v1 §2.A 天真把全窗 `ClearRenderTargetView` 换成脏区 rect 数组。但透明窗（AllowsTransparency/SystemBackdrop≠None）managed partial 清背景走 `PunchTransparentRect`（PRIMITIVE_BLEND_COPY），非 `ClearRenderTargetView` | **scoped `ClearRenderTargetView` 只在不透明背景分支启用**（当前全窗 Clear 分支）；透明窗维持 managed punch 语义，`frameDamageScoped_` 对透明窗的 clear 部分短路 | §2.A |
| R-J | **DPI-only 变化让 PSS 残留 stale-size，且不经任何失效钩子**（blocker，资源生命周期） | v1 §4 只在 OnResize 挂 PSS 失效。但 DPI-only 变化走 `SetDpi`→`SetDpiScale`（已核实 render_target.cpp:2532 转发；renderer 侧只 Reset glyph atlas），且 `Resize` 尺寸不变时提前 return（已核实 render_target.cpp:485）→ OnResize 不触发 → PSS 用旧 dpi 尺寸 blit 到新 dpi back buffer → 错位/拉伸 | 在 renderer `SetDpiScale` 检测 dpiScale 变化时**显式失效 PSS** + 强制下帧 full。新增触碰点 | §4, §7 |
| R-K | **PSS render-to-PSS 与 per-element retained-layer 合成动画共用单槽状态互相摧毁**（blocker，资源生命周期） | v1 §2.C.3 说"复用 BeginRetainedLayerCapture"。但它开头 `if(inRetainedCapture_) return nullptr`（已核实 :4680 语义）单槽守卫 → 帧内 per-element retained realize 被拒 → 合成动画失效；且 `captureRtv_`/`savedScissorStack_` 单槽会被覆盖 | **PSS 绝不复用 `inRetainedCapture_/activeRealizeLayer_/captureRtv_`**。新增独立 `pssActive_ + pssRtv_` RT 重定向路径，不置 `inRetainedCapture_`；`EndRetainedLayerCapture` 在 PSS 模式下恢复 RT 到 `pssRtv_`（当前硬编码回 back buffer :4811 需改） | §2.C.3, §2.C.7 |
| R-L | **PSS 全窗 blit 在 churn（大 dirty）下净收益为负**（major，资源生命周期） | v1 §2.C.4 自认需实测但未给门控。大 dirty 帧 (B) 剔不掉几个 batch，反而多一次全窗采样 → 严格劣于现状 | 给 PSS blit 加**面积自适应门控**：`dirty/全窗 > 阈值（如 0.6）`时本帧放弃 render-to-PSS，直画 back buffer（非脏区靠 FLIP 指针保持），切回帧标 PSS stale。PSS 只在小 dirty 帧启用 | §2.C.4 |
| R-M | **PSS blit 的新 GPU 步骤未被现有 device-lost 门控覆盖**（major，资源生命周期） | v1 §4 说"scoped 帧遇 device-lost 回退全窗"，但 blit 插在 RecordDrawCommands 之后、现有 :1285 门控之后 → device removed 时执行 barrier/blit 可能 AV（uncatchable in .NET） | PSS blit 前**再插一次** `if(!CheckFrameDeviceAlive()){AbortFrame();return DEVICE_LOST;}`；device 恢复首帧强制走 back buffer 直画。照抄 `EndRetainedLayerCapture` 的"GPU 步骤失败则跳过但完成 CPU 状态恢复"结构 | §4 |
| R-N | **B2 cull 放在 loop 顶部会打乱 path-mode resolve 顺序 + 剔除合成图元**（major，正确性/回归） | v1 §2.B.2 把 `continue` 放 for 循环最顶，在 `if(pathActiveOnMsaa) exitPathMode()`（:2919）之前 → 跳过一个非 path batch 会推迟 path resolve 的 z 序；且 SnapshotBlit/PunchRect/LiquidGlass 也会被朴素规则剔除 | **cull 测试移到 `exitPathMode()` 之后**，`pathActiveOnMsaa==true` 时不剔除；**只剔除纯内容类型**（SdfRect/Text/Bitmap/Ellipse/Line），排除 StencilPath/SnapshotBlit/PunchRect/LiquidGlass | §2.B.2, §2.B.3 |
| R-O | **B2 与 mid-frame FlushGraphicsForCompute 的 batches_ 分段生命周期冲突**（major，正确性/回归） | v1 §2.B 假设 RecordDrawCommands 一次遍历完整 batches_。实际 `FlushGraphicsForCompute` 每次 flush 后 **clear batches_**（已核实 :3513-3520）+ 用自己的 `lastFlushSrvBase_` → RecordDrawCommands 只处理"上次 flush 以来"的段。AddSdfRect/AddText 满 instance 自动 flush（:1573/:1643） | B2 cull 判据对**每个 flush 段**同样启用；CaptureSnapshot / backdrop 触发的 flush 段**强制不剔除**（与 R-E 一致）。设计明确"RecordDrawCommands 是 per-segment" | §2.B.2, §2.B.4 |
| R-P | **PSS 退役 fence-gated 滞留 + churn VRAM 膨胀**（major，资源生命周期） | v1 未计 PSS 退役滞留。共享 backend graveyard + 每 renderer 自有 fence → PSS 退役标可能"借"到高 fence 值，本窗口空闲时卡在墓地 | ① PSS 按整数 bucket / next-pow2 分配（复用 bitmap-downscale-cache 思路），resize 连续拖拽命中同 bucket → EnsureSize 短路不退役；② DPI 抖动加滞后阈值；③ §8 VRAM 预算显式计"每窗口最多 2 张 PSS 同时在墓地" | §2.C.2, §8 |
| R-Q | **PSS 未纳入 Shutdown 清理链**（minor，资源生命周期） | v1 §4 只处理 resize/device-lost，漏正常 Shutdown → 裸 `new` 的 pssLayer_ 泄漏或走错销毁 | `DirectRenderer::Shutdown` 的 GPU-idle wait 之后加 `DestroyRetainedLayer(pssLayer_)`（复用现成双路）。§7 触碰点新增一行 | §4, §7 |
| R-R | **PSS state_ 与 implicit-decay / AbortFrame 脱节**（minor，资源生命周期） | v1 未处理 PSS 每帧 RT↔SRV 往返在跳帧/abort 后的状态机脱节（D3D12 implicit decay 回 COMMON） | BeginFrame 进入 PSS 前不信任遗留 `layer->State()`，按 decay 规则校准；`AbortFrame` 补一行复位 PSS state 为 COMMON | §2.C.7, §4 |
| R-S | **删 RC2/RC4 ring 未验证非 D3D12 后端不消费**（minor，回归） | v1 §3 在共享 `Window.cs` 删 ring，未说明 Vulkan/Software 是否读 `_dirtyHistory` | 已核实 Vulkan 走自包含 `frameRetainImage_`（不读 `_dirtyHistory`）、Software 走 GDI。删除**门控为仅 PSS(=2) D3D12 路径**；PSS off 或 backend≠D3D12 时保留 ring，present cadence 不变 | §3 |
| R-T | **env-off 等价性须机械保证而非口头**（minor，回归） | v1 §9 断言 env=0 bit-identical，但 §7 触碰点改的是每帧都跑的共享代码（:1039 clear、:2847 loop） | 三处改动结构化为 `if(frameDamageScoped_){新路}else{逐字节旧代码}`，`frameDamageScoped_` 是唯一门（env=0 时恒 false）。debug 断言：env unset 时 `frameDirtyRects_` 恒空、`frameDamageScoped_` 恒 false（所有帧型） | §6.1, §9 |
| R-U | **B0 传入机制描述不精确 + inflate 重复风险**（major + minor，回归/正确性） | v1 §2.0 建议扩 BeginFrame 签名并说"须新管线"，但 inline partial 路径 dirtyRects_ 在 BeginDraw 前已灌好（已核实 Window.cs:8223 在 :8218 之前顺序）；且 `fullInvalidation_` BeginDraw 已传 BeginFrame。managed `AddDirtyRect` 已 inflate（DPI margin） | `frameDamageScoped_` 严格 `= env && !fullInvalidation_ && !dirtyRects_.empty()`，**在 BeginFrame 内用已传入的 `fullInvalidation_` 参数求值，inline 路径无需新管线**；DIP→px 复用现成 clamp（:1361-1370），**不二次 inflate**。debug 断言 scoped 帧不与 fullInvalidation_ 共存 | §2.0 |
| R-V | **B4 CaptureSnapshot→PSS 幽灵内容风险**（minor，正确性） | v1 §5 第2点让 backdrop 采 PSS，但 PSS 非脏区是上帧内容 → 大核模糊会把幽灵内容折射进去 | **B4 直接降级为"不实施"**：保留 CaptureSnapshot 每帧全窗 CopyResource back buffer（本有 `snapshotValid_+drawOrder_` 快速跳过），PSS 与 snapshot 各管各的。R5 风险移出范围 | §5 |
| R-W | **StencilPath 保留但 resolve blit 覆盖非脏区 → 半透明 path 叠加变暗**（minor，正确性） | v1 §2.B.3 StencilPath 不剔除，但其 `exitPathMode` resolve blit 默认覆盖全 pathContent（:2867 viewport） | scoped 帧下 StencilPath 的默认 scissor（:2861-2869 全 pathContent 分支）收窄为 `dirty union`（或 `path.scissor∩dirty`），使 resolve cover 只落脏区 | §2.B.3 |

> **v2 的三条总纲变化（相对 v1）**：
> 1. **首版目标场景从"iGPU HWND 窗"收窄为"`EnableRenderThread=false` 的 inline 路径"**（R-A）。render-thread 的 damage-scoped 是独立后续子项。
> 2. **B2 从"砍全窗重发"重新定位为"砍单 bbox→多矩形 union 的膨胀差"**（R-C），基准场景改散点 dirty。
> 3. **PSS(B3) 的 blit 从 AddBitmap 改 CopyResource（R-B）、全窗 blit 求正确不做两帧并集（R-G）、面积自适应门控（R-L）、独立 RT 状态不复用 retained-capture 单槽（R-K）、与 backdrop 强制 full 绑定（R-D/R-E）**。PSS 仍为高风险后续，仅在 B2 后 alternate-buffer 残影仍在时才上。

---

## 0. 问题陈述（一句话）

弱 AMD 核显上，hover 一个导航项（极小 dirty rect）触发全窗 present，成本正比于**窗口面积**而非 **damage 面积**：757×484≈7fps/84%，最大化（面积 4 倍）≈2-3fps/129%，空闲 0%。

根因（native 每 present 全窗处理，三条独立证据链）：

| 证据 | 位置 | 后果 |
|---|---|---|
| 每帧全窗 `ClearRenderTargetView(rtv, clearColor, 0, nullptr)`（无 rect 数组） | `d3d12_direct_renderer.cpp:1041` | GPU ROP 清整个 back buffer |
| 每帧 `batches_.clear()` + `RecordDrawCommands()` 重录/重发（每 flush 段的）batch | `:1068` / `:1278` / `:2540` | 顶点+像素光栅 |
| dirty rect 仅喂 `Present1` 的 `DXGI_PRESENT_PARAMETERS` | `:1356-1394` | 只优化 DWM flip 拷贝，**不减 GPU 光栅** |

managed 侧 subtree-boundary retained layer 已实测撬不动此问题：床在 native 每 present 全窗处理。

### 0.3 重要校正（审查 R-C）：partial 帧上"重发所有 batch 覆盖全窗"是不准确的

**已核实**：hover 走的 partial 路径中，managed 端 `PushDirtyRegionClip`（Window.cs:8223）已做两件事：
- `PushClipBounds`（RenderTargetDrawingContext.cs:3089）→ `Visual.ShouldRenderChild`（Visual.cs:774，return 在 :818 `clipBounds.IntersectsWith(childBounds)`）把整棵树剔除到 dirty **bbox**：bbox 外的元素根本不 emit `AddSdfRect/AddText`。
- `PushClipAliased`（RenderTargetDrawingContext.cs:3097）→ 压入 native scissor（dirty bbox 落 `scissorStack_` 底），本帧每个 batch 的 `hasScissor` 因此**几乎恒 true** 且 `batch.scissor ⊆ bbox`（`PushScissor` 与父求交，:1983-1989）。

**推论**：
1. native 侧**几乎不存在"bbox 外还在光栅的 batch"**可供再剔除。B2 对**单个矩形 dirty** 近乎 no-op。
2. 真正残余成本来自：多个分散 dirty rect 被 `aggregator.GetBoundingBox()` collapse 成**一个大 bbox**（Window.cs:8207），tree 被 clip 到大 bbox → **L 形 / 散点 damage 的 bbox 膨胀区被全部重画**。
3. 因此 **B2 的正确定位**（见 §2.B）：把 native root scissor 从"单 bbox"改为"按多 dirty rect 逐 batch 求交剔除"，砍掉 bbox 膨胀区。**不是**砍"全窗重发"。

这直接改写 §1.2 的验收方法（基准场景必须用散点 dirty，见 §1.2）。

---

## 0.5 决定性前提（审查 R-A）：render-thread 路径与 Phase B 的关系

**已核实的硬事实**：
- iGPU 上的普通 HWND 窗（非 DComp）在 `EnableRenderThread` 时启用 render thread（Window.cs:7048-7064，门控 `ShouldUseCompositionRenderTarget()==false && _platformWindow==null`）——**正是 Phase B 的目标机型**。
- render thread 激活时，`RenderFrame` 在 inline 绘制代码**之前**发布帧并 return（Window.cs:7942-7951 `if(rtActive){ ...PublishFrameToRenderThread(); return; }`）。
- render thread 在每次 `BeginDraw` 前调 `SetFullInvalidation`（Window.cs:7944-7946 注释明证："full-present-every-frame ... calls SetFullInvalidation before each BeginDraw"）→ native `fullInvalidation_` **恒 true** → 本设计 `frameDamageScoped_ = env && !fullInvalidation_ && ...` **恒 false** → scoped clear (A) / batch cull (B) / PSS (C) **全部短路到原全窗路径**。
- `PublishFrameToRenderThread`（Window.cs:7267）/ `PresentCaptureOnRenderThread`（:7136）**无任何 dirty-rect 通道**（已核实 :7303 `Render(recorder)` 全量走 record cache host，不携 dirty）。

**结论**：若不处理，Phase B **在启用 render thread 的目标配置下零收益**。§1.1 的量化目标在该配置下不可达。

**Phase B 的决策（首版）**：

- **首版 Phase B 明确以 `EnableRenderThread=false`（inline 路径）为前提**，`JALIUM_DAMAGE_SCOPED` 仅在 inline 路径生效。理由与验收：
  - render thread 当初是为"~110ms iGPU present 不阻塞消息泵"而建。它把 present **移到另一线程**来隐藏成本，但**总 GPU 成本不变**（仍全窗）。
  - Phase B 的 inline damage-scoped 是**从根上把 GPU 成本降下来**（正比 damage）——present 不再需要"移走"来隐藏，因为它本身就快了。
  - **验收（§1.2 面积-成本斜率法）**：在 iGPU 上，`EnableRenderThread=false + JALIUM_DAMAGE_SCOPED=1` 的 inline present 成本，对比 `EnableRenderThread=true`（render-thread-full）。判据：inline-scoped 的 hover 帧 present 不再阻塞泵（因为 GPU 工作掉了，不是因为换了线程），且 GPU% 显著低于 render-thread-full。**必须实测证明** inline-scoped 优于 render-thread-full，才落地首版前提。

- **Phase B-RT（后续独立子项，本文档不实现）**：把 per-frame dirty union 管线化穿过 `PublishFrameToRenderThread → PresentCaptureOnRenderThread`，让 render thread 的 `BeginDraw/EndDraw` 也 damage-scoped（停止无条件 `SetFullInvalidation`）。这是较大改动（涉及跨线程 `_drawingContext` 独占契约，见 Window.cs:7934-7941 的 UAF 警告），**列为 Phase B 之后的独立设计**。

- **门控**：`JALIUM_DAMAGE_SCOPED` 生效时，若检测到 render thread 活跃（`_rtActive`），managed 侧应：① 首版直接**不走 scoped**（等价 env off），或 ② 由用户显式 `EnableRenderThread=false` 后再启用 scoped。文档在 §6 写明这一互斥。

---

## 1. 目标与验收标准

### 1.1 量化目标

以 **1600×1000 最大化窗口 + `EnableRenderThread=false`（inline 路径）+ 散点 dirty（见下）** 为基准场景：

| 指标 | 当前基线 | Phase B 目标 | 说明 |
|---|---|---|---|
| present 率 | 2-3 fps | ≥ 30 fps（受 present-pacing 封顶） | 与刷新率封顶后一致 |
| GPU 占用 | 129% | ≤ 25% | GPU-bound 解除 |
| 空闲 GPU | 0% | 0%（不回归） | 无空转 |
| 全窗帧（resize/首帧/主题切换/backdrop 帧）GPU | 100% | 100%（**不劣化**） | full-invalidation 路径保持原样 |
| 逐像素输出 | 基线 | **bit-identical**（damage 内） | 见 1.3 正确性红线 |

> **前提变化（R-A）**：基准场景加 `EnableRenderThread=false`。render-thread-full 与 inline-scoped 的对比见 §0.5。
> **基准场景 dirty 形态变化（R-C）**：由"单个 hover 项"改为"**两个相距很远的小 dirty rect**"（如左上 caret + 右下 badge），否则 B2 相对已有 managed cull 无增量、斜率法测不出差异。

> **present 率上限说明**：present-pacing 事件驱动机制（MEMORY: `project_composition_target_1ms_freespin_regression`）已按刷新率封顶 tick。Phase B 不改 pacing；30fps 是"GPU 不再是瓶颈后由 pacing/vsync 决定"的自然上限。**验收看 GPU% 与是否 GPU-bound，不是绝对 fps。**

### 1.2 A/B 归因法（量化 GPU 工作 vs damage 挂钩）

利用已有 GPU timing 仪表（`timingSupported_` / `MarkGpuTimingPoint` / DevTools Perf 面板，`EndFrame:1305-1319`）：

1. **面积-成本斜率法**（核心归因，R-C 修正后）：
   - 固定**散点 dirty 动作**（两个远隔小 rect），测三种窗口尺寸（757×484 / 1131×726≈2× / 1600×1000≈4×）的 GPU frame time。
   - **判据**：`JALIUM_DAMAGE_SCOPED=0`（单 bbox clip，膨胀区被重画）时 GPU time 随两 rect 的 **bbox 面积**（≈全窗，因两 rect 远隔）上升；`=1`（多矩形逐 batch 剔除）时 GPU time 只随**两 rect 自身面积之和**变 → 对窗口面积斜率趋近 0。
   - **关键**：**必须用散点 dirty**。单个 dirty rect 下 bbox≈union，`=0` 与 `=1` 几乎无差异（因为 managed cull 已把 bbox 外剔光），会得出"B2 无效"的**正确但误导**的结论。

2. **render-thread 对比法**（R-A，首版前提验收）：
   - iGPU 上 `EnableRenderThread=true`（render-thread-full）vs `EnableRenderThread=false + JALIUM_DAMAGE_SCOPED=1`（inline-scoped）。
   - 判据：inline-scoped 的 present 不阻塞消息泵（输入延迟 ≤ render-thread 路径）且 GPU% 显著更低。

3. **kill-switch A/B**（回归护栏）：
   - 同一 session 内 `JALIUM_DAMAGE_SCOPED` 0↔1 热切换（经 DevTools，见 §6），对比 GPU%/present/逐像素截图。full-invalidation 帧两路必须逐像素一致。

4. **GPU category breakdown**：DevTools 已有 per-category（SdfRect/Text/Bitmap/Path…）GPU 分解。Phase B 后散点 dirty 帧的 category 时间应随 batch 剔除数量下降；PSS blit（若启用）计入 `Bitmap`/copy category。

### 1.3 正确性红线（任何阶段不得违反）

- **R1 半透明正确**：被剔除/被 PSS-blit 覆盖的区域，其 alpha 合成结果必须与全窗重绘 bit-identical。半透明 batch 跨脏区边界时**宁可不剔除**（保守）。
- **R2 painter's z 序**：剔除不得打乱 `batches_` 的绘制顺序（`sortOrder`）；被保留的 batch 相对顺序不变；**不得扰动 path-mode resolve/blit 的时点**（R-N）。
- **R3 裁剪交叠**：rounded-clip / scissor 与 dirty union 求交必须在**同一坐标系**（physical-pixel，post-DPI）。
- **R4 无 stale**：device-lost/resize/**DPI-only 变化**/PSS 自身重建后 PSS 纹理不得残留旧内容（§4，R-H/R-J/R-R）。
- **R5 backdrop 一致**：Mica/Acrylic/LiquidGlass 取样结果不因 Phase B 改变（§5，R-D/R-E/R-V）。**含 backdrop 的帧一律走 full 路径。**
- **R6 合成图元不剔除**：SnapshotBlit/PunchRect/LiquidGlass/StencilPath batch 永不参与 B2 剔除（R-N/R-E）。

---

## 2. 三处改动的具体设计

三处改动**独立可开关、按 A→B→C 顺序落地**，每步单独可验证。核心新增数据：一个 **frame-level dirty union（physical-pixel `D3D12_RECT`）** 贯穿三步。

### 2.0 公共基础：frame-level dirty union 的传入（审查 R-U 修正）

当前 `dirtyRects_`（DIP 坐标，`d3d12_render_target.cpp`）只在 `EndDraw` 末尾转成 physical-pixel 喂 `Present1`（:742-758）。record 期（`RecordDrawCommands`）拿不到 dirty 信息。

**关键校正（R-U）**：**inline partial 路径无需新管线**。已核实：
- managed `AddDirtyRect` 在 `TryBeginDrawOrScheduleRetry→BeginDraw`（Window.cs:8218）**之前**灌好（Window.cs:8200-8205 的 AddDirtyRect 循环在 :8223 PushDirtyRegionClip 之前）→ native `dirtyRects_` 在 `BeginFrame` 运行时**已 populated**。
- `fullInvalidation_` 已由 `BeginDraw` 传给 `BeginFrame`（d3d12_render_target.cpp:648）。
- full/promote 路径调 `SetFullInvalidation()` 且 `AddDirtyRect` 在 `fullInvalidation_` 时提前 return（d3d12_render_target.cpp:2594）→ full 帧 `dirtyRects_` 为空。

**新增（native `D3D12DirectRenderer`）**：

```cpp
// d3d12_direct_renderer.h 新增成员（放在 snapshot 资源附近，~:1067 区块）
std::vector<D3D12_RECT> frameDirtyRects_;   // physical-pixel, this frame; empty ⇒ full
D3D12_RECT              frameDirtyUnion_{};  // physical-pixel bbox of all dirty rects
bool                    frameDamageScoped_ = false; // this frame runs the scoped path
```

**求值（在 `BeginFrame` 内，用已传入的 `fullInvalidation_` 参数，无需扩签名）**：

```cpp
// BeginFrame 入口（:1039 之前）
frameDirtyRects_.clear();
// DIP→physical-px：复用 EndFrame:1361-1370 现成 clamp+取整，NOT 二次 inflate
//（margin 已由 managed AddDirtyRect 加过，见 Window.cs aggregator.Inflate）
if (envDamageScoped_ && !fullInvalidation && !dirtyRects_.empty()
    && !frameHasBackdropConsumer_ /* R-E/R-D，见 §5 */
    && !pssNeedsFullRepaint_      /* R-H，仅 PSS 阶段相关 */) {
    // 把 dirtyRects_（DIP）转 physical-px 填入 frameDirtyRects_，算 frameDirtyUnion_
    frameDamageScoped_ = true;
} else {
    frameDamageScoped_ = false;
}
#ifndef NDEBUG
assert(!(frameDamageScoped_ && fullInvalidation)); // scoped 绝不与 full 共存
#endif
```

- full-invalidation 帧：`frameDamageScoped_=false`，`frameDirtyRects_` 空 → 三处改动全部短路回原路（R-T：env=0 时 `frameDirtyRects_` 恒空、`frameDamageScoped_` 恒 false，机械保证）。
- **透明窗**：clear 部分对透明窗短路（§2.A，R-I）；但 B2 剔除对透明窗仍可用（剔除不改 alpha 语义）。透明窗判定由 native 感知 `alphaMode`/punch 需求（见 §2.A）。

**触碰点**（R-U 简化后）：
- `d3d12_direct_renderer.h:~1067` — 新成员 `frameDirtyRects_`/`frameDirtyUnion_`/`frameDamageScoped_`/`envDamageScoped_`/`frameHasBackdropConsumer_`（+ PSS 阶段的 `pssNeedsFullRepaint_`）。
- `d3d12_direct_renderer.cpp` BeginFrame 入口（:1039 之前）— 上述求值 + DIP→px 转换（复用 :1361-1370）。
- **无需扩 BeginFrame 签名**（`fullInvalidation` 已是参数，:648）；**无需新 P/Invoke**（dirtyRects_ 已在 native）。
- RC4-c 三通道失效记账（`PrevPaintedBounds`/`LastDirtyBounds`/pre-post-layout）**保留**——它决定 `ComputeDirtyRegions` 精度，直接影响 B2 剔除准确性。

---

### 2.A Scoped Clear —— 只清脏区（审查 R-I 修正）

**现状**：`d3d12_direct_renderer.cpp:1039-1042` 全窗清（第 3/4 参数 `0, nullptr`）。

**设计**：`ClearRenderTargetView` 原生支持 rect 数组。damage-scoped 帧改为只清 dirty rects，**但仅限不透明背景**：

```cpp
// d3d12_direct_renderer.cpp:1039 区块替换（结构化，R-T：else 分支逐字节旧代码）
if (clear) {
    float clearColor[4] = { clearR, clearG, clearB, clearA };
    // R-I：透明/半透明窗（AllowsTransparency / SystemBackdrop≠None）的 partial 清背景
    // 由 managed PunchTransparentRect（PRIMITIVE_BLEND_COPY）承担，绝不在此引入
    // scoped ClearRenderTargetView（会覆盖 punch 孔、破坏 WebView/DComp 合成透明）。
    // 仅当当前走的是"不透明全窗 Clear"分支时才 scoped。
    bool opaqueClear = /* alphaMode==Opaque 且 clearA==1（即现全窗 Clear 条件） */;
    if (frameDamageScoped_ && opaqueClear && !frameDirtyRects_.empty()) {
        commandList_->ClearRenderTargetView(
            rtvHandle, clearColor,
            (UINT)frameDirtyRects_.size(),
            frameDirtyRects_.data());
    } else {
        commandList_->ClearRenderTargetView(rtvHandle, clearColor, 0, nullptr); // 逐字节旧路
    }
}
```

**约束与正确性**：
- `pRects` physical-pixel、clamp 到 `[0,width)×[0,height)`（复用 :1361-1370 clamp）。
- **透明窗短路（R-I）**：`opaqueClear==false` 时永远走全窗旧路（或由 managed punch 处理），维持 straight-alpha / punch-through 语义。
- **(A) 的意义主要是"让 clear 不再抵消 (C) 的 PSS 复用"**——若 (C) 上线后仍全窗清，PSS 保留的非脏区会被清掉。故 **(A) 是 (C) 的必要前置**，独立收益小但必须先做。
- 单独价值有限：clear 本身在弱核显上不是最大头。真正主力是 (B) 的 scissor 收窄 + (C)。

**与 R-F 的联动**：(A) 只清脏区后，(B) 中被保留的 `!hasScissor` 半透明 batch 若仍全窗 blend 会污染非脏区 → **(B) 必须给这些 batch 套 dirty scissor**（§2.B.1），二者绑定。

**触碰点**：`d3d12_direct_renderer.cpp:1039-1042`（+ 感知 `alphaMode` 的 `opaqueClear` 判定）。

---

### 2.B Record 期 Batch 剔除 —— 按多 dirty rect 逐 batch 求交（审查 R-C/R-E/R-F/R-N/R-O/R-W 全面修正）

这是**真正减 GPU 光栅**的核心改动。**定位（R-C）：优化"单 bbox clip 无法表达的 L 形/散点 damage 膨胀"，把 native root scissor 从单 bbox 改为按多 dirty rect 逐 batch 求交剔除。不是"砍全窗重发"。**

#### 2.B.1 batch bounds 从哪来 + `!hasScissor` 的处理（R-F）

**`batch.scissor`（physical-pixel、transform-aware、parent-intersected）** 是主上界，已核实：
- `PushScissor`（:1946-1991）把 clip rect 经当前 transform 4 角变换求 AABB，`floor/ceil` 到 physical-pixel，且**与父 scissor 求交**（:1983-1989）。
- 每个 batch append 时快照 `candidate.hasScissor = !scissorStack_.empty(); candidate.scissor = scissorStack_.top();`（:1611-1612 等 6 处）。
- **partial 帧下 `hasScissor` 几乎恒 true**（R-C：managed `PushClipAliased` 把 dirty bbox 压 scissor 栈底），故 `!hasScissor` 在 partial 帧罕见（仅极少数在 root clip 之外 emit 的 batch）。

剔除判据（主路径）：

```
if (batch.hasScissor)  →  cullKey = batch.scissor（紧上界，直接可用）
if (!batch.hasScissor) →  见下 R-F 处理
```

**`!hasScissor` batch 的处理（R-F，关键）**：v1 的"保守保留每帧全窗重发"与 (A) 只清脏区并存 → 半透明全窗 batch blend 到非脏区 → 变暗/重影。**修正**：

- scoped 帧下，对 `!hasScissor` 的 batch **强制套上 dirty-union 的 scissor**：绘制该 batch 前 `RSSetScissorRects(count, frameDirtyRects_.data())`（多 dirty rect），使其**只画脏区、不碰非脏区**。
- 这样"保留它"就无害（它只在脏区内 blend，非脏区不动）。
- 与 B2 的 `IntersectsAnyDirtyRect` 复用同一组 `frameDirtyRects_`。
- **绝不允许"保留全窗 batch 重发"+"只清脏区"并存。**

**可选增强（阶段 B2b，非首版）**：对 `!hasScissor` 的 `SdfRect/Bitmap/Text` batch 用 `ComputeBatchBoundsFromInstances()` 遍历实例算紧界参与剔除。字段齐备（`SdfRectInstance:91` 等），仅 CPU 成本。首版不做（partial 帧下 `!hasScissor` 本就罕见）。

#### 2.B.2 与 dirty union 求交 + cull 放置点（R-N/R-E/R-O）

**RecordDrawCommands 是 per-flush-segment（R-O，已核实）**：`FlushGraphicsForCompute`（:3490）每次 flush 后 `batches_.clear()`（:3513-3520），用自己的 `lastFlushSrvBase_`。故 cull 判据对**每个 flush 段**同样启用；`AddSdfRect/AddText` 满 instance 自动 flush（:1573/:1643）也各自走一遍。

**cull 放置点（R-N，关键）**：`continue` **不能**放 for 循环最顶——会跳过 `if(pathActiveOnMsaa) exitPathMode()`（:2919），推迟 path resolve 的 z 序。**移到 `exitPathMode()` 之后**：

```cpp
// RecordDrawCommands 主循环（d3d12_direct_renderer.cpp:2847 for 循环内）
for (size_t batchIdx = 0; batchIdx < batches_.size(); batchIdx++) {
    const auto& batch = batches_[batchIdx];

    // ① path-mode resolve/blit 时点绝不因剔除而扰动（R-N）
    if (pathActiveOnMsaa && batch.type != DrawBatchType::StencilPath) {
        exitPathMode(/* ... */);   // 原逻辑，位置不变（:2919）
    }

    // ② Phase B damage cull —— 放在 exitPathMode 之后（R-N）
    if (frameDamageScoped_ && !inOffscreenCapture_ && !inRetainedCapture_
        && !pathActiveOnMsaa                         // path 进行中不剔除（R-N）
        && IsCullableContentType(batch.type)) {      // 只剔纯内容类型（R-N/R-E/R-6）
        if (batch.hasScissor && !IntersectsAnyDirtyRect(batch.scissor)) {
            continue;   // 完全在脏区外 → 跳过 Draw（真省顶点/像素）
        }
        // !hasScissor：套 dirty scissor 后保留（R-F），不 continue
    }
    // ... 原有 StencilPath / PSO / scissor / draw 逻辑不变 ...
}
```

其中：

```cpp
// R-N/R-E/R-6：只剔除纯内容类型，排除所有合成/特殊图元
static bool IsCullableContentType(DrawBatchType t) {
    switch (t) {
        case DrawBatchType::SdfRect:
        case DrawBatchType::Text:
        case DrawBatchType::Bitmap:
        case DrawBatchType::Ellipse:
        case DrawBatchType::Line:
            return true;
        // 排除：StencilPath（path-mode 状态机）、SnapshotBlit / PunchRect /
        //       LiquidGlass（合成图元，剔除会丢 backdrop composite，R-E/R-N）
        default:
            return false;
    }
}
```

`IntersectsAnyDirtyRect`：遍历 `frameDirtyRects_`（≤32），任一相交即 true。可先用 `frameDirtyUnion_` 快速否定再多 rect 精判。总成本 O(batch×32) 纯整数比较，可忽略。

**含 backdrop / CaptureSnapshot 的帧整帧禁用剔除（R-E，关键）**：CaptureSnapshot 的 flush 内 `inOffscreenCapture_/inRetainedCapture_` 均 false，上面的 guard 挡不住它剔除快照下的底层 batch。**修正在 §2.0/§5**：这类帧 `frameHasBackdropConsumer_=true` → `frameDamageScoped_=false` → 整帧不剔除。（比在 record 里加特判更稳。）

#### 2.B.3 正确性论证（对照 §1.3 红线）

- **R1 半透明**：被剔除的 batch 其 scissor 与所有 dirty rect 无交集 → 不写任何 dirty 像素。跨界半透明 batch scissor 与某 dirty rect 相交 → 不剔除 → 正常重画 → alpha 正确。`!hasScissor` 半透明 batch 套 dirty scissor 后只在脏区 blend（R-F）。
- **R2 z 序**：`continue` 只跳 draw 不改顺序；cull 在 `exitPathMode` 之后（R-N）→ path resolve 时点不变。
- **R3 坐标系**：`batch.scissor` 与 `frameDirtyRects_` 均 physical-pixel post-DPI。
- **R6 合成图元**：`IsCullableContentType` 排除 StencilPath/SnapshotBlit/PunchRect/LiquidGlass（R-E/R-N）。
- **path/stencil（R-W）**：`StencilPath` 一律不剔除（`IsCullableContentType` 已排除）。但被保留的 StencilPath 的 `exitPathMode` resolve blit 默认覆盖全 pathContent（:2867 viewport）→ scoped 帧半透明 path 会污染非脏区。**修正**：scoped 帧下 StencilPath 的默认 scissor（:2861-2869 全 pathContent 分支）收窄为 `dirty union`（或 `path.scissor∩dirty`），使 resolve cover 只落脏区。
- **backdrop 依赖（R-E）**：含 CaptureSnapshot/backdrop 的帧整帧 full，不剔除（§5）。

#### 2.B.4 独立验证

`JALIUM_DAMAGE_SCOPED=1`，**散点 dirty**（两个远隔小 rect）。DevTools：
- 加 `culledBatchCount_` 计数暴露（scoped 帧被剔的 batch 数）；`batchCountAtFinalize`（:1316）对比。散点 dirty 下 `culledBatchCount_` 应显著 > 0（bbox 膨胀区的 batch 被剔）。
- GPU category time（SdfRect/Text/Bitmap）随剔除数下降。
- **逐像素**：dirty 区外与 (A)+flip-model 一致；dirty 区内 bit-identical。用 computer-use 截图对比 `=0` 基线。
- **回归**：含 Acrylic 侧栏的 caret blink 帧（R-E 场景）必须逐像素等于 full 路（因为该帧被强制 full）。

**触碰点**：`d3d12_direct_renderer.cpp:2847`（循环入口，cull 在 exitPathMode 之后）；新增 `IntersectsAnyDirtyRect` / `IsCullableContentType` helper；`!hasScissor` 套 dirty scissor（`RSSetScissorRects`）；StencilPath 默认 scissor 收窄（:2861-2869）；`d3d12_direct_renderer.h` 加 `culledBatchCount_`（可选）。

---

### 2.C PSS（Persistent Scene Surface）—— 持久场景表面（审查 R-B/R-D/R-G/R-H/R-K/R-L/R-M/R-R 全面修正）

> **定位**：(B) 让"脏区外 batch 不重发"，但脏区外像素靠什么"还在"？两条路：① 纯靠 FLIP_SEQUENTIAL 指针保持 + 现有 RC2/RC4 ring；② PSS 持久纹理。**首版优先 ① + (A) + (B)，PSS 作为 ② 高风险后续增强，仅在 B2 后 alternate-buffer 残影仍在时才上。**

#### 2.C.1 flip-model 已保持非脏区，为何还要 PSS？

已核实：`DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL`（d3d12_render_target.cpp:354，注释 :343 明言依赖 back buffer 保持前帧内容）+ `BufferCount≥2` → 非脏区跨帧字节保持。

**(A)+(B) 单独基本能工作，但有 N-buffer 陷阱**：`FLIP_SEQUENTIAL` 有 `BufferCount` 个 back buffer 轮转，第 N+1 帧轮到的 buffer 对应区域是**两帧前**内容。这正是 managed **RC2/RC4 dirty-history ring**（Window.cs:8419 `SeedDirtyHistoryFullWindow` / :8432 `CommitDirtyHistory` / :8593 `BuildFlushAggregator` / `HandlePresentedFrameFlush`）在解决的：给每个 alternate buffer 补 flush。

**PSS 的价值 = 消灭 N-buffer 陷阱**：PSS 是**单张**窗口大小持久纹理，存"上帧完整合成结果"，与 back buffer 轮转无关。alternate buffer 永远从 PSS 拿完整帧 → RC2/RC4 flush ring 可删（前提：**全窗 blit**，见 R-G）。

#### 2.C.2 PSS 纹理：分配/格式/尺寸（R-H/R-P）

复用 `D3D12RetainedLayer` 承载（已验证签名/生命周期/device 守卫/fence-gated retire）：

- **格式**：固定 `swapChainFormat_`（`R8G8B8A8_UNORM`，:4706）——与 back buffer 一致，**CopyResource 无转换**（R-B）。
- **尺寸**：`ceil(dipW*dpiScale_) × ceil(dipH*dpiScale_)` = 全窗 physical-pixel。
- **bucket 分配（R-P）**：PSS 按整数 bucket / next-pow2 分配（复用 MEMORY `project_bitmap_downscale_cache` 的 next-pow2 bucket 思路），窗口连续拖拽 resize 命中同 bucket → `EnsureSize` same-size 短路（:54）不退役 → 减 churn VRAM。
- **新成员**：`D3D12RetainedLayer* pssLayer_ = nullptr; bool pssValid_=false; bool pssNeedsFullRepaint_=false; bool pssActive_=false;`（h，snapshot 资源附近）。
- **`pssNeedsFullRepaint_`（R-H，关键）**：任何导致 PSS 纹理**新建**的路径（`EnsureSize` 报告"尺寸/格式变了"、首次分配、device 换、DPI 变）都置位。`EnsureSize` 须**返回"是否发生重建"**供 renderer 感知（当前 `d3d12_retained_layer.h:54` 只短路/重建，需加返回值）。`frameDamageScoped_` 已在 §2.0 额外 `&& !pssNeedsFullRepaint_` → PSS 需全画时本帧强制退回全窗直画并清标志。

> **注意 `BeginRetainedLayerCapture` 会 clear 整层为 (0,0,0,0)**（:4729-4730）。PSS 需"保留上帧"→ **不能用默认 clear**，需"不清、只重定向 RT + scoped-clear dirty 区"的变体 `BeginPersistentSurface`（见 §2.C.7）。

#### 2.C.3 render 到 PSS 还是 back buffer？精确流程（R-B/R-D/R-K/R-M）

**方案：render 到 PSS，再整张 CopyResource 到 back buffer（R-B：CopyResource 非 AddBitmap）。** 每帧（scoped path，且非大 dirty，见 R-L）：

```
BeginFrame(clear=false, scoped=true, 非大dirty, 非backdrop帧):
  ├─ device 健康检查（R-M）：device 刚恢复首帧 → pssNeedsFullRepaint_，走 back buffer 直画
  ├─ 若 PSS 无效/尺寸变/DPI 变 → EnsureSize（返回是否重建）
  │    重建 → full clear + pssNeedsFullRepaint_=true（本帧强制 full，dirty=全窗，R-H）
  ├─ PSS state 校准（R-R）：不信任遗留 layer->State()，按 implicit-decay 规则
  │    （RT/PSR 无显式保持 → 视作 COMMON），barrier 到 RENDER_TARGET
  ├─ 重定向主 RT → pssRtv_（独立路径，NOT inRetainedCapture_，R-K）
  │    pssActive_=true
  ├─ (A) scoped clear：只在 PSS 上清 dirty rects（保留非脏区上帧内容）
  └─ viewport/scissor = 全窗 physical-pixel

... AddXxx 照常，drawOrder/batches 累积；帧内 per-element BeginRetainedLayerCapture
    仍能正常嵌套（因为 PSS 没占 inRetainedCapture_，R-K），其 EndRetainedLayerCapture
    恢复 RT 到 pssRtv_（当前硬编码回 back buffer :4811 需改，R-K）...

EndFrame(scoped=true):
  ├─ RecordDrawCommands()：(B) 只发相交 batch → 画进 PSS
  ├─ ★device-alive 门控（R-M）：if(!CheckFrameDeviceAlive()){AbortFrame();return DEVICE_LOST;}
  ├─ PSS: RENDER_TARGET → COPY_SOURCE（barrier）
  ├─ back buffer: PRESENT → COPY_DEST（barrier）
  ├─ CopyResource(back buffer, PSS)  ← R-B：不透明覆盖，逐字节 identical，O(全窗一次拷贝)
  │    （同尺寸同格式 R8G8B8A8_UNORM；透明窗尤其必须 CopyResource 保 straight-alpha）
  ├─ back buffer: COPY_DEST → PRESENT
  ├─ PSS: COPY_SOURCE → PIXEL_SHADER_RESOURCE（备下帧/backdrop 采样）
  └─ Present1(dirty rects)  ← dirty rect 仍喂 Present1 优化 DWM flip
```

**为何 CopyResource 而非 AddBitmap（R-B，blocker）**：`CompositeRetainedLayer`→`AddBitmap`（:4862）走 bitmap PSO = `SrcBlend=ONE/DestBlend=INV_SRC_ALPHA`（:765-772）= premult **over-blend**。把带 alpha 的 PSS（透明窗/backdrop 区 A<1）blend 到未清或含上帧内容的 back buffer = `dst×(1-srcA)+src` ≠ 替换 → 透明窗越叠越暗/双影。**必须 CopyResource**（同格式逐字节拷贝）；若需 quad 路径用已存在的 `copyBlendPSO_`（`SrcBlend=ONE/DestBlend=ZERO`，已核实 :824-835），**绝不复用 AddBitmap**。

**CaptureSnapshot 同帧改采 PSS（R-D，blocker）**：render-to-PSS 模式下主 RT=PSS，"本帧到目前为止的内容"就在 PSS 里。故 CaptureSnapshot 的 source 立即改为 `CopyResource(snapshotTexture_, pssLayer_->Texture())`（见 §5）。但**含 backdrop 的帧一律强制 full**（§5，`frameHasBackdropConsumer_`），避免玻璃折射错帧——所以 render-to-PSS 帧不含 backdrop，此改动主要是保证"若真发生"也正确。

**与 flip-model 的关系**：PSS 是 UI 线程侧真相源，back buffer 每帧从 PSS 得完整画面 → N-buffer 陷阱消除 → RC2/RC4 flush ring 可删（**前提全窗 blit，R-G**）。`Present1` dirty rect 仍有用（DWM 只拷 dirty 区到屏幕）。

#### 2.C.4 关键权衡 + 面积自适应门控（R-G/R-L）

**全窗 CopyResource 的固有代价**：每帧一次 O(全窗拷贝)。**部分抵消** (B) 省下的光栅：
- 小 dirty（hover/散点）：省了"膨胀区 batch 重发"，多了"一次全窗拷贝"。复杂 SDF/文本 batch 昂贵、拷贝便宜 → **净正**。
- 大 dirty（滚动/尺寸动画/大面积重绘）：dirty≈全窗，(B) 剔不掉几个 batch → 变成"重发几乎所有 batch（同现状）+ 额外一次全窗拷贝" → **严格劣于现状**（R-L）。

**修正（R-G/R-L，写死）**：

1. **blit 用全窗 CopyResource，不做"两帧并集 blit"**（R-G）：v1 的"两帧并集 blit 省采样"重新引入 N-buffer 陷阱（并集外区域仍是两帧前内容），与"删 ring"矛盾。**首版选全窗 CopyResource**（正确优先）→ alternate buffer 每帧拿完整帧 → 可安全删 ring。
   - 若未来要省拷贝走"两帧并集"，**必须保留 ring 或等价 native 多帧并集记账**（blit `本帧 ∪ 最近 swapBufferCount-1 帧` dirty）。二者不可"两帧并集 + 删 ring"并存。

2. **面积自适应门控**（R-L）：`dirty union 面积 / 全窗面积 > 阈值（如 0.6）` 时，本帧**放弃 render-to-PSS**，直接渲染到 back buffer（退回 A+B 直画，非脏区靠 FLIP 指针保持 + ring）：
   - PSS 状态机逐帧在 render-to-PSS ↔ render-to-backbuffer 间切换。
   - 切回 back buffer 那帧**标 PSS stale**（`pssNeedsFullRepaint_=true`），下次启用 PSS 时强制 full（因为跳过帧 PSS 没更新）。
   - 思路同 MEMORY `project_rc4b_ancestor_clip_dirty_cull` 的"脏区 3x 视口→≈视口防误 promote"。

> **结论**：PSS 首版做"小 dirty 帧全窗 CopyResource + 大 dirty 帧回退直画"求正确。**若 (A)+(B)+FLIP+现有 ring 已达标，PSS 延后**——§6 分阶段判断点。

#### 2.C.5 与删 ring 的绑定（R-G/R-S）

删 RC2/RC4 flush ring **仅在 PSS 全窗 blit 上线（`JALIUM_DAMAGE_SCOPED=2`）且 backend==D3D12 时生效**（R-S）：
- ring 删除条件写作等价 native 断言 `DirtyHistoryCount >= swapBufferCount-1`，**不静默删**（R-G）。
- PSS off 或 backend≠D3D12（Vulkan 走自包含 `frameRetainImage_`、Software 走 GDI，均不读 `_dirtyHistory`）→ **保留 ring**，present cadence 不变（R-S）。

#### 2.C.6 PSS blit 的正确 PSO/路径（R-B）

| 需求 | 用什么 | 为什么 |
|---|---|---|
| PSS→back buffer 整张覆盖（首选） | `CopyResource(back, PSS)` | 同尺寸同格式 R8G8B8A8_UNORM，逐字节 identical，最快，保 straight-alpha（透明窗必须） |
| 若需 viewport/UV quad 覆盖 | `copyBlendPSO_`（:824-835，SRC=ONE/DEST=ZERO 覆写） | 覆盖非 blend |
| ❌ 绝不用 | `AddBitmap`/`CompositeRetainedLayer`（:4862） | bitmap PSO = SRC_ALPHA over-blend（:765-772）→ blend-over-stale |

#### 2.C.7 PSS 独立 RT 状态 + BeginPersistentSurface（R-K/R-R）

**绝不复用 per-element retained-capture 单槽状态（R-K，blocker）**：
- `BeginRetainedLayerCapture`（:4680）开头 `if(inRetainedCapture_||inOffscreenCapture_) return nullptr` → 若 PSS 整帧持有 `inRetainedCapture_`，帧内 per-element retained realize（合成动画"一次 realize 多帧复用"，MEMORY `project_animation_system_rewrite_v2`）被拒 → 合成动画失效。
- `captureRtv_`（:4754，stencil-path resolve 目标）、`savedScissorStack_`（:4739）、`activeRealizeLayer_`（:1106）均单槽，会被 per-element capture 覆盖。

**修正**：PSS 引入**独立** RT 重定向：
- 新增 `pssActive_` 布尔 + 独立 `pssRtv_`。BeginFrame 把主 RT 指向 PSS 时**不置 `inRetainedCapture_`** → 帧内 per-element `BeginRetainedLayerCapture` 仍能正常嵌套。
- `EndRetainedLayerCapture` 在 PSS 模式下恢复 RT 到 **`pssRtv_`**（当前 :4811 用 `GetSwapChainRtvHandle()` 硬编码回 back buffer，PSS 模式下必须改成恢复到 `pssRtv_`）。
- `captureRtv_`（stencil-path resolve 目标）在 PSS 模式下默认值指向 PSS 而非 back buffer（:2916 `exitPathMode` 逻辑）。

**BeginPersistentSurface 变体（不 clear 整层）**：内部 90% 复用 `BeginRetainedLayerCapture`，但：
- 把 :4729-4730 的全清换成 **scoped clear dirty 区**（首帧/重建才全清，R-H）。
- 不置 `inRetainedCapture_`，改置 `pssActive_`。
- **state 校准（R-R）**：进入前不信任遗留 `layer->State()`（D3D12 implicit decay：ExecuteCommandLists 后无显式 barrier 的 RT decay 回 COMMON，见 :1015-1020 注释）。按 decay 规则把 `layer->SetState()` 校准为实际状态（RT/PSR 无显式保持则视作 COMMON），再 `MakeTransitionBarrier(pss, 校准后 state, RENDER_TARGET)`。
- `AbortFrame`（:1225）补一行把 PSS state 复位 COMMON，与它复位 `inRetainedCapture_`（:1244）对称（R-R）。

#### 2.C.8 触碰点

- `d3d12_direct_renderer.h` — 新成员 `pssLayer_/pssValid_/pssNeedsFullRepaint_/pssActive_/pssRtv_`。
- `d3d12_direct_renderer.cpp` BeginFrame :1039 区块 — PSS RT 重定向（独立路径，R-K）+ scoped clear + state 校准（R-R）+ device 健康检查（R-M）。
- `d3d12_direct_renderer.cpp` EndFrame :1278/:1289-1297 — device-alive 门控（R-M）+ PSS→COPY_SOURCE barrier + `CopyResource`（R-B）+ back buffer barrier 顺序 + PSS→PSR。
- 新增 `BeginPersistentSurface`（复用 `BeginRetainedLayerCapture`:4677 主体，改 clear/state/标志）。
- `EndRetainedLayerCapture`:4811 — PSS 模式恢复 RT 到 `pssRtv_`（R-K）。
- `AbortFrame`:1225 — 复位 PSS state（R-R）。
- `EnsureSize`（`d3d12_retained_layer.h:54`）— 加"是否重建"返回值（R-H）+ bucket 分配（R-P）。

---

## 3. dirty union 的来源与 record 期可得性（R-U/R-S）

| 环节 | 现状 | Phase B |
|---|---|---|
| managed 计算 dirty | `Window.cs` `ComputeDirtyRegions()` → `DirtyRegionAggregator`（≤32 rect，:8200；已 inflate DPI margin） | **保留**（精度不变） |
| managed→native | `RenderTarget.AddDirtyRect`（DIP）→ `jalium_render_target_add_dirty_rect`（NativeMethods.cs:437）→ native `dirtyRects_` | **保留** |
| DIP→physical-px | 仅在 `EndDraw:742-758` 转，喂 `Present1` | **前移**：BeginFrame 内转好供 record 用（复用 :1361-1370 clamp，**不二次 inflate**，R-U） |
| record 期可得 dirty union | **无** | **新增** `frameDirtyRects_`/`frameDirtyUnion_`（§2.0） |
| RC2/RC4 flush ring | Window.cs:8419/8432/8593/HandlePresentedFrameFlush，present cadence 驱动 | **仅 PSS(=2) D3D12 路径删**（R-S）；PSS off / backend≠D3D12 保留。删除写等价 native 断言 `DirtyHistoryCount>=swapBufferCount-1`（R-G），不静默删 |

**关键点**：不新增 P/Invoke，只把 physical-pixel 转换从 EndDraw 前移到 BeginFrame（R-U）。

**删 ring 的非 D3D12 验证（R-S，已核实）**：Vulkan 走自包含 `frameRetainImage_`（vulkan_render_target.cpp:885-901，copy 整帧 seed partial，不读 `_dirtyHistory`）；Software 走 GDI 原生 retain。故 ring 专为 D3D12 FLIP_SEQUENTIAL N-buffer 收敛。删除门控 D3D12+PSS，保 Vulkan/Software cadence。

---

## 4. Device-Lost / Resize / DPI-only / Shutdown 下 PSS 的重建与失效（R-H/R-J/R-M/R-Q/R-R）

PSS 用 `D3D12RetainedLayer` 承载 → 继承 device-generation 守卫与 fence-gated retire。挂钩点：

| 事件 | 现有机制 | PSS 动作 |
|---|---|---|
| **OnResize** | `snapshotTexture_.Reset()` 等失效（:486-518） | :520 后新增 `if(pssLayer_){DestroyRetainedLayer(pssLayer_);pssLayer_=nullptr;} pssValid_=false; pssNeedsFullRepaint_=true;` |
| **DPI-only 变化（R-J，blocker）** | `D3D12RenderTarget::SetDpi` 转发 `directRenderer_->SetDpiScale`（render_target.cpp:2532；renderer 侧 SetDpiScale 只 Reset glyph atlas）；`Resize` 尺寸不变提前 return（render_target.cpp:485 `if(width==width_&&height==height_) return JALIUM_OK;`）→ OnResize **不触发** | **在 renderer `SetDpiScale` 检测 `abs(old-new)>eps` 时显式失效 PSS**：`if(pssLayer_){DestroyRetainedLayer(pssLayer_);pssLayer_=nullptr;} pssValid_=false; pssNeedsFullRepaint_=true;` + 下帧强制 full（新 PSS 非脏区未初始化）。**新增触碰点** |
| **Device-Lost（mid-frame）** | `CheckFrameDeviceAlive():1247` 置 `frameDeviceLost_`，`AbortFrame` 清状态 | PSS layer `Device()` 守卫；scoped 帧遇 device-lost **整帧回退全窗直画**（`frameDamageScoped_=false`），不 blit 半死 PSS。**PSS blit 前再插 device-alive 门控**（R-M，§2.C.3） |
| **Device 重建后首帧** | managed recovery 重建 context | PSS 指针失效（旧 device）→ `BeginPersistentSurface` 检测 `Device()!=device_` → 新建 + full clear + `pssNeedsFullRepaint_=true`（强制本帧 full）。**device 恢复首帧强制走 back buffer 直画**（R-M） |
| **PSS 自身重建（R-H，major）** | `EnsureSize` 短路/重建（:54） | `EnsureSize` **返回"是否重建"**；重建（含 DPI/LRU/其他致纹理新建，managed `_fullInvalidation` 未必 true）→ `pssNeedsFullRepaint_=true` → `frameDamageScoped_` 因 `&& !pssNeedsFullRepaint_` 退回全窗直画。**不依赖 managed `_fullInvalidation`** |
| **Shutdown / renderer 析构（R-Q）** | `DirectRenderer::Shutdown`(:356) GPU-idle wait；RT 析构(:34-45) | Shutdown 的 wait(:361-373)**之后**加 `if(pssLayer_){DestroyRetainedLayer(pssLayer_);pssLayer_=nullptr;}`（复用现成双路：健康 device fence-gated graveyard，removed device OrphanGpuResources，:4868）。放 wait 之后、backend 有效时。**§7 触碰点新增此行** |
| **AbortFrame（R-R）** | 复位 `inRetainedCapture_`(:1244) 等 | 补一行复位 PSS state 为 COMMON（与 implicit-decay 规则对称） |

**stale 防护红线（R4）**：
- resize/DPI 变**必须**先 `DestroyRetainedLayer(pssLayer_)` 再重建（旧尺寸 blit 新尺寸 = 错位/拉伸，R-J）。
- device-lost/PSS 重建后**必须**强制下帧 full（`pssNeedsFullRepaint_`），否则非脏区是未初始化新纹理 = 垃圾（R-H）。

**销毁路径正确性**（已验证）：`DestroyRetainedLayer`（:4865）含双路，PSS 直接复用，无需新写 device-lost 逻辑。

---

## 5. 与 CaptureSnapshot / backdrop 的统一（R-D/R-E/R-V）

已核实：
- `CaptureSnapshot`（:3913-3976）调 `FlushGraphicsForCompute`（:3939）→ `RecordDrawCommands`（:3511），再 `CopyResource(snapshotTexture_, backBuffer)`（:3952）；backdrop（`DrawSnapshotBlurred:3990` / `DrawLiquidGlass:5700`）采样此快照获"其下已画内容"。有 `snapshotValid_+drawOrder_` 快速跳过（:3928-3931）。
- **backdrop 每帧 inline 触发**（已核实）：`DrawBackdropEffect` 从 `Border.cs:866` per-frame inline 调；`EditControl` 用 `s_scrollBarBackdropBlurEffect`（EditControl.cs:42）经 `DrawBackdropBlur`（:5183-5223）在 scrollbar hover 触发。**二者都在 partial 帧触发**（caret blink / scrollbar hover 在 Acrylic/Mica 面板下），**均未** RequestFullInvalidation。故 v1 §5"backdrop 帧多为 full 路"**是错的**。

**统一策略（写死）**：

1. **含 backdrop / snapshot consumer 的帧强制 full（R-D/R-E，核心）**：
   - 检测本帧存在 backdrop 消费（`DrawBackdropFilter`/`DrawLiquidGlass`/pending `SnapshotBlit`/`LiquidGlass` batch，或 managed 侧设 snapshot-requested flag）→ `frameHasBackdropConsumer_=true` → `frameDamageScoped_=false`（§2.0）→ 整帧 full 直画 back buffer + 全窗重画。
   - 这一刀同时解决：
     - **R-E**：CaptureSnapshot 的 flush 不会剔除快照下的底层 batch（整帧不剔除）。
     - **R-D**：不 render-to-PSS，CaptureSnapshot 的 source 仍是 back buffer 且被全窗重画 → backdrop 采到本帧完整内容。
   - 代价：backdrop 帧无 damage-scoped 收益。但 backdrop 帧本就 §6 归为"收益有限"，且正确性 > 收益（R5）。

2. **render-to-PSS 帧的 CaptureSnapshot（R-D 兜底）**：若某帧既 render-to-PSS 又（意外）含 snapshot，source 立即改 `CopyResource(snapshotTexture_, pssLayer_->Texture())`（"本帧到目前为止"在 PSS 里）。但因 (1) 已把 backdrop 帧强制 full，此路径正常不触发，仅作防御。

3. **B4（CaptureSnapshot 统一到 PSS）降级为"不实施"（R-V）**：
   - v1 想让 backdrop 采 PSS 省一次 copy。但 PSS 非脏区是上帧内容，大核模糊会把幽灵内容（上帧有、本帧已移除但非脏区没重画的元素）折射进去 → 违反 R5。
   - **决定**：**B4 不实施**。保留 `CaptureSnapshot` 每帧全窗 `CopyResource(snapshotTexture_, backBuffer)`（:3952，本有 `snapshotValid_+drawOrder_` 快速跳过）。PSS 与 snapshot 各管各的。R5 风险彻底移出范围。

**R5 红线**：backdrop 帧一律 full，Mica/Acrylic/LiquidGlass 逐像素与今日一致。

**触碰点**：§2.0 的 `frameHasBackdropConsumer_` 判定（managed 侧在 record 前告知本帧含 backdrop，或 native 在 BeginFrame 前探测）；CaptureSnapshot source 切换仅作 render-to-PSS 防御（:3944-3952，正常不走）。

---

## 6. env kill-switch 与分阶段落地（R-A/R-G/R-S/R-T）

### 6.1 kill-switch（R-T 机械等价）

**`JALIUM_DAMAGE_SCOPED`**（沿用 MEMORY 惯例 `JALIUM_VK_BATCH_MERGE`/`JALIUM_PATH_MSAA`）：

- 读取：native `D3D12DirectRenderer` 初始化 `_dupenv_s`（MEMORY `project_vulkan_present_mode_mailbox_unlock`），存 `bool envDamageScoped_`。
- 取值：`0`=全关（三处全短路，与今日 bit-identical）；`1`=开 (A)+(B)；`2`=开 (A)+(B)+(C) PSS。
- **默认 OFF**（`0`），验证充分后再议默认（MEMORY `project_render_backend_explicit_switch` 惯例）。
- **热切换经 DevTools**（env 只启动读，运行时切走 DevTools，避免 mid-frame 改渲染路径，MEMORY `project_vello_921_retire_consolidated`）。
- **render-thread 互斥（R-A）**：scoped 生效时若 `_rtActive`，managed 侧首版**不走 scoped**（等价 env off）；需用户显式 `EnableRenderThread=false` 后启用。文档写明。
- **env-off 机械等价（R-T）**：三处改动结构化为 `if(frameDamageScoped_){新路}else{逐字节旧代码}`，`frameDamageScoped_` 唯一门（env=0 恒 false）。**debug 断言**：env unset 时 `frameDirtyRects_` 恒空、`frameDamageScoped_` 恒 false（full/partial/promote/flush/capture 所有帧型）。

### 6.2 分阶段落地顺序（每步独立可验证）

| 阶段 | 内容 | 独立验证 | 依赖 | 可否单独交付 |
|---|---|---|---|---|
| **B-RT 前置决策**（R-A） | 确定 render-thread 互斥：首版 `EnableRenderThread=false`；证明 inline-scoped 优于 render-thread-full | §1.2 render-thread 对比法（iGPU） | 无 | ✅（决策 + 测量，无代码行为变化） |
| **B0** | §2.0 dirty union 前移 record 可见 + env 门控骨架（R-U：inline 无需新管线） | 断言：scoped 帧 `frameDirtyUnion_`==managed dirty physical-px bbox；env off 恒空（R-T） | B-RT 决策 | ✅（纯管道） |
| **B1** | §2.A scoped clear（R-I：仅不透明背景） | 强制 clear+dirty 帧，DevTools 看 clear 时间随 dirty 缩；透明窗走旧路 | B0 | ✅（收益小，为 C 铺路） |
| **B2** | §2.B batch 剔除（R-C 定位/R-F 套 scissor/R-N 放置点+图元排除/R-O per-segment/R-E backdrop 帧 full/R-W path scissor） | **散点 dirty 面积-成本斜率法**（§1.2，R-C）；`culledBatchCount_` 骤升；Acrylic caret blink 回归逐像素=full；逐像素对比 `=0` | B0 | ✅✅（**最大单点收益**，配合 FLIP+现有 ring 可能达标） |
| **B3** | §2.C PSS（R-B CopyResource/R-G 全窗 blit 不并集/R-L 面积门控/R-K 独立 RT/R-D+R-E backdrop 帧 full/R-H+R-J+R-M+R-Q+R-R 生命周期）；§3 删 ring（R-S 仅 D3D12+PSS，R-G 断言不静默删） | alternate-buffer 正确性（快速连续散点 hover 无残影）；GPU% 达标；device-lost/resize/DPI/shutdown 无 stale；churn VRAM 可控（R-P）；合成动画不失效（R-K） | B2 | ⚠️（高风险；仅当 B2 后 alternate-buffer 残影仍在、或要删 ring 简化时才做，且须实测全窗 CopyResource 净正 R-L） |
| **~~B4~~** | ~~§5 CaptureSnapshot 统一 PSS~~ | **降级为不实施**（R-V） | — | ❌（R5 幽灵内容风险，移出范围） |

**推荐落地策略**：
1. **B-RT 决策 → B0→B1→B2**，实测。**很可能 B2 + FLIP + 现有 RC2/RC4 ring 就达标**（B2 砍掉 bbox 膨胀区光栅，非脏区靠 FLIP+ring 保持）。
2. **B2 达标则暂停**，PSS（B3）作为"alternate-buffer 残影仍在 / 要删 ring 简化"的高风险后续。B3 有全窗 CopyResource 固有代价，须实测净正（R-L）再上。
3. **B4 不做**（R-V）。

> **独立验证第一步 = B2**：不需 PSS，直接在现有 back buffer 上按多 dirty rect 逐 batch 剔除（`hasScissor` 求交 + `!hasScissor` 套 dirty scissor），配合 FLIP 保持非脏区。用**散点 dirty** 面积-成本斜率法立刻证明"GPU 工作与 damage 挂钩"。投产比最高、风险最低。

---

## 7. 每处改动触碰的 file:line 汇总（含审查新增行）

| 改动 | 文件 | 行/区块 | 动作 |
|---|---|---|---|
| **B0** dirty union + 求值 | `include/d3d12_direct_renderer.h` | :1067 区块 | 新成员 `frameDirtyRects_`/`frameDirtyUnion_`/`frameDamageScoped_`/`envDamageScoped_`/`frameHasBackdropConsumer_`（+PSS：`pssNeedsFullRepaint_` 等） |
| B0 | `src/d3d12_direct_renderer.cpp` | BeginFrame 入口（:1039 之前） | 用已传入 `fullInvalidation` 求 `frameDamageScoped_`（R-U，无需扩签名）；DIP→px 复用 :1361-1370（R-U 不二次 inflate）；debug 断言（R-T） |
| B0（backdrop 探测） | managed / native | record 前 | 设 `frameHasBackdropConsumer_`（R-D/R-E，§5） |
| **B1** scoped clear | `src/d3d12_direct_renderer.cpp` | :1039-1042 | `ClearRenderTargetView` 带 rect 数组，**仅不透明背景**（R-I）；else 逐字节旧路（R-T） |
| **B2** batch 剔除 | `src/d3d12_direct_renderer.cpp` | :2847（循环，cull 在 `exitPathMode` 之后 R-N）；新增 `IntersectsAnyDirtyRect`/`IsCullableContentType` | `hasScissor` 求交跳过；`!hasScissor` 套 dirty scissor（R-F）；排除 StencilPath/SnapshotBlit/PunchRect/LiquidGlass（R-N/R-E）；per-segment（R-O） |
| B2 StencilPath scissor | `src/d3d12_direct_renderer.cpp` | :2861-2869（默认全 pathContent 分支） | scoped 帧收窄为 dirty union（R-W） |
| B2（可选计数） | `include/d3d12_direct_renderer.h` | 新成员 `culledBatchCount_` | DevTools 暴露 |
| **B3** PSS 承载 | `include/d3d12_direct_renderer.h` | 新成员 `pssLayer_/pssValid_/pssNeedsFullRepaint_/pssActive_/pssRtv_` | — |
| B3 PSS RT 重定向 | `src/d3d12_direct_renderer.cpp` | BeginFrame :1039 区块（独立路径 R-K）；新增 `BeginPersistentSurface`（复用 :4677 主体，改 clear/state R-R/标志） | render-to-PSS，不占 `inRetainedCapture_`（R-K）；device 健康检查（R-M） |
| B3 PSS blit（CopyResource） | `src/d3d12_direct_renderer.cpp` | EndFrame :1278/:1289-1297 | device-alive 门控（R-M）+ PSS→COPY_SOURCE + `CopyResource`（R-B，**非 AddBitmap**）+ back buffer barrier + PSS→PSR；面积门控（R-L） |
| B3 EndRetainedLayerCapture 恢复 | `src/d3d12_direct_renderer.cpp` | :4811 | PSS 模式恢复 RT 到 `pssRtv_`（当前硬编码 back buffer，R-K） |
| B3 EnsureSize 返回重建 | `include/d3d12_retained_layer.h` | :54 | 加"是否重建"返回值（R-H）+ bucket 分配（R-P） |
| B3 AbortFrame PSS state | `src/d3d12_direct_renderer.cpp` | :1225 | 复位 PSS state COMMON（R-R） |
| B3 PSS 失效（resize） | `src/d3d12_direct_renderer.cpp` | OnResize :520 后 | `DestroyRetainedLayer(pssLayer_)` + `pssValid_=false` + `pssNeedsFullRepaint_=true` |
| **B3 PSS 失效（DPI-only）** | `src/d3d12_direct_renderer.cpp`（renderer `SetDpiScale`，被 render_target.cpp:2532 转发调用） | **renderer `SetDpiScale`（新增失效逻辑）** | 检测 dpiScale 变化 → 失效 PSS + 强制 full（R-J，blocker） |
| B3 PSS 失效（device-lost 首帧） | `src/d3d12_direct_renderer.cpp` | :1247 邻域 / BeginFrame device-gen | layer `Device()` 守卫；device 恢复首帧走 back buffer 直画（R-M） |
| **B3 PSS 销毁（Shutdown）** | `src/d3d12_direct_renderer.cpp` | **`Shutdown`:356 的 wait(:361-373) 之后（新增）** | `DestroyRetainedLayer(pssLayer_)` 复用双路（R-Q） |
| B3 删 flush ring（门控） | `src/managed/Jalium.UI.Controls/Window.cs` | :8419/:8432/:8593/`HandlePresentedFrameFlush` | **仅 PSS(=2)+D3D12 删**（R-S）；写等价断言 `DirtyHistoryCount>=swapBufferCount-1`（R-G）；保留 `ComputeDirtyRegions`/`AddDirtyRect`/RC4-c |
| **~~B4~~** CaptureSnapshot 统一 | — | — | **不实施**（R-V） |
| **kill-switch** | `src/d3d12_direct_renderer.cpp` | 初始化 `_dupenv_s` 读 `JALIUM_DAMAGE_SCOPED` / DevTools 热切换 | env `envDamageScoped_`；render-thread 互斥（R-A，managed 侧） |

---

## 8. 风险与开放问题（含审查残留）

1. **B2 相对已有 managed cull 的增量（R-C）**：真正的杠杆是"bbox→多矩形 union 收窄"。**若目标场景 union≈bbox（单 dirty），B2 收益甚微**。必须先测"散点 dirty 下 ShouldRenderChild+bbox-scissor 后存活 batch 数"再断言 B2 高价值。若单 dirty 为主，转向降低 bbox 内合法 batch 的 GPU 成本（如全窗背景 SdfRect）。
2. **render-thread 首版被排除（R-A）**：iGPU HWND 窗若默认开 render thread，Phase B 首版需用户 `EnableRenderThread=false`。render-thread 的 damage-scoped（Phase B-RT）是独立后续，涉及跨线程 `_drawingContext` 独占契约（Window.cs:7934-7941 UAF 警告）。
3. **PSS 全窗 CopyResource 净收益（R-L）**：小 dirty 净正，大 dirty 净负 → 面积门控。必须实测"一次全窗拷贝 < 膨胀区 batch 重发"。
4. **PSS churn VRAM（R-P）**：bucket 分配 + DPI 滞后减退役频率；§8 预算须计"每窗口最多 2 张 PSS 在墓地（旧+新）"瞬时上限（每张 6-25MB）。**PSS 退役滞留延迟 = 本窗口 fence 追上全局标的时间**（共享 graveyard + 每 renderer 自有 fence）。
5. **PSS 与合成动画共存（R-K）**：独立 RT 状态是硬约束，实现须确保帧内 per-element retained capture 仍嵌套正常，否则合成动画失效（B3 上线即回归）。
6. **B3 与 present-pacing 交互**：PSS blit 增固定 GPU 成本，须确认不破 present-pacing credit 记账（MEMORY `project_composition_target_1ms_freespin_regression`/`project_present_pacing_event_driven`）。pacing 不改。
7. **StencilPath 首版不剔除（R-W）**：path 密集场景收益打折；其 resolve scissor 已收窄到 dirty union 防非脏区叠加。
8. **测试环境**：headless 须直接 `host.Measure`（MEMORY `project_virtualization_test_harness_remeasure`）；native DLL 拷贝+restore（MEMORY `project_worktree_test_env_setup`）；真机 GPU 靠 DevTools + computer-use 埋点（`JALIUM_HOVER_TRACE` 式，MEMORY `project_composition_target_1ms_freespin_regression`）。
9. **回归测试补充（R-E）**：验收套件必加"Acrylic 侧栏下 caret blink"partial-frame backdrop 回归（该帧强制 full，须逐像素=full 路）。

---

## 9. 一句话总纲（v2）

**首版明确 `EnableRenderThread=false`（否则 render thread 每帧 full 让 Phase B 失效，R-A）→ B0 打通 record 期可见 dirty union（inline 路径 dirtyRects_ 已在 native，只需前移，R-U）→ B2 按多 dirty rect 逐 batch 剔除，优化的是单 bbox 无法表达的散点/L 形膨胀（不是砍全窗，因 managed 已 cull 到 bbox，R-C），`!hasScissor` batch 套 dirty scissor 而非裸重发（R-F），cull 在 exitPathMode 之后且排除所有合成图元（R-N/R-E），含 backdrop 的帧一律 full（R-D/R-E/R-V），配合 FLIP + 现有 RC2/RC4 ring 很可能直接达标 → PSS(B3) 仅在残影仍在时才上，blit 用 CopyResource 非 AddBitmap（R-B）、全窗 blit 不做两帧并集（R-G）、面积门控（R-L）、独立 RT 不复用 retained-capture 单槽（R-K）、DPI/resize/device-lost/shutdown/自身重建全挂失效钩子且强制 full（R-H/R-J/R-M/R-Q/R-R）、删 ring 仅 D3D12+PSS 且写等价断言（R-S/R-G）→ B4 不实施（R-V）。全程 `JALIUM_DAMAGE_SCOPED` 默认 OFF、DevTools 热切换、env=0 机械 bit-identical（R-T）、full/backdrop 帧永远走原路。**

---

**相关文件绝对路径**（供后续实现）：
- `D:\Users\suppe\source\repos\Jalium.UI\src\native\jalium.native.d3d12\src\d3d12_direct_renderer.cpp`
- `D:\Users\suppe\source\repos\Jalium.UI\src\native\jalium.native.d3d12\include\d3d12_direct_renderer.h`
- `D:\Users\suppe\source\repos\Jalium.UI\src\native\jalium.native.d3d12\src\d3d12_render_target.cpp`
- `D:\Users\suppe\source\repos\Jalium.UI\src\native\jalium.native.d3d12\include\d3d12_retained_layer.h`
- `D:\Users\suppe\source\repos\Jalium.UI\src\native\jalium.native.d3d12\src\d3d12_backend.cpp`
- `D:\Users\suppe\source\repos\Jalium.UI\src\managed\Jalium.UI.Controls\Window.cs`
- `D:\Users\suppe\source\repos\Jalium.UI\src\managed\Jalium.UI.Interop\RenderTargetDrawingContext.cs`
- `D:\Users\suppe\source\repos\Jalium.UI\src\managed\Jalium.UI.Core\Visual.cs`
- `D:\Users\suppe\source\repos\Jalium.UI\src\managed\Jalium.UI.Interop\NativeMethods.cs`

---

设计终版已完成并写入 `C:\Users\suppe\AppData\Local\Temp\claude\D--Users-suppe-source-repos-Jalium-UI\e9f0833c-25d2-4527-bfdc-0cdf02b6ad94\scratchpad\phase_b_final_design.md`（供后续引用）。所有 23 条审查发现（含 6 个 blocker、11 个 major、6 个 minor）均已在文档中吸收或明确降级为"已知风险+缓解+门控"，且每条关键 file:line 断言已逐一 Read 核实（render-thread 早退、AddBitmap 混合 PSO、managed cull 已激活、CaptureSnapshot 驱动 RecordDrawCommands、FlushGraphicsForCompute 分段清 batches_、copyBlendPSO_ 存在、DPI-only 早退路径等）。