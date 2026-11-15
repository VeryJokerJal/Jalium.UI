# CSS Ruby 真实消费者增量验证（R12）

状态：本轮定向、完整聚合与真实窗口验收通过；T4、完整 Ruby 和完整 CSS 目标继续保持部分状态。

## 实现范围

- 五种 Ruby display 角色进入实际层叠、元素和排版路径；不再只有作者提示及独立坐标辅助函数。
- 由原始基字、注音和容器节点组成排版轨道；辅助单元不改动作者的父子关系，不创建可选文字或辅助技术节点。
- 独立关联对参与正文共享行的断行；跨基字注音所覆盖的关联单元保持在一起。支持显式容器、基本缺失外层的修复及多层注音。
- 水平、`vertical-rl`、`vertical-lr` 的 over／under，默认 alternate、alternate under 和同侧层次向外叠加；RTL 使用实际 used direction。
- 普通 TextBlock 基字／注音复用自己的原生文字段落。CJK 字间分配复用既有 justification，绘制、UTF-16 几何、命中和选区使用同一套排版结果；作者的 text-align 没有被局部值覆盖。
- 重新测量、内容修改、改宽和 display 切换时，脱离旧的辅助视觉子节点；恢复缓存段落时按需重新激活，避免累计重复单元。

## 源码入口

- [Display 解析与计算](../src/managed/Jalium.UI.Core/Css/CssDisplayProperties.cs)、[布局分发](../src/managed/Jalium.UI.Core/Css/CssDisplayLayout.cs)、[Ruby 角色](../src/managed/Jalium.UI.Core/Css/CssRubyRoles.cs)。
- [Ruby 关联轨道、断行单元与原节点安排](../src/managed/Jalium.UI.Controls/TextBlock.CssInline.Ruby.cs)、[普通轨道原生内容](../src/managed/Jalium.UI.Controls/TextBlock.CssContent.cs)、[行内片段](../src/managed/Jalium.UI.Controls/TextBlock.CssInline.Frames.cs)。
- [实际消费者与独立像素检查](../tests/Jalium.UI.MacOS.Tests/CssRubyRuntimeTests.cs)。

## 验收方法与证据位置

本轮目录：`artifacts/css-ruby-20261028-cf5d93d4/`。冻结 4,580 个 Git 可见输入，排除其他并行工作对候选的影响。原生 771 个输入与 R11 完全相同，复用八个 dylib 和两个 metallib，记录于 `native-reuse-proof.json`，不声称本轮重编原生代码。

实际控件回归位于 `CssRubyRuntimeTests`，检查角色、来源范围、横竖排位置、多层关联、RTL、字间分配、共享断行、隐藏／折叠、动态恢复及视觉树稳定性。首批 12 项在原实现上出现 11 项失败；加强共享行关联断言后，原实现的对应检查也失败。修复后的同组 12 项通过，扩展检查的最终定向结果见下文。

独立像素参考不含 Ruby display 或 Ruby 格式器：以固定的 32／16 px CJK advance、40／20 px 行框和明确的物理矩形放置普通 TextBlock。要求实际 Software、Metal／Impeller、Metal／Vello 目标的 RGBA 严格相同，并断言目标实际引擎；每组保存 actual／reference PNG，不以引擎请求参数代替实际引擎。

## 本轮已完成的验证

- 最终冻结候选的完整聚合：21,316／21,316 通过，0 失败、跳过、超时，约 1011.0 秒。结果位于 `managed-final-empty-full/summary.json`；205 项新 Ruby 检查全部包含在此结果中。
- 定向回归：634／634 通过，0 失败、跳过、超时，约 135.9 秒。包括新增 205 项 Ruby 检查，以及既有来源范围方向、共享竖排和相关属性回归。
- 严格像素：486 对 actual／reference、972 张 PNG 解码后相同且非空；其中新增 Ruby 144 对，既有 342 对。Software／Impeller、Metal／Impeller、Metal／Vello 各 162 对，其中 Ruby 各 48 对；各后端与自己的独立参考比较，没有声称不同后端输出彼此相同。
- 原生重跑：65／65 通过，0 失败、禁用、跳过，约 49.3 秒。使用与本轮 771 个原生输入完全一致的 R11 原生构建，排除平台窗口及媒体组，不声称重新编译或覆盖被排除项目。
- 真实窗口：本轮自包含应用的八个托管组件与测试载荷 SHA-256 一致；签名后的八个 dylib 的 UUID 和全部有文件数据的 Mach-O 节与测试载荷一致。最终窗口 65 条事件、64 份原节点几何快照，13 个原始 TextBlock 的父子关系均保持。最终签名构建保留并查看 23 张实际窗口截图；初版及中间构建的记录另存，未混作最终载荷的交互结果。
- 实际操作：原始基字拖选 `東京`，Command+A 得到 `東京都`；原注音独立拖选／全选得到 `とう`。Tab 到具名按钮后焦点可见，空格触发模式切换。横排 96 px 短行、vertical-rl／vertical-lr 的 96 px 短列均在原窗口中产生实际断行／换列；还检查了 RTL、注音变长／清空／恢复、位置与分配切换；应用记录尺寸包含 860×590 窄窗、1920×943 系统 zoom 及恢复。未将自动输入当成物理设备验收。
- 恢复证明：最终恢复与初始载入的全部原文几何相同；三个完整内容区裁片解码像素完全相同。AX 树保留原文字与具名按钮；未据此声称 VoiceOver 实际朗读通过。测试窗口已关闭。

