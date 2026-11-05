当前架构约定和最新验收入口见 [custom-control-architecture.md](custom-control-architecture.md)。以下为过程记录，含已解决的中间问题与历史检查点。

# CustomControl 重构与验收

范围是 **Jalium.UI 框架与独立的 Jalium.UI.Gallery 全部 UI**。页面文件迁入 Themes 只是其中一个步骤；剩余程序化复合界面、生命周期和实际运行验收完成前，不能认为整个目标完成。

## 实现契约

- 控件继承 `Control`、`ContentControl`、`ItemsControl`、`Page`、`Window` 或适合的专业控件基类。
- 默认外观位于 `Themes/**/*.jalxaml`。沿用项目原生扩展名，由现有编译器生成模板工厂。
- 可配置数据、选中值、状态和命令通过依赖属性公开。只读派生状态使用只读依赖属性；编辑使用 `SetCurrentValue` 保留绑定。
- `[TemplatePart]` 与 `[TemplateVisualState]` 声明实际扩展契约。缺少可选部件时正常工作。
- `OnApplyTemplate` 连接部件；`OnTemplateChanging` 在旧树拆除前取消事件和动画。重复套用、置空、换主题以及多实例不共享视觉节点或事件订阅。
- 复合外观由模板组成，集合项由 `DataTemplate`/容器生成。禁止把原 code-behind 中的布局搬进控件构造函数，或从另一个控件中抽取、重新挂载模板树。
- 专用绘制控件、布局面板、文本呈现和平台宿主继续保留相应渲染、布局与系统实现职责；它们的可替换装饰和复合界面仍应使用模板。
- 主题、中文内容、MVVM 命令、虚拟化和平台功能保持一致。默认模板与消费者 `Style` 分层，不要求应用样式重复声明默认模板。

## 验收清单

1. 源码：无页面 `.jalxaml.cs`/`InitializeComponent` 视图包装；默认模板可定位；公开属性、部件和状态与实现一致。
2. 编译：普通工具链与隔离构建均可构建；编译模板与运行时加载语义一致；资源相对 URI、附件属性和名字作用域正确。
3. 生命周期：模板替换、置空、缺失部件、反复加载/卸载、多实例、动态主题和绑定持续有效。
4. 行为：原有正常/空/禁用/错误/加载状态，导航、弹层、选择、编辑、取消、关闭、焦点恢复和键盘操作保持。
5. 渲染：实际运行检查主要页面和全目录；检查宽/窄视口、浅/深色、内容溢出、标题层级、对齐、滚动和交互反馈。渲染器返回成功不能替代查看图片。
6. 可访问性：名称、角色、值与范围、选中/展开状态、焦点顺序和键盘路径。自动化契约与实际系统辅助功能验收分别记录。
7. 交付：列明改动、通过/失败/跳过数量、图像证据、平台和未验证事项。不得用无窗口的软件渲染结果宣称原生窗口或 VoiceOver 已验收。

## 当前检查点（2026-11-05）

整体目标继续进行。Ribbon 的默认组合已迁入 `Themes/Controls/Ribbon.jalxaml`，包括标题、标签、单组选中内容、分组、快速访问工具栏、菜单、拆分按钮和库。保留原分组与命令对象，模板替换释放旧部件；紧凑宽度与工具栏位置通过只读依赖属性进入模板。标签控件在横向滚动宿主之前处理方向键、Home/End、禁用标签跳过及从右到左导航，分组内的编辑键不参与切换。

同步修复了运行时与编译字典的 `Source`/延迟样式环境资源作用域、资源递归检查误拦截不同键，以及附加属性的 `TemplateBinding` 被默认字面量覆盖的问题。新增定向资源、绑定与模板生命周期检查。整套模板检查 `all-templates500` 为 717/717；之后的标题按钮导航与绑定检查 `ribbon-keys501` 为 16/16，零字号/兼容接口检查 `zero-font452` 为 62/62。

macOS 校准选择覆盖 2546 个测试用例。单进程 `current-macos453` 留有 19 项跨测试集合主题资源线程归属失败；使用相同冻结二进制分进程复验 `460/461/462`，唯一测试 ID 的并集精确覆盖全部 2546 项：通过 2537、跳过 9、失败 0。这是该选择集的分批结果，不是整个 macOS 测试程序集通过。早期 `454` 至 `459` 是快照运行器的架构/参数启动失败，保留原始日志。

Native350 的 18 帧与已直接审查的 Native349 逐字节一致；另在系统窗口实际注入鼠标和按键，确认标题选择、方向键切换、跳过禁用标签及执行命令。普通 Gallery503 使用原应用宿主构建，无 QA 源码，已查看桌面/手机预览、真实命令结果、浅深主题及键盘切换的五张系统截图。真实宿主中的页面说明换行正常。物理硬件输入、系统 IME、口述 VoiceOver 和其他操作系统仍未据此验收。

新盘点覆盖框架 226 个唯一控件类型与 77 个渲染方法；静态模板候选只用于定位，不能代替实际资源解析。不能把复杂编辑器和地图整体归为绘制原语而跳过可替换外观。Ribbon 阶段证据入口：`artifacts/custom-control-refactor-20261028/ribbon-phase-current.json`。

RichTextBox 的背景、边框和圆角现由 TextControls 默认模板提供；公开 `RichTextBoxView.Owner`、可选 `PART_TextView` 和通用/焦点/编辑状态，文档排版、选择、IME、撤销与自动化状态仍由原控件维护。CSS 文档的私有呈现层挂入模板部件，随模板变换；换模板释放旧部件，不重复呈现旧文档。显式空模板的底层文本引擎、旧 `PART_ContentHost` 和 CSS 切换均保留。Gallery 四处固定 500 宽度改为最大宽度，窄视口可以收缩。

`rich-host511` 的 11 项模板检查和 `rich-contract468` 的 379 项文档、几何、编辑与撤销检查通过。Native354 的 13 帧全部已审查（4 帧直接查看、9 帧与直接审查过的 Native351 完全一致），包含实际 Gallery 页面、CSS/普通文档、替换/恢复、只读、禁用、主题和加载周期。另在系统窗口实际输入文本并点击文档内嵌按钮，确认编辑和命令结果，焦点角色分别为 AXTextArea/AXButton。物理键盘、系统 IME、口述 VoiceOver 与其他操作系统不在该阶段验收范围。证据：`artifacts/custom-control-refactor-20261028/rich-text-phase-current.json`。

EditControl 的默认外框、滚动条、补全列表、悬浮/签名提示、折叠/作用域预览及小地图弹层已转为模板部件。`EditControlView` 与 `EditorSyntaxPreviewView` 只负责编辑器文字和语法绘制；`EditorCompletionList` 使用标准虚拟化列表，`EditorTextHint` 提供文字、签名片段、语法预览与计数状态。重建模板会释放旧部件和命令，文档、选择、滚动及语言服务状态仍由编辑器维护。真实路由按键发现并修复了接受补全后继续插入换行的问题。

本阶段同步修复依赖属性值层提升时，构造函数默认值覆盖已求值模板绑定的问题。该修复的冻结模板检查 `all-templates518` 为 731/731；之后 `editor-previews528` 为 26/26。`editor-previews476` 的 860 项编辑器、字素、撤销和 Writing Tools 检查通过；强调与文字间距夹具显式提供 PasswordBox 文本模板部件后 `editor-emphasis475` 为 81/81。Native358 截图发现补全行高继承通用控件最小高度，且折叠提示状态仍在绘制阶段变化；均已修正。Native361 的 25 帧全部已审查（5 帧直接查看、20 帧与已审查的 Native360 完全一致）；系统鼠标点击补全项和 Native360 的系统 Down/Enter 均正确接受补全，最终文档没有多余换行。追加行高检查及地图首版检查 `maps-editor533` 为 13/13。该阶段没有连接真实语言服务器，也未据此验收物理输入、系统 IME 或口述 VoiceOver。证据入口：`artifacts/custom-control-refactor-20261028/editor-phase-current.json`。

MapView 的外框、缩放按钮、比例尺和署名，以及 MiniMap 的外框与视口，已迁入 Maps 默认模板；两种表面只保留地图/概览内容绘制。`map-pixels483` 的 50 项投影、模型与像素复用检查、`maps-template534` 的 13 项模板检查均通过。Native362 的 10 张原生画面全部直接查看；系统鼠标缩放、空格激活按钮和拖动平移都有前后状态与截图。该窗口使用本地占位瓦片，未以此验证网络地图服务。证据入口：`artifacts/custom-control-refactor-20261028/map-phase-current.json`。

整体目标继续进行：补充审查定位到 MediaElement 的音频/加载占位与字幕框、ChartBase 的标题/内置图例，以及 QRCode 的装饰框仍需模板化。MediaElement 首版已转为 Control 与 MediaVideoSurface，`media-clip538` 的 17 项模板检查、`media-contract485` 的 29 项播放生命周期和 CSS 视频几何检查通过，原生视频复验进行中。其后继续完成剩余渲染/工厂审查，并重跑冻结源码的整体构建与验收。

## 框架样例控件契约（2026-11-04）

样例入口保留原生窗口配置、应用生命周期与服务注册；默认控件树由各项目的 `Themes/*.jalxaml` 提供。AOT、无边框、透明背景、Android、Linux、Hosting 与百万行样例使用源生成的资源字典构建器；框架 Gallery 的 17 组目录使用其独立主题资源。原生扩展名仍为 `.jalxaml`。

| 项目 | 主要复用控件 | 扩展契约 |
| --- | --- | --- |
| 框架 Gallery | `GallerySampleSurface`、Section/Card 及各类 Demo | 数据和选择依赖属性、内容/项目模板、可选部件、Markdown 播放状态；密码保持非依赖属性 |
| AotWindowDemo | `AotCounterSurface` | 文案、只读计数/状态、`CountChanged`、可选计数按钮；主题构建器不调用运行时 XAML 解析 |
| BorderlessDemo | `BorderlessDemoSurface` | `OwnerWindow`、标题/描述、只读状态、窗口操作方法、`TitleBar`/操作按钮部件、窗口状态 |
| TransparentBackdropDemo | `TransparentBackdropSurface` | `OwnerWindow`、`SelectedEffect`、`IsTinted`、`TintBrush`、只读状态、选择器/切换按钮部件 |
| AndroidDemo | `KeyboardDemoSurface`、`KeyboardDemoField` | 九个可绑定文本值、输入类型/回车提示、非依赖属性密码、`Submitted`、可选密码/提交部件 |
| LinuxDemo | `LinuxDemoSurface` | 文本、后端选择、开关、进度、列表选择、独立单选组、只读反馈、可选桌面操作部件 |
| HostingDemo | `HostingDemoShell`、`HostingDemoPage`、`HostingPageLayout` | `SelectedPageType`、可选 `PageFactory`、只读当前页面/指标/错误；Frame/Nav 部件；六个派生 Page 的默认模板 |
| MillionScroll | `MillionScrollSurface`、`MillionScrollListBox`、`MillionScrollMetric` | 行数申请/应用、跳转、速度、选中值、只读虚拟化指标、可选操作部件；基准配置与完成事件 |
| DesktopDemo | `HoverProbeSurface`、`RotationReproSurface`、`EffectReproSurface`、`PathPerfSurface`、`VelloTestSurface`、`ImagePerfSurface`、`ScrollStressSurface`、`NotificationDemoSurface` | 各场景的数据、动画时间/播放、共享图源/分类、滚动诊断、通知命令与请求事件；默认树均位于独立主题字典 |

Hosting 页面绑定真实 ViewModel，动作使用框架 `RelayCommand`；指标和时钟订阅支持重复加载/卸载。外壳替换模板保留当前 Page 与 ViewModel 的身份。Linux 和百万行选择器在旧模板清除 ItemsSource 前解除向外的选择绑定，避免拆树产生的空选择回写到控件。

百万行控件的 `Configure` 保留原有运行参数与 schemaVersion 3 报告。`ShutdownOnBenchmarkCompletion` 在复用控件上默认为 `false`，完成后通过 `BenchmarkCompleted`、`BenchmarkOutputPath`、`BenchmarkError` 通知宿主；原样例 Window 显式开启自动退出以保留 CLI 行为。数据行仍由按索引生成的 `IList` 提供，默认模板实例化可见行。

当前样例契约项目为 `tests/Jalium.UI.Sample.Tests`，编译实际样例源码和主题。`sample374` 为 61 通过、0 失败、0 跳过。Native324 已在实际 Metal 窗口验证百万行中点/底部跳转、自动滚动、换模板保留位置与选择、卸载/重载；3 秒 schemaVersion 3 报告记录 171 帧、171 个样本、52 个已实现容器。这是该次实际运行的记录，不作为其他机器或平台的性能保证。原项目的最终源码增量构建另行记录。

当前已查看的 macOS Metal 客户区证据：Native315 目录 42 帧、Toast 13 帧、计数控件 8 帧；Native316 无边框/透明背景 21 帧；Native318 Linux/键盘表单 18 帧；Native319 Hosting 16 帧。完全相同的 PNG 仅复用已逐帧查看的图像，匹配清单与差异图复核记录在对应 `native-proof/*/visual-review.json`。这些证据不等于物理输入、IME、VoiceOver、Windows DWM 或实际 Android/Linux 平台验收。

当前环境的 Android 原项目构建在工作负载检查处报 `NETSDK1147`（缺少 `android`）；已通过的是可移植源码/主题契约与 macOS 运行。Windows AOT 静态库也尚不可用；不能据此声称 Android APK、Windows 单文件 NativeAOT 或 Windows GPU 路径已通过。

## 桌面样例与框架收尾审查（2026-11-04）

桌面样例的八个旧界面文件现在只保留窗口和应用宿主。各默认树由 `samples/Jalium.UI.DesktopDemo/Themes/*.jalxaml` 提供，生成字典直接构建模板。通知样例保留原始简单、带操作、高优先级、静默、图标、清除和应用内 Toast 操作；`ActionCommand` 与 `ActionRequested` 支持替换执行。示例路由增加 `notifications`、`imageperf`、`scrollstress` 和 `rotation`，原有默认入口仍是效果复现页。

滚动压力控件保留 240 张卡片、12 个独立图源和原有非虚拟化负载；诊断周期与累计计数口径沿用原实现。卡片标题与说明由模板排版。计时器仅在已加载且有滚动部件时运行，旧部件不再产生新周期。不要把这个压力负载与百万行虚拟列表混为一项基准。

真实渲染检查发现并修复了 Path 的字符串到几何绑定丢失、最大缩放遮挡指标栏，以及 `TransformGroup` 的隐式子集合未加载。后者通过补上 `Children` 内容属性声明修复，运行时与编译 XAML 的矩阵和多实例集合独立性均有契约检查。Native332/333 的矢量矩阵恢复了与原始 C# 场景一致的缩放/旋转齿轮；实际使用 Metal/Impeller，尚未验证 Vello 引擎。

框架 Terminal 的 `TerminalView` 改由默认模板创建，通过 `Owner` 依赖属性连接字符缓冲。换模板解除旧 Owner/ScrollOwner，恢复历史滚动位置，保留缓冲区；缺失部件保持空外观。输入处理器在卸载时解除，重新加载前去重。导航分组标题、分隔线和停靠分隔条也已移除手工绘制外观，使用主题模板；标题支持 `ContentTemplate`，分隔条提供状态画刷与只读拖动状态。

InfoBar 的图标、标题、正文、调用方内容、操作按钮与关闭按钮全部迁入 `Containers.jalxaml`。关闭使用标准按钮并保持 `IsOpen` 绑定；关闭时的可见性强制收起，重新打开后恢复调用方原值。旧部件事件及内容引用在替换前释放。Native333 的 12 张图与已逐张查看的 Native332 完全相同，覆盖四级别、宽窄、浅深色、空文本、零字号和换模板；原生输入及 VoiceOver 未验收。

DockItem 的标签、关闭按钮与重排高亮已迁入默认模板；文档仍由 DockTabPanel 的选中内容宿主管理。DockLayout 的外框和目标高亮也已模板化。浮动恢复路径改为穿过模板祖先查找实际内容宿主，避免把 Border/ContentPresenter 当成根容器。Native334 已核对四边布局、重排捕获、关闭与换模板；Native338 核对了正确主题下的 Gallery 停靠页；Native339 创建实际浮动窗口，移除原宿主内容后关闭浮窗，确认同一编辑器恢复到原位置。

SwipeControl 的动作区域改用标准按钮，支持四方向、展开/执行阈值、Alt+方向键与 Escape、命令可用性和更换模板。非可视 SwipeItem 使用现有绑定 mentor 继承行数据上下文，并在卸载和移除后解除。为使声明的动作集合实际加载，运行时 XAML 与源生成器补齐 IList 隐式子项路径；ContentProperty 和 IAddChild 仍优先。Native338 的 16 帧逐张查看，真实 Gallery 的 Flag 命令更新 ViewModel，旧按钮无法调用，命令观察在卸载后归零。

ChartLegend 和 ChartTooltip 的内容由 Charts.jalxaml 提供；图例项支持属性通知，文字、画刷、可见性和集合可动态更新。Native339 的 13 帧逐张查看，包含浅深色、水平/竖直、可选内容隐藏和替换模板。

PasswordBox 的外框与显示按钮迁入 TextControls.jalxaml，由模板创建 PasswordBoxView 绘制掩码、选区、光标和组合文本；密码仍为 CLR 属性。DevTools 属性区改用 DevToolsDetailsList，其布局由默认模板和 ItemsPanel 提供。Native342 的 15 帧完成查看：7 帧直接查看，8 帧与已直接查看的 Native341 完全相同，覆盖真实 Gallery 密码页、按住显示/释放掩码、选区、组合文本提交、卸载重载和真实 Inspector 的宽窄布局与换模板。输入使用路由事件/组合回调；系统 IME、物理键盘和 VoiceOver 未验收。

完整模板 all-templates470 为 706 通过；当前样例 current-samples471 为 61 通过。扩大 macOS 范围 calibrated-mac440 为 2473 通过、15 失败、9 跳过；失败包括旧 StackPanel 测试假设和密码视图迁移后的显示状态/绘制宿主契约，正在修正，不作为通过记录。Ribbon 系列缺少默认主题的遗漏仍在处理，不能宣称全框架收尾完成。

当前阶段证据：完整模板 `terminal-chrome-template452` 为 681 通过；这是加入后续 TransformGroup、InfoBar、DockItem 改动前的整套记录。后续专项 `transformgroup-template453` 8 通过、`infobar-template454` 6 通过、`infobar-mac429` 11 通过、`dockitem-template456` 19 通过，不相加为独立总数。后续完整回归仍待运行。

额外的无过滤 macOS 大套件 `numberbox-mac419` 在停滞后中止，部分结果为 12,423 通过、684 失败、2 跳过，共 13,109 条已报告结果；它不代表完整套件通过或完成。四条日期占位文本失败在隔离的旧 Managed 程序集对照中也出现，其余失败没有完整建立基线。此前已校准范围 `mac421` 的 2,327 通过、9 跳过与此大套件是不同范围。完整原生平台、系统辅助功能与未完成的全套验收继续分别记录。

## 当前迁移批次

| 范围 | 当前状态 | 尚需完成 |
| --- | --- | --- |
| 模板基础设施 | 已补充外部模板清理钩子、Page 命名/渲染、Window 模板内容及自适应尺寸、库默认主题、独立可视状态值层、状态和过渡清理、编译附件属性元素与相对资源 URI | 更广回归、普通工具链、Release/AOT 与平台验收 |
| Gallery 主窗口 | 已移除约 900 行 ShellController；视口、导航、标题、搜索宽度、滚动归属和材质状态由 GalleryWindow 依赖属性与模板驱动；缺失可选部件与换模板有回归 | 已实测默认 1500×920 桌面、独立 390×844 原生移动窗口、Logo、浅深色导航、Tab 焦点和空格激活；设计变量/工作室/详情页 Command+K 返回目录并聚焦；物理边缘拖动在 CUA 中未触发尺寸改变，live-resize 仍待确认 |
| Gallery 页面 | 129 个页面与主窗口外观迁入 Themes；无页面 code-behind；124 条路由浅/深色离屏渲染均无异常，两组各 8 张联系表均已查看 | 交互覆盖；仍有布局/示例附加行为需审查并迁入合适的控件；平台回退与初始动画空态不计为实际功能验收；Ribbon 等可疑初始布局继续复核 |
| Gallery 复用组件 | 124 个页面标题、698 个区块、712 个演示画布及 117 个功能项提取成控件；检查器数字/颜色编辑器已模板化 | 宽窄视口、主题、焦点及替换模板的完整验收 |
| Gallery 实时预览 | 129 个独立预览模板与 9 类精选预览；不再拆取页面视觉树 | 完整目录和样式作用域验证 |
| Gallery 滚动示例 | 行为迁入带可选部件的页面控件 | 实际滚动/回弹与平台验证 |
| Gallery 页面缓存 | 导航宿主继承 ItemsControl，页面容器与默认外观迁入 GalleryNavigation；通过可视状态切换缓存页面 | 新实现的原生导航、深色和宽窄视口复核 |
| 框架诊断 HUD | 默认模板迁入 `Themes/Controls/Diagnostics.jalxaml`，数据与色彩状态由依赖属性和 VSM 驱动 | 实际 HUD 渲染和刷新验收 |
| 框架消息框 | 默认模板迁入 MessageBox.jalxaml，响应按钮、默认/取消及关闭行为具有独立部件和状态契约；macOS 实际确认 OK、默认 No、Escape Cancel 与结果回传 | 浅色、长文本、窗口关闭否决与其他平台完整验收 |
| 框架时间选择器 | 输入区、弹层时分列表及确认/取消按钮迁入 TimePicker.jalxaml；保留待选值、双向绑定、键盘与 CSS 占位文字；消除程序化视觉布局；macOS 已验证初始选中项滚动、Tab 切列、15 分钟步进、回车确认、Escape 取消和焦点恢复 | 触摸、窗口边缘弹层、所有模板变体及其他平台验收 |
| 框架字体对话框 | 已提取公开 FontDialogWindow 与 FontDialog.jalxaml；字体/字号/颜色/效果有双向依赖属性，预览底色按文字对比度选择；无效输入状态、取消和关闭否决具有回归用例 | macOS 实际完成非法字号/颜色、白底黑字与黑底白字、效果预览、确认、取消、字体族编辑、浅深色和一次 Tab 到字号；其余平台及 VoiceOver 待验收 |
| 框架导航控件 | 已公开 PaneWidth 和可选部件/PaneStates；不再提升默认模板为本地值；重套模板释放标题、页脚和菜单；自动化报告选择并保留展开绑定；macOS 辅助功能树和 Tab 已确认不包含隐藏侧栏 | 其余显示模式、嵌套菜单及其他平台/VoiceOver 验收 |
| 集合容器生命周期 | 完整丢弃生成容器时释放 Content 和旧数据上下文，修复模板化缓存页面无法重新挂载 | 更广选择、虚拟化与反复换模板回归 |
| 框架 JSON 树 | 已提取公开 JsonTreeNodePresenter；显示和编辑器迁入 JsonTreeViewer.jalxaml；NodeTemplate/NodeContainerStyle 可替换；搜索、选择和 JsonText 双向绑定，提交保持节点身份与选择；工具栏、严格类型校验、状态消息和换模板恢复编辑具有回归；树缩进与选择画刷通过属性和模板扩展 | macOS 已实际验证 F2、非法数字、提交、取消、焦点恢复、JSON 回写、复制路径、浅深色与解析错误；390 像素已验证上下布局、工具栏换行、鼠标聚焦、键盘输入、错误恢复、F2 回写及展开/折叠；其他平台和 VoiceOver 待验收 |
| 框架属性表 | 已提取公开 PropertyGridRow / PropertyGridEditor、类别模型及六类编辑模板；PropertyGrid 只管理数据和部件，不再创建备用视觉树；支持外部通知、只读、校验、嵌套结构体回写、筛选/排序/选择绑定与换模板保留输入 | 149 项模板、218 项属性表/ComboBox 等回归通过；macOS 已实测连续输入、空格、取消、Tab、复选框、筛选/空态/排序、浅深色与 390 像素上下布局、多实例同步；错误文字对比度与枚举下拉焦点已修复并复测；其他平台与 VoiceOver 待验收 |
| 框架工具栏 | CommandBar 默认外观迁入独立主题，可选部件、命令集合与派生状态 DP、VSM、键盘和换模板契约已实现；AppBar 模板使用样式值、支持 Label 自动化名称和图标颜色继承 | macOS 构建 26/27 已实测命令执行、方向键、焦点恢复、IsSticky、标签布局和 Minimal/Hidden；深色图标缓存已修复；构建 30 已完成桌面与 390 宽度的浅深色、整行命令、键盘及保持打开复核；其他平台、Release/AOT 与 VoiceOver 待验收 |
| 框架导航窗口 | NavigationWindow 的导航条与内容宿主迁入主题模板；命令、历史和 CurrentSource 由 DP 连接；没有内部 Frame 或手工界面树，Gallery 演示也使用模板页面 | 构建 32/33 已实测页面历史、Alt 方向键、隐藏/恢复、Tab/回车、关闭/重开、浅深色及实际 600→390 拖动；其他平台、Release/AOT 和 VoiceOver 待验收 |
| 网格与树形网格 | 拖拽指示器、行头、树形单元格、缩进和展开器迁入 Themes；行保留、键盘编辑、列宽及动态资源已补齐；构建 51/52 原生复验见下节 | 已实测边界 Tab 和 390px 编辑；列元素默认样式、网格线、横向滚轮及树形表格自动化语义继续审查 |
| 其余框架复合 UI | 已完成程序化视觉创建位置盘点 | 网格内部行/列组合、拖拽表面、DevTools 等逐项迁移；框架默认模板单 Border 静态清单已为零 |

