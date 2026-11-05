# 空白窗口内存优化与复现

## 验收口径与当前状态

目标是 Windows x64 下，一个正常可见且可响应的 800×600 空窗口，`Content=null`，使用框架默认自绘标题栏，进程稳定工作集始终小于 **20,000,000 bytes**。表格使用 MiB（1 MiB = 1,048,576 bytes）；20 MB 预算等于约 19.073 MiB。私有提交量另行记录，不能代替进程工作集。

以下为本机当地时间 2026-09-17 的最终验收复测，证据目录为 `final-acceptance-20260916`（目录按 UTC 日期命名）。复测使用已核对哈希的 `checkpoint-stability` 冻结产物，.NET 10.0.12、Windows 11 x64 10.0.26340、完整默认主题和默认 Auto 后端。每轮启动新进程，在窗口可见且可响应后稳定 10 秒，每 500 ms 采样，正式测量持续 20 秒，响应检查上限 100 ms。三种配置均为三轮、各 123 个正式样本，窗口检查失败数均为零。旧数据保留独立目录，没有混入本表。

| 配置 | 工作集均值 MiB | 稳定工作集最大值 MiB | 工作集最大字节数 | 私有提交最大值 MiB |
| --- | ---: | ---: | ---: | ---: |
| NativeAOT LowMemory | 21.950 | 25.441 | 26,677,248 | 11.664 |
| 普通托管 Release | 62.210 | 65.062 | 68,222,976 | 27.633 |
| 普通托管 Debug | 66.219 | 67.492 | 70,770,688 | 28.734 |

NativeAOT 三轮正式采样区间的最大工作集分别为 21.062、25.441、21.875 MiB；所有正式样本范围为 21.059–25.441 MiB，全部高于预算。第二轮在正式区间内出现升高，仍保持一个可见且可响应窗口，不能丢弃该样本。包含启动阶段的进程峰值，三配置分别为 25.441、65.234、69.258 MiB。NativeAOT 私有提交量最大为 12,230,656 bytes，正式工作集最大值距离预算仍差 **6,677,248 bytes**。此前同一冻结产物曾测得 22.223 MiB 最大值；本轮没有沿用较低旧值作为最终验收结果。

另从最新源码发布不含进程内日志、模块枚举或动作调度器的 `MinimalWindowProbe`，保留相同完整框架引用、主题检查和 800×600 默认空窗。三轮、123 个正式样本均值 22.181 MiB、最大 22.660 MiB（23,760,896 bytes），私有提交最大 8.660 MiB，窗口验证零失败。它是不同宿主的补充结果，不能与主矩阵混合；它同样未达到预算。

**20 MB 工作集目标尚未达到。** `Status=Passed` 表示测量完整且窗口检查通过；`WorkingSetBelowBudget=False` 才是当前预算结论。该结果没有证明此技术栈存在无法突破的内存下限。

### 多窗口与长周期结果

独立冷启动的两个、四个空窗口工作集最大值分别为 26.043、24.832 MiB，各 41 个正式样本、窗口检查零失败。不同进程的数值存在波动，不能据此认为四窗比两窗更省。下表来自同一个进程的生命周期测试，用于观察窗口增量；初始值是单窗进程基础占用，没有单独测量零窗口进程。

| 同进程阶段 | 工作集均值 MiB | 私有提交均值 MiB | 与前一阶段工作集差值 MiB |
| --- | ---: | ---: | ---: |
| 1 个窗口 | 21.960 | 8.471 | — |
| 增加到 2 个 | 24.129 | 10.266 | +2.169 |
| 增加到 4 个 | 24.532 | 11.582 | +0.403（新增两个） |
| 关闭到 2 个 | 24.512 | 11.574 | -0.020 |
| 双窗口重建第 1 轮 | 25.023 | 11.500 | +0.512 |
| 双窗口重建第 10 轮 | 25.621 | 11.523 | 相比第 1 轮 +0.598 |

