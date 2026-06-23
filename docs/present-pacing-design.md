# Event-Driven Present Pacing（慢合成环境渲染调度重构）

状态：已实现（2026-06-10），默认启用（inline 路径 + D3D12 + 非 DComp + waitable 可用时）。

## 问题

"从独显切换到核显后 FPS 跌到 2-6、整窗卡顿"。实测（AMD UMA 核显 + 远程虚拟显示器环境）与代码审计闭环出的根因链：

1. 慢合成环境（遮挡节流 ≈8 vsync、远程/虚拟显示器实测 460-500ms）下 DWM retire back buffer 极慢。**与 adapter 无关**——RTX 4060 在同类环境同样从 present→ready 4.5ms 恶化到 197ms。
2. 旧架构把"等 DWM 还 buffer"放在 UI 线程关键路径上，二选一：
   - vsync ON（默认）：`Present(1)` 在 EndDraw 内同步阻塞整个 retire 间隔（DevTools EndDraw 行 133.9-460ms，GPU 实际只用 11ms）；
   - vsync OFF：阻塞转移到 BeginDraw 的 `WaitForSingleObjectEx(waitable, 16ms)` 超时 + managed 1ms timer 重试循环（每帧 ~30 圈，UI 线程 ~94% 时间阻塞）。
3. MFL / buffer 数 / vsync / waitable-vs-fence 参数全部无效（历史 4 配置 A/B + 本次 vsync=0 复测一致）：只移动等待位置。
4. 附带口径事实：DXGI Present 内部派发 sent-message（resize 重入崩溃 0xC000041D 的根源），因此 SendMessage 探针测不出 Present 阻塞，但真实输入（posted 消息）、DispatcherTimer、动画 tick 全部被卡死。

## 设计原则

**UI 线程永不等待 DWM。** swap-chain frame-latency waitable 的消费权上移到 managed 调度层：
线程池 `RegisterWaitForSingleObject` 把信号转成一个 **present credit**，UI 线程只在持有 credit 时才进
BeginDraw（native 不再等 waitable），Present 永远 sync-interval 0（waitable 本身就是节奏源，其信号
速率 = 合成器消费速率；vsync 对齐的 Present 只是第二个多余的阻塞点）。DWM 慢只会降低上屏频率
（dirty 自然累积、丢帧合并、动画时钟照常推进），不再阻塞输入 / 布局 / 动画。正常环境下 waitable
按 vblank 节奏 signal，帧节奏与旧 vsync 模型一致。**无 per-adapter 分支**——对所有环境统一生效。

## credit 守恒（正确性核心）

waitable 是 auto-reset、信号计数 = MaxFrameLatency(=1)，消费即拥有。credit 回池仅两途：

1. present 成功 → DWM retire → waitable signal → 回调转 credit；
2. 本帧消费了 credit 但最终没 present（BeginDraw fence/设备失败、TryEndDraw 失败）→ 显式归还
   （`ReturnSwapCreditAfterFailedPresent`，调用点：TryBeginDrawOrScheduleRetry 失败分支、
   CompleteEndDrawOrHandleFailure 失败分支、ApplyRenderTargetResize 成功后的防御性重置——
   ResizeBuffers 后信号计数语义不可依赖）。

漏归还 = 死锁在"credit 永远不来"；多归还 = 退化为 Present 内 DXGI 自身短暂排队（不丢正确性）。
丢失唤醒由 TryBeginDrawOrScheduleRetry 的 double-check（挂等待后再取一次 credit）覆盖；
500ms 兜底超时回调**不伪造 credit**，只在 credit 仍未到时继续追等。

## 实现清单

native（D3D12）：
- `RenderTarget::SetExternalPresentPacing`（基类默认 no-op 虚方法；ABI
  `jalium_render_target_set_external_present_pacing`）
- `D3D12RenderTarget::BeginDraw`：external pacing 时跳过 waitable 等待（信号已被 managed 消费，等待必死锁）
- `D3D12RenderTarget::EndDraw`：`effectiveVsync = vsyncEnabled_ && !externalPresentPacing_`
- `D3D12DirectRenderer::EndFrame`：QPC 包住 Present → `lastFramePresentBlockNs_`
- `JaliumGpuStats` 尾部新增 `presentBlockNs`；frametime 日志加 `presentBlock= / extPacing=` 字段