此表是进行中的范围台账，不是完成声明。测试结果和渲染证据保存在 `artifacts/custom-control-refactor-20261028/`；初始源码快照与 Git 状态也保存在该目录，以便区分本次迁移与工作区已有并行修改。

## 已取得的验证证据

- Gallery 契约回归：`gallery-suite-39.trx`，188 通过、0 失败、0 跳过，包含模板化缓存、容器换模板、数字控件聚焦、水平色板键盘与两类指针事件去重；新增 JSON 与属性表示例宽窄布局、值编辑及 ViewModel 回写、方向改变后的延迟视口刷新与关闭清理。
- 框架模板回归：`template-suite-53.trx`，149 通过、0 失败、0 跳过，包含弹层独立键盘路由、生成容器完整清理，以及字体窗口绑定、无效输入、待选值、模板替换、关闭否决、字体 Tab，以及自定义祖先类型的编译/运行时命名空间绑定、派生模板与窗口默认尺寸。新增 JSON 节点模板、严格类型校验、模板退役/重建、独立行控件、搜索/选择/JSON 回写绑定、缩进、选择对比度和窄屏工具栏回归，以及属性表编辑、生命周期、输入、对比度和行样式扩展。
- PropertyGrid 原有 API、PropertyItem 和模板部件清理回归：`property-grid-legacy-2.trx`，26 通过、0 失败、0 跳过。新增模板用例覆盖六类编辑器、非法值/抛异常访问器/强制修正、可空值、源通知、只读、分类折叠与筛选、旧编辑器退役、整体/行/编辑器模板替换、嵌套结构体和循环对象。原生输入问题另增两项键盘回归。
- JSON 原有模型、解析器、层级模板、树选择和导航自动化回归：`json-tree-lifecycle-2.trx`，71 通过、0 失败、0 跳过。
- 时间选择器、CSS 占位文字、集合/容器、导航选择和消息框回归：`control-lifecycle-suite-4.trx`，119 通过、0 失败、0 跳过。
- 更广的集合、选择、虚拟化、状态及属性优先级回归：`selection-lifecycle-suite-5.trx`，744 通过、0 失败、0 跳过。与其他专项存在重复用例，不合计为独立总数。
- 字体服务与对话框专项：`dialog-lifecycle-suite-5.trx`，107 通过、0 失败、0 跳过，包含原有 FontDialog 模型和工厂测试。
- 离屏 CLI 回归：`headless-suite-8.trx`，5 通过、0 失败。离屏引擎为 Software/Vello，使用本任务独立构建且通过导出检查的 Release 原生库，不代表原生 GPU 或 Windows/Linux 验收。
- 实际 macOS 窗口已经重建重启并操作、查看截图：窄窗口和最大化布局、首页卡片单击打开、侧栏导航、Command+K 搜索、计数按钮鼠标和空格操作、Escape 返回、主题工作室数值更新与重置、数字控件鼠标聚焦后方向键增量、水平色板左右键及 RGB 同步、新页面缓存的跨页面导航与旧选中高亮清除。
- TimePicker 原生绑定示例实际显示 `Selected: 21:05`；模板化消息框实际记录 `Result: OK`、`Result: No`、`Result: Cancel`，并恢复所有者焦点。已查看浅/深色时间弹层和深色消息框截图；这不等于实际 VoiceOver 口述验收。
- ComboBox 与导航专项：`combo-navigation-suite-1.trx`，193 通过、0 失败、0 跳过。扩大集合/样式回归的第 3 次运行持续占满一个 CPU 后已停止，仅保存采样，不报告通过；已拆分取得 744 项集合/样式等用例通过。CSS 组合回归在 390 项通过后宿主退出，D2 展示渲染用例单独运行通过；组合运行仍记为中止，不宣称全部通过。
- 字体窗口实际显示过 `Current: Menlo, 20pt`；重新构建后的原生窗口按一次 Tab 直接到字号，再提交 `18` 得到 `Current: SF Pro, 18pt`。无效字号按回车保持窗口打开，取消保留原有结果。
- 原生依赖：本任务完成独立 67 步原生构建，产物在 `artifacts/custom-control-refactor-20261028/native/osx-arm64/Release`，已通过 `check-native-exports.py`；未运行 native CTest。
- 主窗口专项 `shell-contract-suite-1.trx`：25 通过、0 失败，包含原生视口回调后的 Width/Height、缺失部件、换模板与手机搜索回归。移动验证副本仅在产物 Info.plist 通过既有 `JALIUM_GALLERY_MOBILE_PREVIEW` 开关选取 390×844，未为验收修改生产启动代码。
- 已查看并保存原生证据：`native-proof/shell-desktop-light-build15.jpg`、`shell-mobile-light-build15.jpg`、`shell-mobile-dark-build15.jpg`、`mobile-command-k-build16.jpg`、`mobile-pane-hidden-focus-build17.jpg` 与辅助功能树文本。
- JSON 原生证据：`native-proof/json-light-committed-build19.jpg` 显示两侧均为 `count: 64`；`json-dark-invalid-build19.jpg` 显示未提交的 `abc` 与可读错误；`json-dark-parse-error-build19.jpg` 显示解析错误。Copy Path 实际粘贴结果为 `$.count`。390×844 构建 20 已实际完成多行框单击聚焦、键盘输入非法 JSON、粘贴中文 JSON 后错误恢复、F2 将 42 改为 64 并回写/恢复焦点、浅深色、展开/折叠。已查看并保存 `json-mobile-toolbar-build20.jpg`、`json-mobile-stack-build20.jpg`、`json-mobile-tree-build20.jpg`、`json-mobile-dark-selected-build20.jpg` 与完整辅助功能树。一次截图被 Quit AppleEvent 正常退出中断，系统日志保存在 `json-mobile-exit-system.log`；重开后完成剩余检查，没有将该中断归因为控件崩溃。
- 仍未完成：整个框架 UI 迁移、完整框架测试、普通与 Release/AOT 工具链、Windows/Linux、实际 VoiceOver、所有页面的键盘/焦点/异常状态验收。

## 主窗口模板扩展约定

`GalleryWindow` 的正文、左右命令区均可替换。正文以公开依赖属性与只读派生属性为输入，导航按钮使用独立的 `GalleryNavigationButton` 模板。命令区 DataTemplate 的 DataContext 是 GalleryWindow，应用 ViewModel 从其 `DataContext` 属性访问（例如 `DataContext.NavigateCommand`）；旧命令区卸载时会断开上下文。

仍需在后续完整审查中覆盖构造期间套用模板、事件重入以及动态注册主题等边界。页面外观迁移、当前主窗口专项和原生 macOS 证据均不替代整个框架 UI 的最终验收。

## 属性表模板扩展约定

`PropertyGrid` 的 `Entries` / `Properties` / `VisibleProperties` 是只读集合依赖属性。`PropertyTemplate` 和 `CategoryTemplate` 替换行和分类，`EditorTemplate` / `EditorTemplateSelector` 替换编辑内容，原有 `RegisterCustomEditor` 工厂继续可用。默认 `PropertyGridEditor` 按属性类型选用 Text / Boolean / Enum / Color / Expandable / Custom 控件模板，未选用的编辑器不创建视觉实例。

`PropertyGridRow` 通过 Property、Owner、SelectedProperty、NameColumnWidth 和 NameForeground 接收数据与外观，选择、焦点、悬停和窄宽布局使用可视状态。`PropertyGridEditor` 提供待提交文本、类型值和 IsExpanded 双向属性，以及只读校验/编辑状态；Enter 提交、Escape 取消、失焦尝试提交，非法输入保留待改值。直接输入不调度全选；F2 开始编辑才请求聚焦/全选。属性源实现 INotifyPropertyChanged 时会通过弱事件更新现有行；不再因改列宽或画刷而重建所有编辑器。

`SelectedObjects` 保持原有取首项行为，多对象属性交集合并不在本次迁移中新增。使用反射的属性和类型转换器仍需在 AOT 应用中保留元数据；Release/AOT 验收尚未完成。`template-suite-47` 被轮次中断，没有结果，不计为通过。

属性表原生验收记录：构建 22 实际连续提交 `Templated value` / `Second edit`，三处同步；Tab 到 FontSize，`abc` 被拒绝，Escape 保留 14，复选框空格操作、键盘排序、筛选与空态通过。390×844 构建 22 已查看并保存上下字段、两种排序上下比较和 `Mobile value` 同步截图。构建 23 查看浅深色错误文字换行与颜色修复，保留非法输入，取消后提交 24；构建 24 实际从文本框鼠标打开枚举，再以方向键和回车选择 Center，三处同步，关闭后焦点仍为 ComboBox，一次 Tab 到 Width。

证据包括 `property-grid-mobile-light-validation-build23.jpg`、`property-grid-mobile-dark-validation-build23.jpg`、`property-grid-mobile-enum-build24.jpg` 与对应辅助功能树、程序集散列。`property-grid-lifecycle-1.trx` 为 50 通过，`property-grid-combo-2.trx` 为 218 通过。新纳入的旧 ComboBox 弹层测试改用已有 DisplayedTestWindow 逻辑根以满足 macOS 可见所有者约束，没有从单元测试创建 AppKit 窗口。构建 24 应用与窄窗口验证副本均通过签名校验。属性行随后将默认焦点策略改为元数据，以允许消费者样式覆盖；这一变更有 `template-suite-53` 用例，原生构建 24 展示的是相同默认焦点值。

## CommandBar 模板化

已保存 `commandbar-before.tar.gz` 与 Gallery 对应快照。CommandBar 不再创建、布局或绘制主命令区、More 按钮与溢出面板；默认结构位于 `Themes/Controls/CommandBar.jalxaml`。命令集合与派生显示属性通过依赖属性公开，四个部件可省略。显示模式、开关和禁用状态由 VSM 驱动。重套模板会释放旧 ItemsSource、弹层、按钮与键盘订阅。IsOpen 默认双向绑定；普通点击关闭、IsSticky、Escape、方向键及焦点恢复有独立回归。AppBarButton / AppBarToggleButton 标签通过附加属性与模板扩展，保留消费者的 IsCompact 本地值与绑定。

同时修复附加属性与同名普通属性在样式解析中的混淆；依赖属性注册记录附加属性身份。祖先绑定在挂载到新容器后重新解析来源，并在来源未就绪时应用显式 FallbackValue。该刷新暴露 TreeSelector 弹层内的旧祖先高度绑定，已改为 TemplateBinding 并验证打开后修改 MaxDropDownHeight 仍更新。失败诊断 `gallery-suite-40` 保留，修复后 `gallery-suite-41` 为 190 通过、0 失败、0 跳过。

当前模板专项 `template-suite-60` 为 163 通过；工具栏旧契约及生命周期 `commandbar-lifecycle-1` 为 22 通过；属性、绑定和样式基础 `commandbar-binding-foundation-1` 为 320 通过。Gallery 工具栏示例去除固定 500×48 尺寸，新增关闭模式、标签布局及保持打开选项，四种语言短标签齐备。macOS 实际交互验收正在进行，尚未计为通过。


CommandBar 原生增量记录：构建 25 暴露缺失 Windows 图标字体、AppBar 标签未进入辅助功能名称和 ScrollViewer 抢先处理方向键；构建 26 通过矢量图标、专用 Invoke/Toggle 自动化 Peer、预览键盘处理修复，并实际验证右箭头到 Edit、溢出 Down 到 Help、Enter 执行关闭并回到 More，保持打开时 End/Enter 执行 About 后仍打开，Escape 恢复焦点。390 宽度 Right 标签自动换行、Collapsed 保留名称、Minimal 只留 More、Hidden 移除内部按钮均有已查看截图。

构建 26 的深色截图暴露 IconElement 手工查色未订阅可变画刷，新增测试先以 2 失败、1 通过复现。IconElement.Foreground 改为共享可继承依赖属性后，`template-suite-63` 为 170 通过、`gallery-suite-43` 为 191 通过、图标/画刷/旧工具栏专项 22 通过；构建 27 已实际确认主区深色图标恢复白色，Down/Enter 仍能执行 Help。强调色命令文字在浅色默认背景原为 3.74:1；对应失败证据保存在 `commandbar-contrast-before-1.trx`，正在按普通、悬停和按下背景修复主题变体。


工具栏对比度与容器调整：主题强调色的 AppBarButtonForeground 按浅深色生成，在默认、悬停和按下背景上保持至少 4.5:1；覆盖 9 种强调色（含白与黑）的 Gallery 回归已通过。`gallery-suite-45` 为 192 通过、0 失败、0 跳过；`template-suite-66` 为 170 通过。CommandBar 弹层现使用独立 ContentControl 模板，每个模板一层 Border；命令行填满弹层宽度，图标和文字左对齐，模板回归验证行宽及箭头导航。

更广主题契约 `commandbar-theme-contract-1` 为 40 通过、1 失败：九个其他控件模板仍违反单模板单 Border 规则，具体范围见 `remaining-multi-border-templates.json`，不能据此声称框架主题全绿。桌面构建 29 已查看浅色布局，实际执行 Help 并恢复 More 焦点。一次 AX 单选操作仅更新了 IsChecked，没有执行主题命令；对应截图已明确重命名为 `radio-selected-without-command` / `overflow-after-radio-ax`，不计入桌面深色验收，后续实际指针操作继续验证。


CommandBar 构建 30 最终原生复核：1500×920 桌面通过实际指针切换到深色，已查看主区及浅色强调前景的溢出菜单；实际点击菜单右侧空白区域执行 Settings 并回到 More。390×844 实际 Add→Right→Enter 执行 Edit，Right 标签两行布局、深色图标和标签保持清晰；保持打开时 End/Enter 执行 About，弹层仍打开，Escape 关闭并回到 More。证据和边界汇总在 `native-proof/commandbar-native-acceptance.json`。`template-suite-66` 为 170 通过；Gallery 最后全量为 `gallery-suite-45` 192 通过，最后工具栏专项为 `commandbar-gallery-final-1`。本批不会改变整个目标仍在进行的状态。


## NavigationWindow 模板化

已保存框架和 Gallery 导航演示迁移前快照。NavigationWindow 采用 `Themes/Controls/NavigationWindow.jalxaml`，C# 不再创建内部 Frame、导航条、按钮、容器或画刷。GoBackCommand / GoForwardCommand / RefreshCommand 为只读依赖属性；历史栈、CurrentSource、导航条画刷和 ShowsNavigationUI 可绑定，显示状态由 VSM 驱动。重套模板自动解除旧模板命令，保留当前内容和隐藏状态。直接 Content 绑定、Source 双向绑定、取消请求、后退/前进/刷新、页面回调与重入均有回归。NavigationService 将实际窗口作为 Navigator，并在页面回调前连接窗口内容；Page 取得的服务与窗口历史一致。

GalleryNavigationService 的手工 StackPanel/TextBlock 演示已迁为 NavigationWindowSamplePage 与独立主题、只读页面状态。下一页创建真实历史记录，复选框直接双向绑定 ShowsNavigationUI，提供四种语言短文案。根 Window 的首次 Measure 现在先解析默认样式，修复父节点为空时默认模板未应用的问题。

当前 `template-suite-70` 181 通过、导航/Frame/窗口主题和旧模板生命周期专项 79 通过、`gallery-suite-47` 194 通过，均为 0 失败、0 跳过。Gallery 演示在 600 与 390 宽度的逻辑窗口测试中完成下一页、后退、前进、刷新和隐藏/恢复导航条；原生窗口验证尚在进行。

NavigationWindow 原生构建 32：实际打开 600×400 演示、下一页 01→02、工具栏后退/前进及 Alt+左右方向键通过。隐藏导航条后同一页面保持 02，辅助功能树移除三个工具栏按钮；实际拖动窗口右下角缩到 390×400，已查看布局，空格恢复导航条、Shift+Tab 到下一页并回车显示 03。关闭后回到主界面并恢复打开按钮焦点；再切深色重开得到全新 01 页面，浅深色截图均已查看；关闭后的主页面后退提示无历史，确认服务已释放。F5 已实际发送且界面保持当前页；刷新事件次数由单元测试验证，不将无可见变化称为原生事件次数验收。

原生截图发现外层 Go Forward / Open NavigationWindow Demo 标签被固定宽度截断，已改为四语言短标签及自适应宽度；代码示例补齐 ICommand 绑定，说明不再引导 code-behind。导航条禁用命令增加透明度反馈。`template-suite-71` 为 181 通过、`gallery-suite-48` 为 194 通过；构建 33 正在复核上述外观增量。

构建 33 已查看 390 宽度入口的短标签，禁用导航按钮具有明确透明度反馈；再次实际下一页并拖到 390×400。浅色与深色的 600×400、390×400 截图均保存并校验实际 JPEG 尺寸。最终范围与边界见 `native-proof/navigation-window-native-acceptance.json`。一次新窗口打开后未刷新截图坐标的拖动没有缩窄，原图已按实际 600 尺寸更名；刷新新窗口截图后重做物理拖动，最终深色窄窗口为实际 390×400，不使用旧截图替代。

## 输入控件与公共表面模板

AutoCompleteBox 现在继承 TextBox，复用文本编辑、撤销、选择、剪贴板与 IME 实现。建议数据通过只读 FilteredItems、HasSuggestions、SelectedSuggestionIndex 依赖属性提供；SuggestionItemTemplate、SuggestionItemTemplateSelector、SuggestionItemContainerStyle 支持替换建议内容和容器。默认模板使用 PART_SuggestionsList / PART_Popup，不再在 C# 中创建 StackPanel、建议容器或绘制另一套下拉列表。原有 Text / SelectedItem / IsDropDownOpen 和事件保留，内部提交使用 SetCurrentValue 维护绑定。可访问性同行复用 TextBox 的 Text / Value 模式并增加 ExpandCollapse。

TextBox 在直接测量时解析默认模板，TextBoxBase 在换模板时立即释放文字宿主。ListBoxItem 的默认可聚焦策略移到元数据，允许建议容器样式关闭焦点。NumberBox 的按钮几何与外观由模板提供，普通、悬停、焦点、只读及微调按钮显示状态使用 VSM；保留无模板的原始直接绘制路径。内部增减和提交保持 Value 绑定，递增后同步显示文本，避免失焦把旧文本提交回来。Cursor 字面量通过现有 CursorConverter 解析。

TextBox、NumberBox、AutoCompleteBox、ComboBox / ComboBoxItem、ContentDialog、PropertyGrid 和 HUD 的装饰层已整理。下拉表面通过独立 ContentControl 模板组合，分隔线和指示条使用布局或图形元素；未放宽项目既有的单 Border 契约，当前静态清单为零违规。ContentDialog 使用语义色、可换行操作区和更小的默认最小宽度；HUD 时间条按逻辑窗口宽度缩放，长诊断文本可换行。

`template-suite-78` 为 198 通过，`gallery-suite-53` 为 198 通过，`input-template-contract-7` 为 241 通过；后者包含输入、IME、CSS、主题资源、文本宿主收敛、容器生命周期、ListBoxItem 与 ContentDialog 回归。Gallery 色板用例曾在键盘 provider 为 null 时运行，已在测试夹具初始化键盘服务，并增加可聚焦及焦点范围断言；没有据此修改生产焦点语义。

