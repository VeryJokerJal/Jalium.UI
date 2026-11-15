# 共享 CSS 竖排布局验证记录（R10，2026-10-28）

状态：本轮列明范围已通过最终代码、原生载荷、像素、实际窗口和源码审计。完整 CSS 与 T4 保持“部分”，本页未列为通过的项目不得视为完成。

## 范围与实现

冻结候选：`artifacts/css-shared-vertical-20261028-388b9b4a/`。本轮在此前独立 TextBlock 字形消费者之上，接入原始 TextBlock 之间及带 CSS 盒的 Run / Span 的共享段落。

- CSS flow 与共享段落使用逻辑 inline / block 坐标计算，再将原始子元素槽位、片段、边框和命中区域转换为物理坐标。支持 `vertical-rl`、`vertical-lr`、`sideways-rl`、`sideways-lr`。
- 区分 `vertical-lr` 的 block-start 与 line-over；共享字形使用每列旋转矩阵，原始行内对象保持物理宽高。
- 首次 Arrange 显式消费此次分配的尺寸，避免读取尚未发布的 `RenderSize`，将 RL 片段放到窗口外。
- `text-orientation: upright` 的共享布局采用 LTR used direction，保留计算方向供正交后代继承。
- 具有不同 writing-mode 的行内来源保持原来的独立文本布局，不再被同一共享段落重排。
- 纵中横组合以一个源单元和一个原生 advance 单元参加共享排版；隐藏空白、大小写展开和组合命中保留原文索引。带边框的原始 Run / Span 投影仍按作者树检查继承序列边界。
- 多列投影继续沿用其既有路径；本轮没有把横排多列投影直接当作竖排片段。

## 负向证据与修复过程

原始候选的 8 项来源几何 / 命中检查全部失败。接通逻辑轴后，测试又检出了投影来源的逻辑 / 物理命中混用，以及首次 Arrange 读取零尺寸的问题。首次尺寸修复后，24 / 24 通过，含 8 个真实窗口 Metal 帧对照。

扩展到混排、直立、侧向、LTR / RTL 后，104 项中 8 项失败，均为 upright + RTL；修复 used direction 后，112 / 112 通过。加入纵中横后，144 项中 16 项失败；进一步加入正交行内检查后，148 项中 20 项失败。原生接口要求组合不能被 advance 单元从内部切开；按整个组合合并 advance 单元后，128 组像素对照通过，另有 5 项空白 / 显示展开的命中检查仍失败，随后修复共享命中的组合边界。

实际窗口检查发现，验证夹具在 `Application.Run` 之前手动调用布局，保留了未塑形的近似片段；移除此过早调用后，原生共享字形恢复。主题另有 TextBlock 局部字体和颜色，夹具显式声明来源字体和前景以确保可读与参考一致；本轮未修改主题属性与 CSS 继承的既有优先级。多列参考也改为逐列绘制合法的原生源范围，避免使用超出单列范围的绘制调用。上述夹具 / 参考问题不计作产品修复或产品通过数。

扩展的 RTL 多列像素对照仍有 8 项失败。独立原生断言确认，RTL 软换行后第二字起始 caret 返回 16，字格应为 32，Software / Metal 均失败。原生亲和边界判断增加竖排浮点余量，防止把舍入后的半字格位置当作接近正确边界的插入点；横排判断保持原样。重新构建原生库后，独立边界 2 / 2、新增共享 / 原生用例 222 / 222 通过。中间尝试改用选择矩形来测量字格没有消除这 8 项差异，已经撤回该尝试。

这些失败记录保存在各 `managed-*/summary.json`、TRX、测试日志及 PNG 中，没有以跳过或放宽像素阈值处理。

## 验证方法

### 托管与原生像素

`CssSharedVerticalTests` 通过实际 CSS、原始对象、Measure / Arrange、绘制、文档几何和命中 API 执行。参考侧使用单个原生段落与字面的物理矩阵，不经过共享分配器或写轴帮助类。逐帧严格比较全部 BGRA 字节，并独立保存 actual / reference PNG。

Software 使用真实离屏后端；Metal 的无窗口目标不提供这项读回，因此通过独立可执行程序，在实际 OS 主线程创建原生窗口表面并完成 Metal readback。每轮运行均校验子进程使用的框架及测试程序集与测试输出 SHA-256 相同，不用旧载荷替代新代码。实际目标显式设置并断言 RenderingEngine，Metal 同时覆盖 Impeller 与 Vello；Software／Impeller、Metal／Impeller、Metal／Vello 各 96 对，共 288 对、576 张 PNG。

新增用例覆盖模式 / 方向 / 字形方向组合、纵中横、来源拼接、首次布局和实际分配尺寸变化、非对称容器 padding / border、原始对象尺寸、正交文本、跨列原文命中、空白组合、长度变化的显示映射、作者树边界及样式恢复。

### 实际窗口

专用 Metal 验证窗口保留原始 Run / Span、跨控件来源与单段参考，使用项目既有的中文验证界面风格。最终交互记录为 62 条事件、61 份原文／几何快照、16 张已查看截图。覆盖五种书写模式、三种文字方向、LTR／RTL、长短列、跨列拖选（1+11，`地玄黄ABCאב宇宙洪`）、Command+A、原始 TextBlock 独立选区、Tab／空格及可见焦点、860×590 窄窗、1040×660 初始尺寸、1920×943 系统 zoom 与恢复。