结果位于 `managed-final-empty-focused/summary.json`、`focused-pixel-identity.json`、`native-repeat/`、`live-ui-summary.json`、`live-restore-pixel-identity.json` 和 `live-final-ax.txt`。

## 发现并修复的问题

1. 回填原节点时误把组合偏移当成布局槽位置：改用相对于 Ruby 宿主的实际矩阵，修复正文前缀和换行后的位置。
2. 注音分配曾被匿名共享段落的紧排几何覆盖：普通基字／注音直接复用其原生段落，接入现有整形与 bidi justification，作者的 text-align 保持原值。
3. 原生默认 strut 额外撑大继承 CSS 行高：Ruby 按已解析的 CSS 行框测量，而不是额外取自然默认行高。
4. 默认物理垂直 start 对齐把竖排分配槽压回紧排高度：最终排版明确消费分配的物理尺寸；分配后刷新相关原生绘制／范围缓存。
5. 全局 Ruby 默认位置和 alternate under 语义缺失，以及不同排版缓存之间切换时辅助单元累计：补齐默认交替、首层侧别、旧单元脱离与缓存恢复激活。
6. 动态把非空基字／注音清空时残留旧行框：清空排版范围，再按当前 CSS 内容测量。新增 9 项横排／两种竖排的基字、注音、两者同时清空与反复恢复检查，在前一版框架载荷上 9／9 失败；该载荷的八个组件与此前 625 项通过候选相同，身份记录位于 `empty-before/product-identity.json`。修复后 9 项包含在最终 634 项定向结果中。此前完整聚合在 13,878 条通过日志、无失败标记时主动中断，重新构建并运行最终聚合，不把中断运行计为通过。

各次失败和修复阶段保留在本轮证据目录。像素阈值保持严格相等，未通过放宽容差消除失败。

## 明确未完成的 Ruby 边界

1. `ruby-position:inter-character` 的横排注音强制竖排、完整尺寸／基线与原文几何链路。
2. 当前草案的 `ruby-merge:merge`、分行后重新合并，以及 auto 的完整布局策略；旧枚举／辅助函数的 collapse 不代表真实消费者完成。
3. 注音与基字相同的自动隐藏、隐藏边界与 merge 的规范交互。
4. 完整匿名盒修复、层内／层间空白规则、所有复杂跨列关联、最小／最大固有尺寸、嵌套 Ruby、嵌入对象和浮动／定位组合。
5. 注音对 line-height 的精细 leading／overflow 规则及 ruby-overhang；当前实现为关联轨道预留保守的完整块轴范围。
6. 不同原始节点组成一个 Ruby 轨道时，规范所要求的跨来源字间分配及更广字体、DPI、边框／padding／margin、正交／sideways、变换／背景／装饰组合验收。
7. 段落跨基字与注音的统一阅读／复制策略、完整 FlowDocument／RichTextBox 接入及其他平台。窗口中的原始 TextBlock 独立选择通过也不能代替这些能力。
8. 物理输入设备、IME、VoiceOver 实际朗读、手工连续拖动缩放及 Windows／Linux 实机验收。

参考：[W3C CSS Ruby Annotation Layout Level 1](https://www.w3.org/TR/css-ruby-1/)。本轮只按已列明场景报告增量，不把角色解析、单张截图或辅助计算函数的通过数当作 Ruby 规范完成。

## 源码与交付范围

本轮增量包含 14 个代码／测试文件及 2 个文档。构建、定向、最终聚合和真实窗口所用代码输入保持一致；源码清单、载荷身份和可重现 patch 由 `final-tested-code-manifest.json`、`source-audit.json`、`live-packaging.json`、`live-native-identity.json` 及 `patch-verification.json` 记录。初版、像素修复阶段、主动中断的聚合和动态清空的反向对照保留作诊断证据，不替代最终结果。

验证对象是冻结候选；基线之后发生的并行源码／文档／测试项目修改保留并排除，逐路径记录在 `external-workspace-drift.json`。没有重置、清理、暂存、提交或推送用户工作。完整 Ruby 与完整 CSS 目标继续保持开放。