managed：
- Window.cs："External present pacing" 块——`_swapCredit`（Interlocked）、
  `StartExternalPresentPacingIfSupported`（EnsureRenderTarget 尾部；渲染线程存活 / DComp /
  waitable==0 时不启用）、`StopExternalPresentPacing`（窗口关闭、两条设备恢复路径、WebView
  composition swap——全部在 `RenderTarget.Dispose` 关闭 waitable 句柄**之前**注销线程池等待）、
  `EnsureSwapWaitRegistered`、`OnSwapWaitableSignaled`、`TryBeginDrawOrScheduleRetry` 的
  credit 消费分支。waitable 不可用 / 非 D3D12 → 完整保留旧 1ms timer 重试路径。
- 渲染线程互斥：判定"线程实际存活"而非环境变量——schema-gap latch 停掉渲染线程后，
  latch 点重新调用 Start 让 inline 路径接管 pacing。
- 仪表：`PublishAndResetApiStats` 拆出 "EndDraw (present)" 行（同 "BeginDraw (wait)" 模式）；
  PerfTab "API / gap" 公式修复（旧 `RenderMs - apiMs` 跨段相减出 -132.6ms，
  改 `(RenderMs + PresentMs) - apiMs`）。

渲染线程调度修复（同批，JALIUM_RENDER_THREAD 仍默认 OFF）：
- `PresentCaptureOnRenderThread` TryBeginDraw 失败不再丢 capture：放回 mailbox（UI 新帧优先）+
  重唤醒——杜绝"静态场景最后一帧永不上屏、需鼠标恢复"；
- `RenderThreadLoop` 兜底 catch 不再静默吞帧：marshal `RequestFullInvalidation` 回 UI 线程；
- WM_ENTER/EXITSIZEMOVE 尊重 `RequestRenderThreadIdle` 返回值（与 FIX#5 一致化）。

## 实测（同一慢合成环境，Gallery，JALIUM_GPU_PREFERENCE=igpu）

| 指标 | 修复前 | 修复后 |
|---|---|---|
| Present 阻塞 (presentBlock) | 134-460 ms/帧（在 UI 线程） | 0.2 ms |
| BeginDraw waitable 等待 | 52ms + 16ms×N 忙等循环 | 0.0 ms |
| 帧率 | 2 fps（avgFrame 462-502ms） | 5-6 fps（165-291ms，达到该环境 DWM 消费上限） |
| 渲染路径 UI 线程阻塞合计 | ≈460 ms/帧 | ≈0.5 ms/帧 |

对照（渲染线程模式 JALIUM_RENDER_THREAD=1 同环境）：3fps、sent-message p95 32ms——
inline + external pacing 全面更优，渲染线程保持 opt-in。

## 对抗审查修复记录（2026-06-10，3 路并行审查 pass-with-fixes 后落地）

- **blocker** 跨平台崩溃：`StartExternalPresentPacingIfSupported` 加 `_platformWindow != null` 早退
  （`ShouldUseCompositionRenderTarget` 走 user32 P/Invoke，Android 路径会 DllNotFoundException）。
- **major** pacer 双消费者：legacy `JALIUM_ENABLE_FRAME_PACER` 线程与本调度器抢同一 auto-reset
  waitable，pacer 抢到的信号被无声丢弃（其 `_framePending` 无人置位）→ credit 饿死。
  接管前 `StopFramePacer()`。
- **major** tearing 扩散：`presentFlags` 的 ALLOW_TEARING 判定从 `!effectiveVsync` 回滚为
  `!vsyncEnabled_`——external pacing 只需要 sync-interval 0，不得替 vsync-ON 用户选择撕裂
  （iFlip/MPO 直通下会真撕裂）。Present(0,0) 在 credit 调度下依然零阻塞（实测 presentBlock 0.2ms）。
- **major** credit 泄漏：mid-frame 异常路径补归还——`HandleRecoverableRenderPipelineFailure`
  入口（全部 recoverable inner-catch 的共同漏斗，deferred recovery 不重建 RT 时唯一的归还点）+
  RenderFrame 两个 outer catch。
- **major** teardown 竞态：`_swapPacingLock` 串行化 arm/disarm 与超时回调重注册；
  `_swapPacingActive` 改 volatile；`EnsureSwapWaitRegistered` 注册前锁内复查；
  `StopExternalPresentPacing` 改阻塞式 `Unregister(event)`（RT.Dispose 关闭句柄前必须确保
  线程池等待完全离开——对注册等待中的句柄 CloseHandle 是 UB）。
