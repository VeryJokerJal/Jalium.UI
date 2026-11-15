# CSS Ruby inter-character 增量验证（R13）

本轮继续“完整 CSS”目标，补上 `ruby-position: inter-character` 的实际消费者，并修复原生缩窗操作暴露的容器覆盖问题。**完整 CSS、T4 和 Ruby 仍为部分完成**。本记录的通过结论只覆盖下文列出的冻结候选与实际场景。

## 实现范围

- 横排 Ruby 中的显式 `ruby-text` 计算为 `vertical-rl`；通过依赖属性读取也能得到计算值。原始 CSS 值、本地值及双向绑定仍留在原层，解除侧排后恢复。显式后代可保留自己的书写方向，普通继承和 `writing-mode: inherit` 能跟随受约束的注音。
- `ruby-text-container` 自身的书写方向保持原值。它控制本层位置，注音节点自身的 `ruby-position` 不覆盖容器位置。匿名注音中的普通来源按原控件树继承方向，不因匿名盒强制改变 DOM 继承。
- 横排的注音放在关联基字的物理右侧，LTR／RTL 均如此；多层向右堆叠，跨基字注音放在覆盖范围最右侧之后。侧排列参与跨列的普通注音测量，避免重复分配宽度。
- 按当前基字内容度量补齐自动高度；显式短高度保留。`start` 按顶部定位，其余按中心定位；较长注音会增加所在关联单元高度。
- 单个普通 TextBlock 的 CJK 分配沿用实际整形、绘制、范围与命中。默认不把相邻注音符号或拉丁字母拉开；显式 `text-justify` 可以覆盖默认策略。显式注音面板的普通注音符号来源也保留连续间距。
- 关联单元进入正文共享行并可在单元之间换行；跨基字注音保持关联。原始文本独立选择，控件父级不变。
- 显式基字容器及侧排注音容器采用基字层的查询边界。原生窗口布局队列再次处理结构容器时，不再让其独立流布局覆盖外层分配。

实现主要位于 `CssRubyWritingMode.cs`、`CssDisplayLayout.cs`、`CssFlowProperties.cs` 和 `TextBlock.CssInline.Ruby.cs`，并连接现有原生内容、bidi 与字间分配路径。没有替换原始文字或把显示内容转成不可选择的绘图副本。

## 候选与测试

本轮目录为 `artifacts/css-ruby-inter-character-20261028-082302be/`。起点冻结 4,586 个 Git 可见输入；本轮代码／测试增量 16 个路径，另有两个文档路径。并行工作保留在工作区，冻结之后的非本轮变更不进入候选。最终源文件、测试载荷和应用载荷分别核对摘要。

`implementation-delta.patch` 是相对本轮冻结起点的增量，依赖已有 R12 实现和起点状态；不能把它当成相对主分支的独立完整补丁。冻结输入清单、命令与完整候选源码供团队对齐依赖和复现。

- 定向：**816／816** 通过，0 失败、跳过、超时，约 156.9 秒，见 `final-container-focused/summary.json`。本轮新增 **182** 项，其中 62 项计算值／几何／状态检查、120 项像素对照。
- 严格像素：**606 对／1,212 张 PNG** 的解码 RGBA 全部相同，各后端 202 对。新增侧排注音 120 对，Software／Impeller、Metal／Impeller、Metal／Vello 各 40 对，覆盖单符号、双符号、长注音、拉丁字母、汉字、四种分配和 LTR／RTL。参考由没有 Ruby 角色的独立横／竖排 TextBlock 放入文字规定的物理槽构建，不读取实际 Ruby 几何，也不调用 Ruby 布局辅助函数。见 `focused-pixel-identity.json`。
- 原生：既有 CSS 相关选择的 **65／65** CTest 重跑通过，0 失败、跳过、禁用，约 39.9 秒。117 个已注册项目中，52 个 platform／media 项目未进入本轮 CTest 选择；不称为全部原生测试通过，清单见 `native-repeat/test-inventory.json`。对比 822 个输入（771 个原生、51 个 Apple 构建输入），与 R12 及实际复用的 R11 冻结源码相同；复用八个 dylib、两个 metallib，**没有声称本轮重新编译原生代码**。见 `native-reuse-proof.json`、`native-repeat/summary.json`。
- 完整聚合（macOS 测试入口）：**21,530／21,530** 通过，0 失败、跳过、超时，约 953.5 秒，见 `final-full/summary.json`。本轮新增 182 项全部包含；前轮 21,316 项的方法用例数全部保留。另增加的 32 项是冻结起点已经存在的 JsonTreeViewer 测试，单独列在 `legacy-case-retention.json`，不算作本轮实现新增。

