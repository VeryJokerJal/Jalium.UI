# gvar 幻影点、零推进量与字体缩放验证

验收启动：2026-10-28。关联 [T4 实现计划](css-implementation-plan.md#t4-垂直书写-逻辑轴与-ruby) 与 [上一轮 vmtx/VVAR 验证](css-vertical-metrics-validation-20261027.md)。本轮继续接入 macOS 独立 TextBlock 的真实原生竖排消费者；完整 CSS、完整 text-orientation 和 T4 仍为部分完成。

2026-10-28 后续增量：已进一步实现字体原点、顶部边距、CFF1/VORG 与一组垂直 BASE 对齐，并修复控件墨迹裁剪；当前验证范围及剩余事项见 [原点与基线验收](css-vertical-origins-validation-20261028.md)。本页计数及待办保留为上一轮历史快照。

## 行为变化

没有 VVAR 的 TrueType 可变字体，可以通过 gvar 的垂直幻影点改变字格。此前适配层只读基础 vmtx，将 CoreText 已反映的部分变量推进量抵消。现在两个竖排“水”字在 32px、字重 100 时总推进量为 64px；上一轮载荷仍得到 76.800003px。字重 400／550／700 时，单字格分别为 38.4／41.6／44.8px。

合法的 0 推进量保持为 0，字形墨迹继续绘制、选区仍映射原文；仅缺少垂直度量时合成一 em。此行为同时覆盖原生 U 类竖排字形与 upright 中隔离塑形的 R 类字形，不把零宽的普通字形误当作附标，也不丢弃其墨迹。

还修复了 font-size-adjust 的组合问题：私有可变字体在缩放后可能保留了看似正确的度量，却将字形换成系统字体；从 CoreText 竖排 run 复制字体也可能把临时旋转矩阵带进 upright 的水平塑形。现在从作者指定的私有字体身份进行缩放，保留轴、特性、字体矩阵及回退链。`font-size-adjust:ic-height 1.2` 的单字格为 38.4px，元素计算字号仍为 32px。

## 实现与标准依据

采用现有 SFNT 安全读取器，新增只读取垂直幻影点差值的内部 gvar 读取器；不替代 CoreText 的轮廓插值、hinting 或栅格化，不增加产品第三方依赖，不修改公共 provider ABI。规则参照 [OpenType gvar](https://learn.microsoft.com/en-us/typography/opentype/spec/gvar)、[共用变量数据格式](https://learn.microsoft.com/en-us/typography/opentype/spec/otvarcommonformats)、[glyf](https://learn.microsoft.com/en-us/typography/opentype/spec/glyf) 和 [VVAR](https://learn.microsoft.com/en-us/typography/opentype/spec/vvar)。

- 短／长 gvar 与 loca 偏移，嵌入／共享峰值、共享／私有点号，隐式／显式全点、字节／字长 delta 和零运行。
- 使用现有 fvar、avar／avar2 归一化坐标，支持正负峰值、多轴标量、显式中间区域及隐式峰值之外的饱和。
- 只使用被指定的两个垂直幻影点；未被引用的幻影点 delta 为 0，不对其做轮廓 IUP 推断。
- 复合字形按组件数量编号；未变换组件的 USE_MY_METRICS 由最后设置该标志的组件提供度量，父级幻影点不重复累加。迭代处理继承链，拒绝循环。
- 任意负轮廓数按复合字形读取；稀疏相对点号可跨过 65,535，覆盖最多 65,536 个轮廓点加幻影点的边界。
- VVAR 优先，避免与 gvar 重复计入。缺失或损坏的数据保留可用的基础度量；结果为合法 0 时不会触发 em 合成。

入口：[可移植 gvar 读取器](../src/native/jalium.native.text/include/opentype_gvar_metrics.h)、[原生字体度量](../src/native/jalium.native.metal/src/metal_font_size_adjust_metrics.h)、[upright 单元](../src/native/jalium.native.text/include/apple_upright_units.h)、[竖排度量适配](../src/native/jalium.native.text/src/apple_vertical_metrics.cpp)、[私有字体缩放](../src/native/jalium.native.metal/src/metal_font_size_adjust.h)、[字体及回退元数据](../src/native/jalium.native.metal/src/metal_backend.cpp)。

## 自动化与负对照

证据根目录为 `artifacts/css-gvar-metrics-20261028-b089e3ab/`。冻结 4,483 个原始输入，使用独立原生／托管输出与 .NET SDK 10.0.300。完整托管回归指 `Jalium.UI.MacOS.Tests.csproj` 在 macOS 上的聚合测试，不代表 Windows／Linux 实机或完整 WPT。最终工作区范围由 `source-audit.json` 和可重放的 `implementation-delta.patch` 核对。补丁应用基线为本轮开始时含既有未提交工作的工作区，原始文件 SHA 及增量前文件保存在 `baseline-manifest.json`、`before-owned/`。

| 检查 | 最终结果 | 证据 |
| --- | --- | --- |
| 完整托管回归 | 20,450 通过，0 失败／跳过／超时；1108.97 秒 | `managed-final-full/summary.json` |
| 相关托管复测 | 865 通过，0 失败／跳过／超时 | `managed-final-focused/summary.json`；包含新类的 150 项与既有 bidi、行内、字体、upright 用例 |
| 原生选择集 | 61 通过，0 失败／跳过 | `native-final/summary.json`；不包含 platform／media 套件 |
| 可移植读取器 | 20,863 项检查通过；ASan／UBSan 均通过 | `portable-final/`；包含 20,000 次确定性数据变异、逐段截断、扩展点号及 1,024 层继承链 |
| 原生度量像素 | Software／Metal 各 1,226 对、40,565 项检查 | `native-final/test.log` |
| PNG 全量解码核对 | 2,452 对完全一致，全部非空 | `final-native-pixel-identity.json`；4,904 张实际／参考 PNG |
| 旧载荷负对照 | 按预期失败，76.800003px 与要求的 64px 不符 | `old-native-negative/summary.json`；同一新测试程序换用上一轮 dylib |
| 导出／CPU／完整构建 | 通过 | `native-export-check.log` |
| 字体 fixture 复现 | 离线连续生成两次，与测试时字节一致 | `fixture-determinism.json` |
| 实际 Metal 窗口 | 81 条事件、80 份有效布局快照、11 张截图 | `live-ui-summary.json`、`live-output/events.jsonl`、`live-screenshots/` |

独立参考直接从原始字体创建 CoreText 字体，以明确写出的字格网格绘制；不调用产品的 gvar 或垂直度量读取函数。覆盖 17 类字体、七档字重、多轴／avar／avar2、复合字形、共享／私有编码、VVAR 共存、零推进量／缺失度量、mixed／upright、完整／拆分 span、显式特性、私有回退，以及 font-size-adjust 的 U 字形和 upright R 字形。另保留上轮的上下文、连字、分解、附标、字符间距及断列重塑形验证。

150 项新控件测试覆盖两种竖排模式、两种方向、Software／Metal 原生几何、轴反复切换、原文选区、短列、正负间距和不可变录制回放。Metal 像素来自原生真实 surface 测试和实际窗口；托管离屏像素使用 Software。显式 FontSize 经过 float ABI、size-adjust 保留 double，因此托管缩放对照比对字形轮廓遮罩；原生缩放测试在同一精确字号下逐字节比较像素。

17 份字体为仓库 MIT 几何轮廓生成的测试字体，中文／拉丁码位有意绘制为矩形或矩形组件，并非生产字体出现方框。出处、输入及每份字体 SHA 见 [生成脚本](../tests/Jalium.UI.MacOS.Tests/Assets/FontGvarMetrics/build-fixtures.py)、[许可证](../tests/Jalium.UI.MacOS.Tests/Assets/FontGvarMetrics/LICENSE.md) 与 [清单](../tests/Jalium.UI.MacOS.Tests/Assets/FontGvarMetrics/PROVENANCE.json)。两份 metallib 复用上一轮相同缓存并登记 SHA，见 `shader-provenance.json`，未声称重新编译 shader。

## 实际窗口与视觉检查

最终 self-contained 应用位于内置盘缓存，.NET 10.0.8、原生库、字体和日志均随应用位于内部目录。8 份框架托管程序集与测试输出 SHA 一致；签名后的 8 份 dylib 与原生载荷的 UUID 及所有文件支持 section 一致。实际加载的 core／platform／text／Metal 与 CLR 路径见 `live-loaded-paths.json`，打包证明见 `live-packaging.json`、`live-native-identity.json`。

已实际操作并查看截图：

- 字重 400→550→700→100→400；基础字格随轴变化，设置 size-adjust 的样本保持字格高度。
- 长／短列切换，字重变化导致的断列重排，真实拖选三格“水水水”对应原文 `0+3`。
- upright／mixed、向左／向右换列、计算 RTL／LTR；upright 的 used direction 保持 LTR。
- 零推进量文本点击后，Command+A 选中完整 `AA水水`；重叠墨迹继续显示。
- Tab 到达操作按钮，焦点框可见，空格键实际切换列方向。
- 原生窗口从 1000×650 放大至 2560×1293，再还原。渲染器有效时采集的还原前后字格、原文与选区几何完全一致。

窗口已正常退出，无 error／fatal 日志。`Closed` 仅记录生命周期，不在渲染器关闭后重新测量文本。TextBlock 当前没有 Home／方向键延伸选区的实现；实测这些输入未处理，未记为键盘选区验收通过。

依据 [UI 界面指南](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md) 中适用于原生界面的语义、命名、焦点和键盘原则复核了窗口；HTML／ARIA 专属条目不适用。正文／标签／按钮／选区的指定前景与背景对比度为 5.265～12.659，见 `ui-color-contrast.json`；这不代替完整辅助技术验收。

截图：[原文拖选](../artifacts/css-gvar-metrics-20261028-b089e3ab/live-screenshots/07-source-selection.jpg)、[键盘焦点](../artifacts/css-gvar-metrics-20261028-b089e3ab/live-screenshots/09-keyboard-focus.jpg)、[窗口还原](../artifacts/css-gvar-metrics-20261028-b089e3ab/live-screenshots/11-window-restored.jpg)、[Metal 对照图](../artifacts/css-gvar-metrics-20261028-b089e3ab/visual-review/metal-gvar-pairs.png)、[Software 对照图](../artifacts/css-gvar-metrics-20261028-b089e3ab/visual-review/software-gvar-pairs.png)。

## 工作区与后续范围

本轮开始时冻结的候选已包含当时的 DataGrid 变更；这些代码未由本轮修改。最终核对未发现登记范围以外的工作区源码漂移。未操作用户的 HEAD、索引或暂存区；本轮只包含登记的代码、测试／字体资产和文档增量。

本轮不关闭以下工作：

1. BASE、合成基线、垂直 origin／bearing。像素参考仍复用产品给出的基线，不能独立证明其完整正确性。
2. 带缩放／旋转／倾斜组件的 USE_MY_METRICS、hinted 度量，以及更多 AAT／CFF2 和其他复杂字体组合；本轮复合 fixture 的组件未施加这类变换。
3. mixed R 侧排、sideways／水平路径的显式保留标签及不同 Run 独立方向的完整交叉覆盖。
4. 共享 CSS 行内格式器、FlowDocument、RichTextBox／编辑器、纵中横、Ruby 和其他容器的完整逻辑轴贯通。
5. Windows／Linux、发布／AOT、大规模 WPT、物理输入法／键盘、真实 VoiceOver 播报及手拖窗口边框的实机验收。

本轮结论为“macOS gvar 垂直推进量、合法零字格及私有可变字体缩放组合的已验证增量”；完整 CSS 目标继续推进。

最终回填：2026-10-28 01:47:38 CST。
