# 输入与缩放的生命周期回归

本文补充 `input-resize-performance.md`，覆盖窗口反复显示、隐藏、关闭，组件卸载、页面替换，及定时器停止、重启、取消后的输入行为。公开鼠标、指针与尺寸事件继续保留；内部视觉更新仍由既有 CompositionTarget 帧时钟合并。

## 修复范围

窗口显示与关闭采用成对的帧订阅管理，取消关闭或 Closing 回调抛出异常时恢复可渲染状态，已经关闭的窗口不能重新创建原生资源。延迟重绘保存自己的代次和队列任务，替换或关闭时取消真实队列节点；旧计时器即使已经开始回调，也不能替新代提交画面。

DispatcherTimer 的每次启用拥有独立代次。停止或修改间隔后，旧 Rendering 快照、旧线程池回调及旧队列任务均失效；Completed/Aborted 清理只作用于所属任务。同步调度钩子在锁外执行，避免取消或重新配置时重入死锁。

ScrollViewer、ScrollBar 和 RepeatButton 在卸载时停止所属计时器、释放自身子树的临时捕获，并提交已接受的最终滚动、拖动或视觉状态。重新挂载复用原有组件与计时器，下一次输入才建立新代。RepeatButton 使用 DispatcherTimer 的单次 UI 分发；运行中修改 Delay、Interval 仍按毫秒转换为 TimeSpan。

页面替换与嵌套子树脱离会清理旧树的悬停、按下、捕获、手势和指针会话；其它窗口、替换页面以及同一批次内移回本窗口的子树保留自己的状态。输入路由的可复用数组在回调结束或抛异常后清空元素引用；命中测试的线程缓存不再强持有最后一次命中的整棵树。

延长到 180 轮后还发现渲染线程的独立生命周期缺口：`RenderTargetDrawingContext` 继承 `DispatcherObject`，会隐式注册线程专属 Dispatcher；`DispatcherCore` 的全局注册表强持有线程，原来的渲染线程退出路径没有撤销注册。现在 `RenderThreadLoop` 在 owner thread 的 finally 中释放已存在的 Dispatcher，未创建过 Dispatcher 的线程不会为清理而创建一个。停止/Join 与原生资源释放顺序保持不变。

Dispatcher 释放同时保证取消观察者异常不能截断清理：逐个取消待执行任务，尝试释放注册表、消息窗口和唤醒资源，再向调用方重抛第一个异常。测试覆盖普通退出、等待异常、取消观察者异常、其余任务仍被取消，以及真实 GPU 呈现后反复返回 Software 时旧线程注册消失、主线程 Dispatcher 仍正常。

最终前台检查还发现：曾经使用 D3D12 HWND flip 交换链的窗口，虽然已经返回 Software 并提交新帧，屏幕却仍显示旧内容。微软 `DXGI_SWAP_EFFECT` 文档的 Remarks 明确指出，同一 HWND 首次成功 flip Present 后，GDI 即使在交换链销毁后也不再生效。因此自动 Windows D3D12 使用框架已有的可解除 DirectComposition 呈现层，保留父 HWND 的 GDI 能力；设备恢复保留该选择，显式后端继续原有策略。原生目标销毁时先清除 composition root/content 并提交解除操作，再释放目标。该修复不重建主窗口。

像素回归单独启动真实前台窗口，在功能激活后和内容清空后抓取桌面实际像素，并在清空后检查内容区域全部恢复到背景色。抓屏发生在后台 PrintWindow 截图之前，避免后台截图本身掩盖显示问题。AOT、Release、Debug 都执行该检查；仅有 `Content=null`、后端为 Software 或 FrameHistory 增长不再被单独视为显示正确的证据。

## 验证口径

`measure-input-lifecycle.ps1` 启动真实可见的 MemoryProbe。每轮创建一个临时窗口，重复显示/隐藏，注入三组各 24 个 client mousemove，再提交 12 次真实 SetWindowPos 尺寸变化。每条受控输入的 PreviewMouseMove、MouseMove、PointerMove 和 PointerMoved 回调数分别核对；系统实际鼠标输入另行计数。