原生构建 34 已实际验证 AutoCompleteBox 的方向键 + Enter / Tab 提交、整行空白处点击、选中数据回写、输入焦点、浅深色，以及 390×844 弹层尺寸。ComboBox 的浅深色弹层、键盘选择、选中标记也已查看。HUD 实际显示 Metal / Impeller 和实时采样；采样数值不作为性能基准。隔离 QA 宿主通过产物目录中的源码和 MSBuild targets 启用 HUD，没有修改生产 Gallery 启动配置。

原生验证发现 NumberBox 方向键递增后失焦回滚、390 宽 HUD 右侧裁切，以及 Gallery 外壳抢先处理模态 Escape 导致背后页面返回。这三项已有修复及针对性回归，正在重建构建 35 复验；构建 34 的问题截图保留。对话框六次 Tab 实际只在内容和三个按钮间循环。鼠标触发后的所有者窗口焦点与键盘触发后的控件焦点需分别校准，不能由单元测试推定实际 VoiceOver 通过。

构建 35 已实际复验按钮点击后聚焦 NumberBox，42→43→44 后失焦和切主题仍保留 44；HUD 在 390×844 内完整显示；对话框内 Command+K 不切换背后页面，Escape 关闭后保留 ContentDialog 页面。后续验收又发现异步命令会暂时禁用打开按钮，导致关闭时跳过焦点恢复；新增用例先复现失败，再在 ContentDialog 上增加一次 ContextIdle 重试，保留其他控件、其他模态窗口及失效目标的焦点保护。原有与新增 ContentDialog 测试 33 项通过。

构建 36 已实际 Tab 到打开按钮、空格打开、Escape 返回原按钮，再次空格打开与 Enter 提交后也回到原按钮。默认标题改用 TextElement 附加属性，实际浅色截图字号为 19；自定义标题模板保留自己的字号。PropertyGrid 已查看浅深色 390×844 外观，实际输入 Template surface / CC36 后三处表格同步。一次合并辅助功能抓取超时，已保留此前编辑树，重开后分步复验，不把超时算作通过。最新模板专项 `template-suite-79` 为 200 通过，详细原生范围见 `native-proof/input-controls-native-acceptance.json`。构建 36 起使用版本独立的应用目录，桌面构建 35 的运行实例保留。

整个重构目标仍在进行。DevTools、DataGrid / TreeDataGrid 等框架组合 UI、Gallery 对话框服务中手工创建的内容以及剩余行为控制器尚需迁移；完整测试、Release/AOT、Windows/Linux 和实际 VoiceOver 均未完成。


## Gallery 对话框与工作台的后续迁移

Gallery 对话框服务不再创建 StackPanel、TextBlock 或 TextBox 视觉树。GalleryDialogForm 与 GalleryDialogPreview 的默认外观位于 Themes/GalleryDialogs.jalxaml；表单通过 Text 双向依赖属性、可选 PART_TextBox，以及禁用、忙碌、校验状态扩展。错误文本允许换行并标记 Polite live setting。延迟校验保证异常时释放 Deferral 和忙碌状态；输入有效值后可在同一表单恢复。四种语言均有标题、字段、操作和状态文案。

GalleryInlineDialog 继承 ContentDialog，通过 Request、Result 和只读 IsShowing 依赖属性管理原位弹窗。它等待实际 Loaded 生命周期，不把尚未挂载的请求交给其他窗口；重复请求和卸载关闭有回归。ContentDialog 换模板时立即释放按钮、标题、卡片和滚动宿主的引用及事件；模板默认最小尺寸会收缩至窄宿主可用空间，放大后恢复，显式最小尺寸仍由调用方控制。正文滚动高度扣除已测量标题、换行操作区、边距与边框，修复 245px 宿主中卡片撑宽的问题。

工作台增加 DemoContent、DemoContentTemplate、DemoContentTemplateSelector 与 InspectorWidth 依赖属性。已移除 GalleryWorkbenchContentBehavior 和 GalleryWorkbenchLayoutBehavior，内容通过 PART_DemoContentPresenter 的模板绑定呈现；宽窄布局由 Wide/Compact 状态、Auto 列和面板宽度控制，CompactWidth 可配置。旧行为遍历按钮并强制设置文字颜色，导致内联强调按钮出现深色字；已在真实工作台宿主中先复现失败，再移除遍历，通过原生构建 38 的浅深色截图复核。内容替换、模板退役、数据模板和尺寸反复切换均有用例。

NumberBox 示例的四个值现由独立 ViewModel 属性双向绑定，示例代码也同步为 MVVM。字段标签和自动化名称支持四种语言，输入列保持对齐并在窄容器中收缩。原生构建 39 已验证桌面 42→44 后失焦保留、390px 窗口输入 150 后限制为 100、步长与 Page Up 得到 55，切换深色后两值不变。工作台已实际查看桌面双栏、手机检查器下置，并操作内边距 18→20。

本轮最终验证：template-suite-82 为 203 通过，content-dialog-host-contract-9 为 33 通过，gallery-suite-69 为 216 通过，均无失败或跳过；专项存在重复，不汇总为独立总数。构建 37 实际完成重命名中文粘贴/结果回写、异步空值阻止关闭及有效输入恢复、全尺寸外观和键盘焦点恢复；构建 38 完成内联交互、颜色、三个选项可见与 Escape 返回入口。截图尺寸及范围见 native-proof/gallery-dialog-workbench-native-acceptance.json。自动输入工具直接 typeText 中文仅留下 .md 的尝试已保留为诊断；完整中文结果通过实际粘贴路径验证，不能据此声称实体输入法组合输入通过。

整个目标仍在进行：DevTools、DataGrid / TreeDataGrid 内部组合及拖拽表面、其余 Gallery 行为控制器仍待迁移；当前 Behaviors 目录还剩 30 个文件，需按实际职责继续审查。完整框架测试、Release/AOT、Windows/Linux、实体输入法与实际 VoiceOver 尚未完成。桌面原始构建 35 保持运行，本轮桌面与手机验收各使用独立 bundle identifier，未替换用户主程序。

## DataGrid 与 TreeDataGrid 模板化

两类网格复用公开的 ColumnDragIndicator / DropLocationIndicator，默认外观位于 DataGridIndicators.jalxaml；拖拽表面通过可选模板部件连接。DataGridRowHeader 改为模板控件，公开行号、选择、分隔线及内容状态；行背景和前景从所属网格的依赖属性传递，显式行样式与列样式保持优先级。整行选择使用单层 tint，单元格不再叠加第二层。模板内的上下虚拟化占位与行内容宿主替代程序化 Border；缺失占位时安全退回全部实现。

TreeDataGridCellPresenter 使用依赖属性表达层级、缩进、展开器尺寸和样式、HasChildren / IsExpanded；可选 PART_Expander 与 Common / Node / Expansion 状态管理交互。缩进、图标、状态过渡位于 TreeDataGrid.jalxaml，移除 C# 组合 Grid / Path / Border 及旋转计时器。展开和折叠通过路由事件交给网格，可取消的展开事件会恢复原状态。网格提供只读 ExpandAllCommand / CollapseAllCommand，命令可用性随节点和 IsEnabled 变化。默认展开器明确清除 ToggleButton 的最小尺寸，实际为 16×16；无模板、替换模板、旧部件事件释放与自动化 ExpandCollapse 有用例。

行宿主使用 PanelChildReconciler 保留仍在视口内的容器，避免每次刷新都拆下正在编辑的 TextBox。列所属关系、Clear / Reset、旧列头回调及退役树行的内容、数据与事件一并清理。列宽标记通过既有 DataGridLengthConverter 注册；此前 Width="250" 被当成 Auto，原生树行因此出现空白列。非视觉列的 DynamicResource 依托资源宿主更新，宿主和目标均不形成全局强引用；首次挂载、局部资源、语言切换与回收有回归。DataGrid 的自动化树现在提供可见列头、实际行与单元格内容及边界。

Gallery 的 TreeDataGridBehavior 已删除，页面与独立预览声明列并直接绑定网格命令，ViewModel 只提供数据和选中项；中、英、日、韩的列头与命令保持动态资源。DataGrid 示例使用可编辑且提供属性通知的 PersonData，提交姓名会更新选中项摘要，取消不写回。两页的代码示例同步为声明式标记和 ViewModel。

原生编辑验收定位并修复了过早聚焦：DataGridCell 在编辑器首轮布局前调用 Focus，窗口更新 IME 上下文时读取 TextBox 的空文本宿主尺寸，创建负尺寸 Rect 并抛出异常。指针已指向编辑器，但后续焦点标志、事件、全选与准备编辑没有完成。临时分段记录取得完整异常栈，诊断钩子随后从框架移除。编辑器现在由 DataGrid 在布局后聚焦，TextBox 在首次 Arrange 前查询光标区域也安全。Home / End / 方向键同步 CurrentCell；Tab 与 Shift+Tab 按可见列显示顺序移动，保持编辑并支持跨行。边界迁移需要在移除编辑器的同一处理过程中完成，以免依赖已退役的原生输入路由。

构建 51 实际查看浅深色截图并完成 F2 全选、物理字符替换、Enter 回写、姓名摘要同步、方向键换行和 Escape 取消。树形网格完成箭头展开、方向键展开/折叠、批量命令、中文切英文、拖拽换序、换序后排序、列宽调整和内部滚动。构建 52 已验证 Tab / Shift+Tab 前后换格、跨行继续编辑；构建 53 验证了首格 Shift+Tab 退到前一个控件，构建 54 进一步排除内部滚动条，末格 Tab 实际进入后面的 XAML 编辑器并将它滚入可见区域。原生证据位于 native-proof，未据此宣称 VoiceOver 语音或其他平台通过。

本阶段 template-suite125 为 255 通过，滚动条边界专项 grid-tab-scrollbar-after124 为 49 通过，新增横向范围/列头同步专项 grid-horizontal126 为 2 通过；TextBox 对齐、缓存和文字几何专项 textbox-geometry16 为 74 通过。Gallery 最终 gallery-suite84 为 223 通过，均无失败或跳过。四处既有英文断言已显式设置并恢复自己的语言上下文，不再依赖本机保存的语言。已失败的诊断和修复前检查保留，不能与后续成功检查相加为独立覆盖总数。

整个目标仍在进行。列元素的 C# 默认样式、单元格网格线、列头回退绘制、树形表格的行/单元格自动化语义、DevTools 及其余 Gallery 行为仍需继续迁移。当前 Behaviors 目录为 29 个文件；完整框架测试、Release/AOT、Windows/Linux、实体输入法与实际 VoiceOver 尚未完成。用户使用中的原始构建 35 和独立桌面构建 45 保持原样，本阶段仅更换专用网格与手机 QA 副本。

## 可复用功能项与窄屏换行

390×844 原生窗口暴露了横向 StackPanel 功能列表的文字裁切。新增 GalleryFeatureItem，继承 Control，以 Text、MarkerBrush、MarkerSize、MarkerMargin 和继承的字体、边框、内边距等依赖属性配置外观；默认模板位于 GalleryPrimitives.jalxaml，Auto / * 两列为标记和可换行文字分配空间，CommonStates 表达普通和禁用状态。控件不创建视觉节点，支持缺少可选 PART_Text、替换模板及主题切换；自动化以 Text 角色提供完整文字，并允许显式名称覆盖。

21 个主题文件中的 117 个重复功能项已迁入此控件，保留 Splitter 的紧凑字号/间距以及命令输出的绑定、名称和强调色。专项 gallery-features83 为 15 通过；完整 gallery-suite84 为 223 通过。构建 55 已查看 TreeDataGrid 页在实际 1500×920 与 390×844 的浅深色、中英文截图，长文字均完整换行。其余迁移页面尚未逐页完成本阶段原生截图验收。

窄网格已实际验证键盘跨列后自动横向滚动，单元格和列头保持同步；CUA 的左右滚轮操作未产生位移，尚未确认输入事件的送达情况，不宣称横向滚轮通过。证据范围、历史失败、实际图像尺寸与剩余事项集中在 native-proof/grid-feature-native-acceptance.json。构建 54 / 55 的 Managed 与 Xaml 程序集哈希一致，构建 55 更新的是 Gallery 功能项控件与页面模板。


## 网格列的主题模板、绑定与编辑事务

Text、CheckBox、ComboBox、Hyperlink 四类列的显示与编辑根元素已移入 `Themes/Controls/DataGridColumns.jalxaml` 的 8 个 DataTemplate，外观由 9 个带类型资源键的 Style 管理。现有默认样式入口保持非空并封存，新增资源键可供主题与派生样式复用；读取默认样式不会创建 Application。列实现不再构造视觉控件或默认 Style，仍负责数据、选择路径和编辑事务。Hyperlink 的样式目标改为实际使用的 HyperlinkButton。行和单元格传递字体，列的显式字体、前景和消费者样式可覆盖默认值。

此次迁移暴露并修复了资源字典把 x:Static / x:Type 键保存为字符串的问题，编译与运行时路径均保留类型键及转义字面量。派生 Setter 接管同一属性层时会解除旧绑定，避免基类的下一次通知覆盖自定义值。显示元素使用绑定声明的单向副本，保留格式化、转换器及显式 fallback/null 配置，四类值和链接标签响应源通知；编辑器保留独立暂存值，提交才回写，取消不改变模型。树行建立并清理正确的数据上下文。OneTime 只观察数据上下文替换，不订阅源及嵌套路径通知。Uri 回写复用框架值转换，空编辑模板安全拒绝编辑。

网格在预览键盘阶段处理编辑事务，防止 CheckBox 与 ComboBox 自行消费 Enter；提交或取消后将焦点恢复到网格，接受换行的 TextBox 保留 Enter 输入。ComboBox 的 Focusable / MinWidth 默认值可由样式覆盖；下拉最小宽度通过主题的 TemplateBinding 跟随实际宽度，删除代码中的固定 Width / MaxWidth 限制，短列的选项可按内容展开。

最终测试：template-suite138 为 280 通过，gallery-suite87 为 223 通过，column-style-binding-regressions19 为 284 通过，均为 0 失败 / 跳过 / 超时。历史失败与修复保留在 `artifacts/custom-control-refactor-20261028/grid-column-template-phase.json`，没有用旧的通过记录覆盖本次失败证据。

macOS 独立构建 61 / 6101 已操作并查看 960×620、390×844 浅深色的 9 张最终截图：F2、普通字符替换、Space、Enter、Escape、Tab / Shift+Tab，四类列的提交与取消、同一行标签通知、窄窗口键盘横向滚动及完整下拉选项。精确 Uri 通过原生辅助功能文本设置后用键盘提交；自动化输入未稳定送达完整标点及粘贴内容，因此不将该路径计为物理输入或剪贴板验收。未访问链接地址。用户原始构建 35 与独立桌面构建 45 保持运行，本阶段专用网格和手机尺寸副本已退出。

整个目标仍在进行。下一步继续处理单元格网格线与列头回退绘制、树形表格自动化语义、DevTools 和其余 Gallery 行为。完整框架测试、所有页面原生交互、Release/AOT、Windows/Linux、实体输入法、物理触控板和实际 VoiceOver 尚未完成。


## 网格线与列头的模板外观

DataGridCell 的网格线及空间分配现由默认模板负责；新增 GridLinesVisibility、HorizontalGridLinesBrush、VerticalGridLinesBrush，并在普通行、树行上共享这三个依赖属性。主题从网格传到行和单元格，支持局部覆盖与 ClearValue 恢复。GridLineStates 控制全部、横向、纵向、无线四种状态，模板为可见线条分配像素带，行分隔线随横线状态隐藏。已删除单元格的手工 Measure / Arrange 扣边及 OnRender 绘制，也删除列头模板之外的手绘背景、文本、排序箭头与分隔线回退。单元格 Focusable 默认值可被消费者样式覆盖。

模板测试 template-suite140 为 286 通过，Gallery 全量 gallery-suite88 为 223 通过，网格 API/主题回归 grid-parity-regressions21 为 78 通过，均为 0 失败 / 跳过 / 超时。旧 API 测试改为检查继承的虚拟布局/渲染槽，主题测试改为核对实际模板元素，替代对已删除的私有绘制函数的反射。

独立构建 62 / 6201 / 6202 已查看 17 张真实截图，覆盖普通网格和树网格四种线条状态、浅深色及普通网格 390×844 窄窗口；普通网格在无线状态的 F2、文字输入、Tab 跨复选框/下拉框、Enter 提交，以及模板列头的实际排序和拖动缩放均已操作。树形验收数据为叶节点，本阶段不代表层级或自动化语义验收。

原生排序后，视觉顺序正确，但辅助功能树仍按源集合排列行。此问题与树形表格的行、单元格、表格模式及列重排后的名称一起继续处理。整个重构目标保持进行中，DevTools、其余 Gallery 行为、完整框架与跨平台及实际 VoiceOver 验收仍未完成。证据与历史失败位于 grid-line-template-phase.json。


## 网格辅助功能的显示顺序与出现项身份

辅助功能行、网格提供器的行索引、单元格 ID 和实际容器边界现随显示列表排序。映射保留源集合的出现项身份，相等对象、同一对象重复出现、值类型及空虚拟行有回归覆盖；单元格缓存按行 peer 弱持有，列变化清理旧缓存，结构通知在布局后合并发送。列访问保留逻辑集合索引，与 WPF 的 IGridProvider 约定一致（参考 [Microsoft 的 DataGridAutomationPeer 源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Automation/Peers/DataGridAutomationPeer.cs)）。排序后的源集合增删、替换和移动重新构建显示视图，SortMemberPath 生效；点击排序列头先完成当前编辑，取消提交则取消此次排序。

本检查点 template-suite143 为 295 通过、gallery-suite89 为 223 通过、grid-automation-regressions22 为 436 通过。原生 6301 / 6302 已查看共享对象重复三行的升降序、源替换、普通两行排序及实际列拖动，辅助功能行和单元格顺序与画面一致。

本阶段未作为完整编辑验收：在已排序并重排列后将 z 改为 a，Enter 提交后仍保持 Beta、a，已保存真实失败截图，下一步须在编辑提交后重排，并使 Tab 导航跟随移动后的数据项。重复行的选择/编辑定位仍以 item 为基础，源改变时的编辑生命周期、GridItem/TableItem 合约、树表格语义、DevTools 与剩余 Gallery 行为亦待继续。参见 grid-automation-view-phase.json。


## 排序编辑、导航与列头状态

构建 64 修复了上一检查点保留的排序键提交失败：编辑事务结束后应用当前排序，Tab / Shift+Tab 在提交后重新计算行与列，继续编辑移动后的原数据项。事务版本和提交保护覆盖结束事件取消、替换编辑以及提交期间更换源集合，旧事务不消费新的编辑。DataGrid 的标量选择由其显示视图管理，避免 Selector 按源索引重放造成选择循环；SelectedValue 与稍后到达的项目仍正确解析。列头排序符号和显隐迁入主题的 SortStates，自定义模板可使用自己的部件名与符号。

最终 template-suite147 为 307 通过、grid-automation-regressions23 为 536 通过、gallery-suite90 为 223 通过，均为 0 失败 / 跳过 / 超时。各专项存在交叠，不合计为独立用例数。保留修复前失败记录。原生 6401 / 64 已操作并查看 8 张 960×620 与 390×844 截图：排序键上下移动、列重排后的 Enter、Tab 与 Shift+Tab 跟随、复选框回写、完整下拉选项、升降序及浅深色。专用副本已退出，用户原始 35 和桌面 45 保留。证据位于 grid-sorted-edit-phase.json。

整个目标保持进行中。后续继续 DevTools、29 个 Gallery 行为与其余框架组合 UI。重复/相等行的输入身份、提交之外的源变动编辑生命周期、树表格语义与 GridItem/TableItem 合约仍待完成；完整框架测试、Release/AOT、Windows/Linux、实体输入法、触控板和实际 VoiceOver 尚未验收。


## DevTools 共享控件与工作区

DevTools 新增 13 个可复用 UI 类型，覆盖操作按钮、分段选择、表面、状态灯、读数、仪表、标签、分段条与九页工作区。默认模板、语义资源、叶子样式及状态位于 DevToolsPrimitives.jalxaml / DevToolsWorkspace.jalxaml。DevToolsUi 从 770 行组合视觉树收敛为 123 行控件配置；按钮保留真实 Style、命令、键盘与禁用合约，分段项有完整名称和方向键导航。工作区通过九个内容与 SelectedPage 依赖属性组合，换模板释放旧内容绑定与逻辑父级。

实际验收修复了初始树模式映射、分段面板方向、受限内容拉伸、浅色应用下的选中标签/异步 UIA 子行墨色，以及 REPL 输入落在非文本 Border 的问题。REPL 使用已有 Terminal 的文本输入上下文，保留求值和历史业务逻辑。Tab 自动化选择使用真实所属项，选中页面内容的父级和失效缓存随实际布局同步；诊断排除作用于延迟模板构造。

最终 template-suite161 为 332 通过、gallery-suite97 为 223 通过、devtools-tab-regressions32 为 258 通过，均为 0 失败、跳过、超时；专项有交叠，不相加为独立用例数。原生构建 73 / 7301 已查看 1040×860 截图并实际操作 UIA 根/子行及属性、REPL 字符输入/Enter 求值/历史/退格/Escape 取消/再次求值、Layout 点击和空格切换、分段焦点与方向键。旧构建的真实失败截图及中间失败测试保留。证据及源码哈希见 devtools-controls-phase.json。

专用副本已退出，用户原始构建 35 与桌面 45 保持运行。CUA 部分选中页内容被聚合，原生协议父子关系与操作测试通过，但不能替代实际 VoiceOver 语音验收。本阶段没有 DevTools 窄窗口验收。整个目标保持进行中，Inspector 行、DevTools 剩余 C# 组合、29 个 Gallery 行为及其他框架 UI 仍需迁移；完整框架、Release/AOT、Windows/Linux 和实体输入仍待完成。


## Inspector 树行与外壳

新增 DevToolsTreeItem，继承 ListBoxItem，以层级、缩进、子项、展开、箭头角度及 RevealProgress 依赖属性表达树行；默认模板在 DevToolsInspector.jalxaml，展开器和滑入内容为可选部件。指针、方向键和自动化 ExpandCollapse 共用展开状态，默认模板提供选择、悬停、焦点和禁用状态。滑入仍只更新变换与合成，完整布局高度和虚拟化保持不变。内部行适配器只把 InspectorRow 模型映射到控件，回收时清理模型、回调和状态。

DevToolsInspector 通过 SearchText / SelectedView、命令、状态和树/详情内容依赖属性提供工具栏、搜索、分段切换与分栏模板。窗口不再创建这些布局或保存其模板部件。InspectorTreeList 的模型 peer 提供可读名称与展开模式；原生虚拟树行也使用对应控件 peer。