全部来源对象身份、原文选区和最终几何核对通过。初始与恢复的文字及行内装饰区域（25,190–1015,475）像素相同；整窗截图中系统指针光晕、原生标题激活和按钮焦点会变化，没有将其计作整窗像素相同。测试应用已关闭。结果见 `live-ui-summary.json` 与 `live-output/events.jsonl`。这只证明工具注入的键盘路径，物理键盘、IME、朗读 VoiceOver 和手拖窗口边框未计通过。

## 最终计数

| 验证集合 | 最终结果 |
| --- | --- |
| 完整 macOS 托管聚合回归 | 20,964 / 20,964；失败、跳过、超时均为 0；732.569 秒 |
| 新增共享竖排及原生边界用例 | 318 / 318；包含在完整聚合结果中，不重复相加 |
| 原生文本／绘制选择集 | 65 / 65；失败、跳过均为 0；不包含 platform / media 选择集 |
| 严格像素对照 | 288 对、576 张 PNG；三个后端／引擎组合各 96 对；所有 PNG 解码像素相同且非空 |
| 最终实际 Metal 窗口 | 62 条事件、61 份原文／几何快照、16 张已查看截图；已验证交互及恢复 |

完整结果见 `managed-final-full/summary.json` 与 TRX，定向结果见 `managed-final-focused/summary.json`，原生结果见 `native-final/summary.json`，像素结果见 `final-shared-pixel-identity.json`。先前 1,935 项相关回归属于中间候选；最终完整聚合回归已覆盖这些既有用例。两次因已发现的现场／载荷问题而停止的全量运行保留在 `before-wrap/` 与 `before-shader-completion/`，不计作完成或超时豁免。

## 源码与载荷边界

本轮使用冻结的 4,547 项原始输入和显式登记的修改路径。托管代码在候选独立构建目录编译。初始负向阶段核对原生源码与 R9 相同后复用其已验证载荷，记录见 `native-reuse-provenance.json`。随后新增 RTL 软换行的原生光标修复，最终载荷从 R10 冻结源码重新构建，完成标记由真实原生构建生成；最终输入、构建命令和 8 个 dylib / 2 个 metallib 的 SHA-256 见 `native-final-input-manifest.json` 与 `native-final-provenance.json`。不得把早期复用载荷当作最终修复的凭据。

CMake 输出默认没有带上 metallib，导致 Vello 请求回退到 Metal／Impeller，原生 65 项中的 7 项失败；新引擎断言也以“期望 Vello、实际 Impeller”明确失败。核对 770 项原生输入与 R9 相同（仅非 shader 的 caret 源码有变更）后，补入两份原已验证且哈希一致的 metallib；65／65 原生回归及 318／318 新增用例随后通过，来源见 `native-shader-provenance.json`。这是 Metal 内渲染引擎的回退，没有把 Software 结果计作 Metal。

最终源码范围为 20 个代码／测试文件和 2 份文档。冻结输入与编译候选核对一致，用户 HEAD、索引和暂存区保持不变；其他并行修改与新增文件记录在 `external-workspace-drift.json`，未合入本轮候选。`source-audit.json`、`final-tested-code-manifest.json` 和 `final-owned-manifest.json` 提供文件级 SHA-256 与边界；`implementation-delta.patch` 在独立临时目录检查、应用并重现全部 22 个文件，证明见 `patch-verification.json`。测试应用的 8 个框架程序集与已测试版本一致，签名后的 8 个原生库保留全部文件映射段及 UUID 身份，见 `live-packaging.json` 和 `live-native-identity.json`。本轮没有提交、暂存或推送用户工作。

界面按适用的[界面规范](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md)核对中文层级、正常及选区对比度、语义按钮、键盘焦点和桌面视口；正文／次级文字／选区对比度分别为 13.175、5.859、6.775，记录见 `design-review.json`。原生 AX 与注入事件只证明所列路径，未替代朗读或物理设备验收。

## 仍未完成的范围

- 任意 Run / Span 独立覆写方向和更广的字体 / 基线 / 变换组合。
- 竖排多列、分页、FlowDocument、RichTextBox 与编辑器的完整布局、命中和交互。
- 不同原始 TextBlock 之间的完整纵中横继承前后文、字体边界和所有间距 / 断行组合。
- 竖排浮动、首字下沉、截断标记、first-line、渐变 / 阴影 / mask 等效果的组合矩阵。
- Ruby 的实际解析、布局、绘制及命中。
- 主题局部 Setter 与 CSS 继承优先级的更广互操作，以及应用主动在原生上下文就绪前手动布局时的缓存升级。本轮窗口使用正常启动顺序，并显式声明字体 / 前景，没有改变这两项既有行为。
- Windows / Linux、其他原生后端、物理键盘、IME、VoiceOver 与设备验收。

本轮的通过数仅证明所列消费者、macOS 后端及用例集合，不构成整个 T4 或完整 CSS 的完成声明。

规范依据：[CSS Writing Modes Level 4](https://www.w3.org/TR/css-writing-modes-4/)。
