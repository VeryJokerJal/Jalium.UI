# Jalium.UI CSS 实现验收复核

复核日期：2026-10-27。结论：当前源码尚未达到“完整 CSS 支持”的验收条件。现有实现已覆盖大量功能，但独立复现发现竖排、Ruby、内容隐藏、Motion Path 和背景裁剪存在实际缺口，原有回归也未全绿。应先修复并复验这些问题，再确认各里程碑状态。

后续状态：本页保留首次独立复核的历史结果。隐藏、变换、背景与源范围修复见 [修复记录](css-repair-validation-20261027.md)；独立 TextBlock 的真实竖排已在后续接线，当前证据和仍未实现的 upright、纵中横、Ruby、文档/编辑器边界见 [TextBlock 竖排验证记录](css-textblock-writing-mode-validation-20261027.md)。历史失败不等同于这些后续快照的测试结果。

证据可用性：原外置盘 `.codex-css-acceptance` 目录后续已不再位于原路径，本页末尾的九个历史证据链接目前不能复读。后续重新构建与执行的证据已迁入仓库 `artifacts/`，入口见上述两份记录；没有将重新执行的结果填入本页的历史计数。

## 基线与测试结果

本轮从工作区冻结 4,426 个输入文件，保留已修改和未跟踪的源码。Git HEAD 为 `b85d354029c7c7b366e0fb610c5d49ab7c8c63b4`；该提交号本身不包含完整待验收实现。运行时、测试程序集与原生 C++ 库均从这份冻结源码隔离构建。冻结源码和实时工作区的差异见审计 JSON。

| 验证项目 | 本轮结果 | 解释 |
| --- | --- | --- |
| 托管 Release 构建 | 0 错误，82 警告 | 警告包含裁剪/AOT 分析及测试分析器提示；构建通过不代表 AOT 发布通过 |
| macOS 聚合全量回归 | 19537 通过，161 失败，2 未执行或跳过，0 超时；共 19700 项。 | 原始 TRX 是最终计数依据 |
| 全部失败方法定向复测 | 283 项：122 通过，161 失败，0 跳过，0 超时 | 使用新进程，161 个失败用例与全量完全一致；与全量有重叠，不累加成覆盖量 |
| 独立消费者与像素检查 | 10 项：1 通过，9 失败 | 覆盖现有展示测试遗漏的场景；完整用例及实测值见 probe-results.json |
| 原生文字、字体和绘制 CTest | 51 项：51 通过，0 失败，0 超时 | 从当前原生源码重建，包含 Software/CoreText/Metal 相关路径；排除了 platform/media 分类，其余 52 项不计通过 |
| 正式 macOS HostSmoke 构建 | 被 SDK 版本检查阻断 | .NET macOS SDK 26.5.10280 要求 Xcode 26.5，本机 Xcode 27.1；未关闭检查 |
| 桌面 Metal 复现夹具 | 已操作并查看截图 | 竖排和隐藏问题均在实际 NSWindow 中复现；按钮、全选及 ASCII 输入有回传 |

已查看 64 张全量测试 PNG 的 12 张汇总图，以及独立用例和桌面截图；这是视觉核查记录，不是全部功能通过的声明。

两份 Metal `.metallib` 从现有载荷复用，已核对其 SHA-256 清单，未重编译着色器。原生 C++ 库为本轮新构建，DXC/SPIRV-Cross 等构建依赖复用已有缓存。软件离屏检查和桌面 Metal 检查相互补充，均不能替代 Windows、Linux、物理输入和辅助技术设备验收。

运行时导出 450 个编写元数据条目。属性名称或 `Supported` 标记只表示注册与编写入口，不能用作规范条款覆盖率。

## 已确认的问题

以下问题都在本轮冻结源码上复现。P1 表示阻碍本次完整性验收，应由原任务负责人修复后重新验证。

### P1 竖排属性没有驱动真实文本布局

关联任务：T4、G1。复现：为相同的 `TextBlock("ABCD")` 分别应用 `horizontal-tb` 和 `vertical-rl; text-orientation:upright`，执行 Measure、Arrange、Render，再比较像素及源字符几何。

`writing-mode` 的计算值确实为 `VerticalRl`，但两个渲染结果逐像素相同。第一、第二字符的基线均为 `77.560546875`，X 从 `0` 增至 `18.67578125`，仍按横排前进。`text-orientation:mixed` 与 `upright + text-combine-upright:all` 也得到相同像素。桌面 Metal 切换按钮重现相同行为。