共享资源、空闲帧缓冲压缩、自然 GC 和分配器页保留都会影响阶段差值，因此不能把这些数值解释为固定的每窗口对象大小。部分关闭后工作集仅小幅回落；对象和原生租约释放的专项断言通过，也不等于操作系统立刻收回所有驻留页。14 个阶段、150 个稳定样本均通过精确窗口计数及响应检查；这些样本在每次操作后的暖身期结束后采集，不证明所有过渡瞬间都无卡顿。

额外 300 轮双窗口重建完成了 602 个窗口的创建，正常退出，无 stderr。321 个进程样本的工作集峰值 31.180 MiB，私有提交峰值 15.883 MiB。分析脚本首尾约 40 秒观察区间的工作集均值为 28.487、28.583 MiB。被动 GC 记录最终为 2 个打开窗口、4 个仍存活对象（含 2 个已关闭窗口），最近一次 GC 堆为 2,177,776 bytes。这组有限时长数据没有重现先前约每轮固定增长的趋势，但不构成所有运行时长都无泄漏的证明。321 个进程样本没有 `Responding=false`；该诊断没有逐样本验证精确可见窗口数，不能冒充上述 150 个生命周期稳定样本的完整检查。

### 功能与稳定性检查点

三种构建均完成两轮真实控件、图像、特效、主题切换、窗口缩放和内容清空，每次清空都完成 Software 空白帧呈现。最终客户端约 840×640，不能与 800×600 新进程空窗混作同一预算场景。功能后空窗的稳态最大工作集分别为 NativeAOT **46.297 MiB**、Release **80.457 MiB**、Debug **80.148 MiB**；对应私有提交最大值为 22.742、27.746、28.500 MiB。完整功能进程的工作集峰值分别为 208.066、274.391、268.711 MiB，不能只报告清空后的低值而隐去功能使用成本。

首次功能初始化仍有可测延迟。此前 Release 暖身样本超过 100 ms 的失败记录保留在 `checkpoint-stability/release-features`。本轮只为显式功能暖身设定 1000 ms 响应上限，正式空窗采样仍为 100 ms；观测到的窗口检查最长耗时为 Release 317.043 ms、Debug 192.767 ms，正式阶段三种配置均低于 7 ms。该检查是采样时的消息响应耗时，不等同于完整初始化耗时，也不是无性能退化的证明。

本轮从当前源码重新构建测试工程，功能回归 **7076/7076 通过，0 跳过**；独立运行的模板性能基准 **1/1 通过**。两项此前失败分别是主题测试仍把纯字面画刷当作立即构造资源，以及 Dispatcher 测试复用线程上有遗留队列；夹具修复保留资源顺序和调度优先级断言，未更改生产调度语义。同一冻结产物在前一检查点的真实操作系统输入已观察到 Button.Click、CheckBox.Unchecked 和文本输入，之后返回 Software 空窗；独立空窗真实点击关闭正常退出。本轮自动功能动作没有被冒充为新的人机输入记录。

## 实现范围

### 空窗渲染与资源生命周期

`Window.EmptyRendering.cs` 在 Windows、Auto 后端、没有内容/特效/透明/背景材质/额外绘制需求时获取共享 Software 上下文租约。默认自绘标题栏仍正常渲染。多个空窗口共享进程上下文，各自保留必要的窗口表面。

加入真实内容、特效、自定义绘制等功能后，窗口通过完整后端创建路径升级渲染上下文。切换前停止渲染线程并释放旧上下文所属的缓存、保留层和目标；创建失败继续保留有效租约以支持恢复。后续实现已支持在内容和绘制需求消失后重新检查策略并恢复 Software。最后一个自动 GPU 窗口释放其租约后，上下文退休；独立持有的原生资源保留各自的后端引用，直到最后一个引用释放后才销毁后端。显式后端、嵌入表面或仍在使用的特效等需求会阻止自动切回。

Software 后端取消重复整窗 DIB，直接从现有帧缓冲呈现；线程池按工作量按需启动。空闲帧缓冲采用可恢复的紧凑存储，实际像素访问时恢复；读回、局部绘制与恢复失败路径有原生回归覆盖。托管 RenderTarget 根据原生实际持有的帧缓冲字节数成对更新 GC 内存压力，旧原生 ABI 则退回保守的历史最大逻辑面积记账。没有用 `GC.Collect` 或工作集裁剪制造采样结果。

