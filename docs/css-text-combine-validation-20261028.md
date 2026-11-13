# CSS 纵中横接入与验收记录

本增量把 `text-combine-upright:all`、`digits 2/3/4` 接入 macOS 独立 TextBlock／普通 Run 的真实测量、塑形、换列、绘制、选区和命中。T4 和完整 CSS 仍为部分状态；共享行内盒、文档、编辑器、Ruby、其他平台及下文列出的字体组合尚未闭环。

冻结候选与复现入口：`artifacts/css-text-combine-20261028-961ffe9d/`。最终 macOS 托管聚合回归 20,646 项全部通过，失败、跳过、超时均为 0；最终窗口和补丁重放也已完成。中止运行和局部结果单独保留，不计入最终全量通过数。

## 实现与语义

- 原生新增可选 `TextParagraphCombineProvider` 和 `jalium_text_paragraph_create_combined`，保留既有创建入口、构造入口和 provider vtable。组合输入指定原文 UTF-16 范围、父级 em 和内部方向；非法范围、重叠、非有限数字及拆开组合的间距单元会被拒绝。
- 水平 CTLine 保存真实字体、字形与绘制属性。内部组合忽略字间距、软连字符与强制换行；外部以一个 em 字格参与布局和间距。优先检查 `hwid`、`twid`、`qwid` 在全部字符单元上的实际替换，按可用宽度字形回退，之后根据真实推进量压缩。没有以字符数估算值决定实际字形宽度。
- 内部占位格复用 caps／emoji 的原文映射，保留公开文本。组合内部没有独立布局光标；部分原文选区的几何扩展到该组合。完整字符和父级继承边界参与候选判定，数字按变换前的最大 ASCII 数字序列判定，超长数字不切成多个组合。
- `mixed`、`upright`、`sideways` 字形方向均可包含纵中横；横排和 `sideways-rl/lr` 书写模式保持原行为。组合以直立单元参与外部双向重排，混排段落仍保留周围文字的方向。
- 补齐侧向父字体的字母基线到字格中心的对齐。取消样式、父级继承变化、字体及内容变化使段落和绘制缓存失效；重复内容复用不可变的水平塑形。
- 公共分段函数与消费者共用分类结果，识别包含换行和纯空白的 `all`，数字分段不拆开组合字符。旧压缩字段仅保留为字体无关的预估提示，实际消费者不使用该值排版。