[CssWritingModeProperties.cs](</Volumes/My PSSD/source/Repos/Jalium.UI/src/managed/Jalium.UI.Core/Css/CssWritingModeProperties.cs:138>) 的变化回调只失效布局；当前 `TextBlock` 排版与绘制没有消费这些书写模式、文字方向及纵中横数据。应接通真实测量、换行、绘制、选区、命中与文档宿主，再复验逻辑轴下的布局任务。

规范依据：[CSS Writing Modes](https://www.w3.org/TR/css-writing-modes-3/#block-flow) 定义了 vertical-rl 的垂直排字方向。

### P1 Ruby 显示值在实际解析器中被拒绝

关联任务：T4、E1、E2。`CssAuthoring.IsValueSupported("display", value)` 对 `ruby`、`ruby-base`、`ruby-text`、`ruby-base-container`、`ruby-text-container` 五个值全部返回 false。[CssDisplayProperties.Register](</Volumes/My PSSD/source/Repos/Jalium.UI/src/managed/Jalium.UI.Core/Css/CssDisplayProperties.cs:319>) 的实际解析分支也没有这些显示模式。

元数据已列出这些值，辅助几何函数已有实现，但它们尚未构成可通过 CSS 使用的 Ruby 盒树。应先接入解析、盒生成及匿名 Ruby 结构，再实现布局、绘制和源位置映射。

规范依据：[CSS Ruby 盒模型](https://www.w3.org/TR/css-ruby-1/#ruby-display) 将这些值定义为 Ruby 容器、基字与注音盒。

### P1 content-visibility 标记跳过后仍绘制内容

关联任务：R2、D2、Q1。确认了三种情况：

| 情况 | 状态 | 实际绘制 |
| --- | --- | --- |
| TextBlock 自身 `content-visibility:hidden` | IsContentSkipped 为 true | 仍有 1,822 个非透明像素 |
| hidden 宿主的孩子显式设置 Opacity 为 1 | 宿主跳过，孩子仍可命中 | 仍有 1,739 个非透明像素 |
| TextBlock 自身 `content-visibility:auto`，明确处于离屏跳过状态 | IsContentSkipped 为 true | 仍有 1,836 个非透明像素 |

桌面 Metal 切换到 hidden 后，红色正文仍可见，AX 树也仍包含该文本。现有 [ApplyChildrenSkippedState](</Volumes/My PSSD/source/Repos/Jalium.UI/src/managed/Jalium.UI.Core/Css/CssContainmentProperties.cs:969>) 只改视觉孩子的透明度和命中层，漏掉控件自身绘制的文字；孩子存在局部值时又不施加抑制。因此不应通过临时覆盖透明度来表达整个跳过语义。需要在布局、绘制、命中及相应辅助技术路径一致地处理跳过状态，并保留原样式。

规范依据：[CSS Containment 的内容跳过规则](https://www.w3.org/TR/css-contain-2/#content-visibility) 同时约束文字、元素、替换内容的绘制及命中。

### P1 移除 Motion Path 后残留位移

关联任务：M1、L4。对一条从 `(0,0)` 到 `(200,0)` 的路径设置 `offset-distance:50%`，实际位移为 `100`。随后改为 `offset-path:none`，位移仍为 `100`，预期应恢复原来的变换。

[CssMotionPath.SyncElementMotionTransform](</Volumes/My PSSD/source/Repos/Jalium.UI/src/managed/Jalium.UI.Core/Css/CssMotionPath.cs:388>) 在路径为 None 时直接返回，没有撤销先前写入的 CSS 变换。修复时需要恢复底层有效变换，覆盖属性删除、none、样式替换、动画结束和反复切换。

### P1 Motion Path 覆盖普通 transform

关联任务：M1、L4、G1。在同一元素上组合上述 100px 的 Motion Path 与 `transform:translateX(40px)`，预期为 140px，实际为 100px。Motion Path 直接写同一个 RenderTransform 层，覆盖普通 transform。

应保留独立变换来源，按 [Motion Path 模块交互顺序](https://www.w3.org/TR/motion-1/#module-interactions) 组合，并让绘制、命中、选区及录制重放使用同一矩阵。

### P1 背景裁剪的并集被实现为交集

关联任务：P1、E1。复现：透明边框、红色背景、透明前景文字，设置 `background-clip:border-area text`。边框环内部采样点预期红色且 alpha 为 255，实测 alpha 为 0，整个目标的非透明像素数为 0。

[CssBackgroundPainter.DrawLayer](</Volumes/My PSSD/source/Repos/Jalium.UI/src/managed/Jalium.UI.Controls/CssBackgroundPainter.cs:65>) 先绘制边框环，再施加字形 alpha 遮罩，形成交集；当前 [CSS Backgrounds 4](https://drafts.csswg.org/css-backgrounds-4/#background-clip) 对 `border-area text` 定义的是并集。需要合并边框和文字覆盖，再绘制单层背景，以避免重叠处重复混合。

同一规范的语法并未允许长属性 `background-clip:padding-box text`。本轮旧回归中相关 4 项失败不能直接当作“旧测试过时”删除：实际实现接受了非法长属性组合，并将简写的 origin 与 text clip 错误合并。应按锁定规范逐条校正。

## 原有回归的失败分布

全量共 19,700 项，其中 161 项失败，分布在 25 个测试类；新进程的 283 项定向复测重现了同一组 161 个失败。它们不是 161 个已经归因的独立产品缺陷；部分测试观察 DrawText 记录，需要进一步用实际像素和源范围核对。但在归因和复验之前，也不能将它们计入通过。

| 测试类 | 失败数 |
| --- | --- |
| CssBackgroundTextClipTests | 4 |
| CssCounterStyleTests | 1 |
| CssDocumentBoxTests | 20 |
| CssDocumentCellHeightTests | 6 |
| CssDocumentColumnHeightTests | 3 |
| CssDocumentColumnTests | 2 |
| CssDocumentHeightTests | 2 |
| CssDocumentIntrinsicSizingTests | 65 |
| CssDocumentLineClampTests | 2 |
| CssDocumentTableWidthTests | 5 |
| CssFirstLetterTests | 2 |
| CssFirstLineTests | 5 |
| CssInitialLetterBlockFlowTests | 2 |
| CssInitialLetterTests | 2 |
| CssInlineBoxTests | 19 |
| CssInputSpacingTests | 1 |
| CssNestedDocumentColumnTests | 2 |
| CssNestedInlineTests | 1 |
| CssParagraphWrapStyleTests | 2 |
| CssPositioningTests | 1 |
| CssSelectionContextTests | 1 |
| CssSharedShapingTests | 3 |
| CssThemeCurrentColorTests | 4 |
| CssWritingToolsTests | 5 |
| RichTextBoxInlineBackgroundTests | 1 |

失败最集中于文档固有尺寸（65 项）、文档盒（20 项）和行内盒（19 项），应优先复查 D1 与 L1/L2/L3 的宿主消费链路。已明确的异常包括首行和首字样式变化、首字下沉反复布局与新树不一致、嵌套百分比 padding、移除 fixed 定位后的恢复、Writing Tools 显示过滤记录、富文本装饰颜色和表格单元格多列几何。全部用例名称、实际值和堆栈保存在 full-failures.json 与两份 TRX 中；单例宽度预期 176px 却得到 32px 等失败需要实际布局复验。

## 为什么原先的验收仍会通过

`CssWritingModesRubyAndAnchorTests` 本轮 8 项全过，`CssAnimationsTimelinesAndMotionPathTests` 6 项全过，但独立测试仍重现上述问题。竖排测试大量断言枚举、坐标辅助函数和几何计算；展示页将文字放进窄列，Ruby 样例则用普通 StackPanel 手工摆放。展示代码中的 native/withoutborder 只改变标签文字，未创建两种实际窗口。这些图片不能证明竖排、Ruby 或两种窗口的原生行为已通过。

背景测试也主要覆盖单独的 border-area、组合值元数据和独立字形捕获，没有检查 `border-area text` 的真实并集像素。动画测试缺少属性撤销与普通 transform 的组合。应增加独立参考、反复修改和像素或几何断言，使测试能识别“已解析但消费者未接入”的实现。

## 实际效果与验收边界

本轮普通横排文字在 Software 和 Metal 中能够渲染，源文本全选保持原值；变长转换（262 项）、跨节点上下文与断词（120 项）及复杂塑形组合（36 项）在本轮全量回归中通过，相关图像已有实际文字输出；桌面夹具的按钮、键盘全选和 ASCII 替换能回传。竖排、隐藏及背景组合存在明显视觉错误，当前不能评价为完整 CSS 效果已稳定。

桌面夹具使用相同的通用 net10.0 运行时与当前原生库，以复核 TextBlock 行为。它不等于正式 macOS HostSmoke 或完整 Gallery：夹具没有完整模板启动入口，输入框和按钮的视觉样式不用于产品外观验收。一次 CUA 中文直接输入未完整回传，本轮没有将其归因于 CSS，也未将中文 IME、物理键盘或 VoiceOver 标为通过。

原计划仍将 G0 标为待审计、G1 标为需扩展、N1 标为部分、N2 标为开放、Q1 标为整体待建设。Windows/Linux 原生后端、跨平台实机输入、辅助技术、物理打印、WPT 全模块映射与独立性能基准仍需完成。450 个元数据条目不能填补这些验收项。

实现计划中的 105 个本地代码链接有 8 个目标缺失，包括书写模式、分页计算器和编译器入口；这些主要是文档链接错误，不能据此推断对应整个功能不存在，但应在团队交接前修正。

## 后续处理顺序

1. 重开 T4、R2、M1、P1，先使用本轮独立失败用例修复真实消费路径；不要仅调整展示页或完成状态。
2. 对 161 个原有失败逐条归因，优先处理文档尺寸与行内盒回归。观察器需要更新时，同步补实际像素或几何参考，避免把缺失绘制当作记录格式变化掩盖。
3. 将消费者、反复修改、撤销、文档宿主、选区与命中、录制重放纳入合入门槛，再运行同一套全量回归。
4. 完成 G0/G1，并恢复正式 macOS 宿主构建。然后执行 Windows/Linux、物理输入和辅助技术设备矩阵，按实际结果更新原实现计划。

## 复现与证据

证据根目录：`/Volumes/My PSSD/.codex-css-acceptance/css-review-20261027-149b219c`。实现代码在本轮未修改，未提交或推送；复现程序与构建产物保存在工作区外。

- [19,700 项全量回归 TRX](</Volumes/My PSSD/.codex-css-acceptance/css-review-20261027-149b219c/results/full.trx>)
- [283 项失败方法定向复测 TRX](</Volumes/My PSSD/.codex-css-acceptance/css-review-20261027-149b219c/all-failures-results/all-failures.trx>)
- [独立用例与实测结果](</Volumes/My PSSD/.codex-css-acceptance/css-review-20261027-149b219c/probe-output/probe-results.json>)
- [51 项原生测试 XML](</Volumes/My PSSD/.codex-css-acceptance/css-review-20261027-149b219c/native-results.xml>)
- [冻结源码和实时漂移审计](</Volumes/My PSSD/.codex-css-acceptance/css-review-20261027-149b219c/source-drift-audit.json>)
- [桌面进程实际加载文件与 SHA256](</Volumes/My PSSD/.codex-css-acceptance/css-review-20261027-149b219c/live-loaded-files.json>)
- [独立用例渲染汇总](</Volumes/My PSSD/.codex-css-acceptance/css-review-20261027-149b219c/independent-probe-contact.png>)
- [桌面 Metal 竖排与隐藏复现截图](</Volumes/My PSSD/.codex-css-acceptance/css-review-20261027-149b219c/live-output/final-metal-window.png>)
- [原计划失效链接明细](</Volumes/My PSSD/.codex-css-acceptance/css-review-20261027-149b219c/documentation-audit.json>)

本机复现独立用例时，为输出选择一个新目录，并使用证据目录中的运行时和原生库：

```sh
CSS_AUDIT_OUTPUT="/absolute/path/to/new-probe-results" \
DYLD_LIBRARY_PATH="/Volumes/My PSSD/.codex-css-acceptance/css-review-20261027-149b219c/native-fresh/osx-arm64/Release" \
JALIUM_METALLIB_DIR="/Volumes/My PSSD/.codex-css-acceptance/css-review-20261027-149b219c/native-fresh/osx-arm64/Release" \
"/Volumes/My PSSD/source/Repos/Jalium.UI/.tools/dotnet/dotnet" \
"/Volumes/My PSSD/.codex-css-acceptance/css-review-20261027-149b219c/probe/bin/Release/net10.0/Jalium.UI.Tests.dll"
```

该程序返回 1 表示检出缺陷，不能因此把失败分支移除。构建参数、依赖和测试命令分别保存在 native-configure-args.json、managed-build.log、test-command.json 和 retest-command.json；团队迁移时需一并移交冻结源码、匹配的原生依赖和字体。