D3D12 的部分特效、计时与渲染资源改为首次使用时创建。空窗不会通过预热提前创建 GPU 上下文。较早冻结版在两轮内容、主题与缩放后清空窗口，工作集最大约 122.996 MiB，私有提交约 150.066 MiB；那时上下文仍常驻。这是自动回收实现前的对照，不能当作当前版本的实测值，也不能混入新进程空窗的结果。

`NativeTextFormat` 在创建至销毁期间持有后端租约，保证外部字体格式不会引用已释放的字体工厂。所有格式原生调用，以及普通和逆变换文本绘制入口，使用调用期租约保护句柄；并发清理先使对象不可再用，最后一个已开始的调用结束后才销毁原生格式。全局测量缓存用代次和清理版本阻止旧上下文或清理前结果重新写入。

### 启动与共享资源

- 默认主题的 Style/模板按资源查找创建，保留资源来源、查找顺序、循环检测与失败重试语义；主题切换复用通用主题结构。
- XAML/AOT 类型注册目录与解析缓存按需建立，显式注册优先级与现有类型保留范围不变。
- 默认进程图标复用进程级 HICON/像素缓存，大、小图标的 Shell 回退合并为一次，保留高分辨率与显式图标行为。
- OLE 拖放注册跟随实际 `AllowDrop` 需求建立和撤销，窗口关闭释放对应引用与线程租约。
- Debug HUD 仅按需创建；关闭时移除视觉子树、清空字段和回调、释放渲染需求并停止计时，关闭后的 F3 路径无法重新建立 HUD。
- 任务栏属性使用 Windows SDK 的固定 PROPERTYKEY，省去四次名称目录解析；仍写入命令、显示名、图标和最后的 AppID。某项重启信息被 Shell 拒绝时仍保留分组身份。PROPVARIANT 使用完整的平台原生布局。
- 任务栏 Shell 函数和窗口属性存储使用受生命周期约束的模块租约；发布及销毁前的属性清理完成后释放对应引用，保留任务栏重启与分组功能。
- TitleBar 的备用按钮按实际需要创建；清空或更换模板时解除旧部件事件和引用，允许备用按钮恢复，并保持按钮状态同步。
- 单条依赖属性值内联存储；帧历史小容量起步并按需增长；类型元数据、转换器及查找缓存按首次使用初始化。
- 软件 WIC 解码用私有 HGLOBAL 流替换 `SHCreateMemStream`，保持输入只读、同步解码和失败释放行为。完整 Release/Debug 原生库都不再导入 SHLWAPI。进程外复测确认该 DLL 的 143,360 bytes 驻留消失；PROPSYS 仍保留，没有把它算作节省。

框架主题中严格符合条件的纯十六进制 SolidColorBrush 现在注册专用延迟条目。该条目保留注册时的 Dispatcher，并在并发首次读取时发布唯一画刷；不会复用带线程访问门禁的通用 Style 延迟条目。额外属性、名称、共享声明及依赖其他资源的画刷继续立即创建。各主题变体的键仍全部可用，复制后的共享身份、跨线程读写和 Changed 事件行为均由专项测试覆盖。

### NativeAOT 发布配置

`tests/Jalium.UI.MemoryProbe/Properties/PublishProfiles/LowMemory.pubxml` 使用 Release、win-x64、NativeAOT、`OptimizationPreference=Size`，并显式设置 `IlcDehydrate=false`。本机 .NET 10.0.12 的 Size 默认会生成 `--dehydrate`，将压缩运行时表在启动时展开为私有可写页面；显式关闭该选项保留映像映射，代价是可执行文件更大。最终编译响应文件含 `--Os`，不含 `--dehydrate`。

LowMemory 同时导入 `eng/aot/EmptyWindowLayout.props`，使用完整程序的方法布局提示，并启用可选 Windows API 的正常按需解析。没有删除未列出的方法或使用 reachability 裁剪。具体边界见 `eng/aot/README.md`。这些选项不保证任何应用达到 20 MB。

