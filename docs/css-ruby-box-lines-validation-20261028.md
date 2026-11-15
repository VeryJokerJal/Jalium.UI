# CSS Ruby 内容盒与行距增量验证（R15）

历史快照说明：本文保留 R15 的实现、计数及当时缺陷。文中默认原点命中问题已在后续 R16 的实际验证范围内修复，当前状态、来源切换及 CSS 过渡边界见 [变换原点验证（R16）](css-transform-origins-validation-20261028.md)；其余完整 CSS／Ruby 开放项继续保留。

本轮继续“完整 CSS”目标，补齐共享行内 Ruby 消费者中的字体内容盒、必要 leading、侧边注音行贡献与部分盒适用性。**完整 CSS、T4 与完整 Ruby 仍为部分完成**。本页只对本轮冻结候选和实际列出的场景给出结论。

## 实现范围

- 普通基字、行间注音及其普通行内文字使用字体 ascent＋descent 内容区；注音自身的 `line-height` 不再撑大这层内容盒。作者的 `LineHeight`／CSS 值仍保留，可读取、改写和恢复。独立 inline-block、替换内容及侧边 `inter-character` 注音保留自身行度量。
- 基字和 Ruby 宿主原始行度量参与行框计算；行间注音超过可用行距时，仅增加防止连续相同行重叠所需的 leading。当前策略按上下注音外伸比例分摊不足空间；墨迹可以伸出行框。覆盖行距 0、40、60、80，以及上下、多层注音和连续三行。
- `separate`、`merge`、`auto` 复用相同逐行单元模型，先对齐同一注音行的基线，再按关联基字定位。此前 `separate` 中不同字号的注音使用各自基线，30 项检查中实际失败 10 项；统一后对齐，并保留各原始来源。
- 侧边注音作为基字层内的 inline-block，其完整 ascent／descent 参与行贡献；即使内容高度恰好等于基字字体区，也必须计入。真实 PingFang 字体区大于 40px 行距的场景覆盖 LTR／RTL、start／center 和三种合并策略；明确侧边高度下的 space-around 仍按自然字宽分配边缘空间。
- 非替换的 `ruby-base-container`／`ruby-text-container` 忽略 margin、padding、border 与不适用尺寸；普通行内 Ruby 盒忽略 CSS 宽高及 min／max，不改写作者值。原生 C# 本地尺寸的优先级另有回归。边框装饰与 border-image 同步遵守结构容器的适用性。
- 字体内容区上下文只沿真实控件父链生效，不沿样式代理父链扩散；原来源离开 Ruby 后恢复普通行高，移入另一 Ruby 或独立 inline-block 后重新判断。保留的 TextBlock 布局缓存包含使用值上下文，避免重挂父级后继续使用旧字体盒。
- 原生窗口暴露后续布局重复应用结构容器 margin 的路径：开启 37px margin 后四个原始文字来源整体下移 37px，关闭后恢复。现在 FrameworkElement 在原生 Measure／Arrange 和自动 margin 分配中也忽略该结构 margin；补充重放保存布局槽的检查，不能只验证根节点的一次 Measure／Arrange。

主要入口为 `CssRubyBoxMetrics.cs`、`FrameworkElement.cs`／`FrameworkElement.CssLayout.cs`、`TextBlock.CssInline.Ruby.cs`／`RubyMerge.cs`、`TextBlock.CssLineHeight.cs` 与现有边框绘制路径。普通来源仍使用原控件、塑形、选择与命中接口。

## 验收状态

最终候选几何回归 **479／479**、定向矩阵 **1,986／1,986** 通过；新增 `CssRubyBoxLineTests` 为 817 项（277 项几何与恢复、540 项像素）。定向矩阵包含既有 Ruby、共享竖排、行内方向与 writing-modes 用例，0 失败、跳过、超时。macOS 完整聚合 **22,700／22,700** 通过，0 失败、跳过、超时，用时 1421.1 秒，约 23 分 41 秒；817 项新增全部包含，前轮 21,883 项精确用例名及重复次数全部保留。见 `final-full/summary.json`、`legacy-case-retention.json`。

