# CustomControl 实现约定与交付范围

本次范围包含 Jalium.UI 框架、框架示例和独立的 Jalium.UI.Gallery。默认外观使用项目原生的 `.jalxaml` 主题资源；它承担请求中 `Themes/xxx.xaml` 的职责，并继续使用现有编译器、主题和资源加载流程。

## 控件的职责

- 可替换的界面组合、背景、边框、标题、按钮、列表、提示框和浮层放在 `ControlTemplate` / `DataTemplate` 中。
- 控件继承 `Control`、`ContentControl`、`ItemsControl` 或相应成熟基类，用依赖属性公开输入、状态和外观契约；派生控件沿用基类模板契约。
- 交互通过命令、路由事件、可选的 `PART_` 部件和可视状态扩展。缺少可选部件时仍保留数据和行为，不在代码中重新创建默认视觉树。
- `OnApplyTemplate` 重新连接部件；替换模板前解除旧部件的拥有者、绑定、事件和命令引用。临时浮层关闭，文档、系列、地图和播放引擎等持续状态保留在控件上。
- 使用 `SetCurrentValue` 维护部件状态，避免覆盖调用者绑定。派生只读依赖属性供模板绑定布局、可见性和内容。

Gallery 的 129 个页面使用具名控件及 `Themes/Pages` 默认模板。页面行为由 ViewModel、命令和控件承担；初始化、资源、宿主和本地化入口仍保留其启动职责。

## 专用渲染视图

文本排版、视频帧、二维码模块、图表数据、地理热力像素等需要专用绘制的内容由模板创建的视图承担。它们只绘制固有内容，默认外框和界面组合仍属于模板。例如：

| 控件 | 默认模板提供 | 专用视图提供 |
| --- | --- | --- |
| EditControl / RichTextBox / DiffViewer | 外框、编辑浮层、小地图视口等 | 文档、词段高亮、选择和光标 |
| ChartBase / Sparkline | 外框、标题、图例、提示框 | 数据、坐标轴和数据标记 |
| MapView / MiniMap | 外框、缩放按钮、比例尺、归属和视口 | 地图、标记和概览内容 |
| MediaElement | 外框、状态和字幕 | 解码视频帧 |
| QRCode | 外框和裁剪 | 码点、必要的静区像素和 Logo |
| GeographicHeatmap | 外框、渐变图例和文字 | 地理强度图像 |
| DockIndicatorVisual | 方向目标、悬停、原生透明浮层中的预览区域 | 共享命中几何；不再自行绘制界面 |
| ResizeGrip | 绑定前景色的 Path 与状态 | 无代码绘制 |

绘制、布局、图形、图标、装饰器和平台宿主等基础类型继续承担本身的职责。运行时文档解析、虚拟项目容器、窗口/Popup 宿主、拖拽快照、CSS 盒模型和 XAML 加载器仍按数据创建对应对象；这些不是固定页面的默认视觉树。底层绘制原语及已有的兼容渲染路径继续保留。

## 验证与证据

当前实现的源码审计、测试 TRX、构建日志、原生截图及逐阶段审查回执在 `artifacts/custom-control-refactor-20261028`。`framework-managed-semantic-final375.json` 扫描 2,023 个托管源文件；图谱、Roslyn 和文字搜索相互补充，静态审计不单独作为完整性或运行通过的证明。

当前原生证据包括实际视频打开/暂停/恢复、图表提示框的系统鼠标操作、编辑器系统键盘输入、地图缩放/平移，以及 DiffViewer 的系统 Down、小地图拖动和 `AXTextArea` 焦点。截图涵盖宽窄布局、明暗主题、禁用、空内容、缺少部件、模板恢复和卸载后挂载。

物理键盘与系统 IME、口述 VoiceOver、实际 Windows/Linux/GPU 以及各平台 Release/AOT 属于未验收范围。Android 工作负载和 Windows AOT 静态原生库缺失已有独立记录。分批 macOS 契约回归并非整个测试程序集通过；历史未筛选运行及已确认的旧失败记录保留，不并入当前通过计数。

最终计数和原始日志以最终验证回执为准；过程中的失败、零测试匹配和暂态读回均保留，不作为通过证据。

## 最终验收记录

架构迁移完成。可复核入口为 [`final-validation.json`](../artifacts/custom-control-refactor-20261028/final-validation.json) 和 [`final-source-manifest.json`](../artifacts/custom-control-refactor-20261028/final-source-manifest.json)。框架和独立 Gallery 的原有未提交工作保留。

| 检查 | 通过 | 跳过 | 失败 |
| --- | ---: | ---: | ---: |
| 全量模板测试 | 764 | 0 | 0 |
| macOS 相关契约，五批去重 | 3,603 | 9 | 0 |
| Gallery 模块 | 263 | 0 | 0 |
| Gallery 无窗口渲染 | 5 | 0 | 0 |
| 框架示例契约 | 61 | 0 | 0 |

macOS 去重结果包含重跑的 860 项编辑器与 379 项富文本检查，不能与表中其他程序集简单相加。六个实际示例和框架 Gallery 的构建均成功；普通 macOS Gallery 构建、签名、启动及实际页面复验成功。

全量回归后的最终代码清理只删除了 334 行不再调用的停靠绘制代码及两个缓存清理调用。四个仍使用的命中几何方法保持原文；清理后重新编译并通过 11 项模板、84 项停靠/图表检查。19 张原生画面（含实际透明提示窗口）与已逐项查看的前一版本逐字节相同。相关源码差异证明、原始回执和失败记录均保留。

普通 Gallery 实测了桌面和手机预览、明暗切换、坐标轴文字，以及 Tab/Space 切换视口；焦点角色为 `AXRadioButton`。RadioButton 方向键已于 2026-11-05 补齐，当前键盘、选中状态与焦点验收见 [radio-button-keyboard.md](radio-button-keyboard.md)；历史回执保留当时结果。实体输入、系统 IME、口述 VoiceOver、其他平台实际运行及 Release/AOT 的边界仍按上文列明。