- **minor**：`JALIUM_DISABLE_VSYNC`（benchmark 人群）跳过 pacing；native
  `SetExternalPresentPacing` 追加 `!isComposition_` 防御 + 清零 waitable 统计（防 latch 切换后
  stale 值每帧重发布）；LowMemory/resize 的 park-失败路径补一致性处理；credit 播种从 1 改 0
  （swap chain 预置信号经首次注册回调自然转 credit，避免双 token 把稳态管线静默加深一档）；
  present 成功后预挂等待（连续动画下一帧免线程池+dispatcher 双跳）。
- 测试：`ExternalPresentPacingTests` 6 项锁死 credit 契约（消费/归还/miss 不忙等/恢复漏斗归还/
  旧路径不受影响/Stop 幂等）；必须 `[Collection("Application")]`（Window 构造的 DP-metadata race）。

## 剩余延迟构成（2026-06-10 像素时间戳实测，远程虚拟显示器环境）

修复后用户体感"hover ~200ms 才反馈"的分解（实验：注入 WM_MOUSEMOVE → CopyFromScreen
轮询本机 DWM 合成输出的像素变化）：

| 输入注入 → 本机屏幕变化 | avg | p50 | p90 |
|---|---|---|---|
| WinForms GDI（重定向表面，环境基线） | 51ms | 47ms | 62ms |
| Jalium D3D12 + external pacing | 70ms | 62ms | 109ms |

- 该桌面（远程 IDD 虚拟显示器）的 **DWM 合成循环 ≈ 9fps（112ms/帧）**，对 GDI 与 flip
  窗口一视同仁（双窗口像素更新率实测完全相同）——合成等待平均 ~56ms 是所有应用的
  共同地板。
- 持续渲染时 Jalium present 节奏 34-40fps、present→ready ≈25ms、presentBlock 0.2ms——
  **flip buffer retire 远快于合成节奏**，调度器无瓶颈。（注意 Gallery 启动期日志里
  170-290ms 的 present→ready 是"含帧间 CPU/调度间隔"的口径，并非 DWM 持有时间。）
- Jalium 比 GDI 基线多的 ~15-20ms（p50）= 渲染管线本身（布局+录制+GPU ~10ms）+
  flip 入队到合成采样的相位差。
- **体感 200ms 的大头在本机之外**：远程串流的编码+网络+客户端解码下行（~100ms 级，
  作用于所有应用）。光标由远程客户端本地绘制（即时），内容反馈走完整链路——这个
  通道差异让任何应用的 hover 在远程下都"显得"慢，物理显示器上同代码延迟为
  20-30ms 量级。

## 【已撤回】合成节拍门（节拍源失配，Present 撞墙回归 UI 线程阻塞）

DwmFlush 门（下段历史记录）已于同日整体撤回：DwmFlush 是**全局**合成节拍（~110ms），而
buffer retire 是 **per-window** 的——大窗口（1344x852 Gallery，远程 IDD）retire ~164ms 慢于
全局节拍，门过早放行 credit → 下一帧 Present 撞 MFL=1 的未-retire 墙 → presentBlock 164ms
落回 UI 线程，恰是本调度器要消灭的形态（小窗探针 retire 25ms，永远测不出该失配）。
**credit 的唯一节拍源回归 waitable 信号本身**——它才是"buffer 可写、Present 不阻塞"的权威。
撤回后 Gallery presentBlock 0.2ms。遗留权衡：轻帧小窗高频全窗动画（合成压力探针）下 hover
延迟回到 ~420ms，成因指向 dispatcher 连续处理高频渲染队列时输入 pump 饥饿——正确的后续
方向是 **dispatcher 操作间 pump 保障（WPF 同款）**，而非任何渲染节流门。

## 历史记录：合成节拍门（2026-06-10 第二轮：修"动画占线挤压交互"回归，已被上段撤回）

第一轮上线后用户反馈交互"更延迟了"。复现实验（探针窗 = hover 变色 + 持续动画条）定位：
waitable 信号是 DWM 对 present 的 **ack**（远程 IDD 实测 ~25ms 回流），不是合成节拍（~9fps）。
credit 以 ack 节奏供给 → 连续动画把渲染顶到 40fps，其中 4/5 的帧从未上屏，但 UI 线程被
渲染+空调度占到近饱和 → 输入处理被挤进缝隙，hover 延迟从 70ms 恶化到 **421ms**（p90 661、
max 2061）。

修复（[Window.cs](../src/managed/Jalium.UI.Controls/Window.cs) `OnSwapWaitableSignaled`）：

