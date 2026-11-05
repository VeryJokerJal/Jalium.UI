# 原生竖排字格度量增量与验证

验收启动：2026-10-27。关联 [T4 实现计划](css-implementation-plan.md#t4-垂直书写-逻辑轴与-ruby)。本轮接入 macOS 独立 TextBlock 的 mixed / upright 原生竖排字形推进量，包含 `vmtx`、`VVAR`、映射与 `avar` / `avar2`。完整 CSS、完整 `text-orientation` 与 T4 仍为部分完成。

后续已补充 gvar 幻影点与零推进量、私有可变字体的 size-adjust 组合，见 [2026-10-28 后续验证](css-gvar-metrics-validation-20261028.md)。以下保留本轮冻结候选的历史结果与范围。

## 行为变化与实现

原生竖排单元按实际塑形后字形的垂直推进量排布，缺失该度量时使用 em 合成值。32px 的自有非等宽字体中，`中文国。` 的字格依次为 38.4、19.2、51.2、25.6px，总计 134.4px；旧载荷错误地得到 128px。变量水字在字重 400 / 550 / 700 下分别为 38.4 / 41.6 / 44.8px。规则依据为 [CSS Writing Modes 的直立排版规则](https://www.w3.org/TR/css-writing-modes-4/#vertical-font-features)、[OpenType vmtx](https://learn.microsoft.com/en-us/typography/opentype/spec/vmtx) 与 [VVAR](https://learn.microsoft.com/en-us/typography/opentype/spec/vvar)。

CoreText 在这些字体上给出的 vertical advance 与字体表不一致，甚至不会随 VVAR 更新。因此适配层先保留原生塑形结果，再按已有字形簇设置内部 tracking 校正。这个内部校正与作者的 CSS `letter-spacing` 独立；保留原文 UTF-16、字体、特性、变体轴、原生字偶定位及附标位置。它不修改公共 provider ABI。

- 先识别实际 GSUB 结果，再读取相应字形的推进量；覆盖上下文替换、连字与单字分解成多个字形。
- 按 GDEF 附标类别及非基字符识别附着字形；CoreText 会分摊基字／附标的 advance，不能只按 run advance 判断是否为附标。
- 同一簇的分解字形保留各自的真实起点，叠加作者的字符间距；正负间距均有像素对照。
- tracking 校正的尾部属于字格，纳入断列与对齐。合法断点由 CoreText 提供；每列重新塑形并计算最终字形的推进量，修复拆开连字后仍使用连字度量的问题。
- 光标边缘、原文选区、文本片段与绘制共享列结果；内部插入位置继续使用 CoreText 的字形簇插入图，不虚构附标或连字内的源位置。

另外修复了私有可变字体更新光学／变体设置时被命名描述符重新匹配成系统字体的问题。只对注册的私有可变字体复制现有字体实例，保留后备链中每个字体的私有身份。早期扩大到系统可变字体的版本导致水平 bidi、两端对齐及行内对象用例失败；已收窄并复测。该次全量运行主动中止，不能当作完整回归结果。

主要入口：

- [度量适配层](../src/native/jalium.native.text/src/apple_vertical_metrics.cpp)
- [列塑形与断列](../src/native/jalium.native.metal/src/metal_font_caps.h)
- [段落几何、组件字形位置和间距组合](../src/native/jalium.native.metal/src/metal_text_paragraph.cpp)
- [私有可变字体与后备链实例](../src/native/jalium.native.metal/src/metal_backend.cpp)
- [原生验收](../src/native/jalium.native.metal/tests/metal_vertical_metrics.mm)、[真实控件验收](../tests/Jalium.UI.MacOS.Tests/CssVerticalMetricsTests.cs)

## 最终候选的验证范围

证据根目录为 `artifacts/css-vertical-metrics-20261027-f6e1a340/`。构建使用冻结的 4,487 个输入、独立原生／托管输出目录与 .NET SDK 10.0.300。

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 完整托管回归 | 20,300 通过，0 失败／跳过／超时 | `full-regression-final/summary.json`；1364.57 秒；新增 31 项 |
| 相关托管复测 | 715 通过，0 失败／跳过／超时 | `managed-final-expanded/summary.json`；包含新增 31 项及水平 bidi、两端对齐、字体、upright 既有用例 |
| 原生选择集 | 60 通过，0 失败／跳过 | `native-final-expanded/summary.json`；不包括 platform／media 套件 |
| 本轮原生度量 | Software / Metal 各 336 组、11,413 项检查 | `native-final-expanded/test.log` |
| 独立参考与后端帧 | 672 对均无像素差异，全部非空 | `final-native-pixel-identity.json`；1,344 张实际／参考 PNG |
| 旧载荷负对照 | 两个后端均以失败退出 | `old-native-negative/summary.json`；134.4px 与旧结果 128px |
| 水平回归定位 | 旧载荷 405 项通过；收窄后 436 项通过 | `old-native-managed-control/`、`managed-private-font-repair/` |
| 导出／CPU／完整构建 | 通过 | `exports-final/summary.json` |
| 字体 fixture 复现 | 离线生成两次，全部字节一致 | `fixture-reproducibility.json` |
| 最终 Metal 窗口 | 94 条事件、93 份有效布局快照、9 张截图 | `live-ui-summary.json`、`live-events-final.jsonl`、`live-ui/` |

参考栅格直接使用原始字体中的字形和独立写明的推进量网格，不调用产品的垂直度量读取函数。覆盖 mixed / upright、完整／拆分 UTF-16 span、原生表驱动的上下文／连字／分解／附标、VVAR 直接与映射索引、avar / avar2、三种光学策略、显式／默认特性、私有可变后备字体、正负字符间距。另测短列、硬换行、正负校正及起始／居中／末端对齐。

本轮复用了原有四份变量度量字体，新增六份带非等宽度量或 GSUB／GPOS 的自有几何字体；均从仓库 MIT 轮廓离线生成，不依赖下载字体或第三方包。出处与 SHA 见 [fixture 清单](../tests/Jalium.UI.MacOS.Tests/Assets/FontVerticalMetrics/PROVENANCE.json)。Metal shader 缓存复用上一轮相同文件，另有 `shader-cache-provenance.json`，未声称重新编译 shader。

## 实际窗口与视觉检查

最终 self-contained 测试应用位于用户内置盘缓存，运行库、原生库、字体和日志均在内部目录。实际进程加载路径记录在 `live-loader-paths.json`。8 份框架托管程序集与测试输出 SHA 相同；8 份签名后 dylib 的 UUID 和所有文件支持 section 与原生测试载荷一致，见 `live-packaging.json` 与 `live-native-identity.json`。

已实际执行并查看结果：非等宽两个字格拖选、基字＋附标完整选取、真实 Hiragino 的重音与 ZWJ emoji 选区、Command+A、Tab 的可见焦点、空格切换 mixed／upright、RTL／LTR 计算方向、向左／向右换列、短列跨列拖选、字重 400→550→700→400，以及原生窗口从 1000×650 放大至 2560×1294 再恢复。最终有效布局与启动布局的字格几何完全一致；所有记录选区都对应原文 UTF-16 与完整组合边界。已关闭测试进程，无 error／fatal 日志。

恢复几何在渲染器仍有效时显式采样；`Closed` 只记录生命周期事件。关闭后重新查询文本会使用回退测量，不能作为窗口恢复的几何证据。

结合 [Web Interface Guidelines](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md) 中适用于原生测试窗口的命名、键盘、焦点与内容边界原则检查了截图。HTML／ARIA 专属规则不适用于此窗口。没有执行真实 VoiceOver 播报、物理键盘／输入法或拖动窗口边框的实机验收。

示例：[最终跨列拖选](../artifacts/css-vertical-metrics-20261027-f6e1a340/live-ui/06-short-column-selection.jpg)、[缩放恢复](../artifacts/css-vertical-metrics-20261027-f6e1a340/live-ui/09-restored-geometry.jpg)、[Metal 对照图](../artifacts/css-vertical-metrics-20261027-f6e1a340/visual-review/metal-metric-pairs.png)、[Software 对照图](../artifacts/css-vertical-metrics-20261027-f6e1a340/visual-review/software-metric-pairs.png)。

## 工作区与剩余边界

本轮验收针对冻结候选。其间工作区的 `DataGrid.cs` 与 `DataGridContainerStyleSizingValidationParityTests.cs` 有并行改动，均保留在原处，未同步到候选、未计入本轮完整回归。对应冻结版本仍与开始时 SHA 一致，见 `external-workspace-drift.json` 与 `source-audit.json`。未操作用户的 HEAD、暂存区或索引。

本轮不关闭以下工作：

1. `BASE`、合成基线和垂直 origin／bearing 的完整精度。像素参考复用产品给出的基线，不能独立证明基线正确。
2. 只有 gvar 幻影点、没有 VVAR 的可变垂直度量，以及更多 AAT／CFF2／复杂字体组合。当前 VVAR 路径通过不代表这些组合已通过。
3. mixed 的 R 侧排、sideways／水平路径中显式保留标签的完整覆盖；不同 Run 独立方向与其他字体特性的交叉组合。
4. 共享 CSS 行内格式器、FlowDocument、RichTextBox／编辑器、纵中横、Ruby 与其他容器的完整逻辑轴贯通。
5. Windows／Linux、发布／AOT、设备输入法、真实辅助技术与大规模 WPT 验收。

因此状态为“macOS 原生竖排字格度量的已验证增量”；完整 CSS 目标继续推进。

最终结果回填：2026-10-28 00:13:42 CST。
