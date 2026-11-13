# CSS Ruby 合并与自动隐藏增量验证（R14）

本轮补齐真实消费者中的 `ruby-merge: merge/auto`、逐行重新配对和原文自动隐藏，继续“完整 CSS”目标。**完整 CSS、T4 和 Ruby 仍为部分完成**；通过结论仅覆盖本文列出的冻结候选、消费者和后端。

## 行为与兼容性

- 标准 CSS 关键字为 `separate | merge | auto`。`merge` 在同一段、同一行内合并注音，长短注音可以共享基字宽度；断行后按原关联重新分组，不将整个段强制变成不可断的原子。`auto` 在每条可共享序列中，全部注音都能放入各自基字时保持独立，否则合并；隐藏项分隔这些序列。
- 实际 `ruby-text-container` 的策略与对齐控制合并结果，单个注音的设置不覆盖容器。`inter-character` 使用独立排版。跨源注音保留各自原生文本段，避免把两个注音拼成一个新塑形来源；当前普通来源接入内部与跨源 CJK 分配，默认注音符号保持连续，显式 `text-justify` 可以覆盖。
- 自动隐藏比较原始文字内容，早于空白折叠和 `text-transform`；忽略元素边界，不做 Unicode 归一化。相同原文的注音没有布局、命中及可访问文本暴露，关联关系仍保留。修改文字、策略或移出原宿主后正确恢复。
- `merge` 禁用原文自动隐藏，并禁用注音自身／继承的 `visibility: collapse` 隐藏；独立后代的 visibility 声明仍适用。隐藏采用独立的使用值状态，不重写作者的计算属性、绑定或原文本。
- 原 C# `CssRubyMerge.Collapse = 1` 保留为 `Merge` 的数值／源码兼容别名；CSS 声明、补全与示例改用规范 `merge`，非规范 `ruby-merge: collapse` 会被拒绝，也不会覆盖先前有效声明。修正属性元数据，使 `ruby-position` 的初始值 `alternate` 与实际依赖属性和公开规范一致。

主要入口：`TextBlock.CssInline.RubyMerge.cs`、`TextBlock.CssInline.Ruby.cs`、`CssRubyVisibility.cs`，并连接共享行内布局、成熟 bidi／原生段落和 TextBlock 的几何、显示、可见性路径。行宽探测、balance／pretty／stable 探测与最终提交分别处理，缓存包含当前关联单元宽度。

## 冻结候选与可复现边界

证据目录：`artifacts/css-ruby-merge-20261028-d664551e/`。起点 HEAD 为 `b85d3540`，工作区有大量既存改动；冻结 4,572 个 Git 可见输入。候选只同步本轮 16 个代码／测试路径及三个文档路径，保留其他工作。完整冻结输入、受测代码摘要、命令、日志、TRX、应用载荷和增量补丁用于团队复现；跨机运行需调整记录中的本机绝对路径。

`implementation-delta.patch` 相对本轮冻结起点，依赖已有 R12／R13 与冻结工作区；不是相对主分支的独立补丁。最终工作区审计和补丁复现结果见 `source-audit.json`、`patch-verification.json`。

## 测试结果

- 构建：最终受测候选编译成功，0 错误、97 条提示；起点构建为 98 条提示。按路径／诊断编号／消息去重比较，新增两类测试分析器提示：集合大小断言 `xUnit2013`、独立 Metal 子进程输出等待的同步路径 `xUnit1031`。具体位置与比较见 `build-diagnostic-comparison.json`，没有作“零编译提示”声明。
- 定向：**1,169／1,169** 通过，0 失败、跳过、超时，约 263.4 秒，见 `final-focused/summary.json`。本轮新增 **353** 项：65 项解析／度量／原始选择范围／隐藏／恢复／稳定性检查，288 项像素用例。
- 严格像素：**894 对／1,788 张 PNG** 解码 RGBA 全部相同，Software／Impeller、Metal／Impeller、Metal／Vello 各 298 对。新增 288 对，每后端 96 对，覆盖三种方向、LTR／RTL、四种分配及共享空间／各自适配／换行／自动隐藏四种场景。九张新增联系图已查看。参考使用没有 Ruby 角色的独立 TextBlock、明确的物理槽和文字规定的尺寸，不读取实际 Ruby 几何，不调用 Ruby 布局辅助函数，像素容差没有放宽。见 `focused-pixel-identity.json`。
- 原生：既有 CSS 相关选择集 **65／65** CTest 重跑通过，0 失败、跳过、禁用，约 57.1 秒。117 个已注册项目中，52 个 platform／media 项未进入该选择集，见 `native-repeat/test-inventory.json`。822 个输入（771 个原生、51 个 Apple 构建输入）逐项与 R13 及实际复用的 R11 冻结源码相同；复用八个 dylib 和两个 metallib，**本轮没有重新编译原生代码**。见 `native-reuse-proof.json`、`native-reuse-input-manifest.json`。
- macOS 完整聚合回归：**21,883／21,883** 通过，0 失败、跳过、超时，约 1074.2 秒，见 `final-full/summary.json`。本轮新增 353 项全部包含；前轮 21,530 项的方法用例数全部保留，没有另计其他新增项，见 `legacy-case-retention.json`。全量中的同组 894 对像素与定向结果相同，九张最终联系图与已查看联系图的 PNG 摘要相同。