模板专项 devtools-inspector164 为 30 通过，完整 template-suite165 为 337 通过。原生 74 发现重建集合使方向键焦点容器退役，随后在树列表按目标 Visual 身份于布局后恢复焦点；该修复的 devtools-inspector34 为 20 通过，原生 75 / 7501 已实际复验连续 Right / Left / Right、选择与属性、原生 Collapse 操作、Ctrl+F / 字符输入 / 清空、模式方向键、Pick 点击与空格、展开/收起全部。修复前原生 74 失败截图和编译/模板失败记录保留。

本阶段只验证 1040×860 DevTools 桌面窗口，未完成窄屏、真实 VoiceOver 与跨平台验收。Inspector 详情行及其他 DevTools 页仍有 C# 组合构造，整个目标继续进行。证据和测试适用版本见 devtools-inspector-phase.json。


## Gallery 视觉行为、入场与响应式状态

Design Tokens 的主题操作区改由 GalleryPage 的 CompactWidth=480 与 Compact 状态控制，删除 DesignTokensLayoutBehavior。OverviewPage 以 IsEntranceAnimationEnabled 和只读 HasEntered 管理一次入场；配色、光晕、阴影及 24 个目标的 Storyboard 位于默认主题。加载和卸载时连接、释放环境事件，换模板前直接切换旧模板根状态来停止动画，避免 GoToState 重新申请模板导致重入。减少动态效果设置会直接显示内容。独立预览使用另一个 OverviewPage 实例，删除重复布局；已移除 OverviewVisualBehavior 和早已不再引用的 HomeCatalogLayoutBehavior，Behaviors 目录现有 26 个文件。

390×844 原生检查发现原 Overview 固定侧栏挤掉标题、按钮与样张重叠。CompactWidth=980 的模板状态将标题、入口、样张和页脚纵向排列，缩小标题、允许文字和页脚徽章换行；Wide 恢复原桌面布局。入口箭头使用矢量路径，文字明确使用 GalleryTextOnAccent，避免缺字以及深色时隐式文字样式覆盖按钮墨色。

响应式迁移还补齐了框架状态路径：Setter 能定位命名 DependencyObject，模板行列定义的字面量与资源表达式进入 ParentTemplate 层，消费者之后设置的局部值保持优先。非视觉资源按属性及优先级层分别订阅，维持弱宿主和原有本地资源入口；状态退出释放自己的绑定与资源，不覆盖其他层。运行时模板解析补上完整命名范围注册，与编译模板一致。本次未新增非视觉对象的自动补间动画。

最终 template-suite169 为 342 通过，gallery-suite109 为 230 通过，nonvisual-style-regressions36 为 149 通过，均为 0 失败、跳过、超时，专项有交叠。中间失败保留；gallery-visual98 虽有 4 个已通过计数，但进程栈溢出、ResultSummary 为 Failed，不能作为成功运行。验证汇总现保留 outcome 与运行错误，避免仅看计数掩盖这类失败。

原生 77 验证 Design Tokens 的桌面与 390×844 浅深色、鼠标及 Tab/Space 主题切换。最终 82 / 8201 已查看 Overview 在 1500×920、390×844 的浅深色截图、英文及韩文窄屏换行，实际完成桌面 Tab/Enter、窄屏点击进入图鉴、滚动到样张和页脚。模板用例覆盖 320、390、720px 与恢复宽屏、换模板、卸载与减少动态效果的本地开关；没有据此宣称真实 OS 动态设置、实体触摸、IME、VoiceOver 语音或其他平台通过。

整个目标保持进行中。原生还发现通用 Selector 会丢失项目到达前设置的 SelectedIndex，Overview 下拉框因此初始未选中，方向键选择后可正常显示；移动标题也落入了“组件”的默认分支。这两项继续修复，不计为已通过。其余 DevTools 组合与属性行、TabControl/TabItem 的 C# 视觉实现、26 个 Gallery 行为，以及完整框架、Release/AOT 和跨平台验收仍待完成。详见 gallery-visual-state-phase.json。

### TabControl / TabItem 默认模板与初始选择（原生构建 83–92）

TabControl 和 TabItem 的视觉树、测量、排列、标题绘制及指示线已从控件类迁入 `Themes/Controls/TabControl.jalxaml`。默认模板通过 DockPanel、ItemsPresenter、TabPanel 和 SelectedContent 的 ContentPresenter 组合；四种位置、选中、悬停、禁用及焦点由可视状态驱动。默认标签面板保持透明，由 TabStripBackground 控制背景，避免遮盖 DevTools 的深色样式。TabStripHeight 为默认模板的最小高度，标题可以按实际内容测量并换行。

TabItem 现在实际呈现 Header、HeaderTemplate、HeaderTemplateSelector 和 HeaderStringFormat。页面内容继续由 TabItem 保持逻辑所有权，通过选中内容槽显示；新增只读的 SelectedContentHorizontalAlignment / SelectedContentVerticalAlignment 供默认及替换模板绑定。替换模板可以省略默认部件，选择属性继续工作；恢复模板后重新显示当前内容，不再创建隐藏的备用 ContentPresenter。

键盘导航跳过禁用、隐藏的标签，支持 Home / End 及 RTL 方向；内容中的方向键交还内容控件，Ctrl+Tab 从内容切换时聚焦新的标签。图形标题使用其可访问文本，显式 AutomationProperties.Name 仍优先；禁用标签拒绝自动化选择，字符串页内容也进入自动化子树。

同时修复初始 SelectedIndex 在子项到达前被清为 -1 的问题。尚不可用的初始索引保留在原值来源中，子项可用后通过正常 DP 回调提交；等待期间不把空选择写回双向绑定，显式 -1 可以取消等待。已有选择继续跟踪条目身份，删除后不会误选后来的条目。ListBox 及自行管理选择事务的控件继续使用各自的选择模型。上述 Gallery 概览下拉框的初始未选中问题已关闭。

Gallery 标签示例改用最大宽度以适应窄屏；概览的移动标题改为四种语言的简短导航标题。中文、英文、日文、韩文均完成实际截图检查。初始 Forest Emerald、后续键盘选择及绑定示例的 Selected tab 状态更新已在原生应用确认。

最终完成的回归：模板 358 / 358、相关框架 161 / 161、Gallery 230 / 230，均无失败、跳过、超时。模板覆盖四向布局、模板替换、逻辑所有权、独立标题与内容模板、对齐属性及自动化；框架回归还覆盖原生桥接、DevTools、主题和触摸事件路由。原生 86 / 87 完成四向布局、深浅主题、模板替换、禁用跳过、编辑器方向键及 Ctrl+Tab；92 完成桌面和 390×844 窄屏 Gallery、绑定状态、DevTools 页切换、录制按钮点击 / 空格停止及 Inspector 搜索焦点回归。

一次 DevTools 原生快照将 Layout 内容合并为标签文字，起初被误判为动作缺失。进一步读取同一进程的 AppKit 原生节点，确认 Layout 下有三个容器，录制、重置、捕获堆栈等都是独立 AXButton；实际点击和键盘状态变化也已确认。保留工具快照与 AppKit 原始记录说明这一区别，没有据此修改通用自动化缓存。原生发现的标签栏背景覆盖则确实修复，并保存了失败与修复后的截图。

证据和源文件哈希见 `artifacts/custom-control-refactor-20261028/tab-controls-phase.json`，测试运行整体结果仍保存在 validation-summary.json，早期失败未覆盖。仅本批控件与相关页面通过上述范围；未执行口述 VoiceOver、真实触摸 / 触控板、Windows / Linux 和完整 Release / AOT 验收。整个框架与 Gallery 的总目标继续进行，下一步处理剩余 DevTools 组合 UI、属性行及 Gallery 行为职责。

### Layout profiler 控件与滚动命中（原生构建 93–101）

新增 DevToolsLayoutProfiler，继承 Control；数据源、行模板、录制、堆栈、排序、计数、页脚与五个操作命令均通过依赖属性提供。默认工具栏、统计排名和失效时间线位于 DevToolsLayoutProfiler.jalxaml，录制、空态、排序、页脚和禁用采用可视状态。两个列表使用虚拟化与 DataTemplate，统计行的三组指标和失效组成条由已有专业控件呈现。外部可替换行模板或整个模板，缺少默认部件时仍保持数据与命令合约。

窗口只保留采样、堆模型与命令协调：继续使用前 30 项选择和最新 120 条事件，复用行模型及容器，每秒更新数据而不重建视觉树。公开展示模型通过依赖属性绑定格式化数据，目标元素仍由原始诊断记录弱引用。主题使用现有石墨色、琥珀操作色与语义指标色，长调用栈裁切并保留完整提示。

原生验证发现并修复了关联的生命周期问题。ItemsControl 的生成和准备、ContentPresenter 的新内容、延迟 ToolTip 和 Inspector 属性面板创建继承诊断排除，避免自测事件挤掉目标窗口的有界事件队列。堆栈捕获补上现有可选回调的默认安装，保留消费者提供的回调。DataTemplate 的触发器在卸载时释放，并在重新加载时恢复，切回 Layout 后指标强调和失效颜色仍有效。

滚动后的首次 Reveal 点击还暴露 VirtualizingStackPanel 快速路径的坐标错误：面板坐标被传给要求容器局部坐标的公开 HitTest。现在先通过统一变换映射到子项坐标；修复前 baseline57 确认 2 项失败，修复后 fixed58 为 3 项通过。回归特意覆盖第一个完整可见行，避免末行的空命中回退掩盖错误；另有横向、纵向的嵌套操作命中用例通过。

最终 template-suite190 为 369 / 369，layout-regressions61 为 136 / 136，gallery-suite115 为 230 / 230，均无失败、跳过或超时，专项有交叠。原生 101 实际滚动后首次点击第 29 行 Reveal，Inspector 正确选中其 Grid；返回 Layout 后 450 条目标失效记录和语义颜色保留。实际拖动窗口从 1040×860 缩到 480×860，页签与工具栏换行，排名和时间线滚动、鼠标与空格排序、停止 / 恢复 / 重置均已操作并查看截图。专用 QA 副本已退出，原始 Gallery 35 和桌面 45 保持运行；临时 FrameworkElement 跟踪代码已移除并验证恢复到调试前哈希。

证据、失败检查点及源码哈希见 `artifacts/custom-control-refactor-20261028/devtools-layout-profiler-phase.json`。本批没有口述 VoiceOver、真实触摸 / 触控板、Windows / Linux、完整框架和 Release / AOT 验收。整体目标继续进行，剩余 DevTools 复合页、Inspector 属性行、Gallery 行为职责与其他框架 UI 仍需完成。

### Bindings 视图与可复用数据流（原生构建 102–104）

新增 DevToolsBindingsView 和 DevToolsBindingFlow，分别管理绑定概览 / 事件日志与来源到目标的数据流卡片。默认模板在 DevToolsBindings.jalxaml；来源、目标、路径、模式、状态、验证错误和计数由依赖属性展示模型驱动，两个端点使用真实命令按钮。来源为普通数据对象时禁用跳转；视觉来源和目标保持弱引用，命令执行时解析当前目标。自动化名称包含完整来源和目标，事件错误也进入可读名称与提示。

数据流采用矢量方向箭头，支持 Default、OneWay、TwoWay、OneTime、OneWayToSource；宽屏横排，窄屏通过命名行列、变换和可视状态纵排。状态同时读取实际验证错误，未录制时也显示当前错误内容。绑定和最新 120 条事件复用模型及虚拟化容器，不再每 400 毫秒清空重建视觉树；总事件数保留诊断队列计数。Layout 和 Bindings 共用诊断列表 / 焦点容器样式。

Bindings 页激活时以录制 400 毫秒、空闲 1 秒刷新；错误在停止录制后仍会发布，因此空闲刷新也保留。切走页签和关闭窗口停止计时器。窗口仅保留绑定枚举、快照和命令协调，原 573 行组合区域已收敛，控件与窗口的绑定区域均无 C# 组合视觉构造。模板可替换，默认部件缺失时仍保持数据、状态及命令合约。

最终 template-suite195 为 378 / 378，bindings-regressions64 为 137 / 137，gallery-suite116 为 230 / 230，均无失败、跳过、超时，专项有交叠。模板覆盖数据源替换、状态恢复、模板替换、端点命令、响应式几何、弱目标、真实计数、验证错误与日志虚拟化；实际窗口集成覆盖真实鼠标分发、目标诊断排除、未录制错误和计时器生命周期。

原生 104 实际键入 Final two-way，来源和目标同步；140 次目标更新、13 次源回写与滑块宽度更新显示正确。未启动录制时，真实 IDataErrorInfo 错误直接出现在红色卡片和事件日志；非视觉来源不跳转，目标 / 来源首次点击分别选中 BindingTarget / BindingSource。真实原生窗口在 480 与 1040 像素宽往返，数据流正确重排；480 下 Tab 依次进入重置、列表、行及具名来源按钮，空格完成 Reveal。311 条事件经过 Inspector 创建、页签往返、窗口宽度恢复和时间线滚动仍保留；停止与重置后日志和计数归零。

中间 102 的“缺失成员”测试来源在现有绑定引擎中回退到空值，没有发布错误，未把它计为错误验收；最终使用真实验证错误。102 的边缘拖动未改变窗口宽度，480 验收来自 104 中实际改变原生窗口宽度的 QA 操作。专用应用已退出，原始 Gallery 35 与桌面 45 保持运行。证据及版本边界见 `artifacts/custom-control-refactor-20261028/devtools-bindings-phase.json`；口述 VoiceOver、实体触控、Windows / Linux、全框架和 Release / AOT 仍未验收。整体目标继续进行。


### Events 路由控件与主题面板默认值（原生构建 105–109）

新增 DevToolsEventsView 与继承 Button 的 DevToolsEventNode，时间线、过滤工具栏、路由连接箭头、源节点标记、焦点与状态进入 DevToolsEvents.jalxaml。数据源、行/节点模板、计数、录制、过滤与命令通过依赖属性扩展；部件可缺省或替换，旧部件解绑。两个展示模型保留路由顺序和弱目标，原始来源、已处理状态及完整名称进入可访问文本。窗口的事件区域从504行收敛至91行，最新80个模型保留身份，原始队列仍最多512条；纵向时间线和横向节点路径均虚拟化。

原生105发现路径仍竖排，定位到 ListBox 构造器的局部 ItemsPanel 默认值压过主题。ListBox、TreeView、TreeSelector 现以密封元数据提供默认面板，备用工厂也遵从实际 ItemsPanel；显式局部值与派生元数据仍优先。修复前200专项7失败，修复后201为18通过。滚动条自动隐藏仅在短路径视口关闭，确保可用鼠标拖动。原生106的滚轮与拖动尝试未通过，没有计为成功；107和最终109真实拖动均到达末端，随后首次物理点击准确选择 Border #RouteAncestor5。

扩展回归还修正了两个已有合约：TreeSelector 根据展开的逻辑顺序选择范围，包含尚未实现的声明节点并排除隐藏/折叠后代；VirtualizingStackPanel 在远距跳转的下一布局中测量并保留目标，避免平均尺寸变化使目标未实现。专项覆盖横向、纵向、可变尺寸、往返跳转与有界实现，目标完整位于视口。没有强制实现整棵树。

最终模板204为396/396、Gallery119为230/230、相关框架74为412/412，均无失败、跳过、超时。进一步扩到滚轮和弹窗的79为595通过、1失败，整体结果为Failed；失败项 PromotedWheelHandlersStopAfterTheirPopupOwnerChanges 在修改前105程序集的配对78中也失败，单独76则通过，保留为既有顺序/全局状态问题，没有宣称扩展套件全绿。79同时确认带正向非零X增量的平台事件能够滚动嵌套路径；75/77使用了反向增量，测试方向已按共享ABI修正。实际工具横向滚轮产生的路由增量为0，不能据此验收实体触控板。

最终109查看1040×860与480×860截图，并实际操作 Tab进入节点/空格Reveal、过滤Enter/失焦应用、三种路由及源节点/Handled标记、窗口往返和历史滚动。600次目标事件产生512条有界记录、80个模型；480宽时13个实现行、23节点长路径只实现7个节点，恢复宽屏并滚动后21个实现行。键盘停止后保留512条，清空后计数/模型归零。一次工具超时后重新读取原生状态确认已停止，未重复切换。10901实际点击TreeSelector叶子，完整路径和自动关闭正确；10902物理聚焦后方向键展开Documents/Work并聚焦Report.docx，未把焦点移动当作选择变化。

验收副本均已退出，原始Gallery35与桌面45保持运行。失败截图、完整TRX结果、源码哈希和版本边界保存在 devtools-events-phase.json / validation-summary.json。没有完成口述VoiceOver、实体触控/触控板、Windows/Linux、全框架及Release/AOT验收。整体目标继续进行，下一批迁移Resources及其余DevTools组合、Inspector属性行、Gallery26个行为职责和其他框架UI。

### Resources 解析链与虚拟化行（原生构建 110–117）

新增 DevToolsResourcesView、DevToolsResourceRow 和依赖属性数据项，默认模板进入 DevToolsResources.jalxaml。过滤、计数、命令、行模板、紧凑阈值和详情显示可扩展；缺省部件、模板替换、弱集合观察及禁用状态有契约检查。窗口 Resources 区域从548行收敛至68行，稳定缓存只保留当前快照，180ms过滤定时器在离页和关闭时停止。快照按引擎规则检查本地、Style、逻辑祖先、重定向宿主及应用资源，包含活动/非活动主题、倒序合并、显式null、循环和预览异常；不分配空资源字典，预览复制画刷，弱宿主令牌避免保留被检查元素。Winners操作支持可选Toggle语义和自动化状态。

构建113/115发现窄屏首次End持续空白：新行先按横向布局测量为34，排列后变为53.2，旧估算使容器落在视口外。回归210复现失败，响应式状态前移到测量阶段后211的15项通过。构建117重复资源变更、主题切换、480宽度、2000键滚动、清空过滤及首次End，末行落在573.49至626.445的可见范围，6162模型仅24个容器。Home/End、空状态、键盘Space开关、恢复1040、合并及主题优先级均已查看真实截图。构建116诊断启动超时未计入验收，临时VSP诊断代码已移除。

完整模板212为411/411，资源与导航88为14/14，Gallery120为230/230。扩大回归85为620通过、1失败、621总数，失败仍是归档105基线78中的Popup滚轮用例，整体结果保留Failed。循环图的公开API构造先前触发既有资源变更通知递归，80中止；后续不发通知的循环回边只验证快照终止，未宣称修复通知引擎。

验收副本已退出，原始Gallery35与桌面45保持运行。证据和源码哈希见 devtools-resources-phase.json，历史失败保留于validation-summary.json。整体目标继续进行；UIA、Perf、Tools、Repl、Inspector属性行、Gallery剩余行为和其他框架UI，以及完整框架、Release/AOT、Windows/Linux、口述VoiceOver等验收尚未完成。

### UIA 层级、属性与可重入布局（原生构建 118–122）

新增 DevToolsAutomationView、DevToolsAutomationPeerRow、可绑定层级数据及属性快照，默认层级、语义值、模式标签和宽窄布局进入 DevToolsAutomation.jalxaml。节点、属性和模式模板、来源、选择、计数、状态及刷新命令均可扩展；树及命名行列部件可缺省或替换。窗口 UIA 区域从270行收敛至96行，不再用C#拼装树、分隔条、属性行或模式标签。

枚举器保留父节点的子游标，超过48个子节点的分支跨批继续，单批最多8父节点、48子节点并在8ms后让出；循环、读取异常、深度与节点限制显式进入快照。保留的快照通过弱Peer和宿主令牌避免持有被检查对象；刷新恢复展开、选择和当前容器焦点，离页暂停、关闭清理队列。旧浅色应用测试已转为检查实际模板部件，继续约束DevTools文字颜色。

真实交互发现并修复三组边界：ItemHeightIndex曾把展开树项的7708高度截成4096，移除该上限后End能到可见末端；TreeViewItem与TreeSelectorItem新增HoverBrush，默认模板消费该属性，DevTools悬停保持深色；属性面板铺满可用区域并有透明命中背景，空白处的滚轮也能滚动。刷新焦点和窄屏滚动分别由94、97的失败基线验证后修复。分隔条写入的本地像素尺寸会压过VSM，224/225复现宽屏只剩200行高；控件在布局方向切换时释放命名行列部件的运行时尺寸，由主题恢复行列布局。

原生122完成612节点快照，含137个按钮，实际验证指针聚焦、Right/Down/Space、展开大分支后的End、Peer136属性与Invoke、刷新后Up/Space选择Peer135、480/1040两种布局、竖向拖动后恢复宽屏、横向拖动后缩窄及整个详情区的滚轮。构建120还验证了目标改名后的快照刷新。118至121中的失败和中间状态保留，未覆盖为通过。

完整模板227为420/420，相关框架102为20/20，扩大回归103为641通过、1失败、642总数，整体仍为Failed；唯一失败与归档105及Resources85中的Popup滚轮基线一致。Gallery检查器调整AccessText/TextBlock和AutoCompleteBox/TextBox匹配顺序后，Headless123为5/5，Main124为230/230。完整TRX、源码哈希与截图见 devtools-uia-phase.json 和validation-summary.json。

验收副本已退出，原始Gallery35与桌面45保持运行。整体目标继续进行，Repl、Perf、Tools、Inspector属性行、Gallery26个剩余行为职责和其他框架UI仍待完成；未完成口述VoiceOver、实体IME/触控/触控板、Windows/Linux、完整框架及Release/AOT验收。


### DevTools REPL console

`DevToolsReplView` 通过默认主题提供工具栏、只读 Terminal 输出与原生 TextBox 输入。窗口保留表达式执行和上下文，不再拼装 REPL 界面或手工维护 UTF16 输入缓冲。`InputText` 默认双向绑定；`Prompt`、`ContextText`、`FontFamily`、结果颜色、只读及容量均可设置。消费者通过 `ExecuteCommand` 或 `CommandSubmitted` 扩展执行，`SubmitCommand` / `ClearCommand` 供模板操作使用。

默认历史容量为200条，完整输出回放容量为512次写入，Terminal 滚动历史为2000行。Up / Down 保留未提交草稿；清空输出保留草稿和历史；Escape 取消输入；复制选区继续使用 TextBox 原生行为。IME 组合期间的 Enter 留给编辑器处理。输入与输出复用同一个字体依赖属性，默认包含 macOS / Linux 等宽字体回退。

