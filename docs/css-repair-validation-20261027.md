# CSS 已知回归修复与复验

日期：2026-10-27。范围：修复 [独立验收复核](css-validation-20261027.md) 暴露的既有回归，以及隐藏、Motion Path、背景并集和文档源范围问题。完整 CSS 目标仍未完成，竖排与 Ruby 保持开放。

后续 T4 增量：独立 TextBlock 与普通 Run 已接通 macOS 原生竖排段落，见 [TextBlock 竖排验证记录](css-textblock-writing-mode-validation-20261027.md)。本页原计数和窗口截图仍对应本页修复快照；复杂行内盒、文档/编辑器、upright、纵中横和 Ruby 继续开放。

证据可用性更新：后续 T4 工作期间，原外置盘 `.codex-css-acceptance` 目录不再出现在原路径。本页保留已实际执行的历史结果，下面旧 TRX、载荷与截图路径目前不能复读。后续新源码及验证证据改存仓库的 `artifacts/css-vertical-20261027-recovered-d477103f/`；具体状态见 [团队计划的 T4 记录](css-implementation-plan.md#t4-垂直书写-逻辑轴与-ruby)。不得把本页历史计数当作新载荷的全量结果。

## 当前结果

| 验证 | 修复前 | 本次最终候选 |
| --- | --- | --- |
| Release 构建 | 0 错误、82 警告 | 0 错误、81 警告 |
| 聚合全量回归 | 19,537 通过、161 失败、2 跳过 | 20,165 通过、0 失败、0 跳过、0 超时；811.803 秒 |
| 原失败方法及受影响模块定向复验 | 原 283 项中 161 失败 | 323 通过、0 失败、0 跳过、0 超时 |
| 独立消费者及像素复核 | 10 项中 1 通过、9 失败 | 7 通过、3 失败；剩余失败均为 T4 |
| 新增消费者测试反向对照 | 同一最终测试程序集换回修复前运行库 | 15 失败、0 通过；修复版 15 项全部通过 |
| 桌面原生窗口 | 已在 Metal 窗口复现问题 | 最终 build 13 已重启并实际操作；托管载荷与最终测试版本哈希一致 |

定向测试与全量测试重叠，不相加计算覆盖数。构建警告仍包含裁剪/AOT 分析及测试分析器提示，不能将构建成功解读为 Native AOT 发布成功。

## 修复内容与实际效果

| 范围 | 修复后行为 | 验证方式 |
| --- | --- | --- |
| 普通文档与复杂行内布局 | 常规文档尺寸不再误入通用盒树；复杂行内内容仍使用对应盒模型 | 原尺寸、多列、分页、表格与内在尺寸回归 |
| 首行、首字与样式失效 | 匹配规则、层叠来源、显示模式和树结构变化刷新伪元素投影 | 既有首行/首字、主题、生成内容及组合测试 |
| 行内 padding 与装饰 | 在实际包含块宽度下解析边距；共享文档装饰保留来源 | 固定值/百分比布局、文字装饰与像素回归 |
| `content-visibility` | 隐藏自身文字、视觉后代和文档源内容，保留容器外观与原文；不覆盖作者设置的透明度 | Software 像素、录制重放、命中、AX 名称、Run/Paragraph/FlowDocument 反复开关 |
| 焦点与按需显现 | hidden 容器自身仍可聚焦，隐藏后代不可聚焦；auto 后代在调用焦点提供器前显现 | 焦点提供器断言；窗口键盘操作 |
| Motion Path | 普通变换 40px 与路径 100px 合成为 140px；关闭路径恢复 40px；响应可变原生 Transform | CSS/原生变换各反复开关、原点/旋转、源对象替换、固定包含块测试 |
| Motion Path 与 3D | 保留原变换完整 3D 矩阵及渲染标记，`translate3d(10,20,30)` 加路径后为 `(110,20,30)`，关闭路径恢复 `(10,20,30)` | 3D 矩阵与深度断言，三次开关；修复前实测 Z 为 0 |
| `background-clip:border-area text` | 将边框环与字形合成同一遮罩后绘制一次，形成并集且不重复混合透明度 | 实际像素、半透明 alpha 上限、不可变录制重放；Metal 窗口观察 |
| 背景简写与长属性 | 简写 `padding-box text` 分别设置 origin 与 text clip；长属性拒绝不合法组合 | 解析及绘制回归 |
| 嵌入 UI 的源范围 | `A + InlineUIContainer + B` 保留为 `A\uFFFCB`，控件占一个源单位；保留实际分配宽度、命中和选区范围 | TextBlock 字符几何/命中/全选；RichTextBox 公共 Render 像素与窗口全选 |
| 滚动锚定 | 进入/退出脱离普通流的定位状态时，当前布局周期抑制锚定补偿；保留普通内容增长的锚定能力 | fixed/static 切换及既有滚动锚定测试 |

主要入口：[新增消费者测试](../tests/Jalium.UI.MacOS.Tests/CssConsumerRegressionTests.cs)、[Motion Path](../src/managed/Jalium.UI.Core/Css/CssMotionPath.cs)、[内容可见性](../src/managed/Jalium.UI.Core/Css/CssContainmentProperties.cs)、[文档盒路由](../src/managed/Jalium.UI.Controls/FlowDocumentPresentation.Boxes.cs)、[首行失效](../src/managed/Jalium.UI.Core/Css/CssFirstLine.cs)、[背景遮罩](../src/managed/Jalium.UI.Controls/CssBackgroundTextClip.cs)、[行内源范围](../src/managed/Jalium.UI.Controls/TextBlock.InlineBoxes.cs)。

## 测试修订与计数口径

没有把原失败统一删除或放宽容差。修订了几处与真实绘制入口或规范不一致的旧观察方法：

- `symbols(cyclic url('x'))` 的图片符号语法合法。将旧的拒绝断言替换为接受并保留图片语法的正向用例；该项不作为“原断言原样通过”统计。依据：[CSS Counter Styles 的 symbols()](https://www.w3.org/TR/css-counter-styles-3/#symbols-function)。
- 原生文字绘制命令保留原文并携带字形可见性掩码。输入间距测试改为检查掩码覆盖的文字，并增加两行实际像素断言，确认被隐藏的行没有墨迹。
- RichTextBox 共享文档通过呈现子树绘制；旧测试仅反射调用控件的 `OnRender`。现在调用公共 `Render`，保留背景范围、顺序和光标断言，并补充嵌入控件实际像素测试。
- 背景简写测试分别检查 origin 与 clip；隐藏测试检查真正的内容抑制与作者值保留，不再要求把孩子的 Opacity 改成 0。

原基线 161 个失败中，160 个原名称用例保留并通过；图片符号这一项按上述规则替换。最终新增 15 个消费者案例，并启用 `JaliumMacNativeGeometryTests=true`，比旧基线多纳入 450 个既有 macOS 原生几何/文本案例。旧基线两个跳过项也在该配置下执行并通过。总数从 19,700 增至 20,165，净增 465；名称对照见 `test-count-reconciliation-final.json`。测试数量变化不代表新增了相同数量的 CSS 功能。

已查看最终全量运行生成的 64 张 PNG 的 9 张汇总图，以及独立复核前后对比和最终窗口截图。包含仍为横排的 T4 展示图，未将其误计为真实竖排通过。视觉核查记录保存在 `acceptance-13-contacts/visual-review.json`；没有由这些图推导整体规范覆盖率或性能结论。

## 原生窗口复验与边界

夹具使用真实 NSWindow、项目主题模板及 Metal 后端；源码与应用保存在本轮证据目录的 `live/` 和 `JaliumCssRepair.app`。普通 `net10.0` 启动器仅用于验证同一托管运行库和原生载荷。

已实际执行并查看截图：

1. 切换 hidden，正文及蓝色容器内文字消失，蓝色容器仍可见；AX 名称消失，作者 `Opacity=1` 保留，恢复后内容重新出现。
2. 通过按钮切换路径，位移在 140px 与 40px 间切换；坐标点击和 Space 激活均有状态回传。
3. 在输入框全选并输入 ASCII 文本，使用 Tab 移动焦点、Space 激活隐藏按钮，检查焦点指示。
4. 在富文本区全选，AX 选中文字为 `A\uFFFCB\n`；绿色嵌入控件及黄色背景范围可见。
5. 将竖排样例切换为 `vertical-rl`，计算值变化但实际仍横排，继续记录为未实现。

最终 build 13 已重启并完成上述窗口操作。新实例读取外置盘 .NET 运行库时曾等待系统访问提示，放行后已实际运行。窗口载入的 `Jalium.UI.Managed.dll` 与全量测试载荷 SHA-256 一致，MVID 为 `714433fb-a71d-4343-a60d-0b54019f76a4`；保存了进程的实际载入文件清单及哈希。最终截图为 `live-output/build13-final.png`、`build13-hidden-path-off.png`、`build13-selected.png`，输入回传为 `CSS REPAIR FINAL 20261027`。复验后关闭了专用测试窗口。

3D 与路径组合的深度保留由矩阵回归测试验证；本次桌面夹具展示二维位移，没有将该窗口操作算作 3D 组合的实机视觉验收。本轮验证普通 `transform` 与路径的组合，没有据此宣称独立 `translate`、`rotate`、`scale` 长属性已贯通。

上述输入来自 CUA 桌面事件，不能代替物理键盘、IME、触控板或 VoiceOver 朗读验收。窗口固定为 900×780 客户区；本轮没有覆盖所有窗口大小、DPI 和平台。Software 离屏结果与 Metal 窗口结果分开记录。

正式 `MacOS.HostSmoke` 仍受环境检查阻断：.NET macOS SDK 26.5.10280 要求 Xcode 26.5，本机为 Xcode 27.1，未关闭检查。旧复核中重建的原生 C++/ObjC++ 载荷通过 51 项 CTest；本轮只改托管代码并复用该载荷，没有再次重编译原生库或着色器。两份 `.metallib` 仍是已核对哈希的缓存产物。

## 仍需团队完成

| 任务 | 已确认的剩余工作 | 完成证据 |
| --- | --- | --- |
| T4 竖排与 Ruby | TextBlock/FlowDocument/RichTextBox 的真实纵向塑形、行列推进、纵中横、逻辑轴；Ruby display 解析、匿名盒及排版 | 像素确实变化；源范围、光标、选区、命中及编辑正确；原生窗口操作 |
| G0/G1 | 锁定模块版本与规范条款，形成注册、计算值、消费者及平台支持矩阵 | 逐项可追踪，不能用属性数量代替规范覆盖率 |
| E2 | 验证现有 C# 发射代码的完整保真，实际编译运行新增规则，验证 Native AOT 与资源诊断 | 生成代码构建/运行与发布产物证据；源码中不存在的旧二进制序列化类不再作为完成依据 |
| N1/N2 | Windows/Linux/其他发布平台后端与设备；六项历史 Linux 原生失败；正式 macOS HostSmoke 环境 | 每项最终命令、原因、结果及对应载荷身份 |
| Q1 | 固定 WPT revision 并移植可映射用例，补性能基线和真实输入/辅助技术验收 | 独立参考、性能数据与设备操作记录 |

完整分工、依赖和验收要求见 [团队实现计划](css-implementation-plan.md)。本轮关闭的是已复现的回归集合，未把上述模块标成全部完成。

## 源码身份与证据

Git HEAD 为 `b85d354029c7c7b366e0fb610c5d49ab7c8c63b4`；当前成果包含大量未提交代码，HEAD 本身不是完整 CSS 基线。本轮在 4,427 个输入的冻结基线上构建，只同步 38 个明确拥有的代码、测试与文档文件，保留实时工作区中的其他并行改动。

审计发现本轮期间四份 README，以及 `RenderTargetDrawingContext.cs`、`RenderTargetDrawingContextPixelSnapTests.cs` 另有并行变化。这些变化已保留，但没有同步到本轮隔离候选，不能把本轮结果表述为实时工作区全部并行成果的集成验收。暂存区、索引内容和 HEAD 均与开始时一致。

证据根目录：`/Volumes/My PSSD/.codex-css-acceptance/css-repair-20261027-f233dc1f`。

| 内容 | 根目录下的位置 |
| --- | --- |
| 最终隔离源码、修复文件清单 | `source/`、`owned-paths.json` |
| 构建命令与日志 | `build-13-command.json`、`build-13.log` |
| 最终全量命令、日志、TRX | `acceptance-13-full/command.json`、`test.log`、`results/result.trx` |
| 定向复验 | `final-13-targeted/summary.json`、`results/result.trx` |
| 独立检查原始数据与 PNG | `probe-13-final/probe-results.json`、同目录 PNG |
| 反向对照 | `negative-13-control/command.json`、`summary.json`、`results/control.trx` |
| 3D 修复前后值 | `motion-3d-before.txt`、`motion-3d-after.txt` |
| 测试计数对照、截图及视觉记录 | `test-count-reconciliation-final.json`、`acceptance-13-contacts/`、`probe-13-before-after.png` |
| 窗口源码、截图、状态与载入身份 | `live/`、`live-output/`、`live-loaded-files-final.json` |
| 源码差异、并行漂移与暂存区审计 | `source-drift-current.json`、`repair-scoped.patch`、`delivery-manifest.json` |

补丁相对本轮开始时的完整脏源码基线生成，不是相对 HEAD 的全部 CSS 补丁；接收方先取得对应完整源码，不能只在干净 HEAD 上套用就宣称内容齐全。这些是本机可复核证据，团队移交时须一并复制所需源码、载荷和测试命令。没有执行提交、推送、清理、重置或自动暂存。