## 耗时观察

完整聚合中四个旧的文档嵌入对象用例，从 R13 的约 21–85ms 增至约 4.3–5.1 秒。相同方法的 30 项隔离复测全部通过，总运行约 3.0 秒；相同诊断程序分别加载 R13／R14 的受测程序集，且在 R14 启用 Ruby 使用值隐藏之前／之后各取样，R14 为约 18–166ms，R13 为约 19–159ms，未复现持续的数秒耗时。两侧程序源码相同，九个程序集逐项与各自受测载荷匹配。

全量顺序／进程状态导致的耗时差异尚未定位，不以隔离结果宣称全部性能通过。明细见 `slow-case-final-comparison.json`、`document-perf-check/summary.json`、`document-perf-isolation.json`、`document-perf-payload-proof.json`；后续保留该复现线索，并继续长 Ruby 段落与完整应用性能验收。

## 原生操作与修复前后证据

最终签名应用 `/Users/jal/.cache/jalium-ui-css-validation/d664551e/JaliumCssMergeRuby.app` 使用本地自包含 .NET 运行库。应用八个框架程序集的 SHA-256 与受测文件相同；八个签名 dylib 的 UUID 和全部文件支持段一致，两个 metallib 的 SHA-256 相同。见 `live-packaging.json`、`live-native-identity.json`、`live-metallib-identity.json`。

实际操作并查看 **30 张截图**，两个同载荷实例合计 **77 个事件、75 份来源几何快照**。每个实例三个场景共 17 个原始 TextBlock，所有记录中原父级不变；两个实例均已关闭，无残留进程或致命错误文件。

- 长短注音在 separate 时占 80px，merge 时占 64px；短行将“先”和两个关联单元换行，注音仍跟随对应基字。三种书写方向、三种合并策略、四种分配及 LTR／RTL 均实际操作。
- 相同原文的注音随 separate／auto 隐藏，merge 恢复；长注音→相同基字→空注音→原文均查看。`merge + visibility: collapse` 保持注音可见，切到 auto 后隐藏，计算属性始终保留 `Collapse`。
- 独立与合并状态下，原始注音拖选得到“じょ”`0+2`，Cmd+A 扩展为“じょう”`0+3`；合并状态下基字选择得到“上”`0+1`。Tab 显示按钮焦点，空格实际切到竖排。
- 客户区 860×630 的横排短行／竖排 48px 短列、1040×700 恢复、系统放大到 1920×942 后还原均查看。最终全部来源文字、可见性、尺寸、矩阵和字符几何与初始相同，两个恢复时点的三个内容区共六对裁剪 RGBA 逐字节相同。

恢复按钮同步清选区时有两条 selection 回调记录在本次布局提交之前；隐藏状态的断言采用已提交布局的事件，记录编号明确保存在 `live-ui-summary.json`，没有将中间回调当成最终帧。截图、事件及像素恢复见 `live-output/`、`live-ui-summary.json`。

修复前 `before/summary.json` 的 30 项中有 27 项失败，覆盖合并尺寸、断行、隐藏与策略解析；实际消费者接入后通过。进一步独立像素检查 `boundaries/summary.json` 真实暴露 12 项失败：水平 LTR 的 space-between／space-around 在没有可分配剩余空间时，仍开启原生 justification，字形上移约 4px。现在只有实际分配内部间距时才启用；参考与容差均未改变，最终三后端全部通过。

## 团队后续任务与验收要求

1. **Ruby 盒与行模型**：完整 intrinsic／min／max／百分比、注音 line-height、容器 border／padding、leading、overhang 和多层行框。用独立物理几何及实际墨迹证明，不能只检查解析属性。
2. **规范归一化与分配**：完整匿名盒、元素／空白边界、尾随空白、复杂注音面板内部和跨来源分配，并建立长 Ruby 段落性能基线。当前普通来源的成功不代表任意嵌套来源都已符合规范；回归用例耗时不是受控性能基准。
3. **方向与断行组合**：独立 ruby／base-container／text-container 方向、多脚本复杂 bidi、正交嵌入、嵌套 Ruby、分页／多列／float／clamp 全组合及更广 WPT 场景。现有 LTR／RTL 与三种书写模式只是已列矩阵。
4. **宿主与平台**：FlowDocument、RichTextBox／编辑器消费者；Windows／Linux、其他 GPU、触屏、物理 IME、VoiceOver 实际朗读。当前检查只覆盖 macOS 原生窗口、AX 树、键盘和当前字体载荷；手拖窗口边框未验收。
5. **其他 CSS 模块**：继续按 [团队实施计划](css-implementation-plan.md) 的开放项分工，保持“规范版本＋真实消费者＋平台／后端＋用例集合”的验收口径，不用聚合总数替代缺失能力。

规范依据：[W3C CSS Ruby Annotation Layout Level 1](https://www.w3.org/TR/css-ruby-1/)，重点为 §2.4 自动隐藏、§3.5 bidi、§4.1 位置与 §4.2 合并；公开版本仍为工作草案。`auto` 的具体合并算法允许由用户代理选择，本文明确给出当前策略，不声称所有代理行为相同。