该配置不关闭全球化、反射、输入法、辅助功能、动态主题或任务栏功能。它是低内存发布配置，不是普通托管 Release 的内存结果。NLS、额外 ICF 折叠、其他数据布局等诊断实验均与正式产物分开；未采用改变原全球化行为的设置。

## 构建

在仓库根目录使用 Windows PowerShell。构建输出根使用绝对路径，并选择新的仓库外目录，避免改写或随临时产物清理而丢失证据。以下为本机 Visual Studio 2026 工具链的完整原生构建方式。

```powershell
$reproRoot = Join-Path (Split-Path (Get-Location) -Parent) `
  ('Jalium.UI-memory-evidence\repro-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$nativeBuild = Join-Path $reproRoot 'native-build'
$nativeOutput = Join-Path $reproRoot 'native-bin'
cmake -S src/native -B $nativeBuild -G 'Visual Studio 18 2026' -A x64 `
  "-DJALIUM_NATIVE_OUTPUT_ROOT=$nativeOutput" -DJALIUM_NATIVE_RID=win-x64 `
  -DJALIUM_BUILD_DEMOS=OFF -DJALIUM_BUILD_STATIC=OFF -DJALIUM_BUILD_VULKAN=ON `
  -DJALIUM_D3D12_BUILD_TESTS=OFF -DJALIUM_PLATFORM_BUILD_TESTS=OFF `
  -DJALIUM_SOFTWARE_BUILD_TESTS=OFF
cmake --build $nativeBuild --config Release --target jalium.native.package.complete
cmake --build $nativeBuild --config Debug --target jalium.native.package.complete

.\tools\verify-empty-window-memory.ps1 `
  -OutputDirectory (Join-Path $reproRoot 'verification') `
  -NativeOutputRoot $nativeOutput -Runs 3 -RebuildCycles 300
```

统一脚本分别构建 NativeAOT、普通 Release 和 Debug，保留编译参数、日志与载荷哈希，再串行执行七组内存场景。只有真实完成聚合原生构建才会生成完成标记；不复制标记、不关闭本地构建守卫。预算未通过时脚本仍保留全部已完成场景，最终返回失败。

## 测量

先结束本轮自己启动的其他探针，避免多组 UI 测试并行。脚本等待窗口真实显示并响应后，稳定 10 秒，再每 500 ms 采样 20 秒。每轮创建新进程；不清理操作系统文件缓存。

```powershell
.\tools\measure-window-memory.ps1 `
  -ProbePath (Join-Path $reproRoot 'verification\aot\Jalium.UI.MemoryProbe.exe') `
  -OutputDirectory (Join-Path $reproRoot 'cold-recheck') `
  -Runs 3 -StabilizationSeconds 10 -MeasurementSeconds 20 -RequireBudget

 .\tools\measure-window-lifecycle.ps1 `
  -ProbePath (Join-Path $reproRoot 'verification\aot\Jalium.UI.MemoryProbe.exe') `
  -OutputDirectory (Join-Path $reproRoot 'lifecycle-recheck') `
  -RebuildCycles 10
```

验证两轮功能加载、主题切换与原生缩放，并以仍可见的 Software 空窗结束：

```powershell
.\tools\measure-window-memory.ps1 `
  -ProbePath (Join-Path $reproRoot 'verification\aot\Jalium.UI.MemoryProbe.exe') `
  -OutputDirectory (Join-Path $reproRoot 'features-return-blank') `
  -Runs 1 -StabilizationSeconds 25 -MeasurementSeconds 10 `
  -StabilizationResponseTimeoutMilliseconds 1000 `
  -WindowResponseTimeoutMilliseconds 100 `
  -ProbeArgument '--exercise-features-return-blank'
```

功能验证成功标记必须包含 `generations=2 blanks=2 themes=2 resizes=2 finalBackend=Software`。旧的 `--exercise-features` 仍以内容附着状态结束；不要追加 `blank` 动作后继续套用它的旧结束状态断言。

冷启动脚本用 `-WindowCount 2` 或 `4` 测量独立多窗口场景；生命周期脚本默认执行 1→2→4→关闭到2→十轮重建2。初始阶段等待10秒、采样10秒，其余阶段等待5秒、采样5秒，并逐样本验证可见和响应窗口数。关闭只作用于脚本本次启动的进程。

`-RequireBudget` 可让冷启动脚本在任意正式样本达到或超过预算时返回失败。所有测量强制 `JALIUM_WORKING_SET_TRIM=off`，不调用强制 GC，不以最小化或隐藏窗口降低数字。Private Memory 记录的是私有提交量，不是私有工作集；模块分析另由 `profile-window-memory.ps1` 完成。

统一脚本拒绝继承非空的 `JALIUM_RENDER_BACKEND`，防止不同场景混用后端。新输出目录保护不会覆盖旧测量；重建脚本异常时保留部分样本，并只对本次创建的进程执行有界关闭与必要的终止。异常清理另有真实进程故障注入验证，不作为内存预算样本。

## 证据与限制

本机当前完整证据根为：

```text
D:\Users\suppe\source\repos\Jalium.UI-memory-evidence\final-acceptance-20260916
```

`executions.csv` 记录本轮构建、功能回归和十组测量，三项冷启动退出失败是严格预算失败，原始采样均保留。`memory-summary.csv`、`lifecycle-summary.csv` 和 `acceptance.json` 分别保存汇总及结论。`test-results/correctness.trx`、`template-performance.trx` 为本轮回归。主矩阵使用同级 `checkpoint-stability/{aot,release,debug}` 冻结产物，原发布参数与日志仍在该目录；本轮复核 182 个文件及 11 个发布时关键源码/配置的哈希均一致。最小宿主是本轮重新发布，证据另存 `minimal-publish*`、`minimal-aot`、`minimal-cold`。功能回归明确排除了 `PageSwapCostBenchmarkTests` 和 `AccentDragCostBenchmarkTests`，选定模板性能基准在独立进程中另外运行，不能写成所有性能基准均通过。

`profile-cold`、`profile-after-features` 保存本轮独立进程的驻留页快照。冷空窗为 MEM_IMAGE 17.375 MiB、MEM_PRIVATE 3.605 MiB、MEM_MAPPED 1.020 MiB；功能返回空窗后为 31.445、11.840、2.684 MiB。冷态主程序映像共 5.797 MiB，其中代码页 2.348 MiB、只读数据页 2.391 MiB、可写数据页 1.051 MiB。后者仍映射 D3D12Core、DXGI、DWrite、着色器编译器及其他系统组件；DLL 仍映射不等于 GPU 对象仍存活。快照不能替代连续预算样本，也未建立不可突破的技术下限。

本次矩阵使用 schema 3 测量器，暖身响应参数为 0 或 10–1000 ms。`scripts/` 和 `script-hashes.csv` 保存所用工具副本与哈希，逐样本输出包括正式阶段标记、工作集、私有提交、线程/句柄、窗口计数和响应耗时。此前 `checkpoint-stability` 的 schema 2 数据保留为历史证据，没有改写成新版本采样。

最终复核记录在 `final-verification.json`：2505 个源码/构建输入、212 个发布文件（含 30 个最小宿主文件）和 6 个测量脚本的哈希一致，无残留证据探针进程。这个结论限定于列出的文件与本轮前后检查，不代替全部历史工作区修改的独立审计。

较早仓库内 `artifacts` 目录曾丢失。原始未优化框架约百 MiB 的历史结果仅能作为会话转录，不作为可重新核验的原始样本。仍保留原始 CSV 的较早同场景 NativeAOT 候选版位于同级 `20260916-followup/baseline-portable-cold`：均值 26.458 MiB、最大 27.305 MiB。该版本已经包含部分优化，不是原始未优化框架。

当前最有价值的下一项实验是对冷态主映像实际触及的类型元数据、只读数据和初始化代码建立分配/页触及归因，针对具体热路径减少启动初始化，而不是继续随机更换已无收益的链接参数。正式区间的短时升高还应与帧缓冲恢复和实际绘制事件建立时间关联；现有 CSV 没有分配调用栈，不能断言那次峰值来自哪个分配。任何后续修改都必须重新完成同口径重复测量。单独更换内存指标、丢弃峰值、禁用文化或输入功能都不构成本目标的完成。