规范依据：[CSS Writing Modes 4 的纵中横规则](https://www.w3.org/TR/css-writing-modes-4/#text-combine-upright-property)。空白处理对照 WPT 的 [normal](https://wpt.live/css/css-writing-modes/tcy-white-space-processing-001.html)、[pre](https://wpt.live/css/css-writing-modes/tcy-white-space-processing-002.html) 和[纯空白字格](https://wpt.live/css/css-writing-modes/tcy-white-space-processing-003.html) 条件编写消费者用例；原始文件和哈希保存在 `reference-sources/`。这些是规则移植用例，不等同于运行完整 WPT 浏览器套件。

## 验证证据

| 范围 | 当前证据 |
| --- | --- |
| 新字体 | 6 个原创几何字体，包含全部／仅半宽／部分覆盖／没有特性／空特性／天然窄字形；MIT 授权，离线生成，两次结果一致 |
| 独立字形与后端 | Software、Metal 各 2,016 组，覆盖 3 种字形方向、LTR/RTL、完整／拆分字体段、1×/2× DPI、原文换行、全宽字符、软连字符、零宽字符及纯空格 |
| 严格像素 | 4,032 组栅格 actual/reference，8,064 张 PNG 解码逐字节相同；其中 576 组按预期没有墨迹，但仍保留一个 em 布局。每个 C++ 用例还比较真实公开后端帧读回 |
| 消费者、分段与图像复测 | 补空白边界后的 127 项通过，0 失败／跳过／超时；包括 CSS 空白像素等价、原文、大小写展开、继承边界、取消样式和录制快照 |
| 最终相关回归 | 398／398 通过，0 失败／跳过／超时，15.253 秒；本增量共新增 57 项托管用例，并复验既有编辑器类的 40 项用例 |
| 最终原生回归 | 文本／绘制选择集 65／65 通过，0 失败／跳过，45.069 秒；未将 platform／media 选择集算作已运行 |
| 最终全量回归 | macOS 托管聚合 20,646／20,646 通过，0 失败／跳过／超时，1039.863 秒；以 `managed-final-full/results/result.trx` 和 `summary.json` 为准 |
| 最终窗口 | 相同最终载荷的 2 个 Metal 会话，57 条事件、55 份原文／几何快照、15 张已查看截图；进程正常退出，无 error／fatal 日志 |

原生参考直接从原创字体绘制字面 glyph ID、推进量和基线，不调用生产的组合、宽度替换、缩放、源映射或 run delegate 辅助函数。已经查看两后端实际／参考图形对照表。

保留了会失败的对照：只使用首选宽度特性时，只有半宽字形的三位数字与参考相差 306 字节；侧向基线未修复时，与字面中心参考相差 415 字节。空白修复前，13 项消费者用例中有 7 项失败，涵盖丢掉空白字格、拆分空格、尺寸错误和 `preserve-spaces` 的像素差异。

全量回归曾暴露既有图像测试的异步等待问题：`NaturalSize.Width > 60` 也接受了尚未解码时的 300px 回退尺寸。独立 62 项图像复测通过；测试现在明确等待真实加权尺寸及流释放，保留原数值断言。中止的旧载荷和修复前运行保存在各 `managed-before-*` 目录，不计入最终通过数。

后续全量运行又发现本轮空白集成导致的真实回归：没有显式 CSS 空白规则且没有组合单元时，隐藏行内内容仍会触发空白遍历；新加的组合条件使折叠循环不消费当前空格，外层循环无法前进。进程调用栈定位到 `CssWritingToolsEditingTests.ServiceOffsetsReplaceVisibleTextAndPreserveHiddenSources`。已恢复零组合单元时原有的消费条件，既有编辑器类 40 项全部通过，并用修复后的载荷重新运行全量测试。旧运行在 1113.847 秒后因确认循环而停止，已完成的 11,214 项不能算作全量通过；诊断、调用栈和中止收据保存在 `managed-before-whitespace-progress-full-cancelled/`、`full-managed-stack.json` 与 `full-running-sample.txt`。修复前的窗口记录位于 `live-before-whitespace-progress-fix/`，上表最终窗口计数来自重新打包、重新操作后的最终载荷。

## 窗口与验证边界

窗口验证使用任务自有、自包含、内部缓存中的应用；8 个框架程序集与测试输出 SHA-256 一致，8 个签名后原生库与测试载荷的 UUID／全部文件存储节一致。实际进程映射记录确认 CLR、core、text、platform、Metal 来自该应用内部。着色器沿用 R8 的两份已登记缓存，未计作本次新编译。

窗口验收检查日期的两位／三位／四位组合、跨列拖选、从空选区 Command+A、`ß` 展开后的原文选区、Tab／Space 和可见焦点、取消与恢复组合、左右列方向、原生放大与恢复，以及纯空格字格。日期由两列实际换为三列，拖选得到原文 `08年10`；大小写展开仍保留 `ß\r\nAB` 五个原始字符。mixed／sideways 的半宽字形墨迹中心相同，窗口从 1000×650 放大到 1920×942 后恢复原尺寸，最终子布局几何与初始值完全相同。

原生尺寸事件先于子布局更新，故补充会话通过恢复动作强制父级布局完成后记录几何，没有把过早的尺寸事件当作布局完成。Space 的证据是工具向已聚焦按钮发送按键后，AX 状态和截图显示模式变化；它不在仅记录文本样本的 KeyDown 日志中。最终布局、选区、截图和退出记录由 `validate-live.py` 核验，工具操作证据与自动日志断言分别标明。

本轮不据此声称物理键盘／触控板、IME、朗读 VoiceOver、Home／方向键扩展选区、withoutborder 窗口、Windows／Linux 或移动设备验收通过。

## 候选范围与复现

验收对象是从 4,528 个 Git 可见输入冻结的候选，再加入本轮 35 个代码／测试／字体文件；另有 2 个文档文件。运行期间工作区有并行修改，它们保留在原位置，未引入本轮候选。`source-audit.json` 和 `external-workspace-drift.json` 列明边界，并核验本轮代码与编译输入一致、冻结的其他输入未变，以及原工作区 HEAD、索引和暂存差异未变。因此本记录不能作为全部最新工作区改动的联合回归结论。

`implementation-delta.patch` 仅包含本轮增量及二进制字体；`patch-verification.json` 保存校验和、`git apply --check` 及重放后所有文件的哈希核验。补丁基于本轮开始时的工作区字节，不能假定适用于任意干净 HEAD。

下表路径均相对冻结候选根目录：

| 证据 | 文件 |
| --- | --- |
| 构建参数 | `native-configure-command.json`、`native-build-command.json`、`managed-build-command.json` |
| 最终测试命令、日志、TRX 与计数 | `managed-final-focused/`、`managed-final-full/`、`native-final/` |
| 独立像素与失败对照 | `final-native-pixel-identity.json`、`native-whitespace/`、`negative-before-width-fallback/`、`negative-sideways-centering/` |
| 最终窗口与载荷身份 | `live-ui-summary.json`、`live-output-primary/`、`live-output/`、`live-screenshots/`、`live-packaging.json`、`live-native-identity.json`、`live-loaded-paths.json` |
| 源码与补丁边界 | `baseline-manifest.json`、`final-owned-manifest.json`、`source-audit.json`、`external-workspace-drift.json`、`patch-verification.json` |

在仓库根目录可用下述入口复验；构建命令完整保存在上表 JSON 中。重跑测试请使用新的阶段名，保留最终原始收据。

```sh
python3 artifacts/css-text-combine-20261028-961ffe9d/run-tests.py managed-repeat-full FULL
/Users/jal/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/bin/python3 artifacts/css-text-combine-20261028-961ffe9d/validate-pixels.py
/Users/jal/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/bin/python3 artifacts/css-text-combine-20261028-961ffe9d/validate-live.py
```

`run-tests.py` 的脚本退出状态不代表测试通过，须检查其 `summary.json` 的 `exitCode`、TRX counters 及失败列表。像素和窗口脚本检查已有证据，不会重新操作窗口。

## 后续实现

1. 将竖排与组合单元扩展至共享 CSS 行内盒、正交嵌套、FlowDocument、分页与编辑器；不能绕开当前 `HasCssInlineBoxes/Fragments` 的布局边界。
2. 补齐组合内部自定义 `tab-size`、复杂 BASE／字体度量覆写、多脚本上下文宽度替换、显式保留字体特性标签，以及全宽字符的完整反向映射与更广变量／后备字体组合。
3. 复验截断、clamp、装饰、强调标记、特殊空白和行内盒边界的全部组合。当前没有把通用段落 API 的自动断词当作完整 CSS 断行证明，TextBlock 的 CSS 断行由源文本和原子边界共同决定。
4. 实现 Ruby 的真实盒树和排版，继续竖排下表格、定位、多列、滚动与设备验证。完整 CSS 目标保持 active。