- 严格像素：**1,434 对／2,868 张 PNG** 的解码 RGBA 全部相同，Software／Impeller、Metal／Impeller、Metal／Vello 各 478 对，真实校验后端及读取像素。新增 540 对，各组合 180 对，覆盖五种方向、LTR／RTL、三种合并策略、紧行距／大行距／下方／零行距／双层／结构容器六种场景。参考由没有 Ruby 角色的普通 TextBlock 放入独立字体度量与明确物理槽构成，不读取实际 Ruby 几何。已查看 15 张新增矩阵联系图与九张更新后的旧 Ruby 联系图。见 `focused-pixel-identity.json`、`contact-visual-review.json`。
- 旧参考语义修正：旧 rb／rt 的 40px／20px 行盒改为字体内容区，紧行距允许注音外伸；侧边 inline-block 的完整行贡献单独计入。为显露外伸墨迹，实际／参考共同平移 32px，旧 Ruby 目标扩为 320px，新行框目标为 400px；根布局约束保留。旧 Ruby 图像按新规范语义更新，不宣称像素未变。另有 342 张非 Ruby 竖排／共享范围旧实际帧与 R14 解码 RGBA 相同。
- 原生 CTest：本轮既有选择集 **65／65** 重跑通过，注册总数 117，平台／媒体模式排除的 52 项不计为通过。复用既有实际 R11 原生构建；与 R14 及实际构建冻结源比较 822 个输入（771 个 native、51 个 Apple 构建输入）相同，没有声称重新编译 native。见 `native-repeat/`、`native-reuse-input-manifest.json`。
- 修复前证据：首批行高检查 30 项中 27 项失败；不同字号注音基线 30 项中 10 项失败；原生重复布局回归 **30 项全部失败**。原生窗口中四个来源的 37px 位移与恢复也有独立事件记录。见 `before/summary.json`、`mixed-before/summary.json`、`native-margin-before/summary.json`、`native-margin-negative.json`。已停止的旧候选矩阵和错误筛选得到的零用例结果均不计为最终通过。

全量中的同组 1,434 对像素与定向结果相同，24 张最终联系图的 PNG 摘要与已查看版本相同。见 `final-pixel-identity.json`、`final-contact-view-identity.json`。最终框架重新编译 0 错误、24 项既有警告；可比的测试项目重编译从 73 增至 75 项警告，新增两处 xUnit1031 位于沿用的有界 Metal 主线程子进程等待模式，不是无警告构建。见 `build-diagnostic-comparison.json`。

证据目录：`artifacts/css-ruby-box-lines-20261028-e25a8d7b/`。起点冻结 4,577 个 Git 可见输入，HEAD 为 `b85d3540`，保留全部既有未提交工作。最终增量为 22 个代码／测试路径和三个文档路径，范围及并行变更以 `source-audit.json` 为准。`implementation-delta.patch` 相对本轮冻结起点，依赖已有 R12—R14 和该工作区，不是相对主分支的独立补丁；`patch-verification.json` 记录检查、应用和最终文件摘要复现。

复现需保留 `source/` 快照、实际 native 载荷和本轮工具；构建参数在 `managed-build-command.json`，定向／全量命令和环境在对应阶段的 `command.json`。`run-tests.py` 使用已有构建，不替团队后续源码重新编译；跨机需调整记录中的本机绝对路径、SDK 和 native 位置。

## 耗时观察

按相同精确用例名的时长中位数比较，旧用例时长总和约从 1069.4 秒增至 1148.8 秒，新增项合计约 267.2 秒。未出现同时满足“至少增加 1 秒且至少 8 倍”的新增离群增长。日志中约 8 秒的文档生成内容用例在 R14 约为 8.7 秒。该聚合记录不能作为受控基准；前轮顺序／进程状态线索、长 Ruby 段落和完整应用性能验收仍开放。见 `duration-comparison.json`。

## 原生窗口与交互

实际查看 **50 张截图**，共五个实例、105 条事件、100 份来源快照；其中三个实例加载最终框架，最后一个验收页显式设置原生缩放原点。每个实例保留 12 个原始 TextBlock 和 14 个父级关系，全部记录中原父级不变。所有实例已关闭，没有剩余进程或致命错误文件。

实际操作覆盖五种方向、三种合并策略、over／under／alternate、LTR／RTL、行距 40／80／0、长注音／空注音／恢复、干扰样式开关，以及 1040×700、860×630、系统放大 1920×942 后恢复。正常尺寸拖选注音得到“と”`0+1`，Cmd+A 得到“とう”`0+2`，基字可选择“東”`0+1`；显式原点的 75% 窄窗拖选得到“とう”`0+2`，再全选得到“とうとうとう”`0+6`。Tab 显示按钮焦点，Space 实际切换方向。30 对恢复／干扰样式内容裁剪的 RGBA 逐字节相同；完整截图另行人工查看，裁剪避开窗口操作指针的阴影。

