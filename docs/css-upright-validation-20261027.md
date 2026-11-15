# TextBlock 直立竖排增量与验证

日期：2026-10-27。关联 [T4 实现计划](css-implementation-plan.md#t4-垂直书写-逻辑轴与-ruby)。本轮将 macOS 独立 TextBlock 的 `text-orientation:upright` 接入真实原生塑形与绘制。完整 CSS、整个 T4 和任意字体的完整 upright 规范约束仍未完成。

后续增量见 [竖排字体特性验证](css-vertical-font-features-validation-20261027.md)：补充 GSUB vert/vrt2 独立控制、作者覆盖、变量字体实例身份与嵌套后备字体验证。下文计数和开放项描述属于本 upright 快照，最新已覆盖范围和剩余边界以该后续记录为准。

## 本轮实现

- 水平脚本按完整组合字符单独塑形，反向旋转字形，使拉丁文、组合字符、希伯来文和阿拉伯文在物理列中直立；阿拉伯文采用隔离形式，`fi` 不跨格连字。CJK、emoji 与包围组合字符继续使用原生竖排形式，蒙古文和八思巴文保留垂直脚本自身的上下文塑形路径。
- 原始 UTF-16 文本保留在 CoreText 段落中，run delegate 仅改变格子的布局度量。没有替换为 U+FFFC，也没有另建公开源位置投影。组合单元使用字体实际 vmtx/VVAR 的垂直 advance，缺失时合成 1em；源矩形、命中、两个光标 affinity、换行、片段和间距共用完整格子的边界。
- upright 的 used direction 固定为 LTR，并通过 CoreText 强制嵌入级别 0 将所有字符视为 LTR，忽略双向控制符。计算得到的 `FlowDirection` 保留，供后代继承和样式查询。文本 `start` 对齐及 clamp 标记使用 used direction。
- 新增可选 `TextParagraphUprightProvider` 与两个 C API：`jalium_text_paragraph_create_upright`、`jalium_text_paragraph_create_upright_advanced`。旧工厂和旧 provider 的虚函数表保持兼容。原生后端不支持时返回空值，竖排消费者不会退回水平字形并冒充成功。
- 字形和原生段落保持不可变，记录的绘制可在原控件更换方向和释放布局后重放。形状缓存按组合单元的文字及实际属性复用。

规范依据：[CSS Writing Modes 的 upright 定义](https://www.w3.org/TR/css-writing-modes-4/#valdef-text-orientation-upright)、[字形方向和字体特性](https://www.w3.org/TR/css-writing-modes-4/)。

## 自动化结果

| 范围 | 结果 | 边界 |
| --- | --- | --- |
| 相关托管测试 | 113 通过，0 失败/跳过/超时 | 含 20 个新增 upright 案例；集合有重叠，不相加作为覆盖率 |
| 原生选择集 | 55 通过，0 失败/跳过/超时 | 排除平台与媒体专项；不是所有原生测试 |
| 新 upright 原生像素对照 | Software、Metal 各 160 组、3,145 次断言通过 | 四字体、五组文本、完整/逐 UTF-16 等价样式拆分、0/6px 字距、两种调用方向 |
| 完整托管回归 | 20,257 通过，0 失败/跳过/超时 | 677.98 秒；比 mixed 快照增加 20 个案例，不代表增加 20 项 CSS 特性 |
| 原生导出与 CPU 架构 | 通过 | 新增 C API 已包含在导出检查中 |

原生参考独立按 32px 网格逐组合单元放置 CoreText 字形，不调用产品方向分类、delegate、度量合成、间距计算或源映射。产品原生栅格与公开渲染后端帧均逐字节比较；Software 直通 alpha BGRA 与 CoreText/Metal 预乘 alpha BGRA 按各自表示比较。Metal 使用真实 NSView/CAMetalLayer 表面。另外使用独立生成的 Primary/Secondary 几何字体，验证真实垂直 advance 为 38.4/19.2px，不能用水平宽度或固定 1em 冒充。附加换行测试确认 `AéB` 在 48/64/65px 的普通/字符换行中不拆组合字符，行源位置覆盖原文。

已查看 [普通字距对照](../artifacts/css-upright-20261027-8be1cc70/upright-reference-contact-0-0.png) 和 [拆分样式与 6px 字距对照](../artifacts/css-upright-20261027-8be1cc70/upright-reference-contact-1-1.png)。图片相等证明本轮与明确列出的 CoreText 参考一致，不代表平台字体塑形本身没有局限。拆分案例使用 context-paint 路径及相同外观属性；任意跨 Run 改字体、改方向的组合并未据此验收。实际控件像素另见 [Software TextBlock 选区与记录绘制](../artifacts/css-upright-20261027-8be1cc70/upright-managed-contact.png)。

首轮新增测试检出 delegate 的字形 advance 与完整组合格宽度不一致：`é` 的末端光标/选区只计算半格。现已在光标修复和片段生成时采用完整格子边界。字距测试另纠正了测量对象：选区边界平分字符间隙，首格只有半个间隙，内部格子包含完整间隙；独立像素对照同时验证实际字形间距，未放宽像素误差阈值。

## 真实窗口

最终载荷已在一个原生标题栏、1000×650 内容区的 Metal 窗口完成实际操作。四字体为 Hiragino Sans、Arial、Times New Roman、Georgia。

| 操作 | 实际结果 |
| --- | --- |
| 四字体鼠标拖选 | 均返回原文 `1+7`、`é👩‍💻`，组合字符和 ZWJ emoji 未拆开 |
| Command+A | `vertical-rl` 和 `vertical-lr` 均返回完整原文 `0+26` |
| Tab 和空格 | 焦点到“直立 / 混排”按钮，绿色焦点框可见；空格切到 mixed，字母侧转，再按空格恢复直立 |
| 短列/长列 | 文本视口在 176/318px 间切换并恢复，正常换列 |
| 跨列拖选 | Arial 短列返回 `13+8`、`天地玄黄宇宙洪荒` |
| 列方向 | 从向左换列切到向右换列，字形保持直立，命中与全选正确，再恢复 |
| RTL/LTR | 计算值切到 LTR 再恢复 RTL；upright 的 used direction 始终 LTR，记录的六组文本矩形保持不变 |
| 退出 | 关闭按钮正常退出，36 条事件，无 error/fatal 日志，进程已退出 |

已查看 [四字体实际拖选](../artifacts/css-upright-20261027-8be1cc70/upright-live-font-selections.png)、[键盘焦点](../artifacts/css-upright-20261027-8be1cc70/live-output/07-keyboard-focus.jpg)、[mixed 切换](../artifacts/css-upright-20261027-8be1cc70/live-output/08-keyboard-mixed.jpg)、[跨列拖选](../artifacts/css-upright-20261027-8be1cc70/live-output/11-cross-column-selection.jpg) 和 [向右换列](../artifacts/css-upright-20261027-8be1cc70/live-output/12-vertical-lr.jpg)。见 [逐项窗口结果](../artifacts/css-upright-20261027-8be1cc70/live-ui-summary.json) 与 [原始事件](../artifacts/css-upright-20261027-8be1cc70/live-output/events.jsonl)。本轮没有验收其他窗口尺寸、手拖边框、无边框宿主或设备缩放。

测试包自带 .NET 10.0.8，位于 `/Users/jal/.cache/jalium-ui-css-validation/8be1cc70/JaliumCssRepair.app`，实际进程加载的 core/platform/text/Metal 和 CLR 均来自该包。八个 Jalium 程序集与测试输出 SHA-256 一致；五个签名后原生 dylib 的 UUID 与所有含实际文件数据的 Mach-O 节 均与测试载荷一致。见 [托管打包身份](../artifacts/css-upright-20261027-8be1cc70/live-packaging.json)、[原生身份](../artifacts/css-upright-20261027-8be1cc70/live-native-identity.json) 和 [实际加载文件](../artifacts/css-upright-20261027-8be1cc70/live-loaded-files.log)。

已按 [Web Interface Guidelines 当前规则](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md) 中适用于原生窗口的标签、键盘、可见焦点与内容处理逐项复核。这不等于 Web、VoiceOver 或完整可访问性认证。

## 仍需团队继续实现

1. **任意字体的方向特性与基线精度。** 当前 U/Tu/Tr 借助 CoreText 竖排开关，R 单元保留原生水平塑形。本轮探针证明仅在水平 CTLine 的字体描述符里开启 `vert` 不会可靠应用竖排替代字形；尚未完成 upright 标点/破折号的 `vert` 约束、禁止字体通过 `vrt2` 自行决定 CSS 方向、缺失中央基线的规范合成以及可变字体/大小写/上下标组合。不得将本轮当作所有字体的 upright 规范完成。
2. **字体塑形局限。** `ḅ̈` 在 Hiragino Sans 回退 Helvetica，以及 Georgia 的水平 CoreText 单元里已有音标定位/advance 差异；关闭中央基线的水平探针也可重现。本轮保持平台水平塑形，没有把这组图片的像素相等写成音标正确性的证明。更多字体和语言需独立验收。
3. **纵中横。** `text-combine-upright:all/digits` 的真实合成、压缩、1em 格、断行、字距及二维命中/选区映射仍待实现。
4. **共享行内、正交嵌套、文档与编辑器。** 本轮入口继续排除共享 CSS 行内盒/片段格式器。Run 自身覆写方向、逻辑盒边的 used direction、Ruby、FlowDocument、RichTextBox、IME、分页、滚动和完整键盘编辑未完成。
5. **平台及发布验收。** Windows/Linux 原生 upright、VoiceOver 实际朗读、设备 DPI/输入、正式打包与 Native AOT 尚未验证。浏览器工具此前拒绝本地参考文件，没有尝试绕过，也没有计浏览器对照通过。

## 可追溯证据

本轮证据根目录：[css-upright-20261027-8be1cc70](../artifacts/css-upright-20261027-8be1cc70/)。命令、冻结源码、初始失败、修复后针对性结果、完整回归、CTests、独立参考图、字体探针及载荷身份均保存在此目录。

冻结了 4,469 个既有输入，本轮修改 22 条代码/测试/构建路径及 4 条文档路径。索引、暂存区和 HEAD 保持不变，未修改其他既有输入。见 [源码审计](../artifacts/css-upright-20261027-8be1cc70/source-audit.json)、[本轮增量补丁](../artifacts/css-upright-20261027-8be1cc70/implementation-delta.patch) 及 [补丁复现检查](../artifacts/css-upright-20261027-8be1cc70/patch-verification.json)。补丁只包含相对本轮开始时的增量；不能用它替代此前全部 CSS 源码。

两份 Metal 着色器复用前一轮已验证缓存并登记 SHA-256，没有计作新编译着色器。末次原生重建仅增加了垂直度量字体测试，所有产品 dylib 字节未变，见 [重建身份检查](../artifacts/css-upright-20261027-8be1cc70/native-metric-test-build.json)。正式 macOS HostSmoke/AOT 发布未执行。