`PART_InputTextBox` 和 `PART_Terminal` 均为可选部件。更换模板解除旧编辑器与终端观察器，完整输出在模板恢复或终端尺寸变化后按新列宽回放。DevTools 在非活动窗口时重建模板后，需要聚焦新编辑器或返回 REPL 页。

专项模板检查16/16、最终完整模板436/436、Gallery主模块230/230、无头5/5通过。复用上一阶段642项方法并加入 REPL 集成检查的回归为642通过、1失败、643总计；失败仍为已记录的 Popup 滚轮路由用例。一次按整类扩大范围的1814项检查有56失败：3项 Meter 元数据顺序问题已修复并由回归覆盖，另53项 Popup 路由失败仍须在后续框架完整验证中处理。

原生126验证实际上下文求值、Unicode粘贴与等宽显示、按钮/键盘提交、只读、480/1040完整输出重排、模板重建、历史草稿、清空与取消。原始 AppKit 树保留独立输入、输出和操作按钮。物理 IME、物理键盘布局、VoiceOver朗读、Windows/Linux、Release/AOT及整个框架验收仍未完成。证据见 `artifacts/custom-control-refactor-20261028/devtools-repl-phase.json`。

## Perf 控件迁移（原生输入验收待完成）

`DevToolsPerformanceView`、`DevToolsFrameGraph`、`DevToolsDrawApiRow` 和 `DevToolsPerformanceBar` 通过依赖属性、可选模板部件和可视状态组织性能页。默认主题为 `Themes/Controls/DevToolsPerformance.jalxaml`；图表使用独立矢量路径，API 列表和渲染器指标使用虚拟化数据模板。窗口的 Perf 区域由950行缩至99行，只协调快照、引擎命令、诊断所有权和500 ms刷新。

原生运行复现并修复了长期 DP 数据源对退役模板绑定的保留。直接源和中间路径使用弱观察器，测试覆盖目标回收、路径切换和40次排序后的订阅上限。模板声明中的 Binding、MultiBinding、PriorityBinding 在后续传输时保留声明值层；可回写编辑器使用该值层，单向声明继续允许本地覆盖。首次表头加载、延迟数据源、编辑重置、取消重开及 NavigationView 内容投影均有回归覆盖。

最后完整模板检查253为465/465；随后滚动条留白与列对齐的定向检查254为30/30，绑定、依赖属性、状态及 Perf 框架回归118为313/313。共享 DiagnosticsItem 模板开始遵守原有 Padding 属性，性能条目和表头为覆盖式滚动条保留12 DIP。

原生131截图验证了1040/480布局、完整表头、实际 Auto/Vello/Impeller 切换、隐藏目标后的 Idle 和空闲时模板重建。另有2000 API行/200指标行的合成数据预览，其60 FPS为样例数据。131早于最后的编辑绑定范围及滚动条留白修正；当前源码仍须重新构建并做原生渲染与输入检查。指针工具未能定位桌面窗口，随后 CUA 工具被移除，尚未完成的键盘焦点、物理滚动、首个 End 和分隔条拖动没有计为通过。此阶段及整体目标均保持进行中，证据见 `artifacts/custom-control-refactor-20261028/devtools-perf-phase.json`。

## Tools 控件与目标输入协调

`DevToolsToolsView`、`DevToolsMeasurementView`、`DevToolsColorSampleView` 的默认界面位于 `Themes/Controls/DevToolsTools.jalxaml`。工具状态、结果、命令与错误反馈通过依赖属性和可视状态表达，宽窗口两列、窄窗口纵向排列。尺子和颜色为不可变数据，提供 AOT 属性访问器；尺子的有限坐标计算避免平方溢出，八方向吸附的指针预览不分配格式化结果。Tools 宿主区域由947行缩至480行，保留目标输入、读回、剪贴板、导出和渲染模式协调。

尺子、取色和 Inspector 拾取互斥，目标窗口和 DevTools 中的 Escape 先取消活动工具。焦点跟踪改用键盘焦点与窗口激活事件，离页或接受关闭后清理订阅。清理跟随 `Closed`，取消 `Closing` 保留会话；既有测试改用实际 `Window.Close()` 验证计时器和覆盖层释放。连续30次切页、普通关闭、取消后再次关闭及缺少模板部件均有回归覆盖。

跨 Gallery 回归发现命名空间 CSS 属性只识别本地值，遗漏了模板声明层的绑定；现已支持该声明来源，仍排除仅由 CSS 产生的值。编译和运行时模板均验证了更新、清除与本地替换。启用 CSS 又暴露了待处理调度队列对脱离模板实例的强引用；队列改为弱引用，实际执行时保留仍存活的节点，覆盖脱离实例回收和存活树延迟更新。

最终 template-suite269 为498/498，tools-css-regressions123 为401/401，gallery-suite134 为230/230，gallery-headless135 为5/5，均无失败或跳过，各专项有交叠。此前266的3项释放失败和267的隔离复现均保留，268修复后定向33/33通过。

原生135使用真实 Metal/Impeller 窗口和帧读回，程序化验证取色得到预期 `#FF754DE8`、尺子提交和吸附、焦点跟踪与清除、重建模板及关闭订阅清理。已查看1040/480结果、重建、错误、焦点与吸附六张当前帧截图；134中数值截断和停止后残留错误样式的截图保留为修复前证据。此运行使用程序化命令、路由事件和焦点调用，未计作物理鼠标、键盘、IME或VoiceOver验收。整体目标继续进行，下一步为 Inspector 详情控件、Gallery26个行为职责和其余框架 UI；证据与源码哈希见 `artifacts/custom-control-refactor-20261028/devtools-tools-phase.json`。

## Inspector 盒模型控件

`DevToolsBoxModel` 通过间距、边框、内边距和实际尺寸依赖属性提供盒模型；默认图形、数值和图例在 `DevToolsInspectorDetails.jalxaml`。不可变数据 `DevToolsBoxModelMetrics` 提供 AOT 访问器、负值和小数格式、未知尺寸状态以及完整辅助功能摘要。默认模板显示三层全部12条边，消费者可替换模板、填充和图例。紧凑状态跟随最终排列宽度，模板缺少部件时数据绑定继续工作。

窗口构建由220行降至23行，只创建控件并绑定目标属性。边距、边框、内边距和尺寸变化后，图形更新而不重建整份 Inspector。template-suite273 为505/505，inspector-box125 为33/33，gallery-suite136 为230/230，均无失败或跳过，专项有交叠。270的200 DIP溢出和271的测试夹具假设已保留并修正；272定向26/26通过。

原生138已查看实际 Inspector 的实时更新、400/200 DIP独立控件、模板重建、图例隐藏和未知尺寸六张帧截图。目标宽度从180变为220，负边距与各边数值均正确更新。137的像素检查暴露了多层填充后的文字对比度不足；减淡填充并提高标签对比度后，138四层背景上的文字比值为6.33至11.21，原始值和计算脚本保留于证据目录。此为默认盒模型样式的数值检查，未声称整个 Inspector 或框架通过可访问性验收。

原生同时显示周围旧属性编辑行仍为快照值，后续继续复用已有 `PropertyGridEditor` 的编辑、校验与模板合约迁移属性行。其余详情组合、窄窗口 Inspector、Gallery26个行为和其他框架 UI仍在范围内；物理输入、口述VoiceOver、平台与Release/AOT检查尚未完成。参见 `artifacts/custom-control-refactor-20261028/inspector-box-phase.json`。

## Inspector 实时基础属性行

`DevToolsPropertyRow` 继承 `PropertyGridRow`，默认模板在 `DevToolsPropertyRows.jalxaml`，复用 `PropertyGridEditor` 的文本、布尔、枚举、校验、提交、取消和模板生命周期。`DevToolsDependencyPropertyModel` 用弱引用适配依赖属性，提供实时值和来源；同值绑定、原生/CSS动画、important遮挡和强制值变化也能更新来源标签，不伪造普通值变化通知。窗口只创建模型与控件；刷新、切换目标和接受关闭时释放订阅，取消关闭后保持会话。

定向回归修复了缺少模板部件时切换属性仍恢复旧草稿的问题。原生139/140还暴露了不存在的输入样式键，改为现有 `DevTools.TextInput`，避免浅色目标窗口让诊断输入框回退成白色。失败记录均保留。最终 `inspector-property-source133` 为325/325，`template-suite278` 为513/513，`gallery-suite139` 为230/230，均无失败或跳过，各专项有交叠。

原生141已查看实际Inspector、480/240 DIP独立控件、外部更新、内联错误、紧凑排列、重设模板和同值绑定七张Metal帧。程序化Enter/Escape验证提交与取消，文本框实际收到0.75，布尔与枚举写回正确，只读字段不可编辑，关闭后模型失效。此次未计作物理输入或口述VoiceOver验收。画刷、字体、间距、尺寸、复杂值及重复的组件详情仍待迁移，整体目标继续进行；证据见 `artifacts/custom-control-refactor-20261028/inspector-properties-phase.json`。

## Inspector 复杂属性与颜色编辑

分类属性区现在全部使用 `DevToolsPropertyRow`。复杂值复用框架 `ValueSerializer`，不支持的对象显示只读摘要；空值、颜色、渐变和运行时枚举切换时，模型通知替换编辑表示。可空枚举保留空选项。画刷颜色复用 `PropertyGridEditor` / `ColorPicker`，默认模板提供折叠色块、色值和可展开的透明度及十六进制编辑。修改克隆当前画刷，保留透明度和变换，不修改共享或冻结源。弱值观察器跟随 Freezable 和集合的原地变化，并在替换、释放后解除订阅。

窗口分类编辑器与旧转换辅助代码减少260行，从本阶段基线5770行降至5510行；实际Button Inspector的101个分类依赖属性均为新控件。组件专属的旧重复区尚未删除，其中额外诊断和字体选择能力仍需迁移。`ColorPicker` 的既有绘制实现也仍属于后续框架审计范围，此处复用了其成熟交互合约。

原生142发现嵌套折叠标题的绑定为空，以及盒模型小数被省略。显式祖先绑定恢复色值；盒模型改为有界自动侧栏、完整上下数值和长值换行。原生145进一步记录尺寸文字需要35.061 DIP但单元只有34 DIP，控件因此请求201.061 DIP。紧凑侧栏各收回1 DIP后，146的文字获得36 DIP，控件请求199.061 DIP并排列在200 DIP内，保持原字号。

最终 `inspector-complex-host135` 为404/404，`template-suite284` 为524/524，`gallery-suite140` 为230/230，均无失败或跳过，专项有交叠。280的标题绑定失败、281的长值溢出及原生142至145的中间记录均保留。原生146已查看11张当前Metal帧，包括真实Inspector、480/260 DIP编辑区、颜色展开与焦点、错误和实时值、空值/只读渐变、模板恢复及200 DIP小数和长值盒模型。此运行验证程序化控件与焦点调用，未计作物理输入或口述VoiceOver验收。整体目标继续进行；详情组合、重复区、Gallery剩余行为、其他框架UI及平台和Release/AOT检查仍未完成。证据见 `artifacts/custom-control-refactor-20261028/inspector-complex-properties-phase.json`。

## Inspector 摘要与祖先导航

`DevToolsElementSummary` 用依赖属性和默认模板显示类型、基类、元素名与统计，支持缺省数据状态、长标识符换行、零宽度后恢复、内容尺寸布局和可访问名称。`DevToolsBreadcrumb` 继承 `ItemsControl`，复用现有 `DevToolsActionButton` 的键盘、命令和自动化行为；路径节点仅保存祖先弱引用。消费者可替换命令、条目和容器模板，默认命令通过类型化路由事件请求宿主选择，仅接受仍属于当前路径且可用的祖先。

窗口不再拼装类型文字、重复调试标题、统计标签、分隔符、点击边框或面包屑鼠标事件。本阶段从5510行降至5389行，只配置控件、收集数据并协调选择。中文元素名实时更新；当前路径项只读，失效目标、其他路径节点和退休模板均不能触发默认导航。200 DIP下的类型声明成组换行，避免关键字与基类标点单独成行。

扩大检查时发现并保留了并行工作区中的依赖对象惰性事件存储改动，订阅计数测试改为检查实际OptionalState委托，继续严格验证释放和数量上限。136的6项反射失败保留；137为406/406。原生147因隔离Css.Tooling资产缺失未运行，恢复既有项目资产后148成功；148中窄屏排版问题修正于149。最终 `template-suite292` 为537/537，`gallery-suite141` 为230/230，均无失败或跳过，专项有交叠。

原生149已查看9张当前Metal帧，覆盖真实Inspector、中文长名称、480/240 DIP预览、程序化焦点和Enter祖先导航、模板恢复、局部主题覆盖和空摘要。实际内容宽度200 DIP，选中项从Button切换到Window；旧按钮未产生导航。此为程序化路由和焦点调用，不计作物理输入或口述VoiceOver验收。分类组、重复组件区、附加属性、样式/资源/绑定/模板详情和整体窄屏Inspector仍待迁移；Gallery及其余框架范围继续有效。证据见 `artifacts/custom-control-refactor-20261028/inspector-summary-phase.json`。

## Inspector 属性分类组

`DevToolsPropertyGroup` 继承 `ItemsControl`，通过依赖属性、标准条目/容器/面板模板和展开状态组织属性。标题计数、类别配色和交替行均在默认主题中，复用 `DevToolsActionButton` 的键盘和自动化合约。折叠后模型继续更新，更换模板后旧标题不再操作当前组；模型仍由窗口在刷新、切换选择和接受关闭时释放。窗口减少75行，从本阶段5389行降至5314行。

最终 `template-suite297` 为542/542，`inspector-groups-host138` 为408/408，`gallery-suite142` 为230/230，无失败或跳过，专项有交叠。293/294的枚举及转换器模板错误、151检查夹具的NaN日志错误均保留。原生152已查看9张Metal帧，覆盖真实83属性Inspector、480/240 DIP预览、200 DIP内容、程序化Space折叠、自动化展开、实时值、校验提交、条目删除、模板恢复与分类换色。

实际Inspector首组虽排列为459.6 DIP，仍沿用旧测量约束下的紧凑模式；未将整体详情布局计为通过。此运行也未计作物理输入或口述VoiceOver验收。详情组合、重复组件区、附加属性、Gallery剩余行为及其余框架和平台检查继续进行。证据见 `artifacts/custom-control-refactor-20261028/inspector-groups-phase.json`。

## Inspector 详情列表与XAML查看器

`DevToolsDetailRow`、`DevToolsDetailSection`、`DevToolsHeading` 和 `DevToolsXamlView` 的默认界面位于 `DevToolsDetails.jalxaml`。网格定义、样式、资源与绑定现在提供数据条目，复用既有资源预览格式；值、名称与补充信息支持换行，画刷色块为副本。列表通过标准条目模板、计数、展开状态和可选命令扩展，空列表显示静态标题，只有操作的区块仍可展开。绑定描述覆盖普通、MultiBinding与PriorityBinding。

查看XAML改用原有专业动作按钮，支持焦点、Enter和Space；刷新后的旧按钮不能执行。原生155发现按钮在加入详情区之前计算命令可用性而保持禁用，调整顺序后156确认实际窗口焦点和Enter打开查看器。查看器使用可选中、只读的原生文本框；156截图中垂直居中的文字已在157改为左上排列。窗口从5314行降至5044行。

完整模板303为550/550，后续对齐定向304为8/8，macOS定向141为410/410，Gallery144为230/230，均无失败或跳过，专项有交叠。299的长文本1.6 DIP溢出及编译夹具、命令初始化、视觉对齐的中间记录均保留。原生157已逐张查看10张Metal帧，覆盖真实Inspector、480/240 DIP预览、200 DIP内容、折叠、数据更新、空资源、模板恢复、按钮焦点及XAML选择。程序化路由与焦点调用不计作物理输入或口述VoiceOver验收。

重复组件属性、旧编辑行、字体能力迁移、附加属性与整体紧凑布局仍在进行；其余框架与Gallery范围不变。证据见 `artifacts/custom-control-refactor-20261028/inspector-details-phase.json`。

## Inspector 组件重复区、字体与附加属性

组件专属的重复属性区和旧Row/AddX编辑辅助代码已移除，统一使用实时依赖属性模型。所需CLR诊断仍通过数据条目显示：尺寸、边界、类型、子项/条目/定义数量及OpenType字重；它们保留此前的快照语义。Grid和Canvas的父容器附加属性并入Layout分类，使用完整名称，重复DP按标识去重，Grid跨度复用原生setter的最小值处理。窗口从5044行降至4087行，减少957行。

字体族和字重保留实际字体预览与编辑，分类组通过命令转发Windows原生字体对话框入口。`PropertyGridEditor`新增可选ActionCommand、参数及PART_ActionButton合约。305发现嵌套模板中的祖先命令在退役后仍可执行，改为编辑器拥有的TemplateBinding后306为40/40，完整307为555/555；另覆盖了整个属性行模板退役。最终macOS定向143为413/413，Gallery145为230/230，无失败或跳过，专项有交叠。

原生159已查看7张Metal帧：真实选中Button的105个唯一模型、字体预览与提交、程序化焦点和Space、整数校验与跨度归一化、480/240 DIP预览、200 DIP内容、模板恢复和Grid切换至Canvas后的附加属性。预览中的字体选择命令明确为模拟消费者操作，没有声称执行Windows原生对话框或通过物理输入/VoiceOver验收。

旧详情滚动与对齐约束仍让部分组在宽面板中保持窄或紧凑排列，下一步迁移整体Inspector布局和空状态。动态菜单、Gallery剩余行为、其余框架及平台和Release/AOT检查仍在范围内；证据见 `artifacts/custom-control-refactor-20261028/inspector-component-phase.json`。

## Inspector 响应式容器、空状态与焦点

`DevToolsInspector`新增紧凑阈值、只读布局状态、空消息、详情滚动策略及布局/数据可视状态。默认模板在1040 DIP并排、480/240 DIP上下排列，复用GridSplitter。详情区使用有限宽度和拉伸对齐，默认禁用横向滚动；自定义宽内容可显式启用。原生548/464/224 DIP视口的横向范围均为0，分类行不再在宽面板内保持窄或紧凑模式。窗口不再创建初始文字占位，只更新空状态，从4087行降至4079行。

310反复调整测试发现，窄屏分隔条的局部尺寸在返回宽屏后留下空白，树区只有258.28 DIP而期望767.8 DIP。按轴保存用户尺寸并退出不活动轴的局部值后311为9/9。160原生运行完成，但截图发现240 DIP下草稿仍在视口外；加入可关闭、随模板和焦点校验的延迟显示行为后，162输入框位置344.857加高度28，完整落在372.857 DIP视口内。

最终完整模板314为562/562，macOS定向145为413/413，Gallery146为230/230，无失败或跳过，专项有交叠。313/161缺少Visual命名空间导致的编译失败未计作运行通过。原生162已查看8张当前Metal帧，覆盖连续原生缩放、分隔条程序化移动、草稿保留及显示、宽屏尺寸恢复、更换模板、搜索焦点和中文空消息。物理拖动/键盘/IME及口述VoiceOver尚未计为通过；整体目标仍在进行。证据见 `artifacts/custom-control-refactor-20261028/inspector-shell-phase.json`。

## Gallery TreeView 数据控件

`GalleryTreeView` 继承原生 `TreeView`，默认层级数据模板位于 `Themes/GallerySelection.jalxaml`。页面通过 ItemsSource、PublishSelection 和可选 SelectionChangedCommand 配置；不再由行为递归构建 TreeViewItem。节点展开、容器、键盘和自动化继续使用框架合约，支持源集合更新、中文数据和消费者模板。保留原生样式键，通过 ItemTemplate 元数据提供 Gallery 默认表示，避免跨字典 BasedOn 查找空值导致原生模板丢失。

定向151为6/6，完整Gallery152为236/236，无失败或跳过。148的隔离原生模板注册缺失、149/150的样式继承失败均保留。原生163的程序化检查完成，但截图发现检查窗口的框架与Gallery主题不同步；164构建未启动，165改为调用生产主题服务并使用实际背景资源键。

原生165已逐张查看7张Metal帧，覆盖1000/480 DIP、深浅主题、选择Cat、中文数据替换、模板重建和消费者标题模板。程序化焦点、Right展开和自动化展开通过；未计作物理输入、IME或口述VoiceOver验收。TreeSelector中的递归节点构建及弹出层补丁、Gallery其余行为职责、框架组合UI和平台/Release/AOT检查仍在进行，整体目标保持active。证据见 `artifacts/custom-control-refactor-20261028/gallery-tree-phase.json`。

## TreeSelector 层级数据、标签模板与状态

TreeSelector现在直接接受ItemsSource与HierarchicalDataTemplate，子数据保持数据形态，由标准容器生成器创建、绑定和回收。层级绑定随属性和集合更新；级联、搜索、路径、选择和检查集合覆盖折叠后代。显式父项保留根条目模板继承，显式子模板选择器返回null时保留默认文本表示。移除数据后清理选择与检查状态，动态增删子项会重新计算父级的完整/半选状态。

TreeSelectorSelectionItem以ContentControl、可选PART_RemoveButton及RemoveRequested路由事件表达可移除选择。默认标签和环绕面板位于TreeView.jalxaml，通过SelectionBoxItemTemplate替换；不再由C#构建Border/StackPanel/TextBlock/Button。只读呈现依赖属性、Selection/Search/DropDown可视状态与模板触发器驱动标签、路径、搜索、空提示、清除和节点可见性。Enter/Space/Down/F4打开、Escape关闭，尺寸变化后重新定位弹出层，替换模板会关闭旧弹出层并释放部件事件。

GalleryTreeSelector只设置默认层级模板、容器样式和状态/命令合约，原TreeControlsBehavior文件已移除。代码示例改为原生ItemsSource/HDT，中文与英文说明同步。其余25个适配器未发现C#组合控件树；视口布局与拖放反馈仍属于后续控件职责迁移。

回归中保留了首次弹出筛选失效、展开箭头被模板默认值覆盖、浅色切换后旧标签画刷残留，以及双集合观察器引发的单选重入等失败证据。修复后最终完整模板321为571/571，macOS定向153为418/418，Gallery161为241/241，无窗口162为5/5，均无失败或跳过，专项有交叠。

原生172已逐张查看11张Metal帧，覆盖1000/480DIP、深浅主题、祖先路径、11项级联后8项半选、中文搜索、模板恢复、焦点与Space移除标签、数据替换。弹层起点204DIP与触发器162+42DIP底边一致；重建后实际显示全部8个可见节点标题。此为程序化焦点、路由与自动化调用，不计作物理输入、IME、触控或口述VoiceOver验收。ColorPicker、其他框架组合、Gallery布局/拖放反馈及平台/Release/AOT检查仍在进行，整体目标保持active。证据见 `artifacts/custom-control-refactor-20261028/gallery-tree-selector-phase.json`。