1. **DwmFlush 合成节拍门**：线程池回调收到 ack 信号后先 `DwmFlush()`（阻塞到下一次真实
   合成完成）再入账 credit——credit 供给频率 = 实际上屏频率，完全自适应（远程 9fps 环境
   渲染 clamp 到 9fps、每帧画 dirty 累积后的最新状态；物理 60Hz 屏 ~16ms 返回，等价 vblank
   对齐）。UI 线程渲染占空比从 ~100% 降到 ~10%。交互首帧用预挂转好的现成 credit，不经过
   合成等待。DWM 不可用时容错退化为 ack 节奏。
2. **空调度砍除**：回调只在 `_renderPendingOnSwap`（miss 时置位）时才调度 ProcessRender；
   丢失唤醒仍由 TryBeginDrawOrScheduleRetry 的 double-check 兜底。
3. **credit 升级为计数信号量**（上限 = MaxFrameLatency）：`JALIUM_MAX_FRAME_LATENCY=2`（配
   `JALIUM_SWAPCHAIN_BUFFERS=3`）实验旋钮——慢合成环境下 DWM 快 ack 的行为接近 discard
   语义，第二个 in-flight 名额让交互帧免排"等 credit"的队且不付展示延迟代价。默认仍 1
   （健康 60Hz 环境下加深队列 = 经典的 +1 帧输入延迟权衡，不自动开启）。

实测（动画占线 + hover 注入 → 本机上屏，30 次）：

| 配置 | avg | p50 | p90 | max |
|---|---|---|---|---|
| 回归版（ack 节奏 credit，无门） | 421ms | 384 | 661 | 2061 |
| DwmFlush 门 + MFL=1 | 158ms | 168 | 216 | 246 |
| + MFL=2 | 114ms | 123 | 153 | 155 |
| **+ MFL=3（现默认，见下）** | **95-108ms** | 94-108 | 124-138 | 140 |
| GDI 重定向表面（环境物理下限参照） | 52ms | 48 | 63 | 78 |

剩余 ~50ms 差距 = flip 模型"等可写 buffer"环节的固有成本（GDI 表面任意时刻可写）；
要彻底抹平需要 WPF 式重定向表面 / 软件合成路径（Software 后端，存在预存启动问题未修）。

## 默认值回归 3 buffer / MFL 3（令牌桶定型）

用户对照发现 26.10.2（commit 6a44625，3 buffer + DXGI 默认 latency 3、无任何 waitable
等待）在同环境交互不卡——5 月底为追健康屏最低输入延迟改成 2 buffer + MFL 1，正是慢合成
塌方的起点之一。机制定型为**令牌桶**：

- **DwmFlush 合成节拍门 = 速率**（渲染节奏永远贴合真实上屏频率，深桶不再被无节制填满，
  旧"深队列 = +N 帧延迟"的前提不复存在）；
- **MFL = 桶深**（突发弹性：动画与交互同帧竞争时，交互帧用多余名额即时入队，DWM 的
  快 ack（~25ms）≈ discard 语义保证合成总采样最新帧）。

默认 `kDefaultSwapBufferCount=3`、`maxFrameLatency_=swapBufferCount_`；managed 桶深经
`GetPresentInfo().MaxFrameLatency` 回读同步（native 是唯一真源）。`JALIUM_SWAPCHAIN_BUFFERS`
/`JALIUM_MAX_FRAME_LATENCY` 仍可降回。代价：+1 张后台缓冲内存；Present(0) 在深桶满时
偶有几 ms 的 DXGI 内排队（实测 presentBlock 7-9ms，预算内）。

## 拖拽 resize 旁路（修"空白区域慢慢填充"）

节拍门会把 modal sizing loop 里每个 WM_SIZE 的重画也限到合成节拍（慢合成下 ~9fps），
新暴露区域要 100ms+ 才填充、松手才恢复——直接操纵反馈被限流是错的。
`TryBeginDrawOrScheduleRetry` 在 `_isSizing` 期间旁路 credit 门逐事件即时重画：
Present(0) 不阻塞、深桶 + DXGI 队列吸收突发；期间账本只进不出（ack 照常入账、clamp
塌缩），无泄漏。

## 明确不解决

- 慢合成环境的屏幕刷新上限本身（DWM/远程链路的物理约束，应用侧只能保证不被它卡死）；
- Vulkan 后端同构改造（等待语义完全不同：fence/acquire UINT64_MAX 无限等待，需独立设计）；
- DComp composition 窗口（Commit 节奏不同，不启用，走旧路径）；
- 渲染线程默认开启（gating criteria 5-7 见 render-thread-design.md）。
