# 混合竖排的字体方向修复与验证

日期：2026-10-27。关联 [T4 实现计划](css-implementation-plan.md#t4-垂直书写-逻辑轴与-ruby)。本轮修复 macOS 原生混合竖排的字体依赖和片段坐标，并验证独立 TextBlock 消费者。完整 CSS、整个 T4 和完整 `text-orientation` 仍未完成。

后续增量见 [直立竖排实现与验证](css-upright-validation-20261027.md)：新增统一 upright 的逐组合单元直立、真实垂直字宽与 LTR used direction。下文计数和载荷身份仍属于 mixed 修复快照，不代表后续候选。

再后续的 [竖排字体特性验证](css-vertical-font-features-validation-20261027.md) 补充 OpenType 竖排单元的 vert/vrt2 独立控制、变量字体及后备字体覆盖；mixed 的 R 侧排显式标签和完整基线/度量仍有开放边界。

## 改变了什么

此前把 `kCTVerticalFormsAttributeName=true` 设置到整段文字。这个开关会采用字体自己的方向决策：Hiragino Sans 中的拉丁文通常侧转，Arial、Times New Roman、Georgia 中则可能逐字直立，宽度也随之增加。同一段 `office AV é` 在旧载荷的 Arial TextBlock 中，纵向期望尺寸约 152.03，实际约 290.06。字体不同不应改变 CSS `mixed` 的方向规则。

本轮增加了可复现的 Unicode 18.0.0 方向数据，在字体、大小写、emoji 和字号调整完成后，按完整组合字符设置方向。首个码点决定方向；包含 Me 类包围组合符的整个组合单元直立，因此数字键帽不会被拆开旋转。连续相同方向合并处理，不为每个字母创建新的塑形边界。中日韩及其他 U/Tu/Tr 单元使用原生竖排字形，R 单元使用水平塑形后沿列侧转。

混排通过 CoreText 的中央基线类和零参考基线对齐。原生光标和片段共用逻辑坐标投影：只有真正采用竖排形式的 run 才减去竖排字形原点；水平形式的 R run 保留其逻辑位置。片段不再把竖排字形空间的 x 值误当成 inline 坐标。

方向数据由 [固定来源清单](../src/native/thirdparty/unicode-vertical-18/UPSTREAM.json) 及 [生成工具](../tools/generate-css-vertical-orientation.py) 产生，原始数据 SHA-256 为 `0803e09669d7aa7137de678e55b848bd418c6632694fa1b05f0b40941103c748`。共生成 192 段非 R 范围和 5 段 Me 范围，托管与原生使用同一来源。升级的是方向表；未据此宣称其他 Unicode 算法都升级到 18。Apple 组合边界仍来自 CFString，仓库共享 grapheme 表仍为 Unicode 17。

规范依据：[CSS Writing Modes 的混合方向](https://www.w3.org/TR/css-writing-modes-4/#mixed-orientations)、[中央基线](https://www.w3.org/TR/css-writing-modes-4/#text-baselines)、[UAX #50 的组合字符例外](https://www.unicode.org/reports/tr50/tr50-35.html#grapheme_clusters)。本轮的 CoreText 对照和字体样本不能替代所有字体及浏览器的规范一致性验收。

主要入口：[原生方向与坐标辅助](../src/native/jalium.native.text/include/apple_vertical_orientation.h)、[真实原生段落](../src/native/jalium.native.metal/src/metal_text_paragraph.cpp)、[托管方向辅助](../src/managed/Jalium.UI.Core/Css/CssWritingModeProperties.cs)、[原生参考测试](../src/native/jalium.native.metal/tests/metal_vertical_orientation.mm)、[真实 TextBlock 测试](../tests/Jalium.UI.MacOS.Tests/CssVerticalOrientationTests.cs)。旧 C 入口、数据结构及 provider vtable 没有改动。

## 最终验证结果

证据根目录：[本轮冻结源码与结果](../artifacts/css-orientation-20261027-98b70706/)。测试使用 .NET SDK 10.0.300、运行时 10.0.8、Release、`osx-arm64`，启用 `JaliumMacNativeGeometryTests=true`。

| 检查 | 最终结果 | 证据 |
| --- | --- | --- |
| 聚合全量托管回归 | **20,237 通过，0 失败、0 跳过、0 超时；840.453 秒** | [汇总](../artifacts/css-orientation-20261027-98b70706/full-final/summary.json)、[TRX](../artifacts/css-orientation-20261027-98b70706/full-final/results/result.trx) |
| 定向消费者、原生段落及页面导航回归 | **72/72 通过**，其中本轮新增 17 项 | [汇总](../artifacts/css-orientation-20261027-98b70706/orientation-focused-final/summary.json) |
| Unicode 方向数据 | 遍历全部 1,114,112 个码点位置，与随测试复制的固定原始数据一致；组合字符例外通过 | [测试源](../tests/Jalium.UI.MacOS.Tests/CssVerticalOrientationTests.cs) |
| 原生回归 | **53/53 通过**，包含本轮新增的 Software/Metal 方向测试；未包含 platform/media 套件 | [CTest](../artifacts/css-orientation-20261027-98b70706/native-ctest.xml)、[日志](../artifacts/css-orientation-20261027-98b70706/native-ctest.log) |
| 四字体独立 CoreText 对照 | Software 和 Metal **各 40 组**；每组同时比对共享文字栅格器与实际后端帧；每个后端 1,066 个检查通过 | [汇总](../artifacts/css-orientation-20261027-98b70706/native-reference-summary.json)、[Software](../artifacts/css-orientation-20261027-98b70706/native-reference-software.log)、[Metal](../artifacts/css-orientation-20261027-98b70706/native-reference-metal.log) |
| 旧原生载荷反向对照 | 同一新原生测试在首个样本检出 **705 字节像素差异**；同一托管测试的 4 个字体案例中 **3 失败、1 通过** | [原生](../artifacts/css-orientation-20261027-98b70706/old-native-control.log)、[真实消费者](../artifacts/css-orientation-20261027-98b70706/old-native-control-managed/summary.json) |
| 生成重现性、导出与 CPU 架构 | 重新生成字节一致；原生导出及 arm64 检查通过 | [生成验证](../artifacts/css-orientation-20261027-98b70706/unicode-generator-validation.json)、[导出](../artifacts/css-orientation-20261027-98b70706/native-exports.log) |

这些集合重叠，不相加计算覆盖率。全量比上一轮的 20,220 增加 17 个案例，不代表新增 17 项 CSS 特性。初次完整 Release 构建记录 85 条警告、0 错误，不能据此宣称 Native AOT 发布通过。

原生参考的方向范围是人工列出的已知样本，不调用产品分类器或生成表。覆盖四种字体、五组文本以及完整/逐 UTF-16 样式拆分；包括拉丁连字、组合音标、双向控制符、希伯来文/阿拉伯文、汉字、标点、数字键帽和 ZWJ emoji。Software 读回是直通 alpha BGRA，CoreText/Metal 是预乘 alpha BGRA；按各自格式换算后逐字节比较，没有放宽字形误差阈值。Metal 像素通过真实 NSView/CAMetalLayer 表面读取，未把不支持的 Metal 离屏创建入口当成验证。

已查看 [完整样式对照图](../artifacts/css-orientation-20261027-98b70706/native-reference/contact-split-0.png)、[拆分样式对照图](../artifacts/css-orientation-20261027-98b70706/native-reference/contact-split-1.png) 和 [实际 TextBlock 软件渲染](../artifacts/css-orientation-20261027-98b70706/managed-consumer-contact.png)。托管像素用例检查的是 Software；Metal 的实际帧另由原生表面及下面的窗口验收覆盖。

两份 metallib 复用上一轮已验证载荷，来源哈希及对应 shader 源码一致；没有计作本轮新编译的着色器，见 [着色器来源](../artifacts/css-orientation-20261027-98b70706/shader-reuse.json)。

## 实际 Metal 窗口

最终运行应用改为本机缓存中的自包含 .NET 10.0.8 应用，不再依赖外置盘运行库，也没有修改系统权限配置。四种字体显示同一段 33 个 UTF-16 单位的源文本。

- 四种字体均通过实际鼠标拖选，准确返回 `5+7` 的 `é👩‍💻`。
- 原生 Command+A 返回完整 `0+33` 源文本；AX 名称保留完整文字。
- Tab 到按钮、可见绿色焦点、空格切换横排并恢复竖排通过。
- 长列 318 与短列 176、向左/向右换列均反复切换并恢复；短列下 Arial 跨列拖选返回 `21+8` 的 `天地玄黄宇宙洪荒`。
- 首版验收夹具短列 152 时，Hiragino 的第六列按默认可见溢出超出固定样本栏；调整夹具短列高度到 176 后重新启动并复验，最终截图内四个样本栏均容纳内容。运行库未因此修改；初版截图单独保留，不作为最终夹具版式通过的证据。

已实际查看的最终截图：[默认四字体](../artifacts/css-orientation-20261027-98b70706/live-output/10-final-baseline.jpg)、[组合选区](../artifacts/css-orientation-20261027-98b70706/live-output/11-four-font-clusters.jpg)、[焦点与全选](../artifacts/css-orientation-20261027-98b70706/live-output/12-final-focus-and-all.jpg)、[横排](../artifacts/css-orientation-20261027-98b70706/live-output/13-final-horizontal.jpg)、[短列](../artifacts/css-orientation-20261027-98b70706/live-output/14-final-short-columns.jpg)、[向右换列](../artifacts/css-orientation-20261027-98b70706/live-output/15-final-rightward-columns.jpg)、[跨列选区](../artifacts/css-orientation-20261027-98b70706/live-output/16-final-cross-column-selection.jpg)。保存文件实际为 JPEG，尺寸为 1000×678。

窗口映射的 `Jalium.UI.Managed.dll` SHA-256 为 `484a2742ad446c4b23ee6656be10628528e857d4b7c3347d04a86e055adcd036`，与最终测试程序集依赖的文件一致。签名后的 core/platform/text/Metal 库 UUID 及全部代码、数据节与测试载荷一致。窗口正常关闭，未记录运行时错误。详见 [窗口检查与载荷身份](../artifacts/css-orientation-20261027-98b70706/native-window-validation.json) 及 [实际事件](../artifacts/css-orientation-20261027-98b70706/live-output/events.jsonl)。

原生适用的标签、键盘、焦点和内容处理按 [最新 Web Interface Guidelines](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md) 复核；这不等于网页、移动端或完整辅助技术认证。浏览器工具因本地文件协议策略拒绝打开参考 HTML，没有尝试绕过，也没有将浏览器对照计为通过。VoiceOver 实际朗读、IME、正式 macOS HostSmoke/AOT 和跨平台显示验收未执行。

## 继续实现的工作

| 工作 | 接续要求 |
| --- | --- |
| `text-orientation:upright` | 本页历史快照没有独立路径；后续 [upright 增量](css-upright-validation-20261027.md) 已建立 macOS 独立 TextBlock 的真实路径，完整字体特性、Run 覆写与其他消费者仍开放。 |
| `text-combine-upright` | 建立真实字形合成、1em 单元、压缩和二维源位置/选区/命中。`all` 的强制换行和字距处理、`digits 2–4` 的完整数字段及跨行内盒前后观察须按规范落实。旧分段辅助及字符数压缩估计不能代替布局。 |
| `vert`/`vrt2` 与基线精度 | 本轮仍借助 CoreText 提供 U/Tu/Tr 的竖排替代字形；需独立验证并落实 CSS 的字体特性约束，不能把 CoreText 开关视为任意字体的完整规范实现。缺少中央基线时的 CSS 合成规则、合成大小写/上下标及更多字体、语言、可变轴组合继续开放。 |
| 共享行内格式器和宿主 | 接入 CSS 行内盒、装饰、嵌入对象及正交流，再接 FlowDocument、RichTextBox、Ruby、分页、编辑和辅助技术源映射。独立 TextBlock 的通过结果不能外推到这些宿主。 |
| 平台与设备 | Windows/Linux 原生竖排消费者、真实窗口与输入设备、VoiceOver/IME、正式打包和 AOT 验收仍需补齐。 |

## 复验与源码边界

本轮冻结 4,460 个既有输入，仅拥有 12 条代码/测试/数据/工具路径以及 3 条文档路径。最终检查的索引、暂存区和 HEAD 保持不变；没有重置、清理、暂存、提交或推送。原工作区其他变动完整保留。见 [源码审计](../artifacts/css-orientation-20261027-98b70706/source-audit.json) 和 [相对本轮开始状态的补丁](../artifacts/css-orientation-20261027-98b70706/implementation-delta.patch)。

在仓库根目录可重跑冻结快照，使用新结果目录保留原证据：

```sh
python3 artifacts/css-orientation-20261027-98b70706/run-tests.py team-focused 'FullyQualifiedName~CssVerticalOrientationTests|FullyQualifiedName~CssVerticalNativeParagraphTests|FullyQualifiedName~CssTextBlockWritingModeTests'
python3 artifacts/css-orientation-20261027-98b70706/run-tests.py team-full FULL
```

这些命令测试的是本轮冻结构建，不会自动编译团队后续修改。构建参数保存在证据目录的 `native-configure-command.json`、`native-build-command.json` 和 `managed-build-command.json`；继续实现时须重新冻结/构建自己的候选并确认实际加载版本。
