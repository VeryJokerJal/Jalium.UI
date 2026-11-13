# TextBlock 真实竖排增量与验证

日期：2026-10-27。关联任务：[T4](css-implementation-plan.md#t4-垂直书写-逻辑轴与-ruby)。完整 CSS 和整个 T4 仍未完成。本增量的范围是 macOS 中未经过共享 CSS 行内盒/片段分配的独立 TextBlock 与普通 Run；进入共享格式器的内容、文档与编辑器继续开放。

后续字体增量见 [混合竖排修复与验证](css-mixed-orientation-validation-20261027.md)：修复默认 CoreText 开关的字体依赖和竖排片段坐标，增加 Unicode 18 方向分类与组合字符例外；最新候选全量 20,237 项、原生 53 项通过，四字体 Metal 实际操作已复验。本页下面的计数、载荷身份与截图仍对应前一轮快照，不能替代后续候选的结果。

## 已接通的行为

- `vertical-rl`、`vertical-lr`、`sideways-rl`、`sideways-lr` 的物理测量、逻辑 inline 约束、逐列排布与字形绘制。`vertical-lr` 按列旋转并向右推进，避免整段反射造成字形镜像。
- CoreText 的 mixed 竖排字形，以及 `text-orientation:sideways` 的侧排字形。汉字、竖排括号、拉丁文字和 emoji 已有实际像素；这不是完整 UAX50 或所有字体的覆盖声明。
- 物理 padding、字符源矩形、`GetPositionFromPoint`、原始选区文本、裁剪、普通 Run 前景和助记下划线。
- 字母间距与变长大小写转换共用 retained 段落的源位置投影。录制帧保留此前的书写模式和原生段落，后续切换模式不会改写旧帧。
- `::selection` 的文字颜色、装饰线和跨列源范围。裁剪先在物理坐标应用，再进入当前列的字形坐标；装饰线和阴影边界使用对应列的变换。
- 父级书写模式变化会刷新后代文本的布局、原生段落和绘制缓存，反复恢复横排不会继续复用竖排结果。

新增 C API 为 `jalium_text_paragraph_create_vertical_advanced`，用于竖排的 CSS 间距。它使用独立可选 provider，保留现有 provider vtable 和旧创建入口。

## 自动化结果

证据根目录：`artifacts/css-textblock-vertical-20261027-b1eb1398/`。构建使用冻结源码与独立输出目录，Release、net10.0、osx-arm64。原生库重新构建；两份 Metal 着色器复用已登记的仓库缓存，没有将缓存载荷计作新编译着色器。

CTest 按本轮文字与绘制范围选择 51 项，排除 platform/media 组，未执行项目不计通过。最终托管源码位于 `source-final/`，原生构建输入位于 `source/`；两者对应原生输入一致。验证后仅更新交付文档，代码与最终构建输入一致。

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| 原生 CTest | 51 通过、0 失败 | `native-ctest.xml`、`native-ctest.log` |
| 原生导出与 CPU 架构 | 通过 | `native-export-check.log` |
| TextBlock 新增消费者 | 41 通过、0 失败 | `textblock-final/results/result.trx` |
| 相关回归，含文档跳页修复 | 94 通过、0 失败 | `final-focused/results/result.trx` |
| 换回上一阶段托管载荷 | 6 项消费者全部失败 | `old-managed-control/summary.json`；同一新原生载荷与候选测试程序集 |
| 仅撤回选区修复 | 12 项选区测试全部失败 | `selection-ablation-final/summary.json`；布局稳定后执行 |
| 本增量首轮全量 | 20,217 通过、1 失败、0 跳过 | `full-current/results/result.trx`；失败为原生字体上下文下的文档跳页 |
| 最终源码全量 | 20,220 通过、0 失败、0 跳过、0 超时；620.27 秒 | `full-final/results/result.trx`、`full-final/summary.json` |

41 项消费者测试覆盖四种书写模式、列推进与物理 padding、6 项独立摆放的原生参考像素、组合字符/emoji/希伯来文源范围、Run 配色、选区文字、竖向装饰线、跨列选区、物理视口裁剪、继承失效、助记下划线、变长转换/间距与录制重放。参考摆放没有调用待测 TextBlock 的书写模式转换或源矩形辅助函数。

全量暴露的文档问题已用 Software、Metal 两个固定上下文重现，修复前两项均失败。连续视图中的一行可能包含分页视图下一页的起始源位置；同步 `ScrollChanged` 将显式跳页的目标页码回写为前页。现在显式导航结束时保留目标页码，普通滚动继续按可见源位置更新。该修复没有把 FlowDocument 的竖排标为完成。

## 实际窗口与图像

已查看四种模式的 TextBlock 实际像素和选区汇总图，确认汉字正立/侧排的区别、文字推进方向、跨列选区和竖向装饰线。最终 Metal NSWindow 已实际操作，并确认加载的托管 DLL 与最终全量载荷一致；签名后的原生库与测试载荷的 UUID、代码和数据节一致。

| 实际操作 | 结果 |
| --- | --- |
| 单列鼠标拖选 | `vertical-rl` 返回 `1+3` 和原始文本“地玄黄” |
| 四种模式跨列拖选 | 分别返回 `1+7` 和“地玄黄宇宙洪荒”；`sideways-lr` 向上推进时也正确 |
| Command+A | 原始三行混合文本全选，UTF-16 长度 28；没有把组合字符或 emoji 改写为显示文本 |
| Tab 与空格 | 焦点移至切换按钮，绿色焦点框可见；空格切到横排，再按空格恢复四种竖排 |
| 列长度切换 | 318 与 152 的实际 inline 约束触发重新换列，源选区保持正确 |
| 窗口模式切换 | 实际有边框/无边框窗口中，文字和选区正常；原生标题栏与窗口按钮随模式变化 |
| 原生缩放动作 | 窗口从 1000×678 放大到 2560×1321，再恢复到 1000×678；文本区域宽度恢复到 200，字形没有随视口拉伸 |
| 鼠标拖动原生窗口边框 | 工具未触发尺寸变化，未计为通过；没有将缩放按钮结果替代手拖验收 |

事件及源矩形保存在 `live-output/events.jsonl`，窗口核查结果、截图尺寸与载荷校验保存在 `native-window-validation.json`。

界面检查参考 [Web Interface Guidelines](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md) 的键盘、可见焦点、明确标签和内容边界要求，并以实际窗口与截图核验原生控件的适用项。

当前图像：

- [四种模式](../artifacts/css-textblock-vertical-20261027-b1eb1398/textblock-first/contact.png)
- [文字颜色、装饰线与跨列选区](../artifacts/css-textblock-vertical-20261027-b1eb1398/textblock-columns-final/selection-contact.png)
- [最终 Metal 窗口](../artifacts/css-textblock-vertical-20261027-b1eb1398/live-output/16-final-restored.jpg)
- [可见键盘焦点](../artifacts/css-textblock-vertical-20261027-b1eb1398/live-output/05-keyboard-focus.jpg)
- [实际横排切换](../artifacts/css-textblock-vertical-20261027-b1eb1398/live-output/06-keyboard-horizontal.jpg)
- [实际无边框窗口](../artifacts/css-textblock-vertical-20261027-b1eb1398/live-output/12-borderless-columns.jpg)

VoiceOver 真实朗读、正式 net10.0-macos HostSmoke/AOT 发布和 Gallery/Jalium.One 全流程未在本增量执行。AX 文本不能替代真实朗读，Software 像素也不代表 Windows、Linux 或其他 GPU 的窗口验收。

## 剩余实现与接续顺序

1. **文字方向和纵中横**：让 `upright` 与 `mixed` 的拉丁文字方向确实不同；将 `text-combine-upright` 接入相同的塑形、em 方格、源位置、命中和选区链路。本页历史快照的 upright 仍沿用 mixed；后续 [直立竖排增量](css-upright-validation-20261027.md) 已接入独立的真实路径，但完整字体特性与其他消费者仍未完成。
2. **共享行内格式器与正交嵌套**：共享格式器分配的盒/片段仍按原有横向契约放置，包括进入该路径的简单内容。当前入口在 `HasCssInlineBoxes` 或 `HasCssInlineFragments` 为真时不启用竖排转换，避免重复变换。须统一外层格式器与文本消费者的逻辑轴，而不是删除保护条件后旋转绘制。
3. **文档与编辑器**：接通 FlowDocument、RichTextBox、文档光标、IME、键盘移动、选择、分页和滚动；当前未证明这些宿主竖排正确。
4. **Ruby**：补齐 `display:ruby*` 解析、真实盒树、匿名盒、基础/注音排版与源范围。现有辅助函数和展示页不能替代消费者。
5. **组合及平台验收**：逻辑尺寸与 Flex/Grid、表格、多列、分页、定位的正交组合；Windows/Linux 原生塑形与绘制；真实辅助技术、输入法、DPI 与设备验证。竖排文本的视口裁剪已验证，长文逐列剔除与性能仍需测量。

实现入口：[书写模式坐标](../src/managed/Jalium.UI.Controls/TextBlock.WritingModes.cs)、[retained 段落消费者](../src/managed/Jalium.UI.Controls/TextBlock.CssUnicodeBidi.cs)、[选区几何](../src/managed/Jalium.UI.Controls/TextBlock.Selection.cs)、[选区绘制](../src/managed/Jalium.UI.Controls/CssSelectionPainter.cs)、[原生绑定](../src/managed/Jalium.UI.Interop/NativeTextParagraph.cs)、[41 项测试](../tests/Jalium.UI.MacOS.Tests/CssTextBlockWritingModeTests.cs)、[文档跳页回归](../tests/Jalium.UI.MacOS.Tests/MacOSFlowDocumentPageNavigationTests.cs)。

工作区审计及本次相对脏基线的补丁保存在证据根目录。没有提交、暂存、重置或清理用户已有改动。