没有改变像素容差。字形、选择与渲染证明针对本机 macOS 与当前字体载荷，不替代其他系统验收。

## 原生操作与修复前后证据

最终签名应用为 `/Users/jal/.cache/jalium-ui-css-validation/082302be/JaliumCssSideRuby.app`，使用本地自包含运行库。应用中的八个框架程序集与受测文件 SHA-256 相同；八个签名 dylib 的 UUID 和全部文件支持段与受测载荷一致，两个 metallib 的 SHA-256 也相同。

实际操作并查看 **24 张最终截图**，记录 **61 个事件、60 份来源几何快照**。三个场景共 13 个原始文本节点：单组侧排、正文共同行／换行、三层注音。

- 原文拖选“中文”得到 `0+2`，Cmd+A 得到“中文字”`0+3`；竖排注音独立拖选与 Cmd+A 得到“ㄓㄨ”`0+2`。
- Tab 显示原生焦点轮廓，空格实际触发方向按钮。操作三种书写方向、四种位置、四种分配和 RTL。
- 客户区 860×630 的短行、竖排 96px 短列、1040×700 恢复，以及系统放大至 1920×942 后还原均查看。
- 长注音→空注音→原注音，清空没有残留原注音。所有事件中原父级保持不变；恢复后全部来源文字、尺寸、矩阵和字符几何与初始相同；三个内容区的解码像素逐字节相同。

最初原生缩窗发现三层注音重叠，见 `pre-container-fix-live-screenshots/08-horizontal-narrow.png`。只做根节点 Measure／Arrange 的无窗口检查不能复现；显式排入 LayoutManager 后续队列后，修复前用例真实失败，见 `resize-queued-negative/summary.json`。修复结构容器的布局归属后，该用例、最终定向和原生同一流程均通过，见 `live-final-screenshots/08-horizontal-narrow.png`。

统一容器归属也揭示匿名注音来源的高度路径差异：普通 inline 来源仍按原生回退高度测量，导致居中偏移约 2.2px。修复前两个用例失败，见 `anonymous-diagnostic/summary.json`；现由匿名注音上下文进入相同 CSS 行度量路径，并验证移出 Ruby 后恢复普通排版。

截图、操作和几何见 `live-final-screenshots/`、`live-final-output/events.jsonl`、`live-ui-summary.json`、`live-restore-pixel-identity.json`。最终测试实例已关闭。

## 仍需团队继续实现／验收

1. `ruby-merge: merge/auto`、自动隐藏、完整匿名盒归一化及空白折叠、跨来源的全部 CJK 分配策略。
2. 完整 Ruby 盒模型与 intrinsic 尺寸，包括规范要求的注音 line-height 处理、容器边框／内边距语义、min/max／百分比及复杂尺寸约束；本轮采用现有度量体系，不宣称规范完整。
3. leading、overhang、复杂 bidi、正交书写方向嵌入与嵌套 Ruby、组合来源和所有断行边界，及更广的规范／WPT 场景。
4. FlowDocument／编辑器消费者、Windows／Linux 和其他 GPU 后端、物理 IME、触屏及 VoiceOver 实际朗读验收。
5. 实施计划中其他 CSS 模块的开放项。本轮的通过数不能用于把它们标为完成。

规范依据：[W3C CSS Ruby Annotation Layout Level 1](https://www.w3.org/TR/css-ruby-1/)，重点为 §3.2、§4.1 和 §4.3；该公开版本自身仍为工作草案。