## 颜色控件模板与复用部件

ColorPicker 原先由单个 OnRender 同时布局和绘制色谱、滑条、预览及十六进制文字。现在保留 Color、显示选项、紧凑模式、形状和 ColorChanged 合约，默认组合完全位于 Themes/Controls/ColorPicker.jalxaml。ColorSpectrum、Slider 派生的 ColorRamp 和 ColorPreview 可独立使用或替换模板；内部叶子仅绘制渐变、选择标记和透明棋盘，缓存按当前尺寸有界保存。色环具备实际绘制和命中区域，滑条支持竖向、反向、键盘与原生 RangeValue 合约。

HSV 状态保留灰阶色相。真正的 TextBox 支持 #RRGGBB/#AARRGGBB、草稿、提交、取消、行内错误及输入法组合保护；Color 双向绑定在部件和自动化修改后仍保留。模板可以省略默认部件，退役的输入、色相、透明度和色谱部件不能再修改当前控件。输入字体、背景、前景和 ErrorForeground 支持宿主配色；Inspector 已接入自己的颜色角色。默认布局按内容收紧，错误文字在深色、浅色和已检查宿主配色下的声明色对比度为5.00–6.57，原生实色像素为5.43–6.96，仅代表这些提示文字的检查范围。

最终完整模板333为588/588，扩展退役检查334为1/1，macOS相关合约164为327/327，Gallery164为241/241，无窗口165为5/5，均无失败或跳过，专项存在交叠。旧零字号测试改用实际模板与独立弹出内容，并修复自动完成建议缺少字体转发的问题；编辑器保留既有每行至少一个布局单位的滚动合约。中间编译、原生库路径、继承枚举触发值及检查程序步长假设失败均保留在阶段记录中。

已查看原生176的13张Metal帧与179的4张当前部件帧，覆盖1000/480DIP、深浅主题、色环与矩形、草稿和错误、宿主配色、替换及恢复模板、竖向和反向滑条。程序化焦点、路由指针/按键、捕获释放、Value/RangeValue 自动化及实际 Gallery VM 双向绑定通过；物理输入、IME、触控、口述VoiceOver以及Windows/Linux和Release/AOT未计为通过。Gallery布局/拖放、CalendarItem及菜单/浮层等剩余实现继续迁移，整体目标保持active。证据见 `artifacts/custom-control-refactor-20261028/color-picker-phase.json`。

## Gallery 视口页与有限布局

GalleryViewportPage 继承导航兼容的 GalleryPage，用只读 ViewportHeight/IsViewportBound、可配置最低视口高度和匹配开关表达布局状态。默认模板位于 Themes/GalleryViewports.jalxaml；ThemeColorsPage 的默认模板绑定实际根高度。原 ViewportHeightBehavior 已移除，控件只跟踪自己的祖先和宿主，不再枚举宿主子树或写入页面 Height。调用者高度绑定、最小/最大尺寸、延迟挂载、卸载、重挂载及自定义模板均有检查；视口发布后的重新测量与顶端对齐消除了居中空白。

实际页面的筛选区通过宽/窄可视状态和分组换行适配1000/480/320 DIP；资源元数据采用有限列宽、裁剪和省略，避免覆盖复制操作。预览 DataTemplate 与真正的页面 ControlTemplate 分开验证，保留此前误改预览、绑定反馈造成重叠等失败证据；最终检查直接约束请求视口，而不依赖可能被内容撑大的页面宽度。

最终定向174为12/12，完整Gallery177为248/248，无窗口178为5/5，无失败或跳过。原生185已查看8张当前Metal帧，使用实际资源服务与647项资源，按视口只创建可见容器；程序化缩放、内部滚动、720/800高度绑定、模板恢复、焦点与Accent筛选、重挂载和640最低高度通过。页面/模板/视口高度一致，根偏移为0；明确宿主内边距20时偏移为20。历史帧名viewport-light-480在此轮实际宽度为320，以日志宽度为准。物理输入、IME、触控、口述VoiceOver及跨平台/Release/AOT未计为通过。拖放反馈、剩余框架组合及更广的应用导航/缓存检查继续进行，整体目标保持active。证据见 `artifacts/custom-control-refactor-20261028/gallery-viewport-phase.json`。

## Gallery 拖放目标控件

GalleryDropTarget 以 ContentControl、只读拖入/允许状态、可选命令和复制后的不可变载荷实现目标逻辑，默认外观位于 GalleryInteraction.jalxaml。七处页面及预览目标改用此控件；DragDropBehavior 只保留原生拖动源、图像与 Shell 适配。状态切换不再覆盖调用者 Background/BorderBrush，数据上下文、角色、命令、禁用、卸载和替换模板会清理旧目标状态。当前示例也使用实际控件。

最终定向179为7/7，完整Gallery182为255/255，无窗口183为5/5，无失败或跳过。原生188已查看11张当前Metal帧，覆盖1000/480DIP、深浅主题、活动拖放中切换主题、拒绝/接受、文本/文件名/自定义载荷投递、模板恢复、禁用及可见代码示例。187程序检查完成但截图滚动方向错误，已保留并用188重新验证。文件载荷为程序化事件，不代表物理文件存在或操作系统拖动验收；物理输入、IME、触控、口述VoiceOver及跨平台/Release/AOT未计为通过。整体目标继续active，Calendar和剩余框架组合继续迁移。证据见 `artifacts/custom-control-refactor-20261028/gallery-drop-target-phase.json`。

## Calendar 日历系列模板

Calendar、CalendarItem、CalendarDayButton 和 CalendarButton 的组合外观全部移入 Calendar.jalxaml，移除手工子树、命中矩形和组合 OnRender。日期/月份模型只提供数据和命令，同一模板内保持实例稳定；替换模板时重建模型以退役旧命令。可选导航/视图部件释放旧订阅与 Owner，真实按钮保留日期范围、禁用日期、选择集合、绑定、键盘/滑动和自动化接口。

只读标题、导航、字体与视图尺寸驱动默认模板。CalendarItemForeground/CalendarForeground 区分主题状态颜色和调用者 Foreground，选择及主题切换后仍保留自定义样式。星期/月标题使用当前区域信息，零字号抑制文字，28字号的星期为22并增加行高/间距；边界外空格保持隐藏而不改变网格位置。导航使用矢量路径，显式清除标准按钮60的最小宽度，使其保持32列宽；月/年/十年视图维持稳定宽度。

最终完整模板344为598/598，Calendar相关macOS174为37/37，完整Gallery188为255/255，无窗口189为5/5，无失败或跳过，定向存在交叠。中间的字体/主题继承、独立控件空颜色、调用者样式优先级及验收范围错误均保留。附带DataGrid自动化断言在重构前188程序集上也复现同一两子项/单子项差异，未混记为本次日历失败。

原生193已查看10张当前Metal帧，覆盖1000/480DIP、月/年/十年、真实按钮程序化焦点与点击、实际Gallery VM、活动页面主题切换、模板恢复与旧命令、禁用、大字号、9999日期边界、Grid接口和调用者配色。物理输入、IME、触控、口述VoiceOver、跨平台及Release/AOT未计为通过。剩余菜单滚动宿主、DatePicker弹出内容及其他浮层继续迁移，整体目标保持active。证据见 `artifacts/custom-control-refactor-20261028/calendar-phase.json`。

## 菜单弹窗滚动宿主模板

MenuPopupScrollHost 改为可替换模板的 Control。MenusToolbars.jalxaml 承担滚动视口、原有容器面板和上下重复按钮；只读依赖属性发布溢出、边缘、偏移与有效按钮高度，可选模板部件和可视状态承担扩展。现有 Menu、ContextMenu、MenuFlyout 和嵌套项使用稳定的容器集合，保留原有逻辑归属、元数据和返回内联面板路径。

替换模板退订旧部件；卸载后释放可视容器并暂停同步，旧宿主的集合或布局操作不会夺走活动弹窗的项目。MenuPopupScrollViewer 在弹窗的无限高度测量探测期间维持已建立的有限视口，并报告自然内容高度，修复仅在真实外部弹窗中出现的偏移立即归零。普通/精确滚轮及边缘事件归属保留，小尺寸限制箭头高度，空间充足时恢复无溢出状态。

最终模板347、菜单回归183、Gallery191和无窗口192的逐项结果见阶段记录，无失败或跳过，定向用例存在交叠。346命令漏设原生库路径产生初始化失败，修正后重跑，保留原始记录。原生194—196揭示的实际视口归零和197验收反射缺陷均已修复并留证。

原生198的9张Metal帧已全部查看，覆盖实际Gallery ContextMenuPage、260×360独立弹窗、32/120DIP滚动、明暗主题、替换模板保留120、重新打开归零、旧宿主隔离、嵌套Enter执行一次并关闭整条菜单。物理输入、IME/触控、口述VoiceOver、其他平台和Release/AOT尚未验收。MenuFlyoutPresenter、菜单项组合绘制和弹窗外框继续迁移；整体目标保持active。证据见 `artifacts/custom-control-refactor-20261028/menu-scroll-phase.json`。

## MenuFlyoutPresenter 默认模板

MenuFlyoutPresenter 的 Border 和滚动宿主移入 MenusToolbars.jalxaml，删除构造函数子树、手工描边和内边距排列。依赖属性、TemplateBinding、可选 PART_ScrollHost 和禁用状态提供扩展；调用者的零圆角、零边框、零内边距与画刷/绑定均保留。模板外框显式转发 UseLayoutRounding，1/1.25/1.5/2 倍缩放下描边位于弹窗内部。

卸载后退订源集合，缓存弹窗重新布局/加载时恢复；替换模板清空旧宿主容器并恢复滚动偏移。子菜单跨模板包装层查找呈现器外观。MenuFlyoutPresenterStyle 改为依赖属性，绑定或运行时修改能更新已有呈现器，清空回到主题默认。Escape 关闭后回到触发按钮，首个启用且可见项目获得初始焦点。

最终模板348、菜单188、Gallery194及无窗口195的逐项结果保存在阶段记录，定向结果存在交叠。中间无 Application 的画刷夹具、分数缩放转发和无关 ContentDialog 链接范围问题均留有原始记录。

原生200的13张Metal帧全部查看，覆盖独立弹窗、96DIP滚动、明暗主题、活动呈现器样式变化、零外框、替换模板保留偏移/绑定、卸载后源变化、嵌套命令以及Escape焦点返回。该夹具使用实际Gallery页面和VM，但明确将页面原有ContextMenu演示替换为真正的MenuFlyout；页面生产示例本身还须修正。物理输入、IME/触控、口述VoiceOver、其他平台和Release/AOT未验收。菜单项组合绘制、外框工厂、MenuItem自动化调用缺口和DatePicker弹出内容继续处理，整体目标保持active。证据见 `artifacts/custom-control-refactor-20261028/menu-presenter-phase.json`。

## 菜单自动化命令链路

MenuItemAutomationPeer.Invoke 进入现有虚拟 OnClick，统一勾选、Click、命令和菜单关闭顺序，尊重 RoutedCommand 目标、参数、CanExecute 与 StaysOpenOnClick。勾选调用使用 SetCurrentValue 保留调用者双向绑定；Toggle 接口仍只切换状态。新增6项共享回归在旧实现上5项失败，修复后扩大菜单回归190为245/245。

原生201的9张当前Metal帧已全部查看，实际ContextMenuPage附加嵌套测试命令，通过自动化Invoke执行一次并关闭根菜单和子菜单，滚动、主题和模板恢复仍正常。本阶段未改主题或布局，没有重复计入此前模板/Gallery全套结果。物理输入、口述VoiceOver、跨平台及Release/AOT仍未验收。整体目标保持active，继续菜单项和浮层模板迁移。证据见 `artifacts/custom-control-refactor-20261028/menu-automation-phase.json`。

## MenuFlyout 菜单项系列模板

MenuFlyoutItem、ToggleMenuFlyoutItem、MenuFlyoutSubItem 和 MenuFlyoutSeparator 的默认组合外观全部移入 MenusToolbars.jalxaml，删除父控件手工文字测量、勾选/箭头/分隔线绘制。只读状态、可选部件、CommonStates/CheckStates 和真实文本/路径/Border 元素承担模板扩展。原有键盘、鼠标、命令、自动化与弹窗归属流程保留，勾选更新保留双向绑定。

MenuIconPresenter 只绘制一个受限尺寸的旧式几何/字形图标；UIElement/IconSource 内容通过其默认 ContentPresenter 模板承载。IconSource 只在源变化/重载时创建缓存图标，卸载退订并释放旧可视内容。冻结、开放/闭合/混合图形、竖线和字形语义保持，菜单行不创建替代子树；弹出外框工厂后续处理。

模板保留调用者和 CSS 前景色优先级，在绘制前同步懒求值的继承色，父容器只换色时也即时更新。字号、字体和字重在测量前同步到只读模板状态；图标前景色变化使缓存的可视内容重绘，默认内容宿主显式转发当前画刷。内容最小高度、独立靠右快捷键和实际模板坐标验证保留 CSS 背景、边框、圆角、内边距、零字号与大字号行为。中间缺少命名空间、状态组位置、重叠零尺寸触发器和旧记录器坐标假设均已修正并保留记录。

最终检查为 template-suite350 598/598、menu-items199 342/342、gallery-suite198 255/255、gallery-headless199 5/5，无失败或跳过，定向结果存在交叠。原生202发现的浅色图标缓存、大字号继承与内容对齐已修正；macOS缺少SymbolIcon所用字形字体仍单独跟踪，当前可视内容验收改用PathIcon。原生 203 的 17 张当前 Metal 帧全部查看，覆盖明暗主题、滚动/换模板、动态样式、图标/分隔线、双向绑定勾选、大字号、嵌套命令及 Escape 焦点返回。该夹具使用实际 Gallery 页面/VM，但明确替换生产页的 ContextMenu 演示。物理输入、IME/触控、口述 VoiceOver、其他平台和 Release/AOT 尚未验收。菜单本体、浮层外框工厂和 DatePicker 等继续迁移，整体目标保持 active。证据见 `artifacts/custom-control-refactor-20261028/menu-items-phase.json`。

## DatePicker 输入与日历浮层模板

DatePicker 的标题、真实 DatePickerTextBox、按钮、Popup 外框与 Calendar 全部移入 Calendar.jalxaml；删除父控件组合绘制、手工测量和命中区域。可选模板部件、只读呈现/校验属性及 Common/DropDown/Validation 状态支持换肤与替换模板。文本支持草稿输入、Enter/失焦提交、Escape 恢复与输入法组合保护，保留 Text、格式、文化、校验事件和绑定契约。

禁用日期集合独立于模板 Calendar，并保留原有公共构造函数及范围校正行为。换模板退订所有旧部件及 Popup 属性桥接，释放旧禁用日期订阅，保留打开意图、日历视图与绑定，开关事件仅触发一次。继承字体在测量/绘制前同步，零字号隐藏标题间距。Gallery 继续使用真实 DatePickerPage/DatePickerViewModel，三个常用字段通过主题左对齐。

当前检查：template-suite351 598/598、datepicker206 115/115、gallery-suite202 255/255、gallery-headless203 5/5，无失败或跳过，定向结果存在交叠。原生205的15张当前Metal帧全部查看，涵盖1000/480宽度、明暗主题、草稿提交、真实日期按钮选择、错误恢复、禁用日期、开着换模板、内部/外部弹窗、Escape焦点返回、禁用与28字号。运行记录开/关各3次、校验事件1次。大字号在固定200宽输入框内裁切并可编辑，未溢出控件。物理输入、IME/触控、口述VoiceOver、其他平台与Release/AOT未验收。框架菜单及浮层工厂等继续迁移，整体目标保持active。证据见 `artifacts/custom-control-refactor-20261028/datepicker-phase.json`。

## Menu / MenuItem 本体模板及生命周期

Menu 和 MenuItem 的默认外框、横向项目宿主、标题 ContentPresenter、图标、勾选、快捷键与箭头均在 MenusToolbars.jalxaml 中定义；删除父控件手工文字测量和组合绘制。只读呈现属性、可选部件与 CommonStates/CheckStates 提供扩展。标题支持可视内容、模板、选择器和格式；停靠布局在有限宽度下保持快捷键靠右并裁切剩余标题，字体与调用者前景色继承仍生效。

关闭时的隐藏零尺寸模板宿主保存子菜单容器，打开时移动原容器。弱关联按数据引用匹配（装箱值按值），避免打开状态插入/删除、相等但不同实例的数据与部分暂存宿主导致重复或串项。旧 ItemsPresenter 在模板销毁时明确退役并释放所有者/滚动引用，阻止旧绑定父级已清空后的迟到测量/排列重新偷走当前容器。原生206至209暴露的列绑定、主题外框与旧宿主布局问题均保留记录，当前版本已修正。

MenuFlyout 首次焦点请求前准备当前模板宿主，并通过逻辑目标完成尚未建立原生 PopupRoot 时的 Escape、左右切换和命令关闭。MenuBar 的项目归属在集合变化时维护，布局前和 Clear/Reset 后都正确；两级集合建立逻辑子级，视觉宿主保留原逻辑父级，使弹出前后命令绑定继承真实 ViewModel，换 DataContext 与移除后也正确更新。另补齐 MenuBar/MenuBarItem 的 ContentProperty(Items) 元数据，修复实际 Gallery 模板装载时菜单层级为空的问题，并增加标记解析回归。菜单栏外观、菜单浮层外框工厂仍继续迁移。CSS 动态状态按既有层级覆盖主题 hover；普通内联基线仍服从原有 trigger 优先级。 缓存弹出层打开前重新检查绑定上下文；单项 DataContext 绑定在树重新激活时发现对象或提供者变化即退订旧源并重新激活，显式 Source 与其它相对源行为保持。旧数据源停止更新目标、新源继续更新与实际 ViewModel 替换均有回归证据。原始鼠标路由载体明确清除默认模板，避免全局主题影响其手工布局。

当前检查为 template-suite354 598/598、menu-rows235 704/704、gallery-suite212 255/255、gallery-headless213 5/5，无失败或跳过，定向用例存在交叠。原生214的22张当前Metal帧全部查看，覆盖1000/480宽度、明暗主题、打开换模板与旧宿主重放、快捷键/图标/勾选、三层菜单命令、Escape焦点返回、禁用和大字号；另在实际MenuBarPage/MenuBarViewModel中验证Escape、右键切换及真实命令，最终状态为 `Edit > Undo clicked`。MenuPage只增加限定QA命令/图标/勾选探针。物理输入、IME/触控、口述VoiceOver、其他平台与Release/AOT未验收。整体目标保持active，证据见 `artifacts/custom-control-refactor-20261028/menu-rows-phase.json`。

## MenuBar / MenuBarItem 默认模板

菜单栏外框和横向项目面板、菜单项标题与完整交互区域的高亮均迁入 MenusToolbars.jalxaml；删除构造函数 UI、父控件组合绘制、手工文字测量及视觉子级/布局模拟。稳定只读 Items、HasItems、IsMenuOpen 与字体/颜色/高亮呈现属性配合可选部件、CommonStates/MenuStates 提供扩展。逻辑父级随集合维护，退役面板清空且不能偷走当前项目；禁用关闭菜单，关闭后延迟焦点请求不会重新抢焦点。键盘单元用例改为可见的无窗口宿主；旧 CommandBar CSS 夹具明确应用已有模板和确定的调色板，并按当前模板层级验证全局坐标，未改变 CommandBar 产品代码。

最终检查为 menu-bar-template355 598/598、menu-bar242 726/726、gallery-modules214 255/255、gallery-headless215 5/5，无失败或跳过，定向集合存在交叠。原生216的20张实际Metal帧全部查看，覆盖真实MenuBarPage/MenuBarViewModel、明暗主题、1000/480宽、打开时更换根及菜单项模板、退役面板重放、替换ViewModel后实际命令、动态菜单集合、禁用、大/零字号、Right/Enter/Escape与焦点返回，最终状态为 `Tools > Run clicked`。物理输入、口述VoiceOver、IME/触控、Windows/Linux与Release/AOT未验收；菜单浮层组合工厂继续迁移，整体目标保持active。证据见 `artifacts/custom-control-refactor-20261028/menu-bar-phase.json`。

## ContextMenu 弹出层模板与真实交互示例

ContextMenu 的 Popup、外框与 MenuPopupScrollHost 均迁入 MenusToolbars.jalxaml 默认模板，取消备用 ItemsControl 面板和 C# 组合工厂。只读有效背景/边框画刷、可选部件及 PopupStates 提供扩展；逻辑打开意图、绝对定位/偏移、公共事件、原有渐入/渐出与显式零外框值保留。替换模板会停止旧计时器、退订旧弹窗并清空旧宿主，恢复当前模板和原始容器，打开状态下不重复公共事件。数据容器按对象身份复用，标题模板/格式/样式变化更新同一容器。

菜单以 PlacementTarget 作为其未被调用者另行拥有时的逻辑父级，并监听触发区域的数据上下文变化，重新激活缓存项目绑定；更换触发区域会退订旧区域。调用者显式 DataContext 保持优先。此修复由原生218和真实页面单元用例中“更换 ViewModel 后命令仍指向旧对象”的失败推动，未修改 Core/Binding.cs。复用已有 ContextMenuAutomationPeer 暴露 Menu 语义。菜单栏延迟焦点单元夹具先排空前序弹窗留下的队列，再检查自身关闭请求，产品菜单栏代码未变化。

Gallery 的 ContextMenuPage 三个操作现在绑定专用 ContextMenuViewModel 命令，结果由绑定呈现，空状态仍用 DynamicResource 本地化。页面与代码示例移除 code-behind 点击修改标签写法，并保留自动 ViewModel 装配架构。宽窄布局下真实页面命令、更换 ViewModel 后的缓存菜单命令与旧模型不串用均有回归。

当前检查为 context-menu-template356 598/598、context-menu249 737/737、gallery-modules218 257/257、gallery-headless219 5/5，无失败或跳过，定向用例存在交叠。原生219的19张实际Metal帧全部查看，覆盖实际ContextMenuPage/VM、明暗主题、1000/480宽、打开时换空模板再恢复、外部弹出窗口、替换ViewModel后真实命令、调用者自定义外框、大/零字号、动态项目、实际嵌套菜单及限定QA命令、Escape与焦点返回；打开/关闭各6次，最终结果 `Nested action`。物理输入、IME/触控、口述VoiceOver、Windows/Linux及Release/AOT未验收；MenuItem、MenuFlyoutSubItem 等其余浮层组合工厂继续迁移，整体目标保持active。证据见 `artifacts/custom-control-refactor-20261028/context-menu-phase.json`。