原生主题实际选择 SF Pro 字体族及其 CJK 回退，基字内容高度 37.6875px，和离屏 Hiragino／PingFang 度量不同。辅助线取实际相邻行的间隔，未把固定 48px 当作原生字体结果。窗口验证不代替其他字体、平台或物理辅助技术验收。

应用位于 `/Users/jal/.cache/jalium-ui-css-validation/e25a8d7b/JaliumCssRubyLines.app`，使用本地自包含 .NET 运行库。八个框架程序集与受测文件摘要相同；八个签名 dylib 的 UUID 和全部文件支持段相同；两个 metallib 另核摘要。事件、截图、几何恢复与载荷证明见 `live-output/`、`live-ui-summary.json`、`live-packaging.json`、`live-native-identity.json`。

## 已复现但尚未修复的原生默认原点问题

宿主只设置原生 `RenderTransform = ScaleTransform(.75, .75)`、没有显式设置原生 `RenderTransformOrigin` 时，2D 绘制／坐标矩阵使用左上角，共用 CSS／3D 命中路径却为默认值补上中心原点。窄窗的可视注音无法获得指针焦点；独立程序的 20 个组合中，五种方向各一个默认原点缩放场景失败，另 15 个命中成功。显式设置 `(0, 0)` 后的十个比较场景均通过，并已在最终原生页重新拖选、全选和逐方向检查。

相同诊断程序集换载 R14 的八个精确框架程序集后，也复现相同的五个失败组合，确认该冲突在前轮已经存在。两侧程序程序集摘要相同，框架载荷逐项核对，见 `hit-diagnostic/r14-r15-comparison.json`。

这是验收页的显式配置规避，**产品中的通用默认值冲突仍未修复**。不能将正式回归的通过外推为全部缩放命中通过。后续应区分原生默认左上角与 CSS transform 默认中心，并同时验证 2D／3D、命中、坐标转换和实际指针，避免直接更改共用默认值造成 CSS 回归。证据见 `hit-diagnostic/Program.cs`、`hit-diagnostic/explicit-result.log`、`hit-diagnostic/summary.json` 与原生截图 39／40、43／44。

## 团队后续任务

0. **通用缩放原点与命中（下一优先项，Core／Input）**：检查 `CssTransform3D.GetElementMatrix3DInParent`、`TryMapParentPointToChildLocal` 与 `UIElement.GetRenderMatrix` 的原点来源；区分原生属性默认、CSS 使用值、本地原点与显式 CSS 原点，统一绘制、命中和坐标转换。把本轮 20 项诊断转为正式回归，以普通块盒／原生控件及 Ruby 验证缩小、放大、平移组合、非零父级位置、奇异矩阵和鼠标选区。保留 CSS 普通布局盒 `transform-origin` 初始值 `50% 50%` 的规范语义及既有 3D／perspective 行为，依据 [CSS Transforms 1 §5](https://www.w3.org/TR/css-transforms-1/#transform-origin-property)。不能只给验收页补显式原点就关闭该项。
1. **剩余盒与基线语义**：完整 rb／rt margin、padding、border 和 intrinsic／百分比／嵌套尺寸组合；规范要求忽略的 `vertical-align: top/bottom`；复杂字体、独立行内盒及混合基线。当前适用性修正不能作为完整 Ruby 盒模型验收。
2. **归一化和空间分配**：完整匿名盒、空白折叠、元素边界及尾随空白，复杂来源内部／跨来源分配、overhang、多层与嵌套 Ruby；建立长段落的受控性能基线。
3. **方向和分片**：ruby／base-container／text-container 独立方向、复杂多脚本 bidi、正交嵌入，分页／多列／float／clamp 的组合及更广规范／WPT 场景。
4. **宿主与平台**：FlowDocument、RichTextBox／编辑器消费者；Windows／Linux、其他 GPU、触控、物理 IME 和 VoiceOver 实际朗读。macOS AX 树和键盘检查不代替这些验收。
5. **其他 CSS 模块**：继续按 [团队实施计划](css-implementation-plan.md) 的开放项分工。沿用“规范版本、真实消费者、平台与后端、正常／边界／恢复、证据”的验收口径。

规范依据：[W3C CSS Ruby Annotation Layout Level 1](https://www.w3.org/TR/css-ruby-1/) 的公开工作草案（2022-12-31），重点为 §3.2 inter-character、§3.3 盒适用性与 §3.6 行计算。leading 的具体分配存在用户代理策略空间，本文说明当前策略。前轮记录为 [R14 合并注音验证](css-ruby-merge-validation-20261028.md)，其历史计数和图像不代表本轮新语义下的最终候选。
