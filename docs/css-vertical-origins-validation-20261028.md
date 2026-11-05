# 竖排字形原点、顶部边距与 BASE 对齐验证

验收日期：2026-10-28。关联 [T4 实现计划](css-implementation-plan.md#t4-垂直书写-逻辑轴与-ruby) 与 [上一轮 gvar／零推进量验证](css-gvar-metrics-validation-20261028.md)。本轮补齐 macOS 独立 TextBlock 的一组真实字体定位行为；完整 CSS、完整 text-orientation 与 T4 继续保持部分状态。

## 修复后的行为

此前 upright 拉丁字形使用字格中点放置，遗漏字体给出的垂直原点；原生 U 类字形也可能沿用 CoreText 合成的原点，忽略实际顶部边距。在本轮的 32px 字号字体中，将顶部边距增加／减少 250 个字体单位，应使墨迹沿列移动 ±8px，字格推进量和原文光标保持不变。现在 Software 与 Metal 都满足此约束。

upright 的 R 类字形保留水平塑形，并使用字体的垂直 BASE 轴对齐。静态宽／窄基线参考产生 −3.2／+3.2px 的横向墨迹位移；变量 BASE 在字重 550／700 时产生 −1.6／−3.2px 位移。字形不再重复叠加水平 BASE 调整。

控件组合测试还发现负顶部边距的墨迹被 TextBlock 的局部裁剪截掉，抗锯齿边缘也可能丢失。对于能放入视口的行，局部裁剪现在包含原生段落返回的源墨迹边界及一个设备像素的覆盖范围；超长不换列文本保留物理视口裁剪，作者指定的祖先裁剪继续生效，源字格、命中和选区几何保持原有语义。墨迹边界按不可变 CSS 行缓存，避免每帧重复查询原生段落。

## 实现与规范依据

[CSS Writing Modes](https://www.w3.org/TR/css-writing-modes-4/#vertical-font-features) 要求 upright 使用可用的垂直字体度量。[OpenType vmtx](https://learn.microsoft.com/en-us/typography/opentype/spec/vmtx) 定义垂直原点为 `yMax + topSideBearing`；长垂直度量和其后的独立顶部边距数组使用不同索引。实现读取这两种形式，保留有符号边距，处理短／长 loca 以及空轮廓。

- gvar 读取器分别返回推进量和顶部幻影点原点，保留既有的轴归一化、区域插值与未变换 USE_MY_METRICS 继承链。截短 TSB 尾部时，推进量调用者仍可读取其所需的完整数据，原点调用者会拒绝不完整边距。
- [VVAR](https://learn.microsoft.com/en-us/typography/opentype/spec/vvar) 的推进量映射和可选 TSB／原点映射分别消费；存在 VVAR 推进量并不自动取消 gvar 原点。TSB 映射使用实际实例轮廓的顶部与独立边距变化。本轮像素 fixture 覆盖独立 TSB 映射；CFF2 的显式 vOrg 映射仍需专项验收。
- TrueType 字体忽略不适用的 VORG 及 CFF2 专用 VVAR vOrg 映射，同时保留有效的 TSB 映射。干扰字体将这些值故意设置为不同原点；修复前在字重 550 的 U 类字形检出 420 个不同像素字节，修复后与字面 TSB 参考一致。
- CFF1 字体优先读取 [VORG](https://learn.microsoft.com/en-us/typography/opentype/spec/vorg) 的默认值及字形记录，缺少 VORG 时使用 CoreText 实际轮廓顶部和 vmtx 边距。本轮自制 CFF1 fixture 验证字形覆写与无 VORG 路径。
- 垂直 [BASE](https://learn.microsoft.com/en-us/typography/opentype/spec/base) 的脚本选择、原生合成与变量基线继续交由 CoreText 解析。upright 单元从同一源字素的原生垂直行提取对齐量，保留水平轮廓。本轮覆盖静态 BASE 和 BASE 1.1／VariationIndex，未据此宣称所有脚本、AAT 或缺表合成行为完成。
- 原生 U 类的原点变化通过绘制位移接入既有字符间距、源墨迹边界和绘制回放；附标随所属源字素保留锚定。源光标与推进量不使用墨迹边界代替。

继续复用 CoreText 的塑形、轮廓插值和栅格化，没有新增产品依赖或变更公开 provider ABI。

入口：[度量读取器](../src/native/jalium.native.metal/src/metal_font_size_adjust_metrics.h)、[gvar／基础原点](../src/native/jalium.native.text/include/opentype_gvar_metrics.h)、[upright 塑形单元](../src/native/jalium.native.text/include/apple_upright_units.h)、[原生垂直绘制位移](../src/native/jalium.native.text/src/apple_vertical_metrics.cpp)、[段落绘制](../src/native/jalium.native.metal/src/metal_text_paragraph.cpp)、[控件裁剪](../src/managed/Jalium.UI.Controls/TextBlock.cs)、[行墨迹缓存](../src/managed/Jalium.UI.Controls/TextBlock.CssUnicodeBidi.cs)。

## 自动化与独立对照

证据根目录：`artifacts/css-vertical-origins-20261028-efa0134d/`。冻结 4,507 个原始输入，使用独立原生／托管构建目录，.NET SDK 10.0.300。完整托管回归指 macOS 上的 `Jalium.UI.MacOS.Tests.csproj` 聚合测试，其他平台的实机验收继续开放。

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 完整托管回归 | 20,589 通过，0 失败／跳过／超时；813.36 秒 | `managed-final-full/summary.json` |
| 相关托管回归 | 1,385 通过，0 失败／跳过／超时，含本轮 139 项 | `managed-final-focused/summary.json` |
| 原生选择集 | 63 通过，0 失败／跳过；未包含 platform／media 套件 | `native-post-reboot-full/summary.json` |
| 原点／BASE 严格像素对照 | Software／Metal 各 1,248 对，逐字节一致 | `native-final/screenshots/origins/` |
| 既有垂直度量／字体特性／upright 像素回归 | 每个后端分别 1,226／186／160 对一致 | `final-native-pixel-identity.json` |
| 全部最终 PNG 解码核对 | 5,640 对、11,280 张；内容一致且有非空墨迹 | `final-native-pixel-identity.json` |
| 便携读取器与损坏数据 | 43,351 项通过；ASan／UBSan 同样通过 | `portable-origins/summary.json` |
| 修改前反向对照 | R7 原生载荷在首个 upright A 参考上检出 223 个不同字节 | `negative-native-before/test.log` |
| TrueType 干扰映射反向对照 | 修复前检出 420 个不同字节；最终原生对照通过 | `negative-truetype-vorg/summary.json` |
| 控件裁剪回归 | 修复前 4 项失败，修复后 4 项精确像素对照通过 | `managed-origins-first/summary.json`、`managed-origin-clip-diagnostic/summary.json` |
| 超长列视口回归 | 180 通过，含本轮 139 项与既有书写模式的 41 项 | `managed-viewport-final/summary.json` |
| 字体重现性 | 14 份 MIT 几何字体；两次离线生成与测试输入一致 | `fixture-reproducibility.json` |

[原生像素套件](../src/native/jalium.native.metal/tests/metal_vertical_origins.mm) 在真实 Software／Metal surface 上请求 readback，覆盖 1×／2× DPI、单字符／双字符、完整／拆分 span、upright R 类 A 与 U 类“水”、变量轴、font-size-adjust 及缺少垂直表的合成分支。参考图从原始字体独立创建 CoreText 字体，使用 fixture 中字面给出的原点、推进量与 BASE 位移。

参考图采用调用者指定的固定中央基线坐标；仅实际绘制侧使用产品报告的基线将该坐标转换为绘制原点，参考图不复用产品的基线数值。vmtx 不定义水平字形原点，参考图沿横轴使用独立 CoreText 字体的水平推进量。字体缩放参考使用相同的精确 double 字号。

[托管消费者套件](../tests/Jalium.UI.MacOS.Tests/CssVerticalOriginTests.cs) 验证 vertical-rl／vertical-lr、mixed／upright、原文字格／命中／选区、正负边距、BASE、轴恢复、私有后备链、size-adjust、软换列与录制回放。原点边距的控件对照逐字节相同；BASE 控件对照比较 alpha≥128 的轮廓，以容纳公共定位 ABI 的 float 量化，精确 BASE 像素由原生套件另行验证。

既有原生参考同时修正了旧的居中模型，并保留严格像素检查。静态默认原点使用独立的测试专用表读取器，gvar 的参考值使用 fixture 中的字面程序，不调用产品的原点或 gvar 读取器。

14 份字体的中文／拉丁码位有意使用矩形几何轮廓，便于精确比较；出处与 SHA 见 [生成脚本](../tests/Jalium.UI.MacOS.Tests/Assets/FontVerticalOrigins/build-fixtures.py)、[许可证](../tests/Jalium.UI.MacOS.Tests/Assets/FontVerticalOrigins/LICENSE.md)、[清单](../tests/Jalium.UI.MacOS.Tests/Assets/FontVerticalOrigins/PROVENANCE.json)。两份 metallib 复用上一轮相同产物并登记 SHA，见 `shader-provenance.json`。

一轮全量托管回归发现两项超长列物理视口回归，已收窄墨迹扩展条件并补充四项正负边距超长列用例。主机重启前的一次原生选择集另有两项既有 Metal 套件达到 30 秒超时；重启后单独复跑两项及完整 63 项选择集均通过，原始超时记录保留在 `native-final/`，复跑记录分别见 `native-reboot-retry/`、`native-post-reboot-full/`。最终计数采用完成的复跑结果。

## 实际 Metal 窗口

最终 self-contained 应用位于内置盘缓存，运行库、原生库、测试字体和日志均位于内部目录。8 份框架程序集与测试输出 SHA 一致，签名后的 8 份 dylib 与原生载荷的 UUID 及文件支持 section 一致；实际加载的 core／platform／text／Metal 与 CLR 路径见 `live-loaded-paths.json`，打包证明见 `live-packaging.json`、`live-native-identity.json`。

已实际操作并查看 11 张最终截图：

- 顶部边距 +250／−250 反复切换，墨迹按预期移动，源字格保留。
- 可变字重 400→550→700→100→400；等高样本保持 38.4px 字格，字形大小与原点使用当前实例。
- 长列两列／短列三列的实际重排，upright／mixed、向左／向右换列及计算 RTL／LTR。
- 鼠标拖选三格得到原文 `0+3`、“水水水”；先点击清除基线样本选区，再用 Command+A 选中完整 `AA水水`。
- Tab 到达模式按钮且焦点框可见，空格实际切换模式。
- 原生窗口 1000×650→2560×1292→1000×650；恢复后的源字格、选区和样式状态与放大前一致。

77 条窗口事件中的 76 份有效布局快照逐一核对通过；关闭事件只记录生命周期。进程已正常退出，无 error／fatal 日志。见 `live-ui-summary.json`。窗口使用五份 TrueType 字体；CFF1 和干扰映射由原生 surface 及控件自动化用例验证。

按 [UI 界面指南](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md) 中适用于原生界面的语义、命名、键盘、焦点、布局与内容原则复核；HTML／ARIA 专属条目不适用。指定文字、按钮、选区的前景／背景对比度均超过 4.5:1。验收窗口为原生标题栏 SingleBorderWindow；本轮没有执行无边框窗口、物理输入或 spoken VoiceOver 验收，也没有将 Home／方向键延伸选区计为通过。

截图：[负边距](../artifacts/css-vertical-origins-20261028-efa0134d/live-screenshots/02-negative-bearing.jpg)、[原文拖选](../artifacts/css-vertical-origins-20261028-efa0134d/live-screenshots/05-source-selection.jpg)、[键盘焦点](../artifacts/css-vertical-origins-20261028-efa0134d/live-screenshots/06-keyboard-focus.jpg)、[软换列](../artifacts/css-vertical-origins-20261028-efa0134d/live-screenshots/07-mixed-compact-ltr.jpg)、[窗口恢复](../artifacts/css-vertical-origins-20261028-efa0134d/live-screenshots/10-window-restored.jpg)、[Metal 独立对照](../artifacts/css-vertical-origins-20261028-efa0134d/visual-review/metal-origin-pairs.png)、[Software 独立对照](../artifacts/css-vertical-origins-20261028-efa0134d/visual-review/software-origin-pairs.png)。

## 工作区与剩余实现

最终核对未发现登记范围以外的工作区源码漂移。HEAD、索引和暂存区均保持不变。最终源审计、逐文件 SHA 和可重放增量见 `source-audit.json`、`final-owned-manifest.json`、`implementation-delta.patch` 与 `patch-verification.json`。补丁基线为本轮开始的输入快照，包含上一轮已有的未提交实现。

本轮覆盖已列出的 TrueType、CFF1 与 BASE 组合，以下工作仍需实现或单独验收：

1. 任意字体的完整基线合成、复杂脚本／标记组合、AAT、CFF2 显式 vOrg 变化，以及带缩放／旋转／倾斜组件的 USE_MY_METRICS 和 hinted 度量。
2. mixed R 侧排、sideways／水平路径的保留字体标签覆盖，Run 独立书写方向或 text-orientation 覆写，更多正交与双向嵌套。
3. 共享 CSS 行内格式器、FlowDocument、RichTextBox 与编辑器的竖排消费者，贯通同一套源范围、测量、换列、命中与录制协议。
4. text-combine-upright、真实 Ruby display／匿名盒／布局，以及表格、Flex／Grid、多列、分页和定位的竖排组合。
5. Windows／Linux 原生实机、Release／AOT、物理输入、辅助技术与更广 WPT 参考验收。

当前进度不能用于将完整 CSS、完整 text-orientation 或整个 T4 标为完成。

最终回填：2026-10-28 03:21:24 CST。