## MenuItem / MenuFlyoutSubItem 子菜单弹层模板

两类菜单项的 Popup、外框与 MenuPopupScrollHost 迁入 MenusToolbars.jalxaml 默认模板，移除 C# 组合工厂。可选部件、只读继承外框/定位/打开/项目呈现属性与 SubmenuStates 提供扩展。MenuItem 取消隐式备用面板，保留模板暂存宿主，并在空模板/正常模板切换时保留原始数据容器（按引用匹配相等但不同的数据）；旧 Popup 回调/宿主退役，外部关闭走正常事件与容器返回路径。

MenuFlyoutSubItem 的稳定可观察集合及只读 Items/HasItems/IsSubMenuOpen 配合逻辑父级维护、缓存绑定激活和原位同步，支持动态/Reset 集合、继承字体颜色、父级外框、禁用/空集合关闭及关闭后延迟焦点取消。旧夹具显式加载子项专用模板，替换后重新获取当前宿主；新增用例检查真实部件、可视状态、相同对象容器、逻辑绑定、集合、禁用、可选部件和真实文字布局。

当前检查 submenu-template358 598/598、submenu257 746/746、gallery-modules222 257/257、gallery-headless223 5/5，无失败或跳过，集合有交叠。原生229的22张及原生230的18张帧全部查看，覆盖真实Menu/MenuBar页面与VM、三层菜单命令、明暗主题、1000/480宽、勾选与快捷键、大小字号、父级外框、打开时更换行/子菜单模板、旧宿主重放、滚动、Escape/焦点、实际命令与更换ViewModel。MenuFlyout 使用限定QA夹具，产品页仍继续改为真实MenuFlyout。替换模板后新 Popup 可按默认策略使用内部浮层；原QA仅检查被退役实例上的外部窗口偏好，现已按实际宿主验收。

重复图像预览曾被误判为空白，逐文件核验后，换模板前后及后续绘制/布局/字体/叶子模板刷新帧均具有相同SHA-256和198个文字区域深色像素，系统窗口截取也确认文字正确；已撤回误判记录，未修改渲染实现。物理输入、IME/触控、口述VoiceOver、Windows/Linux及Release/AOT未验收。整体目标继续active，证据见 `artifacts/custom-control-refactor-20261028/submenu-popups-phase.json`。

## 通用 FlyoutPresenter 模板

FlyoutBase 的 C# Border 包装迁入可复用 FlyoutPresenter（ContentControl）及 Containers.jalxaml 默认外框/内容模板。可选 PART_Root/PART_ContentPresenter、只读 IsOpen、CommonStates/PopupStates 和 FlyoutPresenterStyle 支持调用方内容模板、文字格式、字体与外框（含零值）；MenuFlyoutPresenter 或调用方提供的 FlyoutPresenter 不会重复包装。原生 Popup 保留为窗口与输入基础设施。

私有 Popup 明确维护当前目标的 DataContext，缓存内容在更换 ViewModel 后重新绑定；清除外框样式覆盖会恢复调用方原样式。关闭事件只通知一次，Closed 回调重新打开时旧 Hide 不会覆盖新的状态，禁用外框会关闭并拒绝打开。主题测试改验公开模板行为，既有 CommandBar 溢出夹具按实际 ContentControl 宿主验证，未修改 CommandBar 或通用渲染实现。

当前检查 flyout-frame-template359 598/598、flyout-frame266 761/761、gallery-modules224 257/257、gallery-headless225 5/5，无失败或跳过，集合有交叠。原生235的15张帧及原生236的20张 MenuBar 回归帧全部检查，覆盖明暗主题、1000/480宽、自定义外框、28字号、打开时换模板、原命令/替换VM、禁用、Escape/焦点返回、关闭回调重新打开（最终6次打开/6次关闭）及菜单条换模板。原生235在真实Gallery页面/VM中使用限定QA的CommandBarFlyout，并仅在QA中停用该页原有Flyout行为，避免双重弹层。

Metal帧与程序化交互通过；物理输入、IME/触控、口述VoiceOver、Windows/Linux和Release/AOT未验收。整体目标继续active，证据见 `artifacts/custom-control-refactor-20261028/flyout-frame-phase.json`。

## RazorItemsHost 虚拟列表模板

三个滚动/直通默认模板、四个虚拟化面板模板迁入 Themes/Controls/RazorItemsHost.jalxaml，移除 C# 默认可视树工厂。默认样式、可选 PART_ItemsPresenter/PART_ScrollViewer、只读 ResolvedScrollHost/ScrollOrientation 与 CommonStates/LayoutStates/ScrollHostStates 支持替换；样式触发器保留调用方本地 Template/ItemsPanel 优先级。ItemsPresenter 仍直接作为 IScrollInfo 内容，沿用正常 ItemsControl 宿主退役流程。

原生换行末项检查发现既有滚动轴错误，现按换行排列的垂直方向计算滚动轴，并用于滚动模板及无界约束诊断。单元检查覆盖两种堆叠和两种换行方向的末项，保留惰性百万范围、滚动模式转换、无界恢复及虚拟化绑定。Gallery 实际页面与显示的代码示例显式开启 CanContentScroll，按说明复用外层滚动。

当前检查 razor-items-template361 598/598、razor-items270 805/805、gallery-modules228 257/257、gallery-headless229 5/5，无失败或跳过，集合有交叠。原生238的11张帧全部核验，覆盖真实 RazorVirtualizePage/VM 十万数据（15行实现）、集合与VM替换、打开页面时换模板、明暗1000/480宽，以及百万惰性范围（34行实现）滚到999999、换行滚到1999、None及禁用。原生237最初未断言换行末项且实际未滚动，已明确记录此缺口，最终238补上断言并通过。物理输入、IME/触控、口述VoiceOver、Windows/Linux及Release/AOT未验收。整体目标继续active，证据见 `artifacts/custom-control-refactor-20261028/razor-items-host-phase.json`。

## TextBoxBase 默认编辑菜单

文本框默认编辑菜单改用标准 ContextMenu/MenuItem/Separator 语义行与现有 Themes 默认模板，移除 C# Popup、Border、Grid、TextBlock 组合树、逐行悬停处理及专用动画计时器。沿用 ContextMenuService 的目标/定位/打开关闭事件；每次打开计算选择、撤销重做、只读与剪贴板条件，关闭并恢复焦点后调用已有编辑 API。调用方或祖先菜单、本地 null、禁用服务/编辑器、卸载及取消打开均有检查。

macOS 已注册的系统文本菜单保留其原生请求流程；通用模板菜单在该提供程序缺席时处理 Apps/Shift+F10。UIElement 已调用虚拟 OnKeyDown，因此移除 TextBoxBase 重复注册的实例键盘处理器，取消时只请求一次且不吞键。ContextMenu 补上自身 Escape 关闭路径。旧主题测试改验公开模板，新增链接夹具按当前未改动的主题最小高度38验证（旧期望36）。

当前检查 textbox-menu-template362 598/598、textbox-menu273 1024/1024、gallery-modules230 257/257、gallery-headless231 5/5，无失败或跳过，集合有交叠；前置文本专项279项通过。原生240的13张帧全部查看，真实Gallery TextBoxPage/VM 中通过键盘打开、Enter剪切/粘贴、自动化撤销重做、只读、Escape、自定义/本地null菜单、禁用拒绝、明暗1000/480宽及打开时换模板，最终Z beta、7次打开/7次关闭、焦点返回；仍由QA持有时恢复剪贴板。QA仅在本进程停用系统文本菜单提供程序以检查通用模板，未验收系统菜单实际显示。物理输入、IME/触控、口述VoiceOver、Windows/Linux和Release/AOT仍未验收。整体目标继续active，证据见 `artifacts/custom-control-refactor-20261028/textbox-context-menu-phase.json`。

## FlowDocumentScrollViewer 默认文档界面

滚动外框、工具栏与查找栏迁入 Themes/Controls/FlowDocumentScrollViewer.jalxaml。可选 PART_ContentHost/PART_FindTextBox、只读 DocumentView/查找可见状态、SearchText 依赖属性及 CommonStates/DocumentStates/FindStates 支持替换模板，构造函数只保留语义文档渲染对象及命令，不再创建或追加滚动外框。工具/查找状态驱动真实界面，支持缩放、百分比、查找下一处、关闭、原有打印命令以及搜索框焦点和 Enter/Escape。工具栏项目通过祖先绑定定位拥有者，百分比使用标准内联绑定。

模板退役时解除旧宿主/搜索框回调并清除旧内容；空模板与正常模板切换保留原渲染对象、文档选择、查找、缩放和物理滚动，旧宿主或旧输入事件不影响当前界面。内部查找/缩放更新保留调用方绑定。既有 CSS、原始文档/编辑器所有权、嵌入对象与投影、分页导航、单一Tab目标夹具明确使用当前默认模板。

实际长文档缩放检查发现原实现只扩大布局滚动范围，未应用对应的文字绘制缩放，导致底部空白；渲染对象现在同时使用对应的视觉缩放，新增100/110/140百分比末行位于视口的检查。原生242及关闭绘制缓存的同一二进制具有字节相同的空白底部（正文亮色像素0），最终244底部显示到第80节，正文亮色像素38487；未修改通用渲染器。

查找按钮状态检查也暴露 CommandManager 失效通知未传给 RoutedCommand。现通过弱注册表重查活动命令，命令监听复用既有 WeakEventManager，检查全局状态、显式通知与移除/来源，以及控件监听和命令生命周期，避免退役控件被静态命令保留。扩大命令生命周期组后，测试夹具静态延迟主题字典的跨线程复用产生52项失败，已改为线程内缓存并通过；保留产品资源字典线程约束。

当前检查 flow-document-template363 598/598、flow-document286 1444/1444、gallery-modules232 257/257、gallery-headless233 5/5，无失败或跳过，集合有交叠；前置扩大专项583项通过。原生244的12张帧全部核验，覆盖真实框架查看器/绑定模型、原文选择、长滚动、换模板和旧宿主/输入重放、1000/480明暗主题、大字号、隐藏/空/替换/禁用状态及真实Next/Close按钮，下一处推进221个原文位置单位。限定QA使用调用方文档内容和前景色，不是产品Gallery页面；打印对话框、物理输入、IME/触控、口述VoiceOver、Windows/Linux及Release/AOT未验收。整体目标继续active，证据见 `artifacts/custom-control-refactor-20261028/flow-document-scroll-phase.json`。

新语法盘点覆盖917个Controls源文件，识别222个UI创建候选、1105个待解析的隐式new和71处AddVisualChild调用；这些包含生成容器、语义文档模型、绘制适配及平台基础设施，不作为剩余控件数或完成比例。后续继续逐类核验文档分页/阅读器、编辑器及滚动/工具等默认界面和Gallery真实示例。

## 分页文档与阅读器默认模板

FlowDocumentPageViewer 与 FlowDocumentReader 的外框、查找栏、导航、阅读模式及缩放操作迁入 Themes/Controls/DocumentViewers.jalxaml。控件保留语义页及各阅读模式对象，通过只读 PageView/ActiveViewer 依赖属性、可选模板部件和可视状态支持扩展，不再在构造函数追加默认外框视觉子项。替换为空模板再恢复仍保留原对象、文档、页码、选区、搜索与调用方绑定；退役宿主和输入事件无法影响当前模板。

真实模式按钮、查找、导航与缩放命令更新绑定模型，阅读器自己的查找框聚焦时保留文档选区，禁用阅读模式时通过绑定属性回退。PageViewer 的共享命令别名放在依赖属性注册后，修正与 ScrollViewer 的静态初始化循环。阅读器操作区采用带间距的自动换行布局，320/480 宽度逐项检查操作边界和实际查找/缩放，禁用态可见且无法执行按钮，再启用恢复正常。

扩大原生后端检查发现零字号文字仍尝试创建原生字体格式；NativeTextParagraph 对非正或非有限字号保留托管回退，TextBox 的零字号命中返回开头。明确启用 Metal/软件后端的既有编辑器契约通过，原生截图检查 NumberBox、AutoCompleteBox、RichTextBox 从0恢复到16px。扩大前置组同时发现12项共享/嵌套段落 balance/pretty/avoid-short-last-line 检查失败，发生于该文字保护修改前，已单列证据，未修复或声称通过。

当前选定检查 document-page-reader-template366 598/598、document-page-reader304 1473/1473、gallery-modules238 257/257、gallery-headless239 5/5，无失败或跳过，集合存在交叠；前置专项674项通过。原生250共18帧全部核验或与已查看帧逐字节核对，覆盖阅读器三种模式、原文选择与下一处、模板替换、退役部件、1000/480明暗主题、缩放、模式回退、空与禁用态，以及零字号编辑器恢复。该限定QA使用实际框架控件和调用方文档；打印对话框、物理输入、IME/触控、口述VoiceOver、Windows/Linux及Release/AOT仍未验收。整体目标保持active，继续处理DocumentViewer、滚动/工具等剩余默认界面及Gallery实际示例。证据见 `artifacts/custom-control-refactor-20261028/document-page-reader-phase.json`。

## 多页文档宿主与真实 Gallery 文档

DocumentViewer 的默认外框、ScrollViewer 与页宿主迁入 DocumentViewers 主题。只读 VisiblePageViews/布局属性、可选页宿主部件及通用/文档/显示模式状态支持定制模板，语义页保留原逻辑拥有者，不再追加到根视觉列表。新增 DocumentPagePanel 通过依赖属性排列面对页与连续页，并实现 IScrollInfo，复用现有滚动条和输入处理。退役部件清除页内容与偏移绑定，旧宿主重放滚动或布局不会影响当前页。缩放、适应、页模式、列数和偏移命令保留调用方绑定；视口适应不再调用会切换模式的命令，测量收敛后实际页范围与百分比一致。

原生缩放检查暴露语义页只改变布局、随后又用缩小边界裁切原页的问题。DocumentPageView 现在保留分页器原始布局与裁切坐标，仅缩放显示绘制；50/100/175百分比的原文字几何及页边缘颜色检查和真实窗口适应帧通过，未改通用渲染缓存。FlowDocument 接入既有搜索提供者契约，使用原文、大小写/全字边界和实际页矩形；连续页搜索定位计入缩放、页列与偏移。

Gallery 文档演示和预览绑定真实双语文档，原生显示4页并找到48处document。预览独立状态共享语义内容，通过明确绑定路径和可选StateSink回传状态，保留主题禁止DataContext赋值的架构约束；显式接收器尚未解析时不向继承模型发布临时状态。文档/上下文改变后同步刷新页数，不需再次翻页；缩放仅通过已有双向绑定传递，避免旧状态覆盖新模型。按钮宽度足以显示适应与清除操作，搜索和设置控件有可访问名称。串行测试仍可换工作线程，两处夹具重置前一Application主题状态，避免延迟资源跨线程，产品线程约束保持。

当前检查 document-viewer-template367 598/598、document-viewer314 1491/1491、gallery-modules253 259/259、gallery-headless254 5/5，无失败或跳过，集合有交叠；前置692专项通过。17张当前原生帧全部查看或与已查看帧逐字节核对，覆盖真实Gallery页面/工具栏/预览、导航与搜索、放大/适应、1000/480明暗主题、面对页/连续页及末页、换模板和退役宿主、空/禁用/恢复及上下文替换。模板替换前后的全帧像素完全一致。系统SymbolIcon缺字及Gallery外部命令可用性继续处理；本阶段未验收物理输入、该页面键盘焦点遍历、IME/触控、口述VoiceOver、打印对话框、Windows/Linux或Release/AOT，也未修复已单列的12项共享/嵌套段落换行基线失败。整体目标保持active，见 `artifacts/custom-control-refactor-20261028/document-viewer-phase.json`。

## 语义图标模板与跨平台轮廓

IconElement 继承 Control，SymbolIcon 的默认组合移入 Icons 主题，使用可选 PathIcon/FontIcon 部件、只读 Geometry/Glyph/UsesGlyph 和通用/图标状态；删除自身默认测量与绘制。安装 Segoe MDL2 Assets 的系统保留原生字形，其余平台使用729个独立码点的冻结矢量，764个枚举名保持别名。优先使用官方码点/名称轮廓，Windows 专用缺项使用有记录的语义等价或原创矢量；此覆盖不代表729种轮廓与系统字体逐形一致。固定 MIT 许可的 Fluent UI React MDL2 1.4.9 包并核验 SHA1/SHA512，托管包包含许可及来源，不分发字体，也未新增运行期依赖。

PathIcon 新增可选且校验的 ViewBox 保留图形留白，空值继续按几何边界适应；共享枚举、元素、来源与字符串渲染复用同一矢量及坐标框。运行期 XAML 转换表接入已有 RectConverter，避免矩形字面值被忽略。原生检查确认继承属性延迟解析时更换祖先前景色仍会复用旧绘制，图标基类通过弱观察和模板子树失效修复；加载后重接、卸载清理，未加载退役部件也可回收，未改通用渲染缓存。像素核对确认强调色12873个蓝像素及浅色黑色轮廓。

选定检查 symbol-icons324 40/40、symbol-icons-template368 598/598、symbol-icons325 1511/1511、gallery-modules256 259/259、gallery-headless257 5/5，无失败或跳过，集合存在交叠。Native265的10张48图标样张和Native266真实Gallery文档的17张状态帧全部查看或核对当前逐字节一致帧，覆盖明暗主题、1060/1000/480宽度、继承/本地配色、禁用、换模板、无效图标、导航/搜索/缩放、面对页/连续页、空与恢复、上下文替换及独立预览。已安装字体的实际Windows/Linux、物理输入、IME/触控、口述VoiceOver和Release/AOT未验收，12项既有段落换行基线失败仍单列。外部文档命令可用性、NumberBox默认头部及其余组合继续处理，整体目标 active。证据见 `artifacts/custom-control-refactor-20261028/symbol-icon-phase.json`。

## Gallery 文档工具栏可用状态

既有RelayCommand按页码/页数、演示控件默认缩放边界、搜索输入及结果决定可用性，通过CanExecuteChanged刷新真实按钮。空文档禁用相关操作并清除陈旧搜索状态，禁用的直接Execute请求也不改变状态。独立预览保持自己的命令状态，页码接收器仍只同步页码/页数，缩放仍使用原有双向绑定。Gallery261/261、无窗口5/5通过；Native267共21张明暗/1000/480状态帧核验，包含实际按钮焦点后的程序化Space触发、首页/末页、空/恢复、5%/5000%和恢复100%、上下文替换及独立预览。物理输入、IME/触控、口述VoiceOver、打印对话框、Windows/Linux仍未验收；演示命令使用控件默认缩放上下限。整体目标active，见 `artifacts/custom-control-refactor-20261028/document-commands-phase.json`。

## NumberBox 头部默认组合

头部、内容区和外框由TextControls主题组合；新增可选PART_HeaderPresenter、只读HasHeader、HeaderStates和对象/模板/选择器/格式属性。头部独立自动行与输入框间隔6DIP，默认文字在限定宽度内换行并裁切，空标题完全收起。存在模板时控件不再额外绘制标题或默认外观，自定义模板即使省略输入宿主也拥有完整界面；显式无模板的CSS输入绘制保留。编辑文字、IME几何与光标继续由现有内容宿主提供。

可访问名称使用实际呈现的头部，支持视觉内容、模板及格式，并保留显式名称优先级；范围提供者拒绝只读及禁用编辑。新增合并检查暴露3个CSS夹具沿用前序测试原生主题的最小高度、下拉区域和密码掩码，仅重置测试Application/主题隔离环境，未弱化断言或产品线程约束。

当前检查numberbox-header-template373 603/603、numberbox-header328 1654/1654、gallery-modules260 261/261、gallery-headless261 5/5全部通过，无失败或跳过，集合有交叠；前置205项输入/CSS/零字号专项通过。Native268的14张当前Metal帧全部查看，包含实际Gallery页面、字符串/对象/模板/选择器/格式及长标题、空/恢复、表达式步进及双向值、模板替换与退役按钮、只读提供者、禁用、1000/480明暗主题和大/零/恢复字号。物理输入、IME输入法组合、触控、口述VoiceOver、Windows/Linux、Release/AOT仍未验收；12项既有段落换行基线失败仍单列。整体目标active，见 `artifacts/custom-control-refactor-20261028/numberbox-header-phase.json`。

## ScrollBar 默认模板、输入与箭头配色

ScrollBar 的默认滚动槽、箭头、分页按钮、Track 和 Thumb 全部迁入 Themes/Controls/ScrollBar.jalxaml，删除构造函数中的默认视觉树、模板工厂及根节点手动子控件枚举。独立 ScrollBarLayoutPanel 负责方向、按钮槽位与覆盖式端部间隔；只读布局/呈现属性及已声明的通用、方向、覆盖式和粗细状态支持自定义主题。LineButtonStyle、PageButtonStyle、ThumbStyle 允许分别替换外观，默认键控与隐式样式复用同一模板。

PART_Track 和两端按钮均可省略，公开 Track 反映当前实际套用的可选部件，缺失模板部件时为 null。更换模板前停止交互、解绑按钮/拖动/触控、恢复覆盖式临时值并清理旧 Thumb 的宿主 CSS 层。数值更新保留双向绑定；新增方向键、PageUp/PageDown、Home/End，禁用时拒绝键盘修改。保留 macOS/mobile 覆盖式与自动隐藏、长按/捕获、弹性压缩、CSS 绘制与边距以及宿主布局隔离；显式无模板的既有 CSS 绘制兼容路径继续保留。

禁用状态的变暗由模板可视状态实现。RepeatButton 在未启用箭头动画时不再接管 PART_Arrow 的 Fill/Transform，修复浅色主题固定浅灰箭头；动画启停和退役保留已有填充、变换、前景及模板绑定。当前原生浅色箭头实测对比度为 5.97:1（仅该非文本图标采样），配色可以随主题与用户前景变化。