探针等待最终客户端尺寸与渲染目标一致，并用成功 Present 计数和 FrameHistory 证明最终尺寸已经显示。关闭后每轮至少静置 600 ms，最后一轮至少 2 s，核对关闭窗口不再产生输入、尺寸、生命周期回调或新帧。每五轮挂载功能内容，验证按钮、复选框、文本、图像、效果和主题，然后清空返回 Software。

订阅数量、回调数量、最终状态和正常退出为通过条件。工作集、私有提交、托管内存、GC 次数、线程、句柄及退休渲染上下文引用数同时记录。内存趋势是原始观测，不会仅因功能检查通过而自动判定内存达标；这里也不构成原先 20,000,000 字节空窗预算的通过证明。

生命周期探针的精确事件计数采用受控消息，真实鼠标与系统边框拖拽另用 `measure-window-drag-stress.ps1`。它通过操作系统鼠标输入覆盖连续移动、快速往返、持续拖边框及两者交替，并保留实际发送频率、CPU 时间和最后边界检查。

弱引用回归测试可以执行 GC 来判定对象是否被强引用保留；这些单元测试与进程内存采样分开。实际探针不执行强制 GC、工作集裁剪或隐藏测量窗口。

## 复现命令

先按 `empty-window-memory.md` 构建完整原生包，再将最新源码发布到新的独立目录。以下命令在仓库根执行：

```powershell
$evidence = 'D:\path\to\new-evidence'
$nativeOutput = 'D:\path\to\native-bin'
dotnet publish tests/Jalium.UI.MemoryProbe/Jalium.UI.MemoryProbe.csproj `
  -c Release -r win-x64 -p:PublishProfile=LowMemory `
  "-p:JaliumBuildRoot=$evidence\build" `
  "-p:JaliumNativeOutputRoot=$nativeOutput" -o "$evidence\aot"

.\tools\measure-input-lifecycle.ps1 `
  -ProbePath "$evidence\aot\Jalium.UI.MemoryProbe.exe" `
  -Rounds 60 -OutputDirectory "$evidence\lifecycle-aot-60"

.\tools\measure-window-drag-stress.ps1 `
  -ProbePath "$evidence\aot\Jalium.UI.MemoryProbe.exe" `
  -OutputDirectory "$evidence\physical-input" -PhaseSeconds 30 -Trace
```

Release 与 Debug 采用各自独立输出目录，将发布参数改为对应配置和 `-p:PublishAot=false --self-contained false`。所有测量目录拒绝覆盖既有数据。全套功能测试按现有方式运行，两个独立性能基准类与功能套件分开执行。

本机本轮证据根：`D:\Users\suppe\source\repos\Jalium.UI-memory-evidence\input-lifecycle-20260917`。最终运行、哈希及结果以该目录的 `final-report.md` 为准；历史失败和中间候选保留原名，不混入最终统计。

平台行为依据：Microsoft Learn 的 `DXGI_SWAP_EFFECT` Remarks、`IDCompositionTarget::SetRoot` 和 `IDCompositionVisual::SetContent` 文档。显示修复后的第一组前台像素验证保存在 `verified-pixels-aot`、`verified-pixels-release`、`verified-pixels-debug`。追加线程退出与取消异常修复后的最新产物使用 `lifetime-final` 前缀；`verified`、`worker-cleanup` 和较早 `final` 均保留为历史候选。

## 资源验收边界

订阅、回调和画面正确性通过，不代表完整进程内存已经达标。追加 PSS 句柄快照发现，每次 GPU 功能往返还会重复保留一组相同名称的 Section/Mutant 句柄：`{2627E361-24E2-4F14-99ED-A20D0685D8DD}_v22` 及对应 mutex。该 GUID 同时存在于本机加载的 NVIDIA Game Proxy `nvspcap64.dll` 中，模块信息和哈希保存在 `nvidia-object-fingerprint.json`。这组对象与已修复的框架 Dispatcher 注册分别统计；它不证明全部私有提交增长都由 NVIDIA 模块造成。

没有通过关闭未知句柄、强制卸载 DLL、改变外部覆盖层设置或裁剪工作集制造低读数。完整进程资源的剩余增长、重复功能后高位提交以及原先 20,000,000 字节预算的结果，以最终报告中的原始 CSV 和通过条件为准。
