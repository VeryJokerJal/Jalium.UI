# 竖排字体特性增量与验证

日期：2026-10-27。关联 [T4 实现计划](css-implementation-plan.md#t4-垂直书写-逻辑轴与-ruby)。本轮为 macOS 独立 TextBlock 的 upright 单元及 mixed 的原生竖排单元接入 OpenType `vert` / `vrt2` 独立控制。完整 CSS、完整 `text-orientation` 和整个 T4 继续保持未完成。

## 改变的行为

竖排单元默认启用 `vert`、关闭 `vrt2`，作者的显式 `font-feature-settings` 可以覆盖默认值，最后一次同标签设置生效。其他特性和变体轴继续交给 CoreText 塑形，查找表按字体原有的 OpenType 顺序执行。默认设置、作者覆盖与查找表先后顺序是三个不同层次，不能将“后写的特性值”解释为“其字形查找表最后执行”。依据为 [CSS Writing Modes 的字形方向规则](https://www.w3.org/TR/css-writing-modes-4/) 与 [CSS Fonts 4 的特性优先级](https://www.w3.org/TR/2026/WD-css-fonts-4-20261022/#feature-precedence)。

本轮探针确认了平台限制：水平 CTLine 忽略保留的竖排特性标签；竖排 CTLine 又会将 `vert=1,vrt2=0` 折叠成同一个 AAT 开关，导致二者一同关闭。仅设置字体描述符不能实现独立控制。现在复制实际字体的 SFNT 数据，把 GSUB 中这两个特性重命名为不冲突的私有标签，再显式开启对应标签。没有自己重写塑形器，也没有替换轮廓或源文本。

主要实现：

- [GSUB 重映射](../src/native/jalium.native.text/include/opentype_vertical_alias.h)：重排 FeatureList 并修正 Script/LangSys 的普通、必需特性索引，以及 GSUB 1.1 FeatureVariations 的替代特性索引；共享的 LangSys 只处理一次。输入检查失败时保留原表，不产生半次修改。
- [Apple 字体适配器](../src/native/jalium.native.text/src/apple_vertical_features.cpp)：保留所有原表、轮廓和变量表，重算校验和，去掉已失效的 DSIG。缓存上限为 32 个字体、32 MiB 重建数据；单字体处理上限 256 MiB。过大的有效字体不进入缓存。
- [字体格式](../src/native/jalium.native.metal/src/metal_backend.cpp)：保留作者的原始独立覆盖值，并按实际选中的后备字体复制到源属性；CoreText 字体描述符不能可靠保留这对独立值。嵌套后备字体按已有顺序展平并保存其设置。
- [原生段落](../src/native/jalium.native.metal/src/metal_text_paragraph.cpp)：先完成字号、上下标、大小写、emoji 与等价样式段的合并，再解析实际字体并应用竖排特性。避免过早固定回退字体而拆坏 ZWJ emoji 或键帽组合。

还修复了更新/清空字体特性时的变量字体身份丢失：把旧命名实例描述符直接配到 CGFont 上，会在 550/700 字重静默改用 PingFang。现在从实际图形字体建立新的数据描述符，再复制变体轴、渲染属性和新的特性列表。原有字体特性重置测试与本轮字形矩阵均通过。 第一轮完整回归另外检出 Seravek unicase 的 0.32px 差异，因此新数据描述符重建仅用于变量字体，静态字体继续保留原描述符。该现有用例已在扩大后的 279 项集合中复验通过；初次 20,268 通过 / 1 失败的完整结果保留在 `full-regression/`，最终候选结果位于 `full-regression-final/`。

本轮没有新增公共 C API 或 provider 虚函数。既有段落和记录绘制保持不可变。

## 自动化验证

证据根目录：[css-vertical-fonts-20261027-5b921274](../artifacts/css-vertical-fonts-20261027-5b921274/)。测试为 Release、osx-arm64、.NET SDK 10.0.300 / 运行时 10.0.8，启用 `JaliumMacNativeGeometryTests=true`。

| 范围 | 结果 | 实际覆盖 |
| --- | --- | --- |
| GSUB 适配器 | 770 次断言通过 | 普通/必需/共享语言索引、条件替代、多个变体记录、标签冲突、截断与变异输入 |
| 新原生字形矩阵 | Software、Metal 各 186 组、4,439 次断言通过 | 四个字体、八种特性组合、三个变量字重、完整/拆分样式、相邻覆盖、平级/嵌套后备字体、不可变快照 |
| 原生选择集 | 58 通过，0 失败/跳过 | 排除 platform/media 专项；不是所有原生测试 |
| 相关托管回归 | 279 通过，0 失败/跳过/超时 | 含 12 个新 TextBlock 案例，另有 upright、mixed、源范围、特性优先级、字号调整与大小写合成 |
| 完整托管回归 | 20,269 通过，0 失败/跳过/超时 | 最终候选 810.96 秒；比 upright 快照增加 12 个案例 |
| 导出与 CPU 架构 | 通过 | 完整构建凭据、osx-arm64 与既有 macOS 导出契约 |
| 旧原生载荷反向对照 | 两种后端均如期失败 | 首个默认 vert 字形均检出 510 个不同字节，退出码 1 |

各集合存在重叠，不能相加作为 CSS 覆盖率。最终结果分别见 [原生选择集](../artifacts/css-vertical-fonts-20261027-5b921274/native-final-expanded/summary.json)、[新字形矩阵](../artifacts/css-vertical-fonts-20261027-5b921274/native-final-expanded/test.log)、[相关托管结果](../artifacts/css-vertical-fonts-20261027-5b921274/managed-final-expanded/summary.json)、[完整回归](../artifacts/css-vertical-fonts-20261027-5b921274/full-regression-final/summary.json)、[旧载荷对照](../artifacts/css-vertical-fonts-20261027-5b921274/old-native-negative-final/summary.json)。初期失败和修复过程保留在同一根目录的各次独立运行中。

### 字体与独立参考

[四个新字体](../tests/Jalium.UI.MacOS.Tests/Assets/FontVerticalForms/PROVENANCE.json) 由仓库自有 MIT 几何字体离线生成，没有下载字体或添加包依赖。普通、vert、vrt2、salt 使用可区分的几何字形；分别有同时具备 vert/vrt2、仅 vert、仅 vrt2 和变量字体的样本。变量字体还包含 700 字重触发的条件 GSUB 和 VVAR 度量。生成脚本连续执行两次，七个输入/输出文件的 SHA-256 均保持一致，见 [复现记录](../artifacts/css-vertical-fonts-20261027-5b921274/fixture-reproducibility.json)。

参考使用未添加 GSUB 的原始几何字体，通过原始 cmap 的实际字符选择字形。因此参考不调用产品的私有标签、GSUB 适配器或替代逻辑。R upright 格子的参考按真实垂直 advance 排列；400/550 字重的替代字形分别使用 38.4/41.6px，700 字重的条件替代改用另一字形及其 advance。两特性同时启用时，普通 vert 查找在 vrt2 之前，而 700 字重的条件 vert 查找在 vrt2 之后，测试按原表顺序分别断言。

产品原生栅格和公开后端帧均与参考逐字节比较。Metal 使用实际 NSView/CAMetalLayer；Software 的直通 alpha 与参考的预乘 alpha 按其表示换算后比较，未放宽像素阈值。U 单元的对照验证字形及位置，不据此宣称 CoreText 的垂直 advance 已符合独立 vmtx/VVAR 度量。

已查看三张对照表，共抽查 71 组实际/参考图：[静态字体](../artifacts/css-vertical-fonts-20261027-5b921274/visual-review/static-feature-matrix.png)、[变量字体](../artifacts/css-vertical-fonts-20261027-5b921274/visual-review/variable-feature-matrix.png)、[Metal 竖排与独立覆盖](../artifacts/css-vertical-fonts-20261027-5b921274/visual-review/metal-vertical-and-overrides.png)。自动逐字节比较覆盖上述每后端全部 186 组。

## 实际 Metal 窗口

测试应用自带 .NET 10.0.8，运行于内置盘缓存路径 `/Users/jal/.cache/jalium-ui-css-validation/5b921274/JaliumCssVerticalFonts.app`。最终实例没有再次等待移动宗卷授权。实际加载的 CLR、core/platform/text/Metal 均来自本包；八个 Jalium 程序集 SHA-256 与测试输出相同，五个签名后 dylib 的 UUID 和所有含文件数据的 Mach-O 节与测试载荷相同。见 [打包身份](../artifacts/css-vertical-fonts-20261027-5b921274/live-packaging.json)、[原生身份](../artifacts/css-vertical-fonts-20261027-5b921274/live-native-identity.json) 与 [加载路径](../artifacts/css-vertical-fonts-20261027-5b921274/live-loader-paths.json)。

四字体为 Hiragino Sans、Arial、Times New Roman、Georgia。实际源文本长 32 个 UTF-16 单元，包含组合音标、ZWJ emoji、希伯来文、阿拉伯文、汉字及 `（）—。、`。

| 操作 | 实际结果 |
| --- | --- |
| 字体特性开关 | upright/mixed 下切换默认和显式 vert/vrt2 关闭，再恢复；可见标点替代及原始字形差异 |
| 四字体拖选 | 均得到 `1+7` 和原文 `é👩‍💻`；向右换列后也验证了 Arial 同一范围 |
| Command+A | rl/lr 得到完整原文 `0+32` |
| Tab/空格 | 按钮显示可见焦点，空格切到 mixed；再切回 upright |
| 短列与跨列 | 最终测试页采用 208/318px，短列跨列选区为 `13+8`、`天地玄黄宇宙洪荒` |
| 列与 flow 方向 | 切换向左/向右换列以及计算 RTL/LTR；upright used direction 始终 LTR |
| 原生 zoom/恢复 | 内容区 1000×650 → 2560×1293 → 1000×650；恢复后的八组源矩形及 RenderSize 与首次渲染逐项相同 |
| 退出 | 74 条最终事件，12 张最终截图，正常关闭，进程退出，无 error/fatal 日志 |

初始测试页增加标点后，176px 短列产生了额外列并越出卡片。已调整测试页的短列高度并显式裁剪，重建后重新执行上述流程；初始截图和事件单独保留，没有当成最终布局通过。最终证据见 [逐项结果](../artifacts/css-vertical-fonts-20261027-5b921274/live-ui-summary.json)、[原始事件](../artifacts/css-vertical-fonts-20261027-5b921274/live-events-final.jsonl)、[默认标点](../artifacts/css-vertical-fonts-20261027-5b921274/live-ui-final/01-upright-default.jpg)、[显式关闭](../artifacts/css-vertical-fonts-20261027-5b921274/live-ui-final/02-upright-author-off.jpg)、[跨列选区](../artifacts/css-vertical-fonts-20261027-5b921274/live-ui-final/07-cross-column-selection.jpg) 与 [缩放恢复](../artifacts/css-vertical-fonts-20261027-5b921274/live-ui-final/11-native-zoom-restored.jpg)。

依据 [Web Interface Guidelines 当前规则](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md)，复核了本窗口适用的标签、键盘、可见焦点、文本边界和状态反馈。没有将 AX 树检查等同于 VoiceOver 实际朗读验收。

## 团队接续边界

1. **垂直度量与基线。** upright 的 R 格已有真实 vmtx/VVAR 对照；U/Tu/Tr 原生 run 仍可能采用 CoreText 的 advance，尚未统一接入独立 vmtx/VVAR。缺失 BASE 的规范合成、Tu/Tr 的变换与回退、更多字体和上下标/大小写组合仍需完成。
2. **其他方向分支的显式标签。** 本轮覆盖矩阵限定于 upright 和 mixed 的原生竖排单元。mixed 的 R 侧排、sideways 及普通水平 CTLine 的显式保留标签覆盖仍需接入/验收；当前不会在这些分支上统一使用本适配器。仅 AAT morx/mort、没有 GSUB 的字体也不走别名路径，不能据此声称已具备同等独立控制。
3. **字体组合与性能。** 更多实际字体、CFF/CFF2、复杂语言、特性值大于 1、axes 与 unicode-range/face metrics 的组合及大字体缓存压力需独立验收；本轮四个几何字体不是全字体符合性证明。
4. **消费者。** Run 自身的方向覆写、共享 CSS 行内盒、FlowDocument、RichTextBox、正交嵌套、IME、纵中横和 Ruby 仍按 T4 计划推进。普通 Run 的等价外观拆分测试不代表任意独立 Run 样式组合完成。
5. **平台与设备。** Windows/Linux 原生竖排、VoiceOver 朗读、物理 IME/触控板/设备 DPI、无边框宿主、正式 HostSmoke 与 Native AOT 未在本轮验收。没有浏览器参考通过记录。

## 源码与复现

本轮冻结 4,473 个既有输入，隔离原生和托管构建，保留当前工作区既有未提交内容。最终代码与实际测试快照逐文件校验；索引、暂存区和 HEAD 不变。见 [源码审计](../artifacts/css-vertical-fonts-20261027-5b921274/source-audit.json)、[本轮补丁](../artifacts/css-vertical-fonts-20261027-5b921274/implementation-delta.patch) 和 [补丁复现](../artifacts/css-vertical-fonts-20261027-5b921274/patch-verification.json)。这些补丁仅相对本轮开始时的工作区，不能替代此前全部 CSS 改动。

主要测试入口：[原生矩阵](../src/native/jalium.native.metal/tests/metal_vertical_features.mm)、[表重映射测试](../src/native/jalium.native.text/tests/vertical_alias_tests.cpp)、[真实 TextBlock 测试](../tests/Jalium.UI.MacOS.Tests/CssVerticalFontFeatureTests.cs)。命令、环境、失败证据和最终结果均位于证据根目录。两份 Metal 着色器继续复用前一轮匹配源码的缓存，不计作新编译着色器。

后续原生 U/Tu/Tr 字格度量、VVAR／avar 及私有变量字体身份修复的范围和最终证据见 [原生竖排度量增量](css-vertical-metrics-validation-20261027.md)。本记录中的计数仍仅对应字体特性轮次。