验证 scrollbar-template381 8/8、scrollbar-template380 611/611、scrollbar340 1855/1855、gallery-modules266 261/261、gallery-headless267 5/5，均无失败或跳过，集合有交叠。4项旧断言先在独立构建中对照重构前 ScrollBar 源码/主题，确认相同失败后按既有精确拖动和有效测量复用契约修正；CSS 固定高度用例显式清除原生最小高度，滚动主题夹具及时清理应用环境。Native273 的15张当前 Metal 帧全部查看，覆盖实际 Gallery、垂直/水平、行/页/拖动、聚焦键盘 Home/方向键、双向绑定、覆盖式展开/收起、CSS 渐变/边框/内边距、模板替换及退役按钮、禁用、极端范围、1000/480明暗主题。物理键盘输入及焦点指示、硬件触控、IME、口述 VoiceOver、Windows/Linux、Release/AOT仍未验收，12项既有段落换行基线失败仍单列。ScrollViewer、ToolBar等其余组合继续处理，整体目标 active。证据见 `artifacts/custom-control-refactor-20261028/scrollbar-phase.json`。

## AccessText 文字模板与助记键呈现

AccessText 继承 Control，默认 TextBlock 在独立 AccessText 主题中声明；删除构造函数创建文字控件和手工枚举/排列子节点。PART_TextDisplay 可省略或放入自定义布局；DisplayText、AccessKeyIndex、解析后的字体及对齐通过只读依赖属性提供。字体、颜色、背景、折行、裁剪、行高、装饰和效果由模板绑定配置；控件只保留标记解析及字符范围助记键下划线的语义绘制，并将文字部件局部坐标转换到控件坐标。

字号、字重、字形及前景复用 Control 属性以支持继承；保留 FontFamily 原有可空契约，通过解析属性回退继承字体。弱祖先观察刷新既有模板绑定，并在外层模板接入后重连，避免祖先配色变化复用旧值；卸载清理，模板退役时释放旧文字部件。保留转义下划线、可表达的内联内容、CSS 逻辑对齐/装饰/阴影和零字号路径。

验证 accesstext-template385 4/4、accesstext343 184/190、accesstext-template386 615/615、accesstext345 1977/1983、gallery-modules268 261/261、gallery-headless269 5/5。文字专项及扩大集合各含相同6项 Windows/Win32 渲染夹具跳过，未计为通过，所有已执行项无失败、错误或超时。Native275 的13张当前 Metal 帧全部查看，验证父级24/32/0/20字号、继承和本地配色、折行/中文/组合字符/表情显示、CSS RTL/装饰/阴影、自定义16/12内边距的助记键位置、无文字部件、退役绑定及恢复、1000/480明暗主题。Native274未实际改变字号的样张已标为不完整，未据此声称字号通过。Button 字符串标题的既有字面下划线行为未改变，此处未开启 RecognizesAccessKey。物理键盘/IME、口述 VoiceOver、其他操作系统及 Release/AOT 未验收；12项既有段落换行失败仍单列。ScrollViewer、ToolBar等其余组合继续处理，整体目标 active。证据见 `artifacts/custom-control-refactor-20261028/accesstext-phase.json`。

## ToolBar 默认模板和溢出生命周期

ToolBar 的外框、ItemsPresenter、更多按钮、Popup 及换行溢出面板迁入 MenusToolbars 主题。通过可选 PART_LayoutPanel/PART_ItemsPresenter/PART_OverflowPanel/PART_OverflowButton/PART_OverflowPopup、只读呈现/可见/方向属性及通用/横竖/溢出状态支持定制。ToolBarLayoutPanel 只负责主轴分配，不创建默认 UI；显式 ToolBarPanel 项目模板保留容器身份、顺序与 Never/AsNeeded/Always 模式。动态模式、项目与方向变化重新测量嵌套分配器。

替换模板前归还溢出项目、退订旧按钮/Popup/键盘事件；调用方 IsOverflowOpen 绑定保留。Space 打开、Escape 关闭并回到更多按钮，Popup 外部关闭同步调用模型，禁用状态阻止打开。先提交面板可见与位置，再发布呈现状态，修复同步 Opened 聚焦时项目仍折叠的问题。默认 Popup 使用现有窗口内浮层；QA 显式选择独立原生窗口另验关闭同步，未把宿主策略当成缺陷，也未改动 AppKit。

验证 toolbar-template398 618/618; toolbar350 2000/2006; gallery-modules272 261/261; gallery-headless273 5/5；扩大集合含6项既有 Windows/Win32 夹具跳过，所有已执行项无失败、错误或超时。Native28018张当前 Metal 帧中4张焦点变化帧直接查看，14张与已全部查看的 Native278 逐字节一致；断言实际溢出项目获得焦点，覆盖实际 Gallery 页面、宽窄恢复、增删/身份/溢出模式、独立窗口关闭、禁用、自定义/无部件/退役/恢复、打开时重设模板、横竖托盘和1000/480明暗主题。Native279加强焦点断言后失败，修复后以280为准。主题测试清理全局注册字典，避免 PasswordChar 样式污染后续 CSS 夹具；未减弱前景色断言。物理键盘、触控、物理外部点击关闭、口述 VoiceOver、其他操作系统和 Release/AOT 未验收；12项既有段落换行问题仍单列。整体目标继续 active，ScrollViewer 等剩余默认组合继续处理。证据：artifacts/custom-control-refactor-20261028/toolbar-phase.json。

## ScrollViewer 默认视口、滚动条与模板作用域

默认外框、ScrollContentPresenter、横竖滚动条迁入 Containers 主题，删除构造函数默认滚动条、
手工根可视节点枚举和默认外框绘制。可选布局/内容/横竖滚动条部件、只读 ChromePadding
及通用/滚动条/模式状态支持替换。ScrollViewerLayoutPanel 复用既有滚动状态和布局引擎，
内容呈现器正常生成文字和数据模板；语义内容及 IScrollInfo 与实际布局宿主分别维护。
保留逻辑虚拟化、偏移请求、延迟拖拽、端点锚定、CSS 视口/沟槽/RTL/裁切和仅平移路径。

模板退役停止交互与计时、解绑旧滚动条、恢复临时布局隔离和父模板最小值并清理拥有者配色。
嵌套布局与拥有者同时失效；活动拖拽冻结可见状态，避免绑定绕过冻结。
默认视口明确拉伸，修复无限高度首次测量后逻辑面板缩成零高度、主题颜色页只实现一行的问题。
实际 Gallery 恢复测试通过；临时诊断已按原始字节恢复。

模板名称现在记录原始注册作用域，准确释放原作用域或自己的字典，保留后挂接调用者的同名项。
这修复了独立模板展开后获得新名称作用域、替换模板却到新作用域注销旧名字的问题。
两项独立作用域回归及无限首次测量/有限排列回归通过。

拥有者重新测量时仅刷新中间视口缓存，保留实际内容缓存，使自适应内容能在两轮布局内读取新视口。
无模板回退不再反复应用空模板，避免清理过程提前取消横向虚拟列表的终点锚定。
新增纳入的缩放与横向到底部案例已用重构前隔离实现对照，修复后保持原有数值断言。
文字夹具显式注册所需 AccessText 模板并拥有测量上下文；原生文本将对齐编码在布局盒中，
测试同时验证记录的对齐属性和实际首字符坐标，避免把绘制命令的盒原点误当成字符原点。


Native284 的15张当前 Metal 帧均与已核验 Native283 逐字节一致；283中12张直接查看、3张与已查看281一致。
验证真实 Gallery、聚焦后的路由 PageDown/滚轮、横向500偏移、延迟请求/提交、CSS 外框和沟槽/RTL、
1000/480明暗、旧部件退役/原内容身份恢复、到底部/动作、禁用、文字和空内容。
Native282 提前在旧宽度最大值请求500的 QA 错误已改为等待布局后请求，未为此更改控件。

扩大集合6项既有 Windows 夹具跳过；已执行项无失败/错误/超时。
另3项偏移取整事实在重构前隔离构建与当前来源失败名称、消息完全相同，单列证据且未削弱断言；
12项既有段落换行问题仍单列。物理键盘、触控、口述 VoiceOver、其他系统和 Release/AOT 未验收。
整体目标继续 active，StatusBarItem、DockTabPanel、HexEditor 等候选继续核验。
证据：artifacts/custom-control-refactor-20261028/scrollviewer-phase.json。

验证：scrollviewer-template406 625/625; scrollviewer372 2063/2069; gallery-modules281 261/261; gallery-headless282 5/5

## StatusBar 和 StatusBarItem 默认模板

状态栏外框、ItemsPresenter、语义 StatusBarPanel，以及项目外框、ContentPresenter 和内部
分隔线迁入 MenusToolbars/Primitives 主题。默认内容使用 ContentControl 正常生命周期，
保留标量格式、DataTemplate、UIElement 身份、内容绑定和换模板后的恢复；旧呈现器停止更新。
新增可选内容/项目部件、Common/Items/Separator 状态及只读 SeparatorVisibility。
显式空模板与 CSS 行内适配器保留既有回退，默认主题不再自行绘制字符串和手工拥有内容子节点。

6 项模板契约包含主题 CSS 内容坐标、绑定和退役；13 项既有状态栏/CSS 回归通过。
Native286 的15张 Metal 帧中10张直接查看，5张与已查看285逐字节一致。
验证真实 Gallery、绑定更新、程序焦点/路由动作、内部线、禁用、自定义/缺失/恢复部件、
CSS 盒、内容替换、明暗1000/480、大字号/零字号和清空/恢复项目。
285 首次 QA 在退役清理前记录快照而误报，改为应用替换后记录；286 全流程通过。
独立 Separator 在水平状态栏中默认为水平线并缩成零宽，已加入独立模板迁移清单。
6 项 Windows 夹具跳过；3 项偏移取整和12项段落换行基线问题继续单列。
物理键盘、IME、口述 VoiceOver、其他系统和 Release/AOT 未验收；整体目标继续 active。
证据：artifacts/custom-control-refactor-20261028/statusbar-phase.json。

验证：statusbar-template411 631/631; statusbar376 2076/2082; gallery-modules283 261/261; gallery-headless284 5/5

## Separator 和 AppBarSeparator 默认模板

通用横竖线和 AppBar 线条、外框迁入 Primitives/MenusToolbars 主题，保留显式空模板绘制回退。
可选线条部件、方向/通用/紧凑状态、只读解析画刷和紧凑高度属性支持替换；保持原生、CSS、
继承颜色的优先级，以及厚度校验、零厚度和紧凑24/正常32的固有高度。
状态栏在主题中使用现有 SeparatorStyleKey 提供竖向样式，修复 Gallery 缺失分隔线；
调用方显式方向和样式继续优先。

菜单分隔线使用 SeparatorLinePanel，让端点留白只影响排列、不撑大固有宽度，显式高度仍居中。
CSS 测试改为检查实际主题线条部件或显式空模板回退，原有精确像素、坐标和固有尺寸断言保留。
测试主题注册与清理独立，新增的6项模板契约和160项聚焦回归通过。
共享文字两端对齐夹具显式拥有应用/主题状态，消除仅组合运行时的2项失败；数值断言未改变。

Native28915张 Metal 帧中14张直接查看，1张与已查看287逐字节一致。
覆盖实际三个 Gallery 页面、横竖/状态栏方向、CSS盒/颜色优先级、菜单端点、自定义/缺失/退役/恢复、
明暗1000/480、厚度/零厚度及紧凑/正常高度。Native288中间构建未启动，不作验收证据。
6项既有 Windows 夹具跳过；3项偏移取整及12项段落换行基线问题继续单列。
物理键盘、IME、口述 VoiceOver、其他系统和 Release/AOT 未验收；整体目标继续 active。
证据：artifacts/custom-control-refactor-20261028/separator-phase.json。

验证：separator-template415 637/637; separator384 2212/2218; gallery-modules285 261/261; gallery-headless286 5/5

## SplashScreenPresenter 和启动画面

新增可复用 Control，通过 Source/Stretch/StretchDirection、只读 HasImage、可选 PART_Image
和通用/图像状态扩展。默认 Border/Image 在独立 SplashScreen 主题并由 Generic 合并。
服务 Show 仅创建窗口及语义呈现器，保留图像资源、原始尺寸、Topmost 和自动关闭生命周期；
应用创建前仅把专用主题加载进私有窗口资源，不为获取模板创建 Application。

实际窗口发现 WindowStyle.None 仍可能绘制托管标题栏，服务显式关闭标题栏，避免图片被裁掉。
默认 Image 明确横竖拉伸，以免通用 Image 居中样式改变部件的内容区分配；测试主动注入居中样式。
原窗口声明和模板内容都用当前 Metal 帧验证：图片原点0,0、完整674x783，GPU读回保留透明像素。

Native29315张帧中3张直接查看，12张与已审阅292一致；292的服务标题栏画面被拒绝并由293替换。
覆盖绑定/空图/恢复、缩放、自定义/缺失/退役/恢复、明暗1000/480、重复Show身份、关闭/重开、
已加载主窗口自动关闭，以及真实应用创建前显示、稍后创建应用和主窗口后自动关闭。
Native290/291的QA内部API/资源包含时机问题只在夹具修正，未扩大公共API。

无窗口套件3项显示测试明确跳过，等价流程在原生宿主执行；另6项既有Windows测试跳过。
CSS输入夹具清理完整注册主题并显式提供最小AccessText模板，126项聚焦及完整扩大集合通过；
388旧失败运行已明确中止并由390替代，原有坐标/遮罩/颜色断言未削弱。
7项WebBrowser适配契约通过；该类的ActiveXHost/HwndHost继承链及单一WebView属于原生后端桥接，
无默认组合外框，保留长期导航/脚本身份，分类记录在剩余核验清单。
物理键盘、IME、口述VoiceOver、系统桌面合成、其他系统和Release/AOT未验收。
3项偏移取整和12项段落换行基线仍单列；整体目标继续active。
证据：artifacts/custom-control-refactor-20261028/splashscreen-phase.json。

验证：splashscreen-template418 640/640; splashscreen390 2220/2229; gallery-modules289 261/261; gallery-headless290 5/5

## HexEditor 默认编辑视口与滚动条

外框、HexEditorTextView 和可选竖向 ScrollBar 迁入主题，删除构造函数滚动条和手工根子节点枚举。
保留字节编辑/选择/光标/解释引擎，由语义绘制基元在实际视口内呈现；命中坐标转换为视口坐标。
边框、内边距和滚动条宽度共同分配视口，Range 使用 SetCurrentValue 保留绑定；
模板退役解绑旧条、取消捕获和拖拽，并移除画刷绘制拥有者。通用/焦点/只读/数据状态可扩展。

新视口使用框架既有弱 Brush render-owner API，使画刷原地变色刷新实际缓存且不保留退役视口。
补齐七种 HexEditor 明暗主题画刷，修复白底浅灰字；资源覆盖和显式本地画刷优先级保持可用。
原生度量发现 Cascadia Code 在当前 macOS 回退成比例字体，默认改用 Menlo；
显式排版配置保留，Windows/其他平台默认不变。相关编辑器主题期望及排版切换测试已覆盖。

5项模板契约、39项聚焦检查通过，包括实际字节绘制、范围/绑定、内边距、活动/退役部件、
画刷刷新和默认等宽/显式字体。原有精细滚轮断言保留，夹具改为显式主题和语义条部件；
旧 StatusBar CSS 夹具声明空模板/零原生样式，继续检查原有精确盒坐标，主题路径另有契约。

Native29719张当前 Metal 帧直接查看：实际 Gallery 页面，焦点和框架路由键/文字、字节修改、
滚动/选择/最后字节、CSS 命中坐标、条宽、自定义/缺失/退役/恢复、绑定/数据身份、
只读/禁用、明暗1000/480（配置紧凑行宽）、大/零字号、空/恢复、数值解释及画刷原地变色。
Native294浅色回退和295缓存/字体问题的画面被拒绝，296 QA变量作用域错误未作验收证据。
扩大397与独立398确认菜单键盘夹具缺少宿主主题；显式注册并断言父面板，3993/3，扩大400通过。
9项既有平台/原生窗口夹具跳过；3项偏移取整及12项段落换行基线问题继续单列。
物理键盘、IME、口述VoiceOver、其他系统和Release/AOT未验收；整体目标继续active。
后续仍需 DockTabPanel、Window/DevTools 组合及框架样例和 Gallery 字体字面量核验。
证据：artifacts/custom-control-refactor-20261028/hexeditor-phase.json。

验证：hexeditor-template424 645/645; hexeditor400 2255/2264; gallery-modules293 261/261; gallery-headless294 5/5

## DockTabPanel 默认页签与内容宿主

构造函数中的内容宿主和滚动条、手工根视觉枚举已删除。
DockLayout 主题声明外框、DockTabLayoutPanel、ItemsPresenter、TransitioningContentControl、
可选滚动条及前后 DockTabChrome，最低尺寸和裁剪默认值也移入样式。
保留选择/关闭/重排/停靠注册、页签尺寸、滚动范围、转场及曲线连接算法。
通过模板部件及通用/焦点/方向/溢出/选择状态扩展；支持省略或恢复部件。

渲染在真实布局区域内执行，CSS 边框/内边距参与分配，命中矩形转换为控件坐标。
范围使用 SetCurrentValue 保留调用方绑定，滚动条显式宽/高可调整。
退役模板释放内容、页签生成器、滚动处理及画刷绘制依赖；跨面板释放沿真实祖先寻找宿主。
11 项模板契约、12 项原有/原生聚焦检查通过，覆盖内容/容器身份、四方向、CSS、
空/缺失/恢复、条尺寸/绑定、内容转移、活动及退役画刷缓存。

Native29918帧功能流程完成，但方向切换和缩窄后选中页签可能不可见，视觉验收拒绝。
已修复：方向切换将选择带入视口；缩窄保留此前可见的选择，同时保留手动滚离的位置。
Native30018张当前Metal帧直接查看，包括实际 Gallery DockLayoutPage、四方向、首/尾/手动滚动、
焦点与模型绑定、CSS、退役/恢复、重排/关闭、480浅色选择可见性、禁用、画刷变色与空状态。

9项平台/原生窗口夹具跳过；3项偏移取整及12项段落换行基线继续单列。
物理键盘、IME、口述VoiceOver、跨OS窗口实际拖拽、其他系统及Release/AOT未验收。
整体目标继续active；下一步窗口命令组合、DevTools、框架样例与 Gallery 字体资源审查。
证据：artifacts/custom-control-refactor-20261028/docktab-phase.json。

验证：docktab-template432 656/656; docktab403 2267/2276; gallery-modules295 261/261; gallery-headless296 5/5

## 原生窗口应用命令栏

macOS 原生窗口命令栏的 Border/Grid/两个内容宿主/标题组合已迁入 Windows 主题。
Window 仅保留一个可复用 WindowCommandBar 宿主，原窗口 Content 身份及系统标题栏生命周期保留。
控件通过左右内容、标题和显示状态依赖属性，模板部件及通用/标题/命令存在状态扩展。
模板负责画刷、分栏、标题省略和外框；原生交通灯留白由 Padding 参与真实布局。
退役宿主清理旧内容绑定并释放调用方控件，恢复时使用原控件和原绑定。

6 项模板契约和3项 macOS 适配器检查覆盖资源/绑定、CSS、缺失/恢复、移除/重建、
窗口内容身份、标题与按钮命中及原生交通灯留白。补充命中检查发现 TextBlock 默认可聚焦，
现将标题文本设为装饰性、不可命中，保留可拖拽标题区域与按钮客户区的区分。
Native30216张当前Metal客户区帧直接查看，包含原按钮两次接收框架路由Enter、
标准/扩展标题栏、右侧单独命令、动态标题、480浅色省略、CSS、隐藏恢复和无边框。
Native30116张画面仅作早期视觉记录，新增标题命中契约由302验证。

9项既有跳过及3项偏移取整/12项段落换行基线继续单列。
物理输入、IME、口述VoiceOver、系统交通灯合成画面、Windows/Linux及Release/AOT未验收。
整体目标继续active；证据：artifacts/custom-control-refactor-20261028/window-commands-phase.json。

验证：window-commands-template436 662/662; window-commands405 2270/2279; gallery-modules299 261/261; gallery-headless300 5/5

## Gallery 默认等宽字体资源与窄窗口代码显示

28个主题文件的198处实际 FontFamily/Setter 默认字体改为 MonoFontFamily 资源引用。
精确标签/属性扫描避开注释、CDATA、Razor代码体、代码示例字符串、CSS字体声明与用户字体选项；
原始198属性计划和修改前字节保存。运行时可跟随平台/主题字体，并保留局部资源和显式本地字体优先级。
新增实际 Dock 页面的实时资源切换/本地覆盖检查，原生Menlo的iiii/MMMM宽度均为31.306640625。

Native30315帧发现480窗口代码截断，视觉验收拒绝。
62处Razor代码预览增加换行，8处停靠代码内容使用已有标准ScrollViewer的自动横/竖滚动；
补充窄窗口实际横向滚动检查。当前 Gallery263项、Headless5项全部通过。
Native30418张当前Metal帧直接查看：五类实际页面的1000暗色/480浅色、默认/局部/本地字体、
共享代码和键帽样式、换行、实际横向滚动，并滚到ThemeStudio十六进制与JSON输入字体目标查看。
Framework Managed DLL与已验收Native302字节完全相同，沿用该阶段框架回归记录。
物理输入、IME、口述VoiceOver、其他平台及Release/AOT未验收；整体目标继续active。
证据：artifacts/custom-control-refactor-20261028/gallery-mono-font-phase.json。

## DevTools 与诊断浮层等宽字体

7个诊断主题中的14处默认字体改为MonoFontFamily资源引用。
保留公开readonly DevToolsTheme.MonoFont字段的二进制形态，初始值使用配置的平台等宽字体；
程序绘制的高亮尺寸文字读取当前全局等宽字体。
启动前独立DevToolsUi等宽文本/单元格在缺少字体资源时用SetCurrentValue提供回退，
保留动态样式订阅，后续应用、局部资源及显式本地字体可以覆盖。
3项新增检查覆盖启动前、局部/本地、控件先创建而应用后启动的动态排版；222项聚焦模板检查通过。

Native30618张当前Metal帧直接查看：实际9个DevTools页面、1040/480、运行时字体切换恢复、
局部资源/本地字体、高亮及静态HUD初始显示。Profiler展示真实数据，未注入样本。
HUD采样没有启用，初始D3D12标签不作为后端验收；实际Profiler和readback确认Metal。
早期437遗漏原生库路径造成204项统一加载错误，438使用标准环境222/222通过；
305是QA访问显式接口属性的编译失败，修正QA反射后306构建运行通过，未放宽框架API。
9项既有跳过、3项偏移取整/12项段落换行基线及物理输入/IME/口述VoiceOver/其他系统/AOT继续单列。
整体目标active；证据：artifacts/custom-control-refactor-20261028/devtools-fonts-phase.json。

验证：devtools-fonts-template439 662/662; devtools-fonts411 2273/2282; gallery-modules305 263/263; gallery-headless306 5/5
