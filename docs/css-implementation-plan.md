# Jalium.UI CSS 剩余实现与团队分工

更新日期：2026-10-28。用途：团队认领、并行实现、集成与验收。

当前状态：已有大量 CSS 能力，完整 CSS 目标尚未完成。2026-10-27 的独立复核发现旧验收遗漏了真实竖排、Ruby、隐藏、变换及背景裁剪问题，详见 [验收复核](css-validation-20261027.md) 及 [修复与复验记录](css-repair-validation-20261027.md)。下文旧计数及“已完成交付”对应各自历史快照与阶段范围，不能作为整个模块的规范完成声明。角色和里程碑是分工建议，负责人待团队认领。

## 目标与完成口径

先补齐当前实现的明确缺口，同时建立按规范条款追踪的支持矩阵。不能把“属性已注册”“能解析”“单个场景截图正确”当作完整实现。

建议把 [CSS Snapshot 2026 固定版本](https://www.w3.org/TR/2026/NOTE-css-2026-20260622/) 中正式定义的 CSS 模块作为首个完整性基准；其余候选模块、当前已经接入的较新草案和产品扩展分别登记。Snapshot 按规范成熟度组织模块，并不等于某个浏览器的功能列表。G0 负责锁定每个模块的版本和条款，后续升级也要记录变更。

Jalium.UI 的实现对象是 JALXAML、视觉树与文档节点。涉及 DOM、浏览器导航、HTML 专属状态或脚本 API 的能力，要明确框架对应的宿主接口和使用边界；不能仅因语法能解析就报告完成，也不要求为实现 CSS 另造一套浏览器。

本清单包含四类工作：

| 标记 | 含义 | 处理方式 |
| --- | --- | --- |
| 缺失 | 当前默认 CSS 注册表没有该入口，或源码有明确缺失分支 | 补齐入口、计算值、运行时消费和验证 |
| 部分 | 已有实现，但组合、宿主或后端有明确边界 | 复用已有路径补齐，不从零重写 |
| 待审计 | 尚无逐条规范与用例映射，无法判定完整性 | 先补支持矩阵，再拆成具体缺陷 |
| 待验收 | 有代码或自动化结果，缺实机、物理输入或辅助技术证据 | 安排对应环境运行，不能直接改为完成 |

下文列出了目前确认的缺口和需审计板块；在 G0 完成全模块映射之前，不把它当作已经穷尽所有 CSS 条款的清单。

## 当前基线与不要重复实现的能力

### 连续帧缓存与栅格精度增量（R18，2026-10-28）

接续 R17 的分数位置诊断，Software／Metal 保存向外取整的实际捕获范围，按物理像素相位与 DPI 决定重捕获；修正镜像纹理方向及负缩放文字消失。整数物理位移继续复用缓存，正式十帧序列记录 1,656 次真实合成。

正式全量 **24,189／24,189**、新增专项 **486／486**、重新编译的原生选定集 **65／65** 通过；R17 的 23,703 项精确名称与重复次数全部保留，**4,986 对**完整 RGBA 严格一致。两后端实际查看 40 张最终原生截图并操作选择、键盘、参考盒、分数位置和窄窗口。不透明背景仍有最大 1／2 通道差值，Metal 窗口一次中间恢复仍有局部 RGB 差异；它们继续开放，不纳入正式通过口径。另有一项新增 XML 文档警告待清理。完整 CSS、M1、Ruby、G0／E2 和跨平台仍为部分完成，详见 [连续帧缓存验证](css-retained-raster-fidelity-validation-20261028.md)。

### 运动路径参考盒增量（R17，2026-10-28）

接续 R16，`offset-anchor:auto`／显式位置按实际内容／边框参考盒解析；同值原点的本地值、绑定与动画来源切换会立即更新路径变换。位置解析复用已有 `<position>` 语法，保留边缘偏移；锚点、起点及混合长度动画使用真实尺寸、字体、容器和视口。补上 `ray(at …)`，圆／椭圆按规范从最右侧顺时针采样，并修复连续帧缓存层裁掉抗锯齿外缘的问题。

正式全量 **23,703／23,703**、专项 **1,028／1,028**、原生选定集 **65／65** 通过，2,070 对严格像素一致；R16 的 23,060 项名称及重复次数全部保留。额外诊断中的 Software 分数位置缓存差异、原生 Ruby 初帧与稳定帧差异仍开放，不包含在正式通过计数中。详细版本、反向对照和边界见 [运动路径参考盒验证](css-motion-reference-boxes-validation-20261028.md)。完整 CSS、M1 全部组合、Ruby、G0／E2 与跨平台验收继续保持部分完成。

### 变换原点增量（R16，2026-10-28）

本轮限定修复已验收：原生默认原点与 CSS 默认中心统一消费，相同变换对象的来源切换、隐式内容参考盒与 CSS 过渡取消恢复已补齐。macOS 全量 **23,060／23,060**、定向 **2,474／2,474**、原生选定集 **65／65** 通过，另有 52 个原生测试按模式排除；1,674 对严格像素一致，最终原生实例实际查看 50 张截图并检查选择、键盘、窗口变化和过渡。详细版本及边界见 [变换原点验证](css-transform-origins-validation-20261028.md)；完整 CSS 和完整 Ruby 继续保持部分完成。

R16 当时的接续项为 Motion Path 的 `offset-anchor:auto`／百分比锚点忽略不对称内容参考盒；其额外四项诊断换载 R16／R15 精确运行库均为两项失败、两个控制项通过。该历史缺陷现由 R17 接续处理，正式回归及当前结论见上节。

### 已有能力

下列能力已有实现。团队应从对应代码和 [CSS 使用文档](css-authoring.md) 接续，不能因旧记录中的占位说明把它们重新列为“尚未实现”：

- 样式表解析、资源加载、选择器、层叠、继承、变量、条件规则及编写元数据。
- fixed、sticky、block、inline、Flex、Grid、subgrid、浮动和部分逻辑属性。
- 生成内容、计数器、列表标记、首行与首字、选区样式、部分替换内容。
- 普通控件与文档中的表格、多列、分页、尺寸计算和部分内在尺寸插值。
- 背景、边框图片、渐变、遮罩、混合隔离、部分 SVG CSS 与文字轮廓绘制。
- 水平双向段落、共享行内塑形、字距、词距、自动间距、制表位、两端对齐、手动软连字符。
- 变长文本转换的源位置映射，包括一个源标量对应零个或多个显示标量；原文、选区和复制语义继续来自源内容。

上面每项仍有下文列出的边界，不代表整个模块已验收。Core 中的 Unsupported 占位项可能被 Controls 扩展注册覆盖，必须检查最终注册表及消费者。

### 本轮修复复验（2026-10-27）

已修复文档布局、伪元素失效、隐藏内容泄漏、Motion Path 变换组合与恢复、背景并集、嵌入控件源范围和滚动锚定回归。隔离候选最终全量为 **20,165 通过、0 失败、0 跳过、0 超时**；新增 15 项消费者测试在旧运行库上全部失败，在修复版全部通过。最终 Metal 窗口已重启并实际操作，载荷与测试版本哈希一致。

独立 10 项复核仍有 3 项失败，均属于 T4 的真实竖排、文字方向/纵中横和 Ruby 解析。E2、G0、跨平台与设备验收继续开放。实时工作区另有并行绘制像素对齐改动未同步至本候选；详细边界、命令、截图和计数对照见 [修复与复验记录](css-repair-validation-20261027.md)。

### 历史阶段结果

最近验收记录为 text-transform-source-map-20261025 的 source-stage11：

| 范围 | 结果 | 边界 |
| --- | --- | --- |
| macOS 托管回归 | 19,471 通过，0 失败，2 跳过 | 冻结源码快照，不代表其他聊天的并行窗口改动也已覆盖 |
| 渲染与工作线程重放 | 5,568 组通过，含 96 组仿射场景 | macOS Software、Metal Impeller、Metal Vello；Linux Software、Vulkan Impeller、Vulkan Vello |
| 视觉检查 | 51 组实际与参考图，6 张汇总图已查看 | 不是完整 WPT 或物理输入验收 |
| 反向对照 | 同一最终测试程序集换回修改前运行库，183 失败、39 通过 | 18 项恒等文本校准全部通过 |
| 原生载荷 | 核对了来源与哈希并复用 | 本轮没有重新编译 C++ 或重跑原生套件；原有 6 项 Linux 原生失败仍开放 |

两个跳过项为 AppKit 完整变换几何，以及私有预编辑取消后密码与选区保留。它们不能自动获得最终发布豁免。

[运行时属性清单](css-authoring-inventory.json) 保存了 346 个已登记属性，含别名和简写。该清单来自上述已验收运行库；本次核对的 386 个含 CSS 命名的源码输入与验收快照一致。Supported 仅表示注册与解析入口存在，不表示全部值、宿主、组合和平台均正确。未登记的模块也不会出现在这 346 项中。

### 团队开始前先交接完整源码

当前 CSS 成果包含大量工作区变更。不能只 checkout 当前 HEAD，就认为拿到了与验收相同的完整实现；上一阶段的 scoped.patch 也只是该阶段增量，不能单独当作全部 CSS 源码。

集成人员先建立可共享、可构建的源码基线，登记提交、未提交补丁、资源和原生载荷身份。保留无关并行工作，不执行 reset、clean 或自动 stash。源码与载荷缺失时先补交接材料，不让各组在不同基线上改同一套接口。

本机历史证据位置：

- /Volumes/My PSSD/.codex-css-acceptance/text-transform-source-map-20261025/progress.json
- 同目录的 source-stage11.json、source-audit.json、runtime-audit.json。
- 同目录的 delivery-manifest.json、delivery-verification.json 与 snapshot-stage11。

这些本机路径供移交定位。集成负责人应把团队需要的源码、用例、构建说明和证据复制到团队可访问的位置；不要把已丢失的旧 artifacts 链接作为唯一依据。

## 建议团队分工与并行边界

| 角色 | 主责任务 | 协作边界 |
| --- | --- | --- |
| 集成与标准负责人 | G0、G1、E1、E2 | 管理基线、公共接口、支持矩阵和公共注册表 |
| 文本布局负责人 | T1、T2 | 与原生字体负责人共同确定字符、字形和光标边界 |
| 原生字体负责人 | T3、N1 的文字部分 | 维护后端能力接口和字体行为 |
| 布局负责人 | T4、L1、L2、L3、L4 | T4 的逻辑坐标契约先评审，其他布局按该契约接续 |
| 文档与编辑负责人 | D1、D2 | 保留原文和公开对象身份，不另造第二套文本位置 |
| 绘制负责人 | P1、P2、M1 | 与布局负责人共同确定变换、裁剪及命中坐标 |
| 滚动与平台负责人 | R1、R2、R3、N1、N2 | 原生滚动、系统设置、后端与设备行为 |
| QA 与性能负责人 | Q1，参与全部任务验收 | 从开始就提供独立参考、CI 和设备验证 |

团队较小时可以合并角色，任务 ID 保持不变。每个任务只能有一个主负责人，跨组依赖写进工单。

优先级含义：P0 为当前交接和公共基础；P1 为明确剩余能力；P2 为较大扩展或依赖较多的工作。P2 表示后排，不表示从完整目标中删除。

### 第一批可以立即认领的工作

| 任务 | 可独立开展的内容 | 合入前依赖 |
| --- | --- | --- |
| T1 | 最小失败用例、显示分片映射、截断和制表对齐组合 | G1 的映射契约 |
| T2 | 跨节点语言上下文、自动断词引擎与词典方案 | G1 的语言与源偏移约定 |
| T3 | 原生能力盘点、复杂脚本和连字光标用例 | G1 的后端契约 |
| L2 | 表格百分比、重叠跨度和 caption 最小用例 | L1 的尺寸确定性规则 |
| L3 | 重复表头表尾、列平衡和分片失败用例 | G1、L2 的源片段约定 |
| P1 | Run/Span 效果缺口、后端遮罩能力用例 | G1 的录制与绘制约定 |
| R1 | overflow 的真实滚动语义、吸附与滚动属性入口 | G1 的逻辑坐标约定 |
| N2、Q1 | 原生失败重现、runner、WPT 桥接与物理设备矩阵 | 可从第一天并行，不等待所有功能完成 |

## 公共基础

### G0 支持矩阵与规范范围

状态：待审计。优先级：P0。主责：集成与 QA。

从 Snapshot 的模块列表开始，覆盖语法、属性和值、选择器、at-rule、计算值、使用值、布局、绘制、交互和平台。建立一个可机器读取的矩阵，不能只统计属性名称。

每条至少记录：模块与固定规范链接、条款、语法或值、消费者、平台后端、当前状态、限制、任务 ID、测试 ID、证据、负责人。未知、未实现、部分实现、已自动验证、已设备验证分开存储。当前属性 JSON 可作为种子，不能直接改名为完整支持矩阵。

验收：

- Snapshot 范围内每个模块都有条目；超出范围的草案和扩展单列。
- 已登记属性与未登记模块都能追踪；重名、别名、简写不重复计覆盖率。
- 一条功能不能在解析通过但消费者降级时被标为全部完成。
- 规范与浏览器有差异时，保留条款、浏览器版本和最小用例，不凭单个浏览器结果覆盖规范。

入口：[CssAuthoring](../src/managed/Jalium.UI.Core/Css/CssAuthoring.cs)、[CssPropertyRegistry](../src/managed/Jalium.UI.Core/Css/CssPropertyRegistry.cs)、[CssDiagnostics](../src/managed/Jalium.UI.Core/Css/CssDiagnostics.cs)。

### G1 共享数据与接口契约

状态：已有基础，需扩展。优先级：P0。主责：集成、文本、布局与原生负责人。

先确定以下接口，再让各组并行改实现。下列为需要表达的数据，不要求照抄为新的公开类型：

- 源范围：来源对象、UTF-16 起止、字素边界、语言、样式版本。
- 显示片段：显示起止、所属源范围、物理行、段落方向、生成或隐藏标志。一个源范围允许拥有多个显示片段和多行几何。
- 字形与光标：glyph cluster、逻辑与视觉方向、连字内部光标、前后亲和性，不能用字符串长度互相代替。
- 逻辑坐标：inline/block 轴及 start/end 到物理坐标的映射，供布局、滚动、绘制、命中和辅助技术共用。
- 原生能力：按操作查询能力；不支持时明确降级，不根据操作系统名称猜测整个后端支持情况。
- 录制帧：持有字体、画刷、段落和源映射的不可变快照；改变原对象或换线程不能改写历史帧。

验收：T1 的扩展字符跨片段用例、D1 的跨宿主位置、P1 的录制快照使用同一套约定。可选原生扩展沿用已有兼容机制，不直接改变已发布接口的虚表布局。

入口：[CssBidiProjection](../src/managed/Jalium.UI.Core/Css/CssBidiProjection.cs)、[NativeTextParagraph](../src/managed/Jalium.UI.Interop/NativeTextParagraph.cs)、[TextBlock.CssInline](../src/managed/Jalium.UI.Controls/TextBlock.CssInline.cs)。

## 文本与国际化

### T1 变长映射的剩余组合

状态：托管与 macOS 离屏渲染已完成（2026-10-26），待跨平台与实机设备验收。优先级：P0。主责：文本布局。依赖：G1。

已有零对一、一对多显示映射，并已完成以下分片和排版组合接入：

1. 处理显示片段边界位于同一源字符内部的情况：`CssBidiProjection` 支持 `softDisplayBreaks: (SourcePosition, DisplayOffset)` 与 `NativeDisplayPosition(source, displayOffset, trailing)`，`HasSplitBidiPaintWrap` 允许同一源字符内部的连续显示字素跨行拆分（如 `ß→SS`、`ﬃ→FFI` 在 `break-all` / `anywhere` 下跨行），单控件与共享行内段落按 `[SliceNativeStart, SliceNativeEnd)` 裁剪每行字形并保留多行源几何，同时禁止拆分单显示字素展开（`İ→i+◌̇`）或 UTF-16 代理对。
2. 接通 `text-overflow: ellipsis`、`TextTrimming`、`line-clamp`（含自定义 `block-ellipsis`）、生成省略标记、`::first-line` 和 `::first-letter` 投影（传播 `LanguageProperty` 并保持首字全词上下文与正文转换分离）。生成标记的几何与所有权独立，不写回正文。
3. 补齐普通 LTR 的“变长转换＋保留制表符＋扩展两端对齐”（`DrawPaintWrapSegment` 在含保留制表符与绘制文本变化时走 `DrawPaintedRunSegment`），保持最后一个有效制表符之前的锚点与原有制表尺、光标语义。
4. 覆盖同长度但内部偏移变化、删除附加符、补充平面字符、跨样式源和宽度反复改变。

验收记录（`/Volumes/My PSSD/.codex-css-acceptance/css-t1-display-map-20261025/`）：
- `CssLengthChangingBidiTests` + `CssBidiShapingProjectionTests`：`275 通过，0 失败，0 跳过`（含工作线程录制重放与 29 张实际 PNG 截图核验）。
- 反向对照：使用同一测试程序集对冻结修改前 `Jalium.UI.Managed.dll` 运行新增 T1 用例，触发 `12 失败`（修改后 `0 失败`）。
- 仍待完成：Windows/Linux 原生后端与物理输入、VoiceOver 实机验收（不因离屏软件渲染通过而豁免）。

入口：[显示映射守卫](../src/managed/Jalium.UI.Controls/TextBlock.CssBidiPaintMap.cs)、[PaintWrap](../src/managed/Jalium.UI.Controls/TextBlock.PaintWrap.cs)、[单控件 bidi](../src/managed/Jalium.UI.Controls/TextBlock.CssUnicodeBidi.cs)、[共享 bidi](../src/managed/Jalium.UI.Controls/TextBlock.CssInline.Bidi.cs)、[现有映射测试](../tests/Jalium.UI.MacOS.Tests/CssLengthChangingBidiTests.cs)。

### T2 跨节点语言上下文与自动断词

状态：托管与 macOS 离屏渲染已完成（2026-10-26），待跨平台与实机设备验收。优先级：P1。主责：文本布局。依赖：G1。

1. **跨节点文本转换与词上下文（`CssInlineWordContextWindow`）**：统一 `TextBlock`、`Run`、`Span`、匿名源、行内 `::before`／`::after`、流外内容（`float`／`position: absolute|fixed`）和隐藏内容（`display: none`）的上下文边界；普通行内边界（含行内边距／边框）与空行内盒不终止单词上下文，真实空白、硬换行、块边界与 `inline-block` 原子盒终止上下文。支持跨节点希腊语末尾 sigma（`ΛΟΓ|ΟΣ → λογος`）、立陶宛语 `More_Above`（`Į|\u0307A → į̇a`）、土耳其语 `Before_Dot`、`capitalize`（含跨节点连字符词 `super-|cali|fragilistic → Super-Califragilistic` 与荷兰语 `i|jsvrij → IJsvrij`），并在相邻源文字、可见性或语言变化时自动失效受影响的相邻伙伴。
2. **离线自动断词与正字法拼写变化（`CssHyphenator` + `CssHyphenationData`）**：引入带许可证、上游版本与 SHA-256 完整性校验的离线 Liang／libhyphen 词典（`tools/hyphenation-dictionaries/` + `tools/generate-css-hyphenation-data.py`，含 `en-US`、`de-1996`、`de-1901`、`nl`、`hu`、`ca`、`fr`、`es`，零联网依赖）。单控件与跨节点共享流均支持 `hyphens: auto`、语言特有断行拼写变化（`de-1901` `ck→k-k`、`nl` `ë→e-e` 与 `ee→é-`、`hu` `ssz→sz-sz`、`ca` `l·l→l-l`）、显式 `U+00AD` 覆盖自动词典，以及完整逻辑词上的 `hyphenate-character`、`hyphenate-limit-chars`、`hyphenate-limit-lines`、`hyphenate-limit-last` 和 `hyphenate-limit-zone`。缺词典或校验失败时安全回退，不改写源文本、`SelectedText` 或 UTF-16 偏移。

验收记录（`/Volumes/My PSSD/.codex-css-acceptance/css-t2-context-hyphenation-20261026/`）：
- `CssHyphenationWordContextTests`（`120 通过，0 失败`）及全量双向／行内回归套件（`758 通过，0 失败，0 跳过`），含工作线程录制回放与 2 张汇总 PNG 截图视觉核验（`t2-cross-node-text-transform-and-word-context.png`、`t2-auto-hyphenation-and-spelling-changes.png`）。
- 反向对照：使用同一测试程序集对冻结修改前 `Jalium.UI.Managed.dll` 运行 `CssHyphenationWordContextTests`，触发 `19 失败`（修改后 `0 失败`，见 `negative-control.log`）。
- 仍待完成：Windows/Linux 原生后端与物理输入、VoiceOver 实机验收。

入口：[TextTransform](../src/managed/Jalium.UI.Controls/TextBlock.TextTransform.cs)、[特殊大小写](../src/managed/Jalium.UI.Controls/CssUnicodeSpecialCasing.cs)、[自动断词引擎](../src/managed/Jalium.UI.Controls/CssHyphenator.cs)、[离线词典数据](../src/managed/Jalium.UI.Controls/CssHyphenationData.cs)、[共享断词](../src/managed/Jalium.UI.Controls/TextBlock.CssInline.Hyphenation.cs)、[词上下文测试](../tests/Jalium.UI.MacOS.Tests/CssHyphenationWordContextTests.cs)。规范入口：[CSS Text](https://www.w3.org/TR/css-text-3/)。

### T3 复杂脚本 字体组合与原生段落

状态：托管与 macOS 原生（CoreText／Metal／Software）已完成（2026-10-26），待 Linux／Windows 原生后端与实机设备验收。优先级：P1。主责：原生字体与文本布局。依赖：G1。

1. **原生与托管 `font-variant-position`／`font-variant-caps`／`font-width`／`oblique`／光学字号／字体度量覆盖组合**：
   - 修复 macOS 原生 `CreateApplePositionText`（`metal_font_position.h`），将双向隔离控制符与软连字符（`0x00AD`、`0x061C`、`0x200B..0x200F`、`0x202A..0x202E`、`0x2060..0x206F`、`0xFEFF`）与空白一同视为可忽略格式控制符，避免在双向段落投影中把原生 `sups`／`subs` 误判为缺字而强制回退到合成上下标（`mode = 2`）。
   - 打通 `TextBlock` 单控件双向段落（`TextBlock.CssUnicodeBidi.cs`）、跨来源共享双向段落（`TextBlock.CssInline.Bidi.cs`）、塑形间距（`TextBlock.ShapedSpacing.cs`）、行内塑形（`TextBlock.CssInline.Shaping.cs`）与原生双向行（`TextBlock.NativeBidi.cs`）在 macOS 上的 `font-variant-caps`（原生 `smcp`／`c2sc` 与合成大小写／一对多展开 `ß → SS`、`İ → İ`）、`font-variant-position`（原生与合成 `super`／`sub` 逐片段字体计划切片 `PositionRunAt`）及非 100% `font-stretch`／`font-width` 组合。
   - 在行内盒代理（`TextBlock.InlineBoxes.cs`）与文档视图代理（`TextBlock.DocumentView.cs`、`::first-line`、`::first-letter`、多列／FlowDocument 段落）中通过 `CopyCssFontState` 与 `DocumentFontSource` 完整保留精确百分比 `FontStretchPercentage`、`FontStyleComputed`（`oblique <angle>`）、`FontFamilyComputed`、光学字号 `font-optical-sizing`、`font-size-adjust` 与作用域 `@font-face`／`@font-feature-values` 及度量覆盖（`ascent-override`、`descent-override`、`line-gap-override`、`size-adjust`）。
2. **复杂脚本连接、连字内部光标、跨节点选区与命中一致性**：
   - 支持跨 `Run`／行内盒／兄弟 `TextBlock` 的阿拉伯文连写（含 ZWJ／ZWNJ 显式控制）、拉丁必需／常用连字（`ffi`）及组合附加符（`a + \u0301`）在双向重排、字距、制表符、自动间隔、两端对齐、自动断词拼写变化（如 `de-1901` `Zucker → Zuk-ker`）与省略截断下的统一保留段落塑形。
   - 补齐 `HasCssInlineBoxes` 激活时的 `TextContentContains`（`InlineSourceContains`）、`InlineSourceIndex` 盒距离优先判定、`DocumentTextBounds` 共享双向段落范围查询以及 `CssInlineBidiPart.Boundary`／`Hit` 字素簇选区边界，确保复杂脚本、隔离边界与行内边框／内边距下的 `GetCharacterRect`、`GetSelectionGeometry` 与 `GetPositionFromPoint` 一致，且工作线程录制回放与主线程逐像素一致。

验收记录（`/Volumes/My PSSD/.codex-css-acceptance/css-t3-shaping-font-combinations-20261026/`）：
- 原生 CTest：`18/18` 字体与文本原生测试套件全过（`jalium.native.metal.font-position` 含新增双向控制符与软连字符 `sups`／`subs` 用例共 `336 checks passed`）。
- 托管与离屏渲染套件：`CssComplexShapingFontCombinationsTests`（`36 通过，0 失败`）及全套双向／行内／断词／变长映射回归套件（`1043 通过，0 失败，0 跳过`），含工作线程录制回放与 2 张汇总 PNG 截图视觉核验（`t3-bidi-caps-position-width-and-metrics.png`、`t3-inline-boxes-complex-scripts-and-document-view.png`）。
- 反向对照：使用同一测试程序集对冻结修改前 `Jalium.UI.Managed.dll` 运行 `CssComplexShapingFontCombinationsTests`，触发 `12 失败`（修改后 `0 失败`，见 `negative-control.log`）。
- 仍待完成：Linux 自研文本引擎的阿拉伯语复杂上下文连写与 GDEF 连字光标、Windows DirectWrite／D3D12 实机与物理输入、VoiceOver 实机验收。

入口：[NativeTextParagraph](../src/managed/Jalium.UI.Interop/NativeTextParagraph.cs)、[原生上下标](../src/native/jalium.native.metal/src/metal_font_position.h)、[行内盒字体与命中](../src/managed/Jalium.UI.Controls/TextBlock.InlineBoxes.cs)、[单控件 bidi](../src/managed/Jalium.UI.Controls/TextBlock.CssUnicodeBidi.cs)、[共享 bidi](../src/managed/Jalium.UI.Controls/TextBlock.CssInline.Bidi.cs)、[共享 bidi 命中](../src/managed/Jalium.UI.Controls/TextBlock.CssInline.BidiShape.cs)、[字体计划](../src/managed/Jalium.UI.Core/Css/CssFontRenderingPlan.cs)、[组合塑形测试](../tests/Jalium.UI.MacOS.Tests/CssComplexShapingFontCombinationsTests.cs)。

### T4 垂直书写 逻辑轴与 ruby

状态：部分。macOS 的独立 TextBlock／普通 Run 已具备真实竖排、统一 upright、竖排字体特性与列明的垂直度量／原点／BASE、纵中横基础；原始 TextBlock 之间及带 CSS 盒的 Run／Span 共享段落已接入四种竖向书写模式、mixed／upright／sideways 的来源范围覆写、原文几何／命中与来源内纵中横基础。局部直立的 bidi、父级基线及动态切换在列明的 macOS 用例中通过。更广字体、组合字符跨来源样式边界、效果／正交约束、完整文档／编辑器、多列／分页、完整 Ruby、其他平台与设备验收继续开放。Ruby 已接入五种 display 角色、基础真实横竖排、多层注音、分配及共享断行；完整规范边界仍待补齐。优先级：P1。主责：布局，原生字体协作。依赖：G1。

已有基础：`writing-mode`、`text-orientation`、`text-combine-upright`、Ruby 属性的部分解析和枚举，逻辑边与坐标换算，以及纵中横分段、注音坐标计算辅助函数。这些基础不能代替实际文本格式器。

2026-10-27 后续增量：增加 `jalium_text_paragraph_create_vertical` 与 `NativeTextParagraph.TryCreateVertical`。macOS 的 Metal/Software 共用 CoreText 原生竖排字形段落；段落保持逻辑 inline/block 坐标、UTF-16 源位置、组合字符与 emoji 的光标边界，并支持换列和不可变绘制快照。通过字体的竖排原点恢复逻辑位置，补全双向隔离符边界的光标 affinity，隔离符不参与内容命中。旧创建接口及其 provider vtable 保留。

本增量重建了原生载荷并通过 51 项 CTest、原生导出/CPU 架构检查及 175 项相关托管回归（含 12 项新竖排原生测试）；同一测试程序集换回旧原生载荷，12 项新测试全部失败。已查看 Software 离屏实际像素：汉字正立、括号采用竖排字形、拉丁字母侧排，与整段横排旋转有明确差异；本增量的 Metal 专项验证塑形和几何，未据此声称竖排 Metal 窗口验收通过。

原外置临时证据目录已不再位于原路径，后续从当前源码再次冻结、构建及复验，将新证据保存到仓库 `artifacts/css-vertical-20261027-recovered-d477103f/`。新目录中的 51 项 CTest、175 项定向回归、12 项旧原生载荷反向对照及导出检查已重新执行；该原生基础快照的完整聚合回归为 20,177 通过、0 失败、0 跳过。两份着色器从仓库原生缓存复用并登记来源，没有将其计作新编译着色器。

后续 TextBlock 增量已消费这一入口：逻辑 inline/block 尺寸驱动测量和换列，逐列变换绘制字形，源矩形、命中、选区、裁剪与助记下划线回到物理坐标。`vertical-lr` 单独推进列并旋转各列，避免镜像字形；CSS 间距使用新增的 advanced vertical 段落入口。父级书写模式变化同时使后代段落及绘制缓存失效。`mixed` 的 CoreText 字形和 `sideways` 路径已有实际像素验证；41 项新消费者测试、51 项原生测试与 94 项相关回归通过。旧托管载荷反向对照的 6 项真实消费者测试全部失败；仅撤回选区修复的 12 项对照也全部失败。

本增量的最终全量回归为 20,220 通过、0 失败、0 跳过、0 超时。实际 Metal 窗口已验证四种模式的跨列拖选、Command+A、Tab/空格操作、可见焦点、列长度切换、有边框/无边框和原生缩放后的恢复；手拖窗口边框未通过工具触发，未计通过。源码、全量回归和窗口记录位于 `artifacts/css-textblock-vertical-20261027-b1eb1398/`，完整状态及剩余边界见 [TextBlock 竖排验证记录](css-textblock-writing-mode-validation-20261027.md)。这些计数只证明各自测试范围；FlowDocument、RichTextBox、共享 CSS 行内格式器、完整文字方向/纵中横、Ruby 及其他平台仍未闭环，不得据此将整个 T4 或此前三项独立 T4 复核统一标为完成。

后续 mixed 字体修复：固定 Unicode 18 方向数据，按完整组合字符设置 U/Tu/Tr 与 R 方向并处理 Me 包围符例外，使用中央基线类对齐，修复片段和光标的逻辑坐标。此前 Arial、Times New Roman、Georgia 的默认 CoreText 竖排会把拉丁文字直立；现在采用其水平塑形后侧转。Software/Metal 各 40 组独立 CoreText 与实际后端像素对照、72 项定向回归、53 项原生回归及 20,237 项全量托管回归通过，失败/跳过/超时均为 0；已实际操作四字体 Metal 窗口。旧原生载荷检出像素差异，四个字体消费者对照为 3 失败、1 通过。详见 [混合竖排修复与验证](css-mixed-orientation-validation-20261027.md)。本轮不代表 upright、纵中横、任意字体的 vert/vrt2 与基线合成约束、共享行内/文档/编辑器或跨平台完成。

后续 upright 增量：新增独立可选原生 provider 和普通/advanced 创建入口，按完整组合单元保留水平塑形并反向旋转，使用真实垂直度量，修复 delegate 组合格的末端光标、选区与片段。强制所有字符按 LTR 排列，同时保留计算的 RTL 供继承，接入文本 start 对齐及 clamp 标记。Software/Metal 各 160 组独立字形定位与真实后端帧对照、带独立垂直度量字体的原生测试和 113 项相关托管测试、55 项原生回归和 20,257 项全量托管回归已通过，失败/跳过/超时均为 0；四字体 Metal 窗口已验证拖选、跨列选区、键盘、方向和计算 RTL 切换。具体证据与限制见 [直立竖排验证](css-upright-validation-20261027.md)。水平 CTLine 的 vert 开关并不可靠生效，因此标点方向、vert/vrt2 和基线精度仍开放；本增量不代表完整 text-orientation 或 T4 完成。

后续字体特性增量：为 upright 单元与 mixed 的原生竖排单元建立 GSUB 私有别名适配，默认开启 vert、关闭 vrt2，保留作者显式设置、其他特性及实际字体/变体轴；同步修复变量字体更新特性时静默回退系统字体的问题。相邻文本段和嵌套后备字体的原始覆盖值保存在源属性中，已有段落不随格式修改变化。Software/Metal 各 186 组独立原始字体字形/原生栅格/真实后端帧对照、770 项表重映射断言、279 项相关托管测试与 58 项原生选择集通过；实际四字体 Metal 窗口已完成标点开关、拖选、键盘、换列与原生缩放恢复。最终完整托管回归为 20,269 通过、0 失败/跳过/超时；具体证据与边界见 [竖排字体特性验证](css-vertical-font-features-validation-20261027.md)。U/Tu/Tr 的完整 vmtx/VVAR 度量、缺失 BASE 的合成、mixed 的 R 侧排与 sideways 的显式保留标签覆盖等仍待实现/验收；T4 保持部分状态。

剩余实现与验收：

1. 将已验证的独立 TextBlock 竖排扩展至共享 CSS 行内格式器、文档与编辑器，贯通真实测量、换列、字形方向、绘制、裁剪及源范围命中；补齐任意字体的基线／origin／bearing、更多混合文字与双向文本、正交嵌套。
2. 独立 TextBlock 已接入 `text-combine-upright` 的实际塑形与布局，覆盖 all／digits、三种字形方向、原文、选区、命中、取消／恢复和空白规则。继续扩展共享行内、文档／编辑器、自定义 tab-size、复杂 BASE／字体特性及多脚本／变量后备字体组合，完成完整矩阵验收。
3. Ruby 的五种 display、当前匿名来源、位置／对齐、逐行 merge／auto 和原文自动隐藏已有真实消费者，见 R12—R15 的验证记录。字体内容盒、普通行距与结构容器适用性已有增量；继续补齐完整匿名／空白规则、剩余盒模型／intrinsic 与基线、复杂 leading／overhang、方向组合及文档／编辑器消费者；现有覆盖不能作为完整 Ruby 的完成声明。
4. 复验逻辑尺寸、Flex／Grid、表格、多列、分页、定位及编辑器在竖排下的组合行为。
5. 增加会在当前实现上失败的独立几何、字形像素、原文选区和命中参考；用真实 native／withoutborder 窗口完成键盘、焦点与辅助技术验收。仅切换截图标签不算窗口验收。

早期复核证据（历史基线，后续独立 TextBlock 增量的修复范围见上文）：2026-10-27 独立探针中，横排与 `vertical-rl` 的实际 TextBlock 像素相同；方向与纵中横切换也未改变实际字形；上述五个 Ruby display 值均被实际解析器拒绝。原 `CssWritingModesRubyAndAnchorTests` 的通过记录只证明辅助函数等局部行为，不能作为本任务完成依据。

入口：[书写模式、纵中横与 Ruby 辅助函数](../src/managed/Jalium.UI.Core/Css/CssWritingModeProperties.cs)、[逻辑边](../src/managed/Jalium.UI.Core/Css/CssLogicalBoxEdges.cs)、[display 解析](../src/managed/Jalium.UI.Core/Css/CssDisplayProperties.cs)、[文本消费者](../src/managed/Jalium.UI.Controls/TextBlock.cs)、[原生段落绑定](../src/managed/Jalium.UI.Interop/NativeTextParagraph.cs)、[竖排原生消费者测试](../tests/Jalium.UI.MacOS.Tests/CssVerticalNativeParagraphTests.cs)、[现有局部测试](../tests/Jalium.UI.MacOS.Tests/CssWritingModesRubyAndAnchorTests.cs)。

后续原生字格度量增量：mixed / upright 的 U/Tu/Tr 路径接入实际塑形字形的 vmtx/VVAR、映射及 avar/avar2；保留上下文、字偶定位、连字、分解字形和附标，逐列重算断列后的字形推进量，统一光标／选区与 CSS 字符间距。同步保留私有可变字体与后备链的实例身份。Software/Metal 各 336 组独立网格／实际后端像素对照、715 项相关托管复测与 60 项原生选择集已通过，Metal 窗口完成字重切换、跨列拖选、键盘与缩放恢复。最终完整托管回归为 20,300 通过、0 失败／跳过／超时；冻结候选边界见 [原生竖排度量验证](css-vertical-metrics-validation-20261027.md)。BASE、gvar-only 度量、更多字体组合、共享布局／文档／编辑器、纵中横、Ruby 和其他平台继续开放；T4 保持部分状态。


后续 gvar／零推进量增量（2026-10-28）：补齐没有 VVAR 的 TrueType 可变垂直推进量，解析共享／独立峰值、点号与 delta、区域插值、avar/avar2、多轴与未变换组件的 USE_MY_METRICS；存在 VVAR 时不重复累加 gvar。合法的 0 字格保留墨迹和原文，缺少垂直度量才合成 em。修复 font-size-adjust 缩放私有可变字体时的系统字体回退与临时竖排矩阵污染，覆盖主字体和后备链。Software/Metal 各 1,226 对严格像素对照、20,863 项便携读取器检查及 ASan/UBSan、865 项相关托管测试与 61 项原生选择集通过；真实 Metal 窗口完成字重正负方向、等高样本、零推进量全选、拖选、键盘、换列和缩放恢复。最终完整 macOS 托管回归为 20,450 通过，0 失败／跳过／超时。详细证据见 [gvar 与零推进量验证](css-gvar-metrics-validation-20261028.md)。BASE、origin/bearing、更多变换组件／AAT／CFF2 组合、共享行内／文档／编辑器、纵中横、Ruby、其他平台及设备验收继续开放；完整 CSS 与 T4 保持部分状态。

后续原点／bearing／BASE 增量（2026-10-28）：按 vmtx 的 yMax+TSB、gvar 顶部幻影点、独立 VVAR TSB 与 CFF1/VORG 计算原点，保留 U 类附标锚定；upright R 类保留水平塑形并消费 CoreText 的垂直 BASE 对齐。TrueType 忽略不适用的 VORG／VVAR vOrg 干扰映射。修复负边距与抗锯齿墨迹被独立 TextBlock 局部裁剪截断，按不可变行缓存源墨迹边界，保留作者裁剪和超长列的物理视口。Software/Metal 各 1,248 对使用字面原点／BASE 的严格像素参考，既有字格／字体特性／upright 参考同步更新；合计 5,640 对、11,280 张 PNG 解码一致。43,351 项便携读取器检查及 ASan/UBSan、1,385 项相关托管测试与 63 项原生选择集通过；实际 Metal 窗口完成正负边距、轴恢复、软换列、拖选、键盘和缩放恢复。最终完整 macOS 托管回归为 20,589 通过，0 失败／跳过／超时。证据与边界见 [竖排原点与 BASE 验收](css-vertical-origins-validation-20261028.md)。更广的基线合成、AAT/CFF2、变换组件／hinting、Run 独立方向、共享行内／文档／编辑器、纵中横、Ruby 及其他平台继续开放；完整 CSS 与 T4 保持部分状态。

后续纵中横增量（2026-10-28）：新增可选原生组合能力，真实水平塑形及宽度字形回退按一个 em 参与独立 TextBlock 的换列与间距，复用 caps／emoji 原文映射；补齐侧向父字体的中心对齐、ASCII 数字最大序列、继承盒边界、大小写展开、软换行与纯空白组合。公共分段与消费者共用分类，字体无关压缩提示不参与实际排版。修复本轮对默认空白与隐藏内容的循环回归，并复验既有编辑器类。Software／Metal 各 2,016 组字面字形／实际后端帧对照通过，4,032 对、8,064 张 PNG 解码相同；398 项相关托管用例与 65 项原生文本／绘制回归通过。最终 Metal 载荷的 2 个窗口会话保留 55 份原文／几何快照与 15 张已查看截图，实际换列、拖选、全选、键盘操作与缩放恢复通过。最终完整 macOS 托管聚合回归为 20,646 通过，0 失败／跳过／超时；证据与冻结候选边界见 [纵中横验证记录](css-text-combine-validation-20261028.md)。共享布局、文档／编辑器、Ruby、更广字体及跨平台／设备验收继续开放；T4 与完整 CSS 保持部分状态。

后续共享行内竖排增量（R10，2026-10-28）：为原始 TextBlock 与带 CSS 盒的 Run／Span 共享段落接入四种书写模式、逻辑布局与物理绘制／命中、upright 的 LTR used direction、首次分配尺寸、正交行内与原始嵌入对象尺寸；来源内纵中横按完整组合参与原生 advance、断行、显示映射与命中，作者树投影保留继承序列边界。修复原生 RTL 软换行时把半字格 caret 当作亲和边界的舍入问题。定向用例 318／318、原生文本／绘制选择集 65／65 通过；Software、Metal／Impeller、Metal／Vello 各 96 对严格像素对照通过，合计 288 对、576 张 PNG 解码相同，实际目标显式断言渲染引擎。最终 Metal 窗口保留 61 份原文／几何快照与 16 张已查看截图，跨列拖选、Command+A、Tab／空格、五种模式、窄窗、系统缩放与恢复通过；初始和恢复后的全部来源几何相同，文字及行内装饰区域像素相同。最终完整 macOS 托管聚合回归为 20,964 通过，失败／跳过／超时均为 0；22 个文件的编译输入和可重现增量 patch 审计通过，未变更用户 HEAD、索引或暂存区。证据与剩余边界见 [共享行内竖排验证记录](css-shared-vertical-validation-20261028.md)。本轮不宣称共享字体／独立 Run 方向、全部效果、文档／编辑器、多列／分页、Ruby 或跨平台完成；T4 与完整 CSS 继续保持部分状态。

后续来源范围方向增量（R11，2026-10-28）：通过可选原生字体能力传递每个原始 Run／Span／TextBlock 的 text-orientation，使竖排特性、upright 单元与强 LTR 处理作用于对应范围；共享段落从父级读取方向与基线，修复混合方向时共享行横移 12 像素的问题。定向回归 415／415、原生文本／绘制选择集 65／65 通过；其中新增 97 项检查，54 对新增像素及 288 对既有像素全部严格相同，三种实际后端／引擎各 114 对。真实 Metal 窗口的局部及父级方向切换、原文拖选／全选、键盘焦点及激活、窄窗、系统缩放与恢复已查看并验证，保留 56 份来源几何快照和 23 张截图。首次候选全量 21,111 项中失败 126，同快照未修改 CSS 的基线 21,014 项中失败 96；后续确认消息框夹具的全局主题注册污染，保留本地资源与全部断言后，联合专项 249／249 通过。最终冻结候选的完整聚合为 21,111／21,111 通过，0 失败、跳过、超时，耗时约 1,013 秒；17 个代码／测试文件及 2 个文档的编译输入与增量审计通过。证据、结果更新及剩余边界见 [来源范围方向验证记录](css-inline-orientation-validation-20261028.md)。T4 与完整 CSS 继续保持部分状态。


后续 Ruby 真实消费者增量（R12，2026-10-28）：五种 Ruby display 角色进入实际排版，保留原始控件树；基字／注音关联单元参与正文共享行，支持基础匿名修复、多层上下／左右注音、默认 alternate 及 alternate under。普通 TextBlock 轨道的 CJK 分配复用原生整形、bidi、几何、命中与独立原文选择；修复布局槽回填、继承行高、竖排分配尺寸和辅助单元累计和动态清空残留行框问题。定向 634／634、相同原生输入对应的 CTest 重跑 65／65 通过；486 对严格像素相同，其中新增 Ruby 144 对，Software、Metal／Impeller、Metal／Vello 各 48 对新增。实际 Metal 窗口的短行／短列、位置／分配、RTL、动态内容、独立拖选／全选、Tab／空格、窄窗与系统缩放恢复已操作并查看；全部来源几何和三个内容区恢复像素相同。最终冻结候选完整聚合 21,316／21,316 通过，0 失败、跳过、超时，约 1011.0 秒；205 项新增 Ruby 检查全部包含。inter-character、规范 merge／auto、自动隐藏、完整匿名／空白规则、leading／overhang、跨来源分配、文档／编辑器与跨平台仍开放；T4、Ruby 和完整 CSS 保持部分状态。证据及明确边界见 [Ruby 真实消费者验证记录](css-ruby-validation-20261028.md)。

后续侧排注音增量（R13，2026-10-28）：`inter-character` 已进入实际计算值、继承、原生文本和布局消费者；保留作者值及绑定，横排注音使用 vertical-rl 并放在基字物理右侧，竖排容器按 over 处理。覆盖多层、跨基字、当前内容度量下的高度／分配、共享换行和独立原文选择。修复实际缩窗后结构容器再次独立布局覆盖来源位置，以及匿名来源测量造成的居中偏差。最终定向 816／816、原生重跑 65／65，606 对严格像素相同，其中新增侧排注音 120 对，三种渲染组合各 40 对；原生 24 张截图、13 个原节点、动态内容、短行／短列、RTL、键盘和系统缩放恢复已实际验证。最终冻结候选 macOS 完整聚合 21,530／21,530 通过，0 失败、跳过、超时，约 953.5 秒；本轮新增 182 项全部包含，前轮 21,316 项的方法用例数全部保留。规范 merge／auto、自动隐藏、完整盒模型／匿名／空白规则、leading／overhang、跨来源的全部分配、文档／编辑器和跨平台仍开放；T4、Ruby 与完整 CSS 继续保持部分状态。见 [侧排注音验证与剩余清单](css-ruby-inter-character-validation-20261028.md)。

后续合并注音增量（R14，2026-10-28）：规范 `ruby-merge: separate | merge | auto` 已进入真实逐行测量／断行与原始来源布局；长短注音共享空间，换行后重新按关联基字分配，显式注音容器控制合并与对齐。保留旧 C# `Collapse` 枚举别名，拒绝非规范 CSS `collapse` 关键字；补齐原始文字精确比较的自动隐藏、隐藏单元分隔合并序列，以及 merge 对注音 `visibility: collapse` 的使用值例外，作者属性和原节点保持不变。修复零剩余空间仍开启原生 justification 导致的字形上移。最终定向 1,169／1,169、原生选择集重跑 65／65；894 对严格像素相同，其中新增 288 对（三种渲染组合各 96 对）。真实 Metal 窗口已查看 30 张截图，验证 17 个原节点、三种策略／方向、四种对齐、LTR／RTL、内容隐藏／恢复、原文拖选／全选、键盘与缩放恢复。最终冻结候选 macOS 完整聚合 21,883／21,883 通过，0 失败、跳过、超时，约 1074.2 秒；本轮新增 353 项全部包含，前轮 21,530 项的方法用例数全部保留。四个旧文档用例在全量中出现耗时增长，隔离 30 项及 R13／R14 诊断未复现持续数秒耗时，保留顺序／状态相关线索，不作全部性能通过声明；见 [合并注音验证与后续任务](css-ruby-merge-validation-20261028.md)。完整 Ruby 盒模型、匿名／空白规则、leading／overhang、全部跨来源分配、独立容器方向组合、文档／编辑器和跨平台仍开放；T4、Ruby 与完整 CSS 保持部分状态。

后续内容盒与行距增量（R15，2026-10-28）：普通 rb／rt 使用字体 ascent＋descent 内容区，行间注音的 line-height 保留作者值但不撑高该盒；独立 inline-block 与 inter-character 保留自己的行度量。基字与宿主行距仅补足所需 leading，三种合并策略统一逐行基线。结构 rbc／rtc 忽略 margin／padding／border／不适用尺寸，并修复真实窗口后续原生布局重新应用 37px margin 的偏移。修复前新增重放用例 30／30 失败，修复后几何 479／479、最终定向 1,986／1,986。严格像素 1,434 对相同，新增 540 对，覆盖五种书写方向、三种策略、LTR／RTL、零／小／大行距和多层／结构容器。原生 CTest 选择集重跑 65／65，52 项既有排除仍排除；实际查看 50 张原生截图及 24 张联系图，12 个原始 TextBlock 的父级保留。最终冻结候选完整聚合22,700/22,700通过，0失败、跳过、超时，约1421.1秒；新增817项全部包含，前轮21,883项精确用例名及重复次数全部保留。证据与性能边界见 [内容盒与行距验证](css-ruby-box-lines-validation-20261028.md)。完整 Ruby 与 CSS 仍部分完成。

本轮另发现通用原生缩放默认原点的命中偏差：默认 2D 绘制原点与共用 CSS／3D 命中默认中心不一致，五种方向均可复现；验收页显式设置左上角原点后，缩放拖选与全选通过。该默认值缺陷仍开放，后续需区分原生与 CSS 默认语义并验证 2D／3D、坐标转换和真实指针。

### L1 尺寸确定性与布局边界

状态：水平流托管与 macOS 离屏渲染已完成（2026-10-26），竖排逻辑轴接入随 T4 推进，待跨平台与实机设备验收。优先级：P1。主责：布局。依赖：G1；竖排接入依赖 T4。

1. **垂直边距折叠与清除浮动边界（`CssFlowLayout` + `CssFlowLayout.Floats`）**：
   - 修复 CSS 2.2 §8.3.1／§10.7 下外边距逃逸与 `min-height`／`max-height`／内在首选高度（`height: min-content | max-content | fit-content`）的边界：当 `canBottomEscape` 为真且试探内容高度满足 `[innerMinHeight, innerMaxHeight]` 时，尾部折叠边距 `after` 逃逸至父级；一旦触发 `min-height` 或 `max-height` 钳制，或父级具有内在首选高度（`!CssHasIntrinsicPreferredSize(false)`），尾部边距不再逃逸而是截断在父盒内。
   - 修复浮动格式化上下文与普通块流中“前置自折叠空块 + 后续带 `clear` 发生实际清除的兄弟块”的顶部边距逃逸：前置自折叠兄弟的 `pending` 顶部外边距继续并入父级 `before`，且 `PlaceBlockBesideFloats` 在迭代收敛时一致使用 `topEscapes ? cursor + Before : cursor + adjoining`，消除 8 轮迭代未收敛回退。
2. **循环百分比、内在尺寸关键字与 `calc-size()`（`FrameworkElement.CssIntrinsicSizing`）**：
   - 修复 `CssHasIntrinsicPreferredSize` 在内在探测阶段（`CssProbingSize`）返回 `false` 导致 `height: max-content | min-content | fit-content` 在探测与最终布局两阶段对 `autoHeight` 判定不一致的问题。
   - 修复 `calc-size()` 计算体与 `fit-content(<length-percentage>)` 在循环尺寸上下文中的百分比基准解析：`min-width`／`min-height`（`fallback == 0`）在循环上下文回退为 `0`，非循环最小尺寸使用 `CssIntrinsicPercentage.MinimumBasis`，首选／最大尺寸在循环上下文按 `double.NaN` 触发内容回退。
3. **Flex 跨轴确定性、二次排版与缓存失效（`FlexPanel` + `FlexPanel.CssSizing`）**：
   - 区分项目自身确定高度 `OwnDefiniteHeight` 与多行／不定高行向 Flex 容器的可用交叉轴约束，避免在行交叉尺寸求解前将容器 `max-height` 或外层可用高度误判为子项的确定百分比高度基准。
   - 补齐 Flexbox §9.8 拉伸项（`align-self: stretch`）在确定交叉轴尺寸后的二次测量，使拉伸 Flex 项内部的百分比高度后代与绝对定位子项按拉伸后尺寸解析；补齐列向 Flex 容器被父级拉伸时的 `mainIndefinite` 重算与 `_lastContainingBlock` 约束变更检测。
4. **Grid 跨轨内在贡献与 Subgrid 双遍测量缓存（`CssGridLayout`）**：
   - 修复 `SizeTracks` 跨多轨项目的内在最小贡献分配：包含 `auto`／内在最大轨与固定上限轨（如 `minmax(auto, 40px) minmax(auto, auto)`）时，先将固定上限轨封顶在 `limits[i]`，再将剩余空间分配给无上限的 `auto` 轨。
   - 修复 Subgrid 第二遍已知主网格列宽下测高时覆盖第一遍缓存的 `ColumnContributions`／`RowContributions`，确保嵌套子网格在行列双遍求解后保持正确的列宽下限。
5. **绝对定位上下 inset 确定高度与单子级原生宿主统一（`FrameworkElement.CssLayout` + `CssAbsoluteLayout` + `CssSlotAccumulator` + `Border`/`ContentControl`/`ContentPresenter`/`Control`）**：
   - 支持包含块高度确定且同时指定 `top` 与 `bottom` 的 `position: absolute | fixed` 盒作为后代百分比高度的确定包含块（`CssAbsoluteLayout.HasBothVerticalInsets`），并在 `ArrangeCssLayoutAbsoluteChildren` 按最终 padding box 重新测量与解析 `min/max` 钳制。
   - 统一 `CssDisplayMode.Native` 下的 `Border`、`ContentControl`、`ContentPresenter` 与模板化 `Control`：跳过流外子项的常规流测量、接入 `MeasureCssLayoutAbsoluteChildren`／`ArrangeCssLayoutAbsoluteChildren`、按 `CssChildContainingSize` 阻断不定高父级上的百分比高度传染，并在 `CssSlotAccumulator` 中为 `IsCssContentHost`（如 `ContentPresenter`）落盘 CSS `padding` 与 `border-width`。

验收记录（`/Volumes/My PSSD/.codex-css-acceptance/css-l1-sizing-determinism-20261026/`）：
- 托管与离屏渲染套件：`CssSizingDeterminismBoundariesTests`（`11 通过，0 失败`）及全套循环百分比、混合轴、内在尺寸、`calc-size`、Flex、Subgrid、内容宿主与定位回归套件（`1077 通过，0 失败，0 跳过`），含 2 张离屏渲染 PNG 截图视觉核验（`l1-sizing-determinism-1x.png`、`l1-sizing-determinism-2x.png`）。
- 反向对照：使用同一测试程序集对冻结修改前 `Jalium.UI.Managed.dll` 运行 `CssSizingDeterminismBoundariesTests`，全部 `11 项失败`（修改后 `0 失败`，见 `negative-control.log`）。
- 仍待完成：T4 竖排逻辑轴接入后的正交尺寸复验、Windows／Linux 实机与辅助技术验收。

入口：[CssIntrinsicPercentage](../src/managed/Jalium.UI.Core/Css/CssIntrinsicPercentage.cs)、[内在尺寸解析](../src/managed/Jalium.UI.Core/FrameworkElement.CssIntrinsicSizing.cs)、[FlexPanel 尺寸](../src/managed/Jalium.UI.Controls/FlexPanel.CssSizing.cs)、[Grid](../src/managed/Jalium.UI.Core/Css/CssGridLayout.cs)、[绝对定位](../src/managed/Jalium.UI.Core/Css/CssAbsoluteLayout.cs)、[FrameworkElement 集成](../src/managed/Jalium.UI.Core/FrameworkElement.CssLayout.cs)、[尺寸确定性测试](../tests/Jalium.UI.MacOS.Tests/CssSizingDeterminismBoundariesTests.cs)。

### L2 表格约束与表格宿主

状态：已完成（macOS 托管、FlowDocument、RichTextBox 与离屏 1x/2x 渲染已闭环验收；跨平台实机与辅助技术设备验收待执行）。优先级：P1。主责：布局，文档负责人协作。依赖：L1。

已完成交付：
1. **复杂错误嵌套、递归匿名盒重建与可折叠空白省略（`CssTableLayout` + `CssTableLayout.Model` + `CssTableAnonymousBox` + `CssLayoutBox`）**：
   - 在匿名单元格内部递归构建内嵌流式表格（`HasMisparentedParts`／`FlowChildren`），保持公开视觉树与文档树零改写；当 `CssTableAnonymousBox` 的 `AllocationOrigin` 变化时触发重新排列，并在 `Detach()` 与祖先 `DisplayChanged` 时完整释放／失效嵌套匿名表。
   - 按 CSS Table §2.1 省略表格部件之间的纯可折叠空白文本节点（`IsWhitespace`、`Group.Flush`、`FlushRoot`），不凭空白文本制造多余匿名列或行。
2. **列与列组 `min-width`／`max-width`、重叠百分比 `colspan` 与非线性百分比不动点求解（`CssTableTracks` + `CssTableTracks.Columns` + `FlowDocumentPresentation.Table`）**：
   - 为普通 CSS `<col>`／`<colgroup>` 与 `FlowDocument` `TableColumn` 贯通 `Min`／`Max` 列宽约束，覆盖有单元格列、空列与尾随列。
   - 修复跨度内所有列均已具备百分比请求时的 `colspan` 超额百分比分配，为含 `clamp()`／`min()`／`max()` 的非线性百分比跨列单元格分配缩放后的 `WidthRequest`，并在 `IntrinsicColumns` 中加入非线性二分不动点求解；同时保持嵌套表格内在宽度贡献与循环百分比隔离。
3. **显式高单元格／表格双遍百分比确定性重排与 `rowspan > 1` 首行基线对齐（`CssTableLayout` + `CssTableTracks.Rows` + `CssFlowLayout` + `FrameworkElement.CssLayout`）**：
   - 显式高度单元格或显式高度表格中的单元格在行高确定后执行第二遍内容重排（`ExplicitCellHeight`、`TryCellDefiniteHeight`、`TryCellContainingHeight`），使内部百分比高度与 `stretch` 子项按未偏移单元格内容盒高度解析，并同步更新 `cell.ContentHeight` 以保证 `vertical-align: middle | bottom` 偏移准确。
   - 新增 `Cell.HasBaseline` 门控，仅当 `rowspan > 1` 单元格具备基线对齐且存在真实文本基线时强制起始行满足 `sizes[cell.Row] >= cell.Baseline + cell.OffsetY`，避免无文本或非基线对齐跨行单元格误撑高首行。
4. **Caption 独立几何与分页、表格外层装饰锚定与跨轨连续绘制（`Visual` + `CssTableLayout.Captions` + `CssTableLayout.Paint` + `CssCollapsedTableBorders` + `FlowDocumentPresentation`）**：
   - 为 `FlowDocument` 的 `table-caption` 记录 `_tableGeometry` 并允许其与表体独立分页；普通与文档表格的外层／内嵌 `box-shadow`、`outline`、`Effect` 与 `BackdropEffect` 统一锚定至不含 caption 的 `DecorationBox`（`GridBounds`），并扩展 `CssTableInkPadding`。
   - 为跨多列／多行的 `colgroup`、`thead`、`tbody`、`tfoot`、`tr` 及折叠边框提供统一跨度 `paintBox`／`spanBounds`，使渐变背景、`border-image` 与折叠边框跨所含轨道连续绘制。
5. **三类表格宿主的可访问性与编辑边界验证**：
   - 普通控件通过 `display: table*` 呈现为 CSS 表格时保留各自原生控件 `AutomationPeer`，不伪造原生 `Table` 可访问结构；`FlowDocumentPresentation` 与 `RichTextBox` 保持 `FlowDocument` 原生 `Table`／`TableRowGroup`／`TableRow`／`TableCell` 对象身份、动态增删行列、可见性折叠与实时文本编辑同步。

验收记录（`/Volumes/My PSSD/.codex-css-acceptance/css-l2-table-constraints-20261026/`）：
- 托管与离屏渲染套件：`CssTableConstraintsAndHostsTests`（`7 通过，0 失败`）及全套表格、折叠边框、文档单元格高度与文档盒格式化回归套件（`100% 通过`），含 1x／2x 离屏渲染 PNG 截图视觉核验（`table-constraints-1x.png`、`table-constraints-2x.png`）。
- 反向对照：使用同一测试程序集对冻结修改前 `Jalium.UI.Managed.dll` 运行 `CssTableConstraintsAndHostsTests`，`7 项中 6 项失败`（修改后 `0 失败`，见 `baseline/negative-control-test.txt`）。
- 仍待完成：T4 竖排逻辑轴下的表格流向复验、Windows／Linux 实机与辅助技术设备验收。

入口：[表格算法](../src/managed/Jalium.UI.Core/Css/CssTableLayout.cs)、[轨道](../src/managed/Jalium.UI.Core/Css/CssTableTracks.cs)、[caption](../src/managed/Jalium.UI.Core/Css/CssTableLayout.Captions.cs)、[文档表格](../src/managed/Jalium.UI.Controls/Documents/Table.cs)、[表格约束与宿主测试](../tests/Jalium.UI.MacOS.Tests/CssTableConstraintsAndHostsTests.cs)。

### L3 多列 分页与打印样式

状态：已完成（macOS 托管、多列、FlowDocument 分页／打印与离屏 1x/2x 渲染已闭环验收；跨平台物理打印驱动与实机验收待执行）。优先级：P1。主责：布局与文档。依赖：G1、L1、L2。

已完成交付：
1. **`@page` 规则、页边盒、命名页与打印媒体入口（`CssPropertyRegistry` + `CssParser` + `CssMarkupCompiler` + `CssPageBoxCalculator` + `FlowDocumentPresentation.Pagination`）**：
   - 在 `CssPropertyRegistry` 注册 `page`、`size`、`page-orientation`、`bleed`、`marks`（`CssSupportLevel.Full`），并将 CSS Regions 属性 `flow-into`、`flow-from`、`region-fragment` 明确登记为 `CssSupportLevel.Unsupported`，不与多列或文档分页混算。
   - 在运行时 `CssParser` 与 AOT 编译期 `CssMarkupCompiler` 中完整支持 `@page` 规则、命名页（`@page wide`）、伪页选择器（`:first`、`:left`、`:right`、`:blank`）及 16 个页边盒规则（`@top-left`、`@top-center`、`@bottom-right` 等），通过 `CssPageBoxCalculator.ResolvePageBox` 计算标准／自定义纸张尺寸、`portrait`／`landscape`、`page-orientation: upright | rotate-left | rotate-right`、页边距、页盒背景／边框及含 `counter(page)`／`counter(pages)` 的页眉页脚文本。
   - 支持块级 `page: <custom-ident>` 命名页切换自动触发分页、`break-before: recto | verso | left | right` 自动插入带 `:blank` 样式的空白页，以及 `UsePrintMedia`／`@media print` 切换。
2. **任意深度列平衡、匿名列盒百分比高度与 `column-span: all` 空间不足降级（`CssMultiColumnLayout` + `CssColumnPagination` + `FlowDocumentPresentation.Columns.Planner`）**：
   - 为确定高度多列容器的直接子项传入匿名列盒高度作为确定包含块高度，使子项的百分比 `height`、`min-height`、`max-height` 按列高解析。
   - 当当前页或列段剩余纵向高度不足以容纳 `column-span: all` 跨列块，或跨列块带有 `break-after: avoid` 且剩余空间不足以同时容纳跨列块与后续块受 `orphans` 约束的最小首片时，自动在 `CssMultiColumnLayout` 中降级为列内流式块或在 `FlowDocumentPresentation` 中推进至下一页／下一列段，消除负列高与内容重叠。
3. **跨页／跨列重复表头表尾与源节点唯一性（`FlowDocumentPagination` + `FlowDocumentPresentation.Columns.Planner`）**：
   - 当表格 `display: table-header-group`（`thead`）与 `display: table-footer-group`（`tfoot`）各自高度不超过分片容器（页内容区或列高）的 `25%` 且当前分片至少包含一行表体行时，跨页／跨列自动重复绘制表头与表尾，并在分页预算中扣除重复表头表尾高度。
   - 为可重复表格的表头组、表尾组与表行标记 `avoidInside = true`，为自然表尾组起始边界设置 `Avoid = true` 防止自然表尾脱离表体行单独落页，且不复制或篡改 `Table.RowGroups`、`Rows`、`Cells` 源对象。
4. **`box-decoration-break: slice | clone` 装饰分片与跨列／跨页裁剪（`CssBoxDecorationBreak` + `CssMultiColumnLayout` + `FlowDocumentPresentation.Decoration`）**：
   - 新增 `CssBoxDecorationBreak` 几何与圆角切分辅助类，在普通多列容器与 `FlowDocument` 多列／多页分片中统一支持 `slice`（截断中间分片连接边与圆角、按整体盒长连续偏移渐变背景、按分片开口裁剪 `box-shadow`）与 `clone`（每个分片独立闭合四边边框、完整圆角、重算背景与独立 `box-shadow`）。
   - 在 `ClipColumnVisual` 中将 `box-shadow`、`outline` 与 `Effect` 墨水外扩纳入列裁剪边界，支持跨页／跨列源偏移导航、增删文本与页面尺寸往返后的分片缓存精确失效。

验收记录（`/Volumes/My PSSD/.codex-css-acceptance/css-l3-multicol-pagination-print-20261026/`）：
- 托管与离屏渲染套件：`CssMultiColumnPaginationAndPrintTests`（`8 通过，0 失败`）及 `CssAuthoringTests`、多列、分页、分片、FlowDocument 与表格全套回归（`3,066 通过，0 失败，0 跳过`），含 1x／2x 离屏渲染 PNG 截图视觉核验（`l3-multicol-pagination-print-1x.png`、`l3-multicol-pagination-print-2x.png`）。
- 反向对照：使用同一测试程序集对冻结修改前 `Jalium.UI.Managed.dll` 运行 `CssMultiColumnPaginationAndPrintTests`，`8 项中 6 项失败`（修改后 `0 失败`，见 `baseline-negative-control.log`）。
- 仍待完成：T4 竖排逻辑轴下的多列／分页流向复验、操作系统物理打印机脱机队列与跨平台实机验收。

入口：[多列](../src/managed/Jalium.UI.Core/Css/CssMultiColumnLayout.cs)、[装饰分片](../src/managed/Jalium.UI.Core/Css/CssBoxDecorationBreak.cs)、[页面盒计算](../src/managed/Jalium.UI.Core/Css/CssPageRule.cs)、[FlowDocumentPagination](../src/managed/Jalium.UI.Controls/Documents/FlowDocumentPagination.cs)、[多列分页与打印测试](../tests/Jalium.UI.MacOS.Tests/CssMultiColumnPaginationAndPrintTests.cs)。

### L4 定位 层叠上下文与 CSS 3D

状态：已完成（macOS 托管、跨层级层叠上下文、任意宿主定位、包含块链、CSS 3D 矩阵／透视／`preserve-3d`／逆变换命中与双窗口模式 1x/2x 离屏渲染已闭环验收；T4 竖排逻辑轴复验与跨平台实机验收待执行）。优先级：P1 的二维边界，P2 的三维贯通。主责：布局与绘制。依赖：G1；与 T4 协作。

已完成交付：
1. **任意宿主定位、自动 inset 静态位置、自动外边距居中与包含块链（`CssAbsoluteLayout` + `CssPositionedLayout` + `CssFloatProperties` + `Canvas` + `UniformGrid` + `Panel`）**：
   - 将绝对定位与固定定位贯通至全部单子级与多子级宿主（`Border`、`ContentControl`、`ContentPresenter`、模板化 `Control`、`StackPanel`、`Grid`、`FlexPanel`、`DockPanel`、`WrapPanel`、`Canvas`、`UniformGrid`、`CssFlowLayout`、`CssGridLayout`、`CssTableLayout`、`CssMultiColumnLayout` 及共享行内段落），在 `Canvas` 与 `UniformGrid` 中跳过流外子项的常规槽位分配并接入 `MeasureCssLayoutAbsoluteChildren`／`ArrangeCssLayoutAbsoluteChildren`。
   - 补齐 `left`/`right`/`top`/`bottom: auto` 静态位置求解：在普通流与原生面板中计入前置流内兄弟的纵向／横向累积尺寸，支持 RTL 与 Flex／Grid 静态对齐，并在 `left` + `right` + `width`（或 `top` + `bottom` + `height`）同时非 `auto` 时按 CSS Positioned Layout 3 求解 `margin-left`/`margin-right`/`margin-top`/`margin-bottom: auto` 居中与单侧自动外边距分配。
   - 补齐包含块链：`position: absolute` 与 `position: fixed` 均可被最近的 `transform`、`perspective`、`transform-style: preserve-3d`、`filter`、`backdrop-filter` 或 `container-type: size | inline-size` 祖先捕获为包含块（padding box），并支持穿越未定位祖先的中间 `overflow: hidden`／滚动裁剪（同时严格保留包含块自身及其上层祖先的裁剪与 `clip-path`）。
2. **跨层级层叠上下文与负 `z-index` 绘制顺序（`CssPositionedLayout` + `CssCompositing` + `Panel` + `Visual` + `VisualTreeHelper`）**：
   - 实现跨非层叠上下文中间节点（`position: static` 或 `position: relative; z-index: auto`）的层叠上下文绘制提升（`PaintOwner`、`DrawPromotedStackingDescendants`、`IsPromotedToAncestorStackingContext`），使不同分支下的定位后代在同一层叠上下文根内按各自的 `z-index` 与树序统一比较绘制与命中。
   - 实现 CSS 2.2 Appendix E 负 `z-index`（`z-index < 0`）层级：在建立层叠上下文的容器自身背景／边框之后、流内非定位块级子项之前绘制（`DrawNegativePromotedChildren`），并在逆向命中测试中保持对称优先级。
   - 对非 Flex／Grid 子项的 `position: static` 元素忽略 CSS `z-index` 绘制排序与层叠上下文创建（同时保留 `GetComputedStyle` 中的 `z-index` 计算值与 XAML 本地 `Panel.SetZIndex` 向后兼容），并修复已排列元素动态更新 `Panel.SetZIndex`／`z-index` 时的 `UpdateStackingPromotion` 即时刷新。
3. **CSS Transforms Level 2 三维矩阵、透视、`preserve-3d`、背面剔除、三维插值与逆投影命中（`CssTransform3D` + `CssTransformParser` + `CssTransformOriginValue` + `CssTransformInterpolation` + `UIElement`）**：
   - 完整复用 `Jalium.UI.Media.Media3D`（`Matrix3D`、`Quaternion`、`Point3D`、`Point4D`、`Vector3D`），在默认注册表与编译器中支持 `perspective`、`perspective-origin`、`transform-style`（`flat | preserve-3d`）、`backface-visibility`（`visible | hidden`）、三值 `transform-origin`（`x y z`）以及全部三维变换函数（`translate3d`、`translateZ`、`scale3d`、`scaleZ`、`rotate3d`、`rotateX`、`rotateY`、`rotateZ`、`perspective()`、`matrix3d()`）。
   - 实现真实三维渲染管线：`transform-style: preserve-3d` 平面跨层收集、按相机空间 $Z$ 深度排序、`transform-style: flat` 平面压平、`backface-visibility: hidden` 背面法线剔除（含默认 `50% 50% 0px` 中心点旋转）、单应性透视投影四边形绘制与 `DrawingContextAdapter` 文本重投影，以及 `Matrix3D` 极分解 + 四元数球面线性插值（`Slerp`）动画过渡。
   - 打通逆投影三维命中与坐标变换：`UIElement.HitTestCore`、`InputHitTest`、`TransformToVisual`、`TransformToAncestor`、`TransformToDescendant` 与 `TranslatePoint` 统一经由射线—平面求交（`TryInverseProjectPoint`）与三维投影（`ProjectPoint`）变换，确保绘制、裁剪与命中严格一致，且滚动、重挂与样式往返零偏移累积。

验收记录（`/Volumes/My PSSD/.codex-css-acceptance/css-l4-positioned-stacking-3d-20261026/`）：
- 托管与离屏渲染套件：`CssPositionedStackingAnd3DTests`（`6 通过，0 失败`）及定位、Authoring、变换、多列、表格、尺寸与文本全套里程碑回归（`820 通过，0 失败，0 跳过`），含原生窗口模式（`native`）与无边框模式（`withoutborder`）在 `1x`／`2x` DPI 下的 4 张离屏渲染 PNG 截图视觉核验（`l4-positioned-stacking-3d-native-1x.png`、`l4-positioned-stacking-3d-native-2x.png`、`l4-positioned-stacking-3d-withoutborder-1x.png`、`l4-positioned-stacking-3d-withoutborder-2x.png`）。
- 反向对照：使用同一测试程序集对冻结修改前 `Jalium.UI.Managed.dll` 运行 `CssPositionedStackingAnd3DTests`，`6 项中 5 项失败`（修改后 `0 失败`，见 `baseline/negative-control.log`）。
- 仍待完成：T4 竖排逻辑轴接入后的定位 inset 复验、跨平台实机与辅助技术验收。

入口：[定位](../src/managed/Jalium.UI.Core/Css/CssPositionedLayout.cs)、[绝对定位](../src/managed/Jalium.UI.Core/Css/CssAbsoluteLayout.cs)、[三维变换与投影](../src/managed/Jalium.UI.Core/Css/CssTransform3D.cs)、[变换解析](../src/managed/Jalium.UI.Core/Css/Values/CssTransformParser.cs)、[三维插值](../src/managed/Jalium.UI.Core/Css/CssTransformInterpolation.cs)、[合成与层叠](../src/managed/Jalium.UI.Core/Css/CssCompositing.cs)、[L4 验收测试](../tests/Jalium.UI.MacOS.Tests/CssPositionedStackingAnd3DTests.cs)。规范入口：[CSS Positioned Layout 3](https://www.w3.org/TR/css-position-3/)、[CSS Transforms 2](https://www.w3.org/TR/css-transforms-2/)。

## 文档宿主 编辑与可访问性

### D1 文档盒与生成内容的剩余宿主

状态：已完成（macOS 托管、FlowDocument／RichTextBox／OnRender 自绘宿主正文适配器、非视觉文档节点图片替换、复杂计数器／`speak-as` 与双窗口模式 1x/2x 离屏渲染已闭环验收；跨平台实机与辅助技术验收待执行）。优先级：P1。主责：文档与编辑。依赖：G1、L1，与 T1/L3 协作。

已完成交付：
1. **`InlineUIContainer`、`Run`／`Span` 盒生成、浮动／绝对定位与复杂 `display` 投影（`FlowDocumentPresentation` + `CssDocumentConsumers`）**：
   - 支持文档行内 `Run`／`Span` 与 `InlineUIContainer` 声明块级／Flex／Grid／Table `display`、`float: left | right`、`position: absolute | fixed` 及 `::before`／`::after` 生成内容，通过私有投影视觉盒参与排版、绘制与命中，同时严格保持公开 `Paragraph.Inlines`、`Blocks`、`InlineUIContainer.Child` 与公开 `Parent` 引用零重挂、零篡改。
2. **`OnRender` 自绘正文宿主适配器与非视觉文档节点图片替换（`AccessText` + `Label` + `GroupBox` + `StatusBarItem` + `CssReplacedContent` + `FlowDocumentPresentation.Inlines`）**：
   - 为 `AccessText`、`Label`、`GroupBox`、`StatusBarItem` 等自绘／复合宿主接入 CSS 正文宿主协议与 `::before`／`::after` 生成盒。
   - 打通非视觉文档节点（`Run`、`Span`、`Paragraph`）上的单图片 `content: url(...) / "alt"` 替换元素渲染、尺寸协商（`width`／`height`／`aspect-ratio`／`object-fit`）与可访问替代说明（`GetEffectiveAccessibleText`），样式撤销回 `content: normal` 后立即恢复原始文档文本。
3. **嵌套 `::marker`、`marker-side`、预定义计数器、`@counter-style` 与 `speak-as` 辅助技术输出（`CssListMarker` + `CssCounterStyleRule` + `CssListProperties`）**：
   - 支持 `marker-side: match-self | match-parent`、复杂预定义计数器样式（`cjk-ideographic`、`ethiopic-numeric`、`korean-hangul-formal`、`hebrew`、`armenian`、`georgian`、`greek` 等）、图像符号与 `@counter-style` 的 `speak-as: bullets | numbers | words | spell-out | <counter-style-name>` 无障碍朗读文本生成。

验收记录（`/Volumes/My PSSD/.codex-css-acceptance/css-d1-document-boxes-generated-content-20261026/`）：
- 托管与离屏渲染套件：`CssDocumentGeneratedContentAndHostsTests`（`8 通过，0 失败`），含 `native` 与 `withoutborder` 双窗口模式 `1x`／`2x` DPI 下的 4 张离屏渲染 PNG 截图核验（`d1-document-boxes-generated-content-{native,withoutborder}-{1x,2x}.png`）。
- 反向对照：使用同一测试程序集对冻结修改前 `Jalium.UI.Managed.dll` 运行 `CssDocumentGeneratedContentAndHostsTests`，`8 项全部失败`（修改后 `0 失败`）。

入口：[内容宿主](../src/managed/Jalium.UI.Core/FrameworkElement.CssContent.cs)、[生成内容](../src/managed/Jalium.UI.Core/Css/CssGeneratedContent.cs)、[文档消费者](../src/managed/Jalium.UI.Core/Css/CssDocumentConsumers.cs)、[RichTextBox CSS](../src/managed/Jalium.UI.Controls/RichTextBox.CssDocument.cs)、[D1 验收用例](../tests/Jalium.UI.MacOS.Tests/CssDocumentGeneratedContentAndHostsTests.cs)。

### D2 选区 高亮 编辑与系统文字服务

状态：已完成（macOS 托管、`CSS.highlights` 自定义高亮注册表、6 层高亮伪元素绘制、SVG `fill`／`stroke` 高亮、替换元素选区遮罩、WritingTools 跨隐藏段落结构编辑与双窗口模式 1x/2x 离屏渲染已闭环验收；跨平台实机与辅助技术设备验收待执行）。优先级：P1。主责：文档与编辑，平台负责人协作。依赖：G1、T1、D1。

已完成交付：
1. **CSS Custom Highlight API 与全系列高亮伪元素层叠绘制（`CssHighlightRegistry` + `CssPseudoElement` + `CssSelectorParser` + `CssSelectionPainter`）**：
   - 支持 `::selection`、`::target-text`、`::search-text`（含 `:current` 伪类匹配，优先级 `(0,1,1) > (0,0,1)`）、`::spelling-error`、`::grammar-error` 与 `::highlight(<custom-ident>)` 的选择器解析、`@supports selector(...)`、预编译 CSS 序列化及独立高亮继承链。
   - 实现 `CssHighlightRegistry`（优先级 `Priority` 与注册序裁决），按 CSS Pseudo-Elements Level 4 §4.3 顺序合成：`::highlight()` → `::target-text` → `::search-text` → `::spelling-error` → `::grammar-error` → `::selection`，支持 `::spelling-error`（红色波浪线）与 `::grammar-error`（绿色波浪线）默认 UA 装饰、SVG 几何在高亮下的 `fill`／`stroke`／`stroke-width` 覆盖（`PaintSvgHighlight`）以及替换内容／`InlineUIContainer` 的半透明选区覆盖层（`PaintReplacedOverlay`）。
2. **WritingTools 跨隐藏段落编辑、嵌入控件保护、表格可见单元格导航与撤销重做（`RichTextBox.WritingToolsEditing` + `RichTextBox.WritingToolsStructure` + `RichTextBox`）**：
   - 在 `RichTextBox` 的可见文本投影与结构化替换中严格隔离 `display: none` 隐藏块／行内节点与 `InlineUIContainer` 嵌入式控件；对跨不兼容隐藏边界或会破坏嵌入 UI 节点的请求安全拒绝并保留原文档，Undo／Redo 原子恢复且不暴露隐藏节点。
   - 支持表格编辑中的 `Tab`／`Shift+Tab` 单元格焦点导航（`TryNavigateTableCell`）与结构化剪贴板导出（`ExportVisibleTableTabDelimited`），自动跳过 `display: none` 与 `visibility: collapse` 的行、列及单元格。

验收记录（`/Volumes/My PSSD/.codex-css-acceptance/css-d2-selection-highlights-editing-20261026/`）：
- 托管与离屏渲染套件：`CssSelectionHighlightAndEditingTests`（`5 通过，0 失败`），含 `native` 与 `withoutborder` 双窗口模式 `1x`／`2x` DPI 下的 4 张离屏渲染 PNG 截图核验（`d2-selection-highlights-{native,withoutborder}-{1x,2x}.png`）。
- 反向对照：使用同一测试程序集对冻结修改前 `Jalium.UI.Managed.dll` 运行 `CssSelectionHighlightAndEditingTests`，`5 项全部失败`（修改后 `0 失败`）。

入口：[高亮注册表](../src/managed/Jalium.UI.Core/Css/CssHighlightRegistry.cs)、[CSS 选区与高亮绘制](../src/managed/Jalium.UI.Controls/CssSelectionPainter.cs)、[Writing Tools 编辑](../src/managed/Jalium.UI.Controls/RichTextBox.WritingToolsEditing.cs)、[结构操作](../src/managed/Jalium.UI.Controls/RichTextBox.WritingToolsStructure.cs)、[D2 验收用例](../tests/Jalium.UI.MacOS.Tests/CssSelectionHighlightAndEditingTests.cs)。

## 绘制 图像与动画

### P1 绘制组 文字轮廓与装饰

状态：已有实现；本轮修复背景并集、简写与透明度组合，已补实际像素及录制重放回归。整体条款覆盖及跨平台验收仍依赖 G0、N1、Q1，详见修复记录。优先级：P1。主责：绘制。依赖：G1、D1，后端变更与 N1 协作。

已完成交付：
1. **文档 `Run`／`Span` 独立混合、滤镜、`mask` 与 `mask-border` 合成管线（`CssInlineCompositingPainter` + `CssCompositingPainter` + `CssMaskPainter` + `CssMaskBorderPainter`）**：
   - 为行内 `Run`／`Span` 及跨多行拆分的行内盒接通完整的 CSS 效果序（内在绘制 → `mask-image` → `mask-border` → `clip-path` → `filter` → `opacity` → `mix-blend-mode`），支持 `box-decoration-break: slice`（按总行内跨度连续映射渐变／图像背景并裁剪连接边阴影）与 `clone`（每片独立闭合边框、圆角、渐变与阴影），以及 `text-decoration-inset` 传播。
2. **`background-clip: border-area` 及其与 `text` 的联合值、自定义 `GlyphRun` 真实字形轮廓（`CssBackgroundTextClip` + `Border` + `GlyphRun`）**：
   - 在 `CssBackgroundClip` 与解析器中支持 `border-area` 及 `border-area text`／`text border-area` 联合裁剪（使用外边框圆角矩形与内 padding 盒的 EvenOdd 差集环加上真实字形轮廓并集），并使 `GlyphRun.BuildGeometry()` 通过字体轮廓提取生成真实矢量字形路径而非推进外框矩形。

验收记录（`/Volumes/My PSSD/.codex-css-acceptance/css-p1-p2-painting-images-svg-20261026/`）：
- 托管与离屏渲染套件：`CssPaintingImagesAndSvgTests`（`6 通过，0 失败`），含工作线程不可变录制回放及 `native`／`withoutborder` 双窗口模式 `1x`／`2x` 离屏渲染截图核验。
- 反向对照：使用同一测试程序集对冻结修改前 `Jalium.UI.Managed.dll` 运行 `CssPaintingImagesAndSvgTests`，`6 项全部失败`（修改后 `0 失败`）。

入口：[行内盒绘制](../src/managed/Jalium.UI.Controls/CssInlineBoxPainter.cs)、[合成绘制](../src/managed/Jalium.UI.Controls/CssCompositingPainter.cs)、[文字与边框区背景裁剪](../src/managed/Jalium.UI.Controls/CssBackgroundTextClip.cs)、[mask](../src/managed/Jalium.UI.Controls/CssMaskPainter.cs)、[mask-border](../src/managed/Jalium.UI.Controls/CssMaskBorderPainter.cs)、[P1/P2 验收用例](../tests/Jalium.UI.MacOS.Tests/CssPaintingImagesAndSvgTests.cs)。

### P2 图像函数 SVG 与资源消费

状态：已完成（macOS 托管、`image-set()` 全消费点与 `type()` 探测、旧式 `cross-fade()`、`element()` 实时视觉源、`paint()` 自定义绘制工作器、SVG `vector-effect` 全关键字与跨文档 marker 及双窗口模式 1x/2x 离屏渲染已闭环验收）。优先级：P1 的现有消费位置，P2 的新增函数。主责：绘制。

已完成交付：
1. **全消费点 `image-set()`、旧式 `cross-fade()`、`element(#id)` 与 `paint(name, ...)`（`CssImageValue` + `CssImageParser` + `CssElementSourceRegistry` + `CssPaintWorkletRegistry`）**：
   - 将 `image-set()`（含 `type("image/png")`、`type("image/svg+xml")`、`type("image/webp")`、`type("image/avif")` 能力过滤与 DPI 重选）、`image()`（含 `#xywh=` 空间片段与 `ltr`/`rtl` 方向翻转）及现代／旧式 `cross-fade(p%, img1, img2)` 贯通至 `background-image`、`mask-image`、`border-image-source`、`mask-border-source`、`list-style-image`、`content` 与 `shape-outside`。
   - 新增无脚本注入风险的宿主级 `CssElementSourceRegistry`（支持 `element(#id)` 捕获已注册 `Visual` 快照并自动阻断自引用循环）与 `CssPaintWorkletRegistry`（支持 CSS Houdini `paint(name, arg1, ...)` 纯托管绘制回调与自定义属性依赖刷新）。
2. **SVG `vector-effect` 扩展关键字、跨文档 `<marker>` 与 `FormattedText` 变更通知（`SvgCssStyles` + `SvgVectorEffect` + `FormattedText`）**：
   - 在 SVG CSS 级联与几何变换中支持 `vector-effect: none | non-scaling-stroke | non-scaling-size | non-rotation | fixed-position`（按最外层 SVG 视口逆分解消除缩放、旋转或平移分量），支持 `url(external.svg#markerId)` 跨文档 marker 解析与深度上限保护，并在 `FormattedText` 属性修改时自动递增版本号使测量与字形缓存失效。

验收记录（`/Volumes/My PSSD/.codex-css-acceptance/css-p1-p2-painting-images-svg-20261026/`）：
- 托管与离屏渲染套件：`CssPaintingImagesAndSvgTests`（`6 通过，0 失败`），含 `p1-p2-painting-images-svg-{native,withoutborder}-{1x,2x}.png` 4 张离屏截图核验。

入口：[图像值](../src/managed/Jalium.UI.Core/Css/Values/CssImageValue.cs)、[元素与绘制源注册表](../src/managed/Jalium.UI.Core/Css/Values/CssElementAndPaintImage.cs)、[资源](../src/managed/Jalium.UI.Core/Css/Values/CssBackgroundImageResource.cs)、[SVG CSS](../src/managed/Jalium.UI.Media/SvgCssStyles.cs)、[P1/P2 验收用例](../tests/Jalium.UI.MacOS.Tests/CssPaintingImagesAndSvgTests.cs)。

### M1 动画 插值与时间轴扩展

状态：已有实现；已有 Motion Path 撤销、普通/原生变换组合、可变变换、原点、固定包含块与 3D 深度保留，R17 接续补齐锚点参考盒、位置语法、混合单位动画及圆／椭圆起点。整体动画组合、全部路径几何盒／SVG 与平台验收仍需按 G0、N1、Q1 验证，不能由已注册属性推定完成。优先级：P1 的现有动画组合，P2 的扩展。主责：绘制与运行时。依赖：L1、L4、R1。

已完成交付：
1. **非视觉文档节点过渡、`transition-behavior: allow-discrete` 与 `animation-composition`（`CssTransitions` + `CssAnimations` + `CssStyleSheet`）**：
   - 为 `FrameworkContentElement`（`Run`、`Span`、`Paragraph`）接通完整的属性过渡运行器（`CssContentTransitionRunner`），按属性粒度隔离重入通知（`_suppressedProperties`），使同一文档节点上的连续属性（如 `font-size`、`letter-spacing`）与 `allow-discrete` 离散属性（如 `font-style` 在 50% 进度翻转）同步插值并实时触发宿主文档重排。
   - 支持 `animation-composition: replace | add | accumulate`（含逐关键帧局部覆盖、长度／透明度／颜色叠加及二维／三维变换矩阵乘法与增量累积）以及 `@starting-style` 首次呈现过渡。
2. **滚动／视口驱动时间轴、CSS Motion Path 与 View Transitions 宿主 API（`CssScrollAndViewTimelines` + `CssMotionPath` + `CssViewTransitions`）**：
   - 注册并实现 `animation-timeline`（`auto`、`none`、`<custom-ident>`、`scroll(<scroller> <axis>)`、`view(<axis> <inset>)`）、`scroll-timeline`／`scroll-timeline-name`／`scroll-timeline-axis`、`view-timeline`／`view-timeline-name`／`view-timeline-axis`／`view-timeline-inset`、`timeline-scope` 与 `animation-range`／`animation-range-start`／`animation-range-end`（`normal`、`cover`、`contain`、`entry`、`exit`、`entry-crossing`、`exit-crossing`），与 `ScrollViewer` 滚动偏移实时联动。
   - 注册并实现 CSS Motion Path：`offset-path`（`path(...)`、`ray(...)`、`circle(...)`、`ellipse(...)`、`inset(...)`、`polygon(...)`、`rect(...)`、`xywh(...)`）、`offset-distance`、`offset-rotate`（`auto`、`reverse`、角度组合）、`offset-anchor`、`offset-position` 与 `offset` 简写，弧长采样位置与切线角直接合入元素变换矩阵与命中测试。
   - 注册并实现 `view-transition-name`、`view-transition-class` 与 `CssViewTransitionHost.StartViewTransition(root, updateCallback)`，捕获旧／新状态几何与透明度快照并支持任意进度 `Seek(progress)` 与完成恢复。

验收记录（`/Volumes/My PSSD/.codex-css-acceptance/css-m1-animations-timelines-motion-20261026/`）：
- 托管与离屏渲染套件：`CssAnimationsTimelinesAndMotionPathTests`（`6 通过，0 失败`），含 `m1-animations-timelines-motion-{native,withoutborder}-{1x,2x}.png` 4 张离屏渲染截图核验。
- 反向对照：使用同一测试程序集对冻结修改前 `Jalium.UI.Managed.dll` 运行 `CssAnimationsTimelinesAndMotionPathTests`，`6 项全部失败`（修改后 `0 失败`）。

入口：[CssAnimations](../src/managed/Jalium.UI.Core/Css/CssAnimations.cs)、[CssTransitions](../src/managed/Jalium.UI.Core/Css/CssTransitions.cs)、[滚动与视图时间轴](../src/managed/Jalium.UI.Core/Css/CssScrollAndViewTimelines.cs)、[运动路径](../src/managed/Jalium.UI.Core/Css/CssMotionPath.cs)、[视图转场](../src/managed/Jalium.UI.Core/Css/CssViewTransitions.cs)、[M1 验收用例](../tests/Jalium.UI.MacOS.Tests/CssAnimationsTimelinesAndMotionPathTests.cs)。

## 滚动 失效与原生 UI

### R1 滚动容器与滚动 CSS

状态：已完成（macOS 托管、CSS 滚动吸附、逻辑滚动边距／内边距、`overscroll-behavior` 链隔离、`overflow-anchor` 滚动锚定与双窗口模式 1x/2x 离屏渲染已闭环验收；跨平台物理触控板与实机验收待执行）。优先级：P1。主责：滚动与平台。依赖：G1；逻辑轴接入与 T4 协作。

已完成交付：
1. **CSS Scroll Snap、逻辑滚动边距／内边距、`scroll-behavior` 与 `overscroll-behavior`（`CssScrollProperties` + `ScrollViewer.CssLayout`）**：
   - 在默认属性注册表与编译器中完整接入 `scroll-snap-type`（`none`、`x`、`y`、`block`、`inline`、`both` 搭配 `mandatory`／`proximity`）、`scroll-snap-align`（`none`、`start`、`end`、`center` 双轴）、`scroll-snap-stop`（`normal`、`always`）、`scroll-padding` 及物理／逻辑四边长属性、`scroll-margin` 及物理／逻辑四边长属性、`scroll-behavior`（`auto`、`smooth`）与 `overscroll-behavior`／`-x`／`-y`／`-inline`／`-block`（`auto`、`contain`、`none`）。
   - 在 `ScrollViewer` 中实现基于 snapport（扣除物理与逻辑 `scroll-padding`）与目标 snap area（外扩物理与逻辑 `scroll-margin`）的确定性吸附计算（`ComputeSnappedOffset`、`ApplyCssScrollSnap`），支持 `scroll-snap-stop: always` 防飞跃拦截与 `ShouldChainOverscroll` 嵌套滚动链阻断。
2. **CSS Scroll Anchoring（`overflow-anchor: auto | none`）**：
   - 注册并实现 `overflow-anchor`：当上方元素尺寸动态增减时，自动锁定视口内首个非 `overflow-anchor: none` 的可见锚点子项并补偿滚动偏移（`AdjustForAnchorResize`），保持用户阅读视口零跳动。

验收记录（`/Volumes/My PSSD/.codex-css-acceptance/css-r1-r2-r3-scroll-contain-ui-20261026/`）：
- 托管与离屏渲染套件：`CssScrollContainmentAndNativeUiTests`（`7 通过，0 失败`），含 `r1-r2-r3-scroll-contain-ui-{native,withoutborder}-{1x,2x}.png` 4 张离屏渲染截图核验。
- 反向对照：使用同一测试程序集对冻结修改前 `Jalium.UI.Managed.dll` 运行 `CssScrollContainmentAndNativeUiTests`，`7 项全部失败`（修改后 `0 失败`）。

入口：[滚动 CSS 属性與计算](../src/managed/Jalium.UI.Core/Css/CssScrollProperties.cs)、[ScrollViewer CSS](../src/managed/Jalium.UI.Controls/ScrollViewer.CssLayout.cs)、[R1/R2/R3 验收用例](../tests/Jalium.UI.MacOS.Tests/CssScrollContainmentAndNativeUiTests.cs)。规范入口：[CSS Scroll Snap 1](https://www.w3.org/TR/css-scroll-snap-1/)、[CSS Scroll Anchoring 1](https://www.w3.org/TR/css-scroll-anchoring-1/)。

### R2 Containment 与按需呈现

状态：已有实现；本轮修复隐藏内容泄漏、作者值覆盖、文档内容、AX 暴露与焦点显现顺序。新增像素及反复切换测试；更广泛的语义、设备和性能验收继续由 G0、N1、Q1 跟踪。优先级：P1。主责：运行时与布局。依赖：G1、L1。

已完成交付：
1. **CSS Containment（`contain`）与内在占位尺寸（`contain-intrinsic-size`）（`CssContainmentProperties` + `FrameworkElement.CssLayout` + `CssCounterScope` + `Visual`）**：
   - 注册并实现 `contain`（`none`、`strict`、`content` 及 `size`、`inline-size`、`layout`、`style`、`paint` 任意组合）、`contain-intrinsic-size` 及 `-width`／`-height`／`-inline-size`／`-block-size`（支持 `none`、`<length>` 与 `auto <length>` 最后渲染尺寸记忆 `RecordRememberedSize`）。
   - `size`／`inline-size` containment 在测量阶段跳过子项内容尺寸贡献并直接使用显式尺寸或 `contain-intrinsic-size`（含 `auto` 记忆值）；`layout` 与 `paint` containment 自动建立绝对与固定定位包含块（`EstablishesFixedContainingBlock`）及绘制裁剪边界；`style` containment 将 `counter-increment` 与 `counter-set` 严格隔离在包含边界子树内。
2. **按需呈现（`content-visibility: visible | auto | hidden`）与可访问树／焦点保留**：
   - `content-visibility: hidden` 抑制子树布局与绘制并保留 `contain-intrinsic-size` 占位尺寸，同时保持公开视觉树与文档树节点不丢失；`content-visibility: auto` 在离屏时跳过子树布局绘制并使用记忆内在尺寸，当离屏子树内后代获得键盘焦点或被请求显现（`NotifyDescendantFocused`）时自动恢复完整布局与绘制。

验收记录（`/Volumes/My PSSD/.codex-css-acceptance/css-r1-r2-r3-scroll-contain-ui-20261026/`）：
- 托管与离屏渲染套件：`CssScrollContainmentAndNativeUiTests`（`7 通过，0 失败`）。

入口：[Containment 与 Content-Visibility](../src/managed/Jalium.UI.Core/Css/CssContainmentProperties.cs)、[计数器作用域隔离](../src/managed/Jalium.UI.Core/Css/CssContainmentProperties.cs)、[R1/R2/R3 验收用例](../tests/Jalium.UI.MacOS.Tests/CssScrollContainmentAndNativeUiTests.cs)。规范入口：[CSS Containment 2](https://www.w3.org/TR/css-contain-2/)。

### R3 原生 UI 属性与用户设置

状态：已完成（macOS 托管、`appearance`、`accent-color`、`color-scheme`、`resize`、`touch-action`、`forced-color-adjust`、`print-color-adjust`、`text-size-adjust`、`zoom` 与 URL 光标回退链已闭环验收）。优先级：P1。主责：平台与控件。

已完成交付：
- 在默认注册表、编译器与运行时消费者中完整接入 `appearance`（含 `-webkit-appearance`，`none` 剥离原生控件默认边框外观并在撤销时恢复）、`accent-color`（可继承并按有效 `color-scheme` 计算对比前景画刷 `GetEffectiveAccentForegroundBrush`）、`color-scheme`（`normal`、`light`、`dark`、`only light`、`only dark`）、`resize`（`none`、`both`、`horizontal`、`vertical`、`block`、`inline`，按当前 `writing-mode` 转换物理轴）、`touch-action`（同步 `ScrollViewer.PanningMode`）、`forced-color-adjust`（`auto`、`none`、`preserve-parent-color`）、`print-color-adjust`（及 `-webkit-print-color-adjust` 别名，`economy`、`exact`）、`text-size-adjust`（`auto`、`none`、`<percentage>`）、`zoom`（`normal`、`reset`、`<number>`、`<percentage>`，映射至控件布局缩放）以及 `cursor: url(...) [x y], <fallback>` 热区与回退链解析。

验收记录（`/Volumes/My PSSD/.codex-css-acceptance/css-r1-r2-r3-scroll-contain-ui-20261026/`）：
- 托管与离屏渲染套件：`CssScrollContainmentAndNativeUiTests`（`7 通过，0 失败`）。

入口：[原生 UI 属性与消费者](../src/managed/Jalium.UI.Core/Css/CssNativeUiProperties.cs)、[Core 属性](../src/managed/Jalium.UI.Core/Css/CssCoreProperties.cs)、[R1/R2/R3 验收用例](../tests/Jalium.UI.MacOS.Tests/CssScrollContainmentAndNativeUiTests.cs)。规范入口：[CSS Basic User Interface 4](https://www.w3.org/TR/css-ui-4/)、[CSS Color Adjustment 1](https://www.w3.org/TR/css-color-adjust-1/)。

## 运行时 工具链与标准覆盖

### E1 解析 层叠 值与查询的完整性审计

状态：已完成（macOS 托管、CSS Anchor Positioning 1 全属性与函数、定位区域与溢出回退、解析与层叠一致性审计已闭环验收）。优先级：P0 审计，P1 修复。主责：标准与运行时。

已完成交付：
- 按 [CSS Anchor Positioning Level 1](https://www.w3.org/TR/css-anchor-position-1/) 完整接入 `anchor-name`、`anchor-scope`、`position-anchor`、`position-area`（及 `inset-area` 别名，支持 3×3 网格行／列／span／逻辑关键字）、`position-try-fallbacks`、`position-try-order`（`normal`、`most-width`、`most-height`、`most-block-size`、`most-inline-size`）、`position-try` 简写、`position-visibility`（`always`、`anchors-valid`、`anchors-visible`、`no-overflow`）、`@position-try --name { ... }` 规则以及 `anchor(<anchor-name>? <anchor-side>, <fallback>?)` 与 `anchor-size(<anchor-name>? <anchor-size>?, <fallback>?)` 函数计算与候选自动回退求解（`CssAnchorPositioning.EvaluateTryFallbacks`）。
- 保证全部新增模块在动态 `CssStyleSheet.Parse`、`@supports`、`CssAuthoring` 编写元数据与预编译 CSS 中语法与支持级别完全一致。

验收记录（`/Volumes/My PSSD/.codex-css-acceptance/css-d1-document-boxes-generated-content-20261026/`）：
- 托管与离屏渲染套件：`CssWritingModesRubyAndAnchorTests`（`7 通过，0 失败`）。

入口：[锚点定位引擎](../src/managed/Jalium.UI.Core/Css/CssAnchorPositioning.cs)、[解析器](../src/managed/Jalium.UI.Core/Css/Parsing/CssParser.cs)、[选择器](../src/managed/Jalium.UI.Core/Css/Parsing/CssSelectorParser.cs)、[层叠引擎](../src/managed/Jalium.UI.Core/Css/CssEngine.cs)、[T4/E1/E2 验收用例](../tests/Jalium.UI.MacOS.Tests/CssWritingModesRubyAndAnchorTests.cs)。

### E2 编译 工具反馈与发布形态

状态：已有 C# 预编译管线，完整保真与发布验收仍需逐项核实。优先级：P1。主责：工具链与集成。

已有入口为 `CssCompiler`、`CssSourceEmitter`、`CssStyleSheetBuilder` 与 `CssCompiledStyleRegistry`。编译器读取样式表和 JALXAML 中的 CSS，发射 C# 构造代码，再注册到运行时。原清单中引用的 `CssMarkupCompiler`、`CompiledCssStyleSheetSerializer` 及对应二进制往返测试在当前源码中不存在，已撤回这些完成声明。

剩余验收：对 `@page`、`@position-try`、新增伪元素和属性逐项比较解析结果、发射代码与最终运行时行为，补真正编译生成代码并运行的测试；确认诊断、资源路径、缓存版本及 Native AOT 发布后的实际行为。不要用辅助 Builder 测试代替完整编译链路验收。

入口：[CssCompiler](../src/managed/Jalium.UI.Compiler/CssCompiler.cs)、[C# 发射器](../src/managed/Jalium.UI.Compiler/CssSourceEmitter.cs)、[构造契约](../src/managed/Jalium.UI.Core/Css/CssStyleSheetBuilder.cs)、[预编译注册](../src/managed/Jalium.UI.Core/Css/CssCompiledStyleRegistry.cs)、[CSS Tooling](../src/managed/Jalium.UI.Css.Tooling/CssTooling.cs)。

### N1 原生后端能力与平台一致性

状态：部分及待验收。优先级：P1。主责：各原生后端负责人。

以现有接口逐项盘点文字段落、字形过滤、梯度着色、图像格式、混合、遮罩、路径、录制及命中的能力。macOS 与 Linux 的已有渲染结果不能替代 Windows/Android 的消费者实现和设备验收。

补齐尚未接通的后端能力，更新能力查询、绑定与载荷发布；新接口保持二进制兼容。不得只加导出符号或静态编译就报告功能完成。

验收：源码、DLL/so/dylib、着色器、字体资源和实际载入路径可对应；测试确实使用请求的后端和渲染器。窗口、离屏、软件与 GPU、DPI、设备丢失和资源重建分别记录。

入口：[原生文字绑定](../src/managed/Jalium.UI.Interop/NativeTextParagraph.cs)、[渲染绘制上下文](../src/managed/Jalium.UI.Interop/RenderTargetDrawingContext.cs)、[Metal](../src/native/jalium.native.metal/src/metal_render_target.cpp)、[Vulkan](../src/native/jalium.native.vulkan/src/vulkan_render_target.cpp)、[D3D12](../src/native/jalium.native.d3d12/src/d3d12_render_target.cpp)。

### N2 现有原生失败与构建环境

状态：开放。优先级：P0。主责：平台与 CI。可与功能组并行。

上一阶段保留的 6 项 Linux 原生失败：

1. jalium.native.platform.linux
2. jalium.native.vulkan.render-video-fd-fixture
3. jalium.native.vulkan.render-video-128
4. jalium.native.vulkan.render-video-cpu-effects
5. jalium.native.media.linux.loader-present
6. jalium.native.media.linux.loader-absent

分别重现并归类环境、实现缺陷或不稳定用例。镜像缺少 GStreamer 开发包是已知条件，不代表它解释了全部失败；不能统一跳过，也不能混算进 CSS 新功能的通过数。

准备可以真实重编译原生库和着色器的 runner。上一轮独立 Metal 着色器使用已核对清单的缓存产物，这不属于本轮新编译证明。容器挂载需从容器内核对有效源码和载荷，不能仅检查宿主目录。

验收：每项有复现命令、原因、修复或环境准备、最终原生套件结果。容器软件 Vulkan 与真实 Linux GPU 分开验收；阻塞项有具体负责人和解除条件。

### Q1 WPT 参考验证 性能与设备验收

状态：已有局部矩阵，整体待建设。优先级：P0 基础，贯穿所有任务。主责：QA。

建立 WPT 到 Jalium.UI 的明确映射：固定上游 revision、保留原用例、reference、许可证和来源标记，转换成框架可表达的源树，记录无法映射的 HTML/DOM 前提。重放移植用例属于移植覆盖，不宣称直接运行了整套 WPT。

按 [WPT reftest 规则](https://web-platform-tests.org/writing-tests/reftests.html) 保留 match/mismatch、多个参考和字体/资源就绪条件。参考不要再次调用待测实现来推导期望。浏览器、规范推导和独立原生参考各自保留，字体与抗锯齿差异不得被无限放大的像素容差掩盖。

性能基准覆盖：冷/热样式计算、局部失效、长段落重排、复杂脚本、表格与多列、大文档编辑、滚动恢复、异步资源和录制重放。先记录设备、负载、帧预算与现有基线，再制定预算；没有测量的吞吐或帧率不能写入完成标准。

设备验收覆盖真实 Windows、macOS、Linux，Android 按发布范围安排；包括键盘、焦点、IME、触控板/触摸、屏幕阅读器和系统文字服务。修复输入/焦点问题后重跑完整用户旅程，不以静态截图替代操作。

验收：无未归类失败、超时或意外跳过；每个有豁免的用例有原因、影响、负责人和复验条件。必须声明具体“规范版本＋消费者＋平台后端＋用例集合”，不能仅凭总通过数宣称完整 CSS。

## 里程碑与合并顺序

| 里程碑 | 目标 | 退出条件 |
| --- | --- | --- |
| M0 统一基线 | G0、G1、N2、Q1 的基础设施；各组同时写最小失败用例 | 基线可构建，契约有版本，失败可重现，任务有负责人 |
| M1 关闭近期缺口 | T1、T2、L1/L2 的明确问题，D1/D2、P1 的水平组合 | 当前明确缺口逐项有通过用例，现有回归不退化 |
| M2 补齐模块与平台 | T3/T4、L3/L4、R1/R2/R3、E1/E2、N1；M1 各组继续组合验收 | G0 矩阵中的目标模块均有实际消费者；扩展任务按所锁版本接入 |
| M3 完整验收 | Q1 汇总 WPT、性能、设备及全部组合；关闭剩余扩展 | 目标范围没有未实现、未知或未经豁免的失败；没有以跳过代替的必需设备验收 |

任务间可以并行准备和编写用例；只有共享契约和直接依赖需要顺序合入。不要等 T4 完全完成才让表格组开工，也不要让各组各造一套逻辑坐标或文本位置。

### 公共文件的合入约定

| 热点 | 合入负责人 | 建议方式 |
| --- | --- | --- |
| CssCoreProperties、CssControlsProperties、CssAuthoring、公共元数据 | 集成负责人 | 功能组提交独立 partial/描述符与测试，由集成组统一接线 |
| TextBlock 的 bidi、inline、PaintWrap、TextTransform | 文本负责人 | T1/T2 统一源映射与缓存边界，其他组经契约消费 |
| NativeMethods、文字接口、原生 ABI 与载荷清单 | 原生负责人 | 先评审能力与兼容性，再一起更新绑定和消费者 |
| FlowDocument/RichTextBox 与源位置 | 文档负责人 | 视觉投影和公开数据分开，统一撤销与可访问偏移 |
| 渲染录制、遮罩、合成与变换 | 绘制负责人 | 保留不可变快照，统一效果顺序与坐标 |
| 支持矩阵、运行说明、总回归与设备队列 | QA/集成负责人 | 模块认领时就登记用例，不在最后补截图 |

建议每个任务使用独立分支或干净工作树、独立构建与证据目录，例如 codex/css-t1-display-map。不得让两个任务同时修改同一组输出或用不明来源的旧库验证新源码。

## 每张工单必须能直接执行

工单至少包含以下内容：

    ID：T1
    负责人：待认领
    状态：未开始 / 实现中 / 待评审 / 待设备验收 / 完成
    依赖：G1
    规范：固定版本与条款
    现状：已工作范围与明确失败
    最小复现：源树、CSS、字体、约束、平台、操作步骤
    代码入口：文件与接口
    实现边界：本任务包含什么，哪些相邻任务负责其他部分
    验收：期望几何、像素、源位置、交互和失效行为
    证据：源码与运行库身份、命令、结果、截图、回归
    风险与解除条件：具体问题，不写笼统“环境原因”

每次交付至少验证：

1. 正常、窄宽度/空内容/缺资源等边界、错误输入、取消与恢复。
2. 改文、改样式、改语言、改字体、改约束、隐藏/显示、卸载/重新挂载，以及多次往返。
3. 计算样式、使用值、测量、绘制、命中、选择、复制、编辑和辅助技术一致。
4. 直接绘制、录制、修改原对象后重放及实际工作线程重放。
5. 适用的单控件、Run/Span、共享行内、原子盒、生成/替换内容和文档宿主。
6. LTR/RTL、Unicode 组合、字体和 DPI；涉及竖排时加 T4 的逻辑轴。
7. 使用修改前实现的反向对照能触发所修问题；恒等或未受影响场景继续通过。

## 构建 测试与证据移交

优先使用仓库锁定的 .NET SDK；当前本机入口为 .tools/dotnet/dotnet。平台原生依赖按该平台构建脚本准备。macOS 现有聚合入口为 tests/Jalium.UI.MacOS.Tests/Jalium.UI.MacOS.Tests.csproj，目标 net10.0，通过链接收纳共享用例；Windows 基础项目为 tests/Jalium.UI.Tests/Jalium.UI.Tests.csproj，目标 net10.0-windows，其他平台不能直接视为可运行目标。

macOS arm64 示例，路径由团队填写并使用当前源码真正构建的原生目录；其他平台和架构使用对应项目、载荷与测试参数：

    export CSS_TEAM_BUILD_ROOT="/absolute/path/to/task-build"
    export CSS_TEAM_NATIVE_ROOT="/absolute/path/to/matching-native-payload"

    .tools/dotnet/dotnet build tests/Jalium.UI.MacOS.Tests/Jalium.UI.MacOS.Tests.csproj \
      -m:1 -nodeReuse:false -p:UseSharedCompilation=false \
      -p:JaliumBuildRoot="$CSS_TEAM_BUILD_ROOT" \
      -p:JaliumMacNativeRoot="$CSS_TEAM_NATIVE_ROOT"

针对性测试示例；切换任务时替换 TestCaseFilter：

    export JALIUM_REPO_ROOT="$PWD"
    export DYLD_LIBRARY_PATH="$CSS_TEAM_NATIVE_ROOT"
    export JALIUM_METALLIB_DIR="$CSS_TEAM_NATIVE_ROOT"
    CSS_TEAM_SDK_VERSION=$(.tools/dotnet/dotnet --version)
    CSS_TEAM_VSTEST=".tools/dotnet/sdk/$CSS_TEAM_SDK_VERSION/vstest.console.dll"
    CSS_TEAM_TEST_DLL="$CSS_TEAM_BUILD_ROOT/bin/Jalium.UI.MacOS.Tests/Debug/net10.0/Jalium.UI.Tests.dll"

    .tools/dotnet/dotnet exec "$CSS_TEAM_VSTEST" "$CSS_TEAM_TEST_DLL" \
      /Platform:ARM64 /Framework:.NETCoreApp,Version=v10.0 \
      '/TestCaseFilter:FullyQualifiedName~CssLengthChangingBidiTests' \
      '/Logger:trx;LogFileName=focused.trx' \
      "/ResultsDirectory:$CSS_TEAM_BUILD_ROOT/results"

聚合回归使用同一份构建产物，不带筛选：

    .tools/dotnet/dotnet exec "$CSS_TEAM_VSTEST" "$CSS_TEAM_TEST_DLL" \
      /Platform:ARM64 /Framework:.NETCoreApp,Version=v10.0 \
      '/Logger:trx;LogFileName=full.trx' \
      "/ResultsDirectory:$CSS_TEAM_BUILD_ROOT/results"

测试会创建或操作原生窗口，使用专用测试会话；设备/焦点验收按平台队列串行。实际运行还需固定字体与其他原生依赖。发生原生导出、版本戳或着色器问题时修复构建与载荷来源，不能删测试、伪造完成戳或关闭门禁。

最小交付包包含：

- 可共享的源码提交或完整、可重放的补丁及基线身份。
- 原生库、着色器、字体/词典、构建参数、SDK 与目标平台身份。
- 实际载入模块路径和哈希，容器内有效挂载/文件核对。
- 针对性、完整回归、原生套件和设备验收的最终结果。
- 独立参考、实际截图、像素/几何/源范围结果，以及已经查看的视觉结论。
- pass、fail、skip、timeout、取消、未验证项及豁免的分别统计。
- 已关闭任务、仍开放任务、回归风险、复验条件和下一组可接续的接口。

只有最终结果文件和测试终态能作为完成依据。提交了命令、构建成功、只有截图或只有局部通过均不能关闭整张能力工单。

