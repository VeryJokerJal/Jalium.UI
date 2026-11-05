# CSS 属性与值元数据

团队接续开发见 [CSS 剩余实现与团队分工](css-implementation-plan.md)，其中包含已确认缺口、任务代码入口、依赖与验收要求。

从 Jalium.UI 26.11.0 起支持 CSS。编辑器应先判断目标项目引用的框架版本，再展示 CSS 功能；编辑器自身使用的框架版本不能代替目标项目版本。

`Jalium.UI.Styling.CssAuthoring` 从运行时属性注册表和解析器提供元数据：

```csharp
using Jalium.UI.Styling;

// 所有已实现的属性，包括已注册别名。
var properties = CssAuthoring.GetSupportedProperties();
foreach (var property in properties)
{
    Console.WriteLine($"{property.Name}: {property.InitialValue}");
    Console.WriteLine(string.Join(", ", property.Values));
}

// 查询单个属性的默认值、规范名称、继承信息和候选值。
var transform = CssAuthoring.GetProperty("text-transform");
var values = CssAuthoring.GetValues("text-transform");
bool supported = CssAuthoring.IsValueSupported("text-transform", "uppercase");
```

`GetProperties()` 也包含明确不支持的属性，可通过 `Support` 与 `UnsupportedReason` 区分。未知属性的 `Support` 为 `Unknown`，不会出现在 `GetSupportedProperties()` 中。

`Values` 是经过同一运行时解析器验证的关键字和有效示例，适合用于补全。长度、颜色、字符串、变量和函数的取值范围不能穷举，因此它不代表全部合法值；校验具体值时使用 `IsValueSupported()` 或运行时 `Css.Supports()`。

通过 `CssMappings.RegisterAlias()` 新增的别名会出现在后续查询中，并共享规范属性的元数据与值候选。元数据缓存随注册表版本更新。

## 在 JALXAML 中引用 CSS 文件

使用 `Css.StyleSheetSource`，无需在代码中手动加载：

```xml
<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:local="clr-namespace:MyApp.Controls"
      Css.StyleSheetSource="Styles/Controls.css">
    <local:MyControl Css.Class="card" />
</Grid>
```

```css
/* Styles/Controls.css */
.card {
    background-color: #2563eb;
    padding: 12px;
}
```

普通路径相对于**声明该属性的 JALXAML 所在程序集的资源根目录**，不是 JALXAML 文件所在文件夹，也不是进程工作目录。项目使用 `Jalium.UI.Build` 的默认构建 targets 时，`.css` 自动作为 `JaliumCss` 编译，无需复制到输出目录；关闭 CSS 编译时，应把文件作为嵌入资源提供。

跨程序集资源可写成 `Css.StyleSheetSource="/MyLibrary;component/Styles/Controls.css"`，也支持 `pack://application:,,,/MyLibrary;component/Styles/Controls.css` 形式。`file:///...` URI 可以加载外部文件。样式表中的 `@import` 相对于 CSS 文件解析。

样式作用域包含宿主及其子树，也适用于继承 `Control` 的自定义控件。`Css.StyleSheetSource="{Binding ThemeCssPath}"` 可以动态更换样式表；设为空、`{x:Null}` 或清除附加属性会移除该属性加载的样式表，保留其他样式表。加载失败会抛出包含资源路径的异常，并保留先前成功加载的样式表。

同时使用 `Css.StyleSheet` 时，其文本规则在外部样式表之后参与层叠，和属性书写顺序无关。`Css.Style` 的内联声明及控件本地属性仍遵循已有的优先级规则。页面选择器仍不匹配模板内部元素。

## 固定定位与粘性定位

`position: fixed` 与 `position: sticky` 现在参与实际布局，不再降级为静态定位。

```css
.section-heading {
    position: sticky;
    top: 8px;
    z-index: 1;
}

.floating-action {
    position: fixed;
    right: 16px;
    bottom: 16px;
    width: 80px;
    height: 40px;
}
```

`sticky` 保留原有的正常流占位，偏移由最近的滚动区决定，并受包含块边界约束。支持物理滚动、`StackPanel` 的 `IScrollInfo` 滚动、嵌套滚动区、双轴 inset、百分比及样式撤销；两个 inset 都是 `auto` 的轴不产生粘性偏移。百分比 inset 以相应滚动视口的尺寸计算。

`fixed` 沿用 Panel 的绝对定位协议，包括原生 `StackPanel`、`Grid`、`Canvas` 及 CSS block/flex/grid 格式化布局；它不贡献正常流尺寸。默认包含块为宿主视口，可由 `Css.ViewportMetrics` 提供。最近的变换或 CSS filter 祖先会建立固定定位包含块。尺寸和 inset 随视口调整重新计算。

固定元素可以越过中间的 overflow/滚动裁剪，但仍保留祖先的显式 `Clip`、`clip-path`、透明度、可见性和输入限制。绘制时在固定定位包含块中按 `z-index` 排序；原有的视觉父级、事件路由和 Tab 源顺序保留。滚动、样式更换与重新挂载不会累积偏移。

定位语义参考 [CSS Positioned Layout Level 3](https://www.w3.org/TR/css-position-3/)。任意宿主、自动 inset 静态位置、自动外边距居中、包含块链、跨层级层叠上下文与 CSS 3D 变换的完整说明见文末“定位、跨层级层叠上下文与 CSS 3D 变换”一节；垂直书写逻辑轴（T4）仍需继续补齐。

## 混合与隔离

`mix-blend-mode` 和 `background-blend-mode` 支持 normal、multiply、screen、overlay、darken、lighten、color-dodge、color-burn、hard-light、soft-light、difference、exclusion、hue、saturation、color、luminosity，以及 Level 2 的 plus-lighter。背景混合列表从最上层图片起匹配，短列表循环使用，`none` 图片仍占据索引；`background` 简写不重置独立的 `background-blend-mode`。

```css
.artwork { isolation: isolate; }
.artwork > .highlight { mix-blend-mode: screen; }
.tile {
    background: linear-gradient(#00b8d4, #00b8d4), #ef476f;
    background-blend-mode: multiply;
}
```

`isolation` 使用 auto / isolate，三项混合属性均不默认继承。背景图片与背景色先在独立组内合成，再与元素外部背景合成。元素混合应用于已完成的内容组，保留 filter、clip-path、mask 和组透明度的顺序。带混合后代的 opacity、transform、filter、clip-path、mask、定位层叠上下文会隔离后代；定位元素的 `z-index:auto` 与显式整数 0 保持区别。SVG 图片本身隔离，内部二维变换不单独隔离；clip-path 依据 CSS Masking 建立层叠上下文。

当前接入视觉元素、背景及 SVG 样式树，支持直接绘制和录制回放，macOS Metal 与原生软件后端提供分组混合扩展。旧原生载荷或其他未实现扩展的绘制上下文会报告 LossyConversion。文档 Run / Span 的独立混合组、其他原生平台后端、完整三维层叠与全部 CSS 模块仍需继续；这不是完整 CSS 一致性声明。

`background-blend-mode:plus-lighter` 按 [Compositing Level 2](https://drafts.csswg.org/compositing-2/#background-blend-mode) 的语法接入；当前 Chromium 155 的 CSS 背景语法尚不接受该值，像素参考使用独立 Canvas lighter 运算。Level 1 的分离与非分离公式见 [Compositing Level 1](https://www.w3.org/TR/compositing-1/)，clip-path 层叠规则见 [CSS Masking](https://www.w3.org/TR/css-masking-1/#the-clip-path)。原生离屏入口为 `--css-compositing`，验证记录见[混合与隔离记录](../artifacts/css-compositing-20261021/README.md)。

## 生成内容

内置 Panel、Border、TextBlock、直接内容的 ContentControl、ContentPresenter，以及以模板视觉子级承载内容的 Control 可以用 `::before`、`::after` 生成文本盒：

```css
.status { display: flex; gap: 8px; }
.status::before {
    content: "[" attr(Tag, "未命名") "]";
    color: #2563eb;
}
.status:hover::after { content: " · 当前项"; }
```

支持字符串拼接、CSS 字符转义、`var()`、从原始元素读取的 `attr()` 和 `/ "替代说明"`。缺少 `content`，或其值为 `normal`、`none` 时不生成布局盒；空字符串仍可生成带背景、尺寸、边框等样式的盒。`attr()` 读取原始元素的属性并随其变化更新。`content` 不默认继承，其他可继承样式和自定义属性从原始元素继承。

生成盒按 before、原有内容、after 的顺序参与布局，支持内置 StackPanel、Grid、FlexPanel、WrapPanel、DockPanel、Canvas、UniformGrid，以及下面的内容宿主协议。原始 `Children`、`Child`、`Content` 引用和模板视觉子级保持不变，生成盒不参与 `:empty`、子序号和 `:has()` 的文档子级匹配，也不改变 Tab 源顺序。鼠标命中归属原始元素；替代说明用于生成文本的可访问名称。CSS 构建编译器会保存伪元素类型，运行时文本解析与预编译样式保持相同语义。

这仍是生成内容支持的一部分：任意自绘正文、其他尚未接入内容协议的宿主及其他尚未列出的伪元素仍待实现；列表标记见后文。当前伪元素必须位于选择器末尾，不接受伪元素后的状态或子选择器。相关语义参考 [CSS Pseudo-Elements](https://www.w3.org/TR/css-pseudo-4/#generated-content) 与 [CSS Generated Content](https://www.w3.org/TR/css-content-3/)。

## 图片内容与元素替换

`::before`、`::after` 和显式 `::marker` 的 `content` 可以按顺序混合 URL 图片、已有的 CSS 渐变、`image-set()` / `-webkit-image-set()`、字符串、计数器和引号；`/` 后仅接受文字与计数器替代说明。图片后的文字可以跨行，图片与文字共享基线、浮动排除区和原始命中归属。生成的私有图片和文本盒不进入公开文档集合。

```css
.chapter::before {
    content: "第" counter(chapter) "章 " url("chapter.png") " 摘要：" / "章节图示";
}
.brand {
    content: image-set("brand.png" 1x, "brand@2x.png" 2x) / attr(Tag);
    width: 128px;
    object-fit: contain;
}
```

单图片伪元素和普通 `FrameworkElement` 的单图片 `content` 使用替换元素尺寸规则：自动轴读取自然尺寸和比例，光栅候选按分辨率换算 CSS 尺寸，SVG 不受分辨率缩放。支持显式宽高、最小和最大限制、`aspect-ratio`、内边距、边框、圆角，以及 `object-fit` / `object-position`。作者指定的 aspect-ratio 按 box-sizing 所选的盒计算；图片自然比例始终属于内容区。框架默认的 `border-box` 保持不变；需要 CSS 内容盒宽度时显式写 `box-sizing: content-box`。无自然尺寸的渐变默认使用 300 × 150；无效图片的自然尺寸为零，显式尺寸仍然生效。图片读取、解码与动画帧更新会请求重绘，过时资源完成不会恢复已取消的内容。

普通元素仅在 `content` 为一张图片（可带替代说明）时替换其绘制和格式化内容，原有 `Text`、`Inlines`、`Children`、`Child`、`Content` 和模板对象保持原样。替换期间原有子级不参与绘制、命中、Tab、计数器或可访问子级；设回 `normal` 后恢复。替代说明支持动态属性和计数器，并用于可访问名称，显式 `AutomationProperties.Name` 优先。普通元素的文字 content-list 不改写原始内容；`FrameworkContentElement` / `Run` 等非视觉文档节点的图片替换尚未接入。

这里复用已实现的图片语法（包括下文的 image() 和 cross-fade()）；`element()`、`paint()` 等仍待实现，完整双向、垂直排版及各平台物理设备和 VoiceOver 验收也未完成。替换元素尺寸限制依据 [CSS 2.2 §10.4](https://www.w3.org/TR/CSS22/visudet.html#min-max-widths)，内容语义依据 [CSS Generated Content](https://www.w3.org/TR/css-content-3/#content-property)。

## 内容容器格式化

block、flex、grid 和 subgrid 共用框架元素的内容宿主协议。Border、ContentControl、ContentPresenter 和 Control 的原生视觉子级可以与生成盒共同排版；不会将原始内容重新挂到一个公开 Panel，也不会把模板内部节点暴露给普通 CSS 选择器。模板自己的内联样式仍可定义其内容布局。没有启用 CSS 格式化或有效生成盒时，继续使用原有的 MeasureOverride / ArrangeOverride。

```css
.notice { display: grid; grid-template-columns: 1fr; row-gap: 8px; }
.notice::before { content: attr(Tag); color: #146342; }
.notice::after { content: "内容可动态替换"; font-size: 14px; }
```

非 Panel 内容容器在未指定 display 时，有效生成盒会启用流式内容排版；撤销生成内容后恢复原生布局。更换 Child、Content 或模板不会丢失或重复生成槽位。边距、内边距、边框、定位子级、标记、命中裁剪和键盘顺序延续同一套格式化规则。auto 高度的 grid 按内容测量，排版阶段再使用分配的高度；原生换行 TextBlock 的最小内容宽度也使用 CSS 的固有尺寸测量上下文。

自定义视觉容器可以覆写 `protected bool SupportsCssContentLayout` 返回 true；如果内容需要延迟创建，可在 `PrepareCssContentLayout()` 中准备。控件仍负责通过原生可视树 API 注册真实内容子级。此协议要求内容由视觉子级表示：在 OnRender 中自行绘制正文的其他控件仍需要单独的正文适配器，不能据此视为已支持完整的生成内容或所有原生模板绘制语义。

## TextBlock 正文与生成内容

TextBlock 的原始 Text / Inlines 现在通过一个内部匿名正文盒接入 block、flow-root、flex 和 grid。启用显式 display 或有效生成盒后，前置盒、正文与尾注共同排版；没有伪元素时，显式 display 仍保留正文。正文直接复用原始 TextBlock 的字体、富文本、换行、修饰和选择绘制，不创建另一份 TextBlock，不替换原始 Inlines。padding / border 只由外层盒计算一次。

```css
.article { display: grid; grid-template-columns: 1fr; row-gap: 8px; white-space: normal; }
.article::before { content: attr(Tag); font-size: 20px; color: #146342; }
.article::after { content: "阅读完成"; font-size: 14px; }
```

匿名正文盒是内部视觉子级，不是 CSS 文档节点；选择器不能给它直接设置尺寸、order 或生成更多伪元素，Tab 与可访问树也不会增加一个重复正文节点。正文的源文本偏移保持不变，选择点映射和源文本片段坐标包含正文分配位置。显式 flex/grid 中，空正文或仅含文档空白的正文不占匿名项目与 gap；NBSP 等非文档空白仍保留。伪元素的 order 影响布局和绘制，正文保持匿名项目的初始 order 0，视觉枚举仍保留源顺序。

撤销 CSS 格式化或全部有效生成盒后恢复原生正文布局，并重建空白处理缓存。显式原生 TextWrapping 等本地值继续使用原有优先级。TextBlock 也可作为带内侧或外侧标记的 list-item。匿名盒语义参考 [CSS Display](https://www.w3.org/TR/css-display-3/#intro) 与 [Flex 项目](https://www.w3.org/TR/css-flexbox-1/#flex-items)。

TextBlock 自身的行内生成文本与原始正文共用段落行框。前置文字可以跨多行，在最后一行继续排正文，尾注也从正文末尾继续；块级伪元素会拆开前后段落，inline-block 保留原子盒。空白跨来源合并，来源边界本身不会产生断词机会，字素边界、中文标点、显式换行、手动软连字符和紧急换行沿用原生文本规则。选择与源文本片段坐标读取实际行片段，前置文字区域不会误报为非吸附正文位置。生成文字更新即使宽高不变，也会刷新整个共享段落的片段位置。

```css
.quote { display: block; white-space: normal; }
.quote::before { content: "前置文字也可以跨行："; color: #146342; }
.quote::after { content: " 〔尾注〕"; color: #32674f; }
```

共享段落支持整体 start / end / left / right / center 对齐、首行缩进、行内基线与 vertical-align，以及 RTL 基准方向下的盒顺序。生成行内盒的普通背景、边框和圆角按片段绘制，box-decoration-break: slice 只保留开头/结尾的侧边；clone 在每个片段重复水平边缘。不替换型行内盒的 width / height / min/max 不覆盖片段尺寸，inline-block 仍使用其声明尺寸。百分比 padding 先按真实包含块解析。空行内生成盒在没有文字或边缘时不额外创建空行。

普通 CSS block / flow-root 宿主的直接子级也可共用行框：不同的叶级 TextBlock、宿主自己的行内生成文字以及 inline-block 控件可以在同一段落中排列。前一个 TextBlock 的最后片段后可以继续后一个 TextBlock，保留公开 Children、原始 Run、各自的文本选择偏移、可访问名称和作者设置的 Tab 顺序。片段之间的包围矩形空白不参与普通点命中；相对定位移动整个来源的片段，不改变其他来源的流式位置。改变宽度、进行 min/max-content 测量、修改同尺寸文字或 padding、切换 display 和重新挂载后都会恢复对应的布局缓存。内在最小宽度按跨来源的完整单词计算；普通分配与内在测量分别缓存，避免把零宽分配带入 Grid / Flex 的宽度贡献。nowrap 来源内部不接受紧急换行，旁边的 inline-block 则依据父级的换行模式决定是否可以另起一行。

```css
.paragraph { display: block; line-height: 1.6; }
.paragraph > .label, .paragraph > .body, .paragraph > .note { display: inline; }
.paragraph > .label { background: #d3eadc; padding: 2px 4px; box-decoration-break: clone; }
.paragraph > .badge { display: inline-block; width: 66px; }
```

共享行框中的 line-padding 只由整行两端最内层的来源提供，不会在子级接缝处叠加；继承到各来源的值也不会重复应用。语义参照 [CSS Text 4 的 line-padding 草案](https://www.w3.org/TR/css-text-4/#line-padding-property)。

这仍不等于完整的浏览器 inline formatting context。带原生 LayoutTransform 的子级保留原子盒；变换后文字内部断行、跨来源联合字形塑形、完整双向重排、垂直书写，以及片段间连续的渐变/图像背景和完整阴影效果仍需补齐。CSS 已支持的高级文本功能在原生文本路径中的行为保持原有范围。

### 输入占位文字

`TextBox`、`AutoCompleteBox`、`NumberBox`、`PasswordBox`、`DatePicker`、`TimePicker`、可编辑与普通 `ComboBox`、`TreeSelector` 支持 `::placeholder`。提示来自控件的 `PlaceholderText`；组合控件通过其已绑定的模板提示部件应用样式。只接受双冒号语法，不能再串接 `::before` 等伪元素。

```css
.search::placeholder {
  font-size: calc(1em + var(--hint-increase, 2px));
  color: #446454;
  letter-spacing: .03em;
  opacity: .85;
}
.search:disabled::placeholder { color: #68746c; }
```

按 [CSS Pseudo-Elements Level 4 草案](https://drafts.csswg.org/css-pseudo-4/#placeholder-pseudo) 的属性范围，复用首行可用的字体、颜色、背景、字距、文字变换、装饰、阴影、强调标记与透明度；`line-height`、`vertical-align`、盒尺寸、边距、内边距、`display`、`direction`、`writing-mode` 和替换 `content` 不作用于提示。提示继承控件文字样式，未声明的颜色保留现有主题的提示颜色，变量、相对字号、`currentColor`、状态、作用域、层叠和 `!important` 沿用正常 CSS 流程。

提示背景跟随文字宽度，并按控件实际文字区域裁剪；日期与时间提示预留右侧按钮空间。输入值、选中项、原控件字号与焦点入口保持原有行为。输入或选择值后隐藏提示，清空后恢复；样式表与变量更新会刷新模板部件，重新应用模板会解除旧部件的关联。没有提示规则或移除规则后回到原生提示绘制。提示投影不增加公开视觉树子级或自动化节点；表单仍应提供独立的可访问标签。

原生离屏验证入口为 `--css-placeholder`、`--css-placeholder-lifecycle`、`--css-placeholder-gallery`。当前验证使用 macOS 软件与 Metal 离屏载荷；官方 AppKit 主机的工具链版本限制、实体输入与 VoiceOver、其他平台设备验收仍需分别完成。这一项不代表完整 CSS 已实现。

### 首行样式

TextBlock、文档 Paragraph 和已有 CSS block / flow-root 容器支持 `::first-line`，也接受兼容的 `:first-line`。样式应用于实际排版后的第一行；首行字号、字体、字距、换行规则和行高参与断行，后续文字恢复普通样式。宽度、内容、状态、字体资源和浮动避让区域变化会重新计算范围，多列排版不会在每列重新开始首行样式。

```css
.article { display: block; font-size: 16px; line-height: 1.5; }
.article::first-line {
  font-size: 1.5em;
  color: #146342;
  background: linear-gradient(90deg, #bee8d1, #e9efb0);
}
.article::first-letter { font-size: 2em; }
```

祖先块的首行沿第一个正常流块子级延续，多个首行样式按外到内的顺序继承。行内 Run / Span 的相对字体值在首行的虚拟父级下重新计算，原始对象的普通计算值保持原样；`::first-letter` 继承所在首行的字体。空的折叠空白不抢占后续块的首行，保留的空首行会结束首行范围；浮动、流外元素、列表标记和行内原子盒沿用各自的边界规则。普通生成前后缀可以贡献首行文字。

已接通现有字体、颜色、背景、透明度、文字装饰、文字变换、字词间距、行高及适用的排字属性。背景在整个首行范围内连续绘制，透明度按首行合成，普通行内背景保持其绘制顺序。`direction`、`writing-mode`、`text-orientation`、自定义属性与非继承属性的显式 `inherit` 继续从普通元素树取得继承值；首行自身的变量声明只影响其自身的 `var()` 替换，不向子级片段传递。`display`、`position`、宽高、边距和 `content` 不用于改变首行盒。层、`@scope`、`@supports selector()`、编译 CSS 和 Native AOT 使用同一套选择与级联路径，scope 边界选择器仍不接受伪元素。

私有排版片段不拆改公开 Text / Inlines、原始 Run、TextPointer 或 UTF-16 选择偏移，也不增加 Tab 停靠点或可访问子级。组合字符的范围保持完整；选区按当前渲染根的 DPI 对齐。已有行截断保留完整源文本，撤销截断或首行规则后恢复普通内容。

这仍不代表完整 CSS Pseudo / Inline 或全部 WPT 符合性。复杂双向与垂直排版、所有字体及文本变换组合、复杂浮动／背景边界下的选区合成、完整分片及其他未覆盖宿主仍需继续完善。语义参考 [CSS Pseudo 4 首行草案](https://drafts.csswg.org/css-pseudo-4/#first-line-pseudo)。原生检查入口为 `--css-first-line`，实际运行、独立参考场景、公开 API、编译 CSS / Native AOT 及来源记录见[首行验证记录](../artifacts/css-first-line-20261022/validation-report.json)；离屏渲染及托管键盘检查不替代可见窗口、实体输入、VoiceOver 或其他操作系统验收。

### 首字样式与传统浮动首字

TextBlock、文档 Paragraph 和已有 CSS block / flow-root 容器支持 `::first-letter`，也接受兼容的 `:first-letter`。选择器对原始块匹配，继承从首字所在的 Run / Span 开始；祖先块和段落的首字样式按原始包含顺序嵌套。颜色、字体、字距、文本变换、背景、边框、圆角、透明度及行内边距通过私有行内盒排版，不拆改公开 Text / Inlines，也不改变原始 UTF-16 选择偏移、TextPointer、命中归属或可访问文本。

```css
.article { display: block; font-size: 16px; line-height: 1.6; }
.article::first-letter { font-size: 2.5em; color: #146342; }
.chapter::first-letter {
  float: left;
  font-size: 4em;
  line-height: 1;
  margin-right: 8px;
}
```

首字按完整字素选择，保留组合附加符、emoji 肤色 / ZWJ / 旗帜序列，并按荷兰语内容语言把 `ij` 作为一个单位。关联引号、标点和间隔按 [CSS Pseudo 4 草案](https://drafts.csswg.org/css-pseudo-4/#first-letter-pseudo) 的字符类别选择；后置开标点和破折号不包括在首字中。保留的前导空格使用普通字号，首字从后续文字开始。显式换行或 pre 中的空首行不会选择第二行。display:none 的内容及浮动 / 绝对定位的前置兄弟不占用首字；行内原子盒或图片会阻断祖先首字。普通 ::before / ::after 文字可以提供首字，列表标记不参与选择。换文、换类、悬停、变量、样式表和前置兄弟显示变化会刷新投影；撤销后恢复原生正文。

`content`、`display`、`position` 和普通首字的 width / height 不用于替换或隐藏首字。当前实现已接通上述排版路径，但不等同于完整 CSS Pseudo / Inline 一致性：`::prefix` / `::suffix`、跨来源标点组合的全部情况、标点组合换到后续行后的范围收缩，以及完整双向 / 垂直排版仍待补齐。首行样式的当前范围见上节。Chromium 对荷兰语 ij、部分空格和标点仍有不同处理；不能把本实现、草案或单个浏览器的结果互相当作全部符合性证明。

原生测试入口为 `--css-first-letter`，本轮的实际渲染、公开 API、预编译 / Native AOT、布局稳定性及源代码与程序集来源记录见[首字验证记录](../artifacts/css-first-letter-20261022/validation-report.json)。这些检查不代替真实窗口、VoiceOver、物理输入设备和其他操作系统的验收。

### 按行数设置首字

`initial-letter` 已接入值解析、级联、继承、样式查询、过渡与关键帧，可用于首个正常流行内盒或 `::first-letter`：

```css
.article { display: block; font-family: Georgia; font-size: 16px; line-height: 24px; }
.article::first-letter {
  initial-letter: 3;
  initial-letter-align: alphabetic;
  initial-letter-wrap: first;
  color: #146342;
  margin-right: 4px;
}
```

`normal` 撤销效果；数字表示所占行数，可带整数 sink，省略时取行数向下取整。`raise` 令 sink 为 1，`drop` 使用默认 sink。计算表达式按 CSS 的数值范围规则处理。行内首字的实际字号使用根行内盒的字体度量与包含块的行高计算：`::first-line` 可以改变前者，行数间距仍使用包含块的行高。自己的计算字号、行高和 vertical-align 不用于决定实际字号；计算值和 em 尺寸仍保留。原始 Text、Run、选择偏移和公开字号不会被替换。inline-block 等原子首字保留自己的字号，自动高度使用所请求的行跨度。

较长的行内首字在父级的同一个行内格式化上下文中复用普通断行规则，正文从最后一行大字旁开始；窗口宽度改变时重新断行。每一行分别计算文字墨迹、嵌入原子内容的 margin box 和装饰片段，`box-decoration-break:slice` 只保留首尾侧边，`clone` 为每行保留完整侧边。嵌入内容保留自己的字体和原始父子关系，绝对定位与浮动后代不参与首字文字的缩放和墨迹尺寸计算。祖先行内盒的装饰边界排除首字，并保留属性继承与原始阅读顺序。

自动尺寸忽略首字自身的 `text-align` 和 `align-content`。固定宽度时 `text-align` 按通常的字形侧承对齐，固定高度时 `align-content` 使用字体的上下承对齐；`width`、`min-width`、`max-width` 的最终结果用于正文避让。没有块起始侧边框和 padding 时，超出 over 对齐点的上部墨迹保留绘制但不扩大布局；存在该侧装饰时计入片段高度。每行的下伸墨迹分别决定下一条大字行的位置。

`initial-letter-align` 接受 alphabetic、ideographic、hanging、leading，以及前置 border-box。普通边框与 padding 扩大避让区域，border-box 则把它们计入对齐跨度。字体 API 未提供 BASE 表位置时，表意与悬挂点按草案附录的代表字墨迹合成；语言和脚本默认值在 UA 级联层应用，只作用于明确声明语言的位置，保留后代的继承行为。

`initial-letter-wrap` 的 none 使用矩形；first 仅在首行按轮廓收拢，后续首字符是 Zs 空格时保持矩形；all 在各行使用字形或 shape-outside，并应用 shape-margin；grid 按包含块的 1ic 与字距扩展避让宽度，使用 justify-self 对齐首字。长度或百分比显式调整首行，百分比以首字最后一行的宽度为基准；grid 的字格也使用最后一行宽度。数值行数和 sink 可动画，sink 保持整数；长度／百分比绕排可混合插值，关键字按离散动画规则处理。

首字维持正常流的缩进和行内位置，后续行复用浮动形状避让，但普通浮动的 clear 不把首字当成浮动清除。独立格式化上下文的高度包含首字片段底边；共享正常流的短段落保留正文行高，把剩余避让传给后续普通块。文档选区按各个片段的实际边缘裁切，行导航使用各片段所属的普通逻辑行；公开文本、字号、选区偏移和无障碍名称仍来自原始内容。

跨正常块的避让保留边框、padding、margin 和嵌套坐标，空块不会截断它。后续普通块的行内文字沿用剩余区域，离开首字底边后恢复完整行宽。后续块在首字所属包含块的行内起始侧指定 clear，或建立独立格式化上下文（例如 flow-root），会清除该首字；另一侧的 clear 保留绕排。后续块自身也有首字时，其首行移到前一首字之后，块边框仍保留正常流位置。原生入口 `--css-initial-letter-block-flow` 和[跨块首字验证](../artifacts/css-initial-letter-block-flow-20261022/validation-report.json)记录这一范围。

首字与浮动共存时，按首字当前片段的整个 margin box 检查浮动的 margin box，包括起点位于普通首行以下的浮动；即使 `shape-outside` 为正文留下更窄的轮廓区域，首字仍避开完整外框。首字与它的普通首行一起向内或向下移动，上浮首字移动首行后重新断行，多行首字的后续片段也分别检查其完整高度。同一普通行上的行内起始侧浮动处于首字外侧；后续行的同侧浮动无论是否声明 clear，都会移到下沉首字之后，同时保留浮动自身的源顺序行位置。共享正常流中的后续块保留这一规则，RTL 按所属包含块的方向解析侧边。原生入口 `--css-initial-letter-floats` 与[首字及浮动验证](../artifacts/css-initial-letter-floats-20261022/validation-report.json)记录这一范围。

首字必须处于适用的行内位置，`float` 为 none 且 `position` 为 static。改成块盒、contents、none、浮动、相对 / 绝对定位或 `initial-letter:normal` 时，会撤销私有字号、字体度量、片段分配和正文避让；再次符合条件时重新计算。`visibility:hidden` 保留布局，`display:none` 撤销布局分配和命中。移除、重新挂载或移到另一个包含块后按新环境恢复继承；字号与文字的后续变化会重新排版。

普通 `::before` / `::after` 生成文字可以承载行内首字，通过私有内容盒绘制较长的首字；生成文字不写入原始 Text 或选区。文本生成盒与图片替换盒沿用各自的排版路径。状态切换检查入口为 `--css-initial-letter-lifecycle` 和 `--css-initial-letter-generated`，记录见[首字生命周期验证](../artifacts/css-initial-letter-lifecycle-20261022/validation-report.json)。

这部分仍在完善，不能视为完整 CSS Inline 符合性：跨列 / 跨页分片、浮动交互的其余组合、所有 marker / 宿主组合、复杂双向与垂直书写、全部字体与变换组合仍需验证或补齐。草案对后续行的行内结束侧浮动是否必须整体清除首字仍有未决问题，目前保持普通浮动的非重叠放置；不能据此宣称该未决行为已完成。当前轮廓提取不接受带有私有 web-font / 字宽 / 字体变体身份或仅位图字形的字体，无法获得准确轮廓时使用矩形。语义参考 [CSS Inline 3 初始字草案](https://drafts.csswg.org/css-inline-3/#initial-letter-styling)。原生检查入口还包括 `--css-initial-letter`、`--css-initial-letter-fragments` 与 `--css-initial-letter-flow-gallery`，记录见[逐行首字验证](../artifacts/css-initial-letter-fragments-20261022/validation-report.json)；离屏检查不代表可见窗口、实体输入、VoiceOver 或 Windows / Linux 验收。

### 文字着重号

`text-emphasis` 以及 `text-emphasis-style`、`text-emphasis-color`、`text-emphasis-position` 已接入文字样式计算和 TextBlock 排版、绘制：

```css
.article {
  text-emphasis: filled dot #146342;
  text-emphasis-position: under right;
}
.article Run.note { text-emphasis-style: open sesame; }
```

样式支持 `none`、filled / open 与 dot / circle / double-circle / triangle / sesame 的组合，以及字符串。当前水平排版中，单独的 filled / open 使用 circle；字符串保留第一个完整字素，空字符串不绘制标记。标记复用原来的字体设置，启用 OpenType `ruby` 特性并使用一半字号。位置接受 over / under 和可选的 right / left，省略后一项时为 right；左右项保留用于竖排，当前文字布局仍以水平模式解释上下位置。

三个长属性均可继承。简写只重置样式和颜色，不重置位置。`currentColor` 在子元素自己的文字颜色上求值，支持父样式切换和画刷更新。每个排版字素绘制一个标记；分隔符、控制 / 格式 / 未分配字符及普通标点跳过，保留规范列出的符号类标点和带组合字符的空格。跨 Run 的组合字符按完整字素处理，原文和源节点不被改写。

标记参与行高度量，以现有保留排版的范围几何定位，不通过重新塑形字符串前缀估计位置。普通文字、原始 Run、首行和首字的当前验证见[着重号实现记录](../artifacts/css-text-emphasis-20261023/README.md)。选区前景会重绘原有标记；选区独立的 `text-emphasis-color`、竖排、ruby 注音交互及其余排版组合仍待继续，不能据此认定完整文字装饰或完整 CSS 已完成。语义依据 [CSS Text Decoration 3](https://drafts.csswg.org/css-text-decor-3/#emphasis-marks)。

TextBox 与 PasswordBox 已接通横排着重号，RichTextBox 通过共享文档排版使用同一标记能力。输入行额外的标记空间参与光标、软换行命中、滚动、选区和 IME 范围几何；选区背景覆盖标记，线装饰仍以正文为基准。密码隐藏状态按可见掩码单元绘制，不根据原密码中的空格或标点分布生成标记；显示密码时使用普通字素规则。当前 macOS 原生与公开 API 验证见[输入控件着重号记录](../artifacts/css-editor-emphasis-20261023/README.md)，其他平台的实际字形输出与物理输入验收仍需单独完成。

### 选区高亮 `::selection`

`::selection` 支持双冒号选择器、`@supports selector(::selection)`、普通选择器优先级、状态、层叠层、`@scope` 和预编译 CSS。选区的文字颜色、背景、文字阴影及已实现的文字装饰长手通过私有样式计算和原始字形重绘生效；字号、padding、display、content 等布局与内容属性不会应用到选区。

```css
.article::selection {
  color: #fff8de;
  background-color: #20537a;
}
.article Run.emphasis::selection {
  color: red;
  background-color: lime;
}
```

未声明的适用属性，包括通常不继承的背景，从父元素对应的选区样式继承。`inherit` / `unset` 也遵循这条高亮继承链；根选区的继承文字颜色为 `currentColor`，按实际选中文字原来的颜色绘制。自定义属性来自原始元素，包括 `@property` 中 `inherits:false` 的注册属性，不沿选区链继承。

原生选区的默认前景和背景作为一对回退色：作者在任一颜色上提供有效级联或继承值后，撤销这对默认颜色。只设文字颜色不会留下原生背景，只设背景会保留原来的文字颜色。显式颜色的 alpha 按作者值绘制，不再乘选区的默认透明度；原始文字阴影位于高亮背景下，选区阴影位于所有文字下。文字装饰可以使用独立颜色、厚度、样式、下划线位置与偏移，以及当前已实现的 decoration inset 控制。

只声明选区阴影或装饰时，输入框仍采用 `SelectionTextBrush` 的默认前景；阴影和装饰中的 `currentColor` 也使用这个实际前景。下划线以原生文字基线定位，不受选择背景行高取整影响。通过 `text-underline-offset` 移到行框外的装饰及其阴影保留在控件的有效绘制范围内，不被选择背景截断。

高亮中的 `currentColor` 和包含它的颜色函数在绘制时解析：文字颜色相对于实际底层文字，包括 `::first-line`、`::first-letter` 与下沉首字的颜色；背景、阴影和装饰颜色相对于该高亮最终使用的文字颜色。继承的颜色表达式保留这项关联；阴影中的 `em` 等长度仍按声明所在的高亮上下文计算，继承时不会按子元素字号重复换算。修改底层排版颜色或替换样式表后，颜色与阴影缓存随级联失效。

没有独立背景、变换、裁剪或合成效果的首行与内联片段共用选区绘制阶段，先完成背景，再绘制高亮阴影和文字。只有原生选区时仍保留该控件原有的阴影与选区顺序。选区背景保持既有选择几何；字号大于行高时，字形着色与阴影保留行框外的墨迹，部分选区按实际文字行裁剪。实现与当前验证范围见[选区颜色上下文记录](../artifacts/css-highlight-context-20261023/README.md)。这部分不代表 SVG 文本选区、其他高亮伪元素或完整 CSS 已完成。

被 `visibility:hidden`、折叠或行数限制抑制的排版片段不会提前绘制选区背景或阴影；反复切换可见性保留原文、布局和选择范围。绘制异常后会清理片段覆盖与效果捕获状态，后续重绘可以恢复。基线、装饰阴影和异常恢复的验证见[选区墨迹记录](../artifacts/css-highlight-ink-20261023/README.md)。

绘制覆盖 `TextBlock`、`TextBox`、`PasswordBox`，以及 `RichTextBox` 的原生行与共享文档布局。输入框的 `SelectionTextBrush` 也实际参与选中字形绘制。部分选区、跨 Run、双向文字、组合字符、首行和下沉首字使用既有排版与选区映射，原始文字、格式、源节点和选区位置不被改写。注册属性激活后，私有排版片段的挂载与移除不会再次触发原始文档的注册表重算。

原生检查入口为 `--css-selection`、`--css-selection-context`、`--css-selection-fallback` 和 `--css-selection-gallery`，证据见[选区高亮验证](../artifacts/css-selection-20261022/validation-report.json)及上述后续记录。当前范围仍不等于完整高亮模块：自定义高亮 API、搜索 / 目标 / 拼写 / 语法伪元素、SVG 的 fill / stroke 高亮、替换内容的选区覆盖，以及所有分片、字体和宿主组合仍待补齐或验证。macOS 离屏检查不代表实体键鼠、可见窗口、VoiceOver 或 Windows / Linux 实机验收。语义参考 [CSS Pseudo 4 高亮草案](https://drafts.csswg.org/css-pseudo-4/#highlight-cascade)。


### 段落换行样式

原生文本、匿名 CSS 正文、原始 Run、生成前后缀、兄弟 TextBlock 与嵌套行内盒均接入 `text-wrap-style`，也可以使用 `text-wrap` 简写：

```css
.title { text-wrap: balance; }
.article { text-wrap: pretty; }
.caption { text-wrap-style: avoid-short-last-line; }
.editing { text-wrap: stable; }
```

`balance` 比较各行剩余空间，`pretty` 同时降低行长差异和过短末行的惩罚，`avoid-short-last-line` 只在末行偏短且不会产生过短前行时尝试调整。它们保持普通贪心排版的行数，按显式强制换行分别处理；空行、来源偏移和原始对象保持完整。`auto` 与 `stable` 使用现有逐行策略，不依据后续行重排先前内容。样式作用于建立行内格式化上下文的块，普通行内子级的该属性不替换所属段落的策略。

共享段落试排复用已有字素、断词、NBSP/word-joiner、制表符、悬挂空白、软连字符和紧急换行规则。试排只收窄断行约束，不改变真实包含块、百分比缩进/边缘、制表尺或浮动放置的基准。浮动、形状排除、原子行内盒和 slice/clone 边缘在每次试排后恢复，最终按选定约束重新放置；固有尺寸和 nowrap/pre 保持原有行为。改变宽度、样式和原生本地换行值后会刷新现有片段，FlowDocument 继续使用原来的段落代理。

算法参照 [CSS Text 4 的 text-wrap-style](https://drafts.csswg.org/css-text-4/#text-wrap-style)，具体断点选择由实现决定，不能要求与每个浏览器逐字相同。当前采用有限宽度搜索：balance 超过10行、其他优化样式超过24行或单组超过4096个 UTF-16 单元时保留普通策略。完整 line-clamp 后再平衡、长段落的全局最优断点、完整双向/垂直排版、联合塑形和全部 WPT 仍未完成。

## 共享段落的两端对齐

匿名 CSS 正文、原始 Run、兄弟 TextBlock 和嵌套行内容器共用整行的扩展空间。`text-align: justify` 处理普通软换行；段落末行和强制换行前的行使用 `text-align-last`。`justify-all` 同时要求末行两端对齐。

```css
.article { display: block; width: 24em; text-align: justify; text-justify: auto; }
.article > .part { display: inline; }
.article .fixed { text-justify: none; }
.article.closing { text-align-last: center; }
```

`auto` 在词分隔符及已识别的东亚字符边界分配空间，`inter-word` 只调整词分隔符，`inter-character` 按扩展字素边界分配，`none` 禁用对应文字内部的机会。NBSP 等非换行词分隔符仍可扩展；实际推进量为零的分隔符不会被重新拉开。相邻连续的原子行内盒作为一个排版单元，字素中的组合标记和 Emoji 序列不被拆开；已识别的连写文字对不会被插入间隙。机会由所属文字来源或相邻来源的最近公共行内祖先决定，不因字号、来源拆分而另开一套空间预算。

绘制、装饰、原始选择范围与公开位置命中使用同一组扩展坐标。内部来源的推进盒不再单独裁掉相邻字形的抗锯齿边缘，作者设置的原生或 CSS 裁剪仍生效。行高、浮动排除带、内在尺寸探测、多列文字快照和 FlowDocument 代理沿用原始来源；改变样式、宽度、Run 的方法或原生本地对齐值后会重建相关坐标。普通原生本地对齐值的优先级保留。

多列投影的普通命中和邻近行选择使用实际绘制范围，避免扩展后的末字被自然行宽排除或误归到邻列。命中测量只比较完整字素边界，避免将家庭 Emoji 的不完整 UTF-16 前缀测成多个字形后误命中先前位置；原生文本盒仍保留自身的溢出裁剪规则。

如果没有有效机会可填满整行，按 `text-align-last` 回退；它仍为 justify 时居中。当前安装的 Edge 在部分无机会用例上采用起始对齐，WebKit 不支持 `text-justify` 属性而继续使用默认方法；这些差异单独记录，不计为浏览器全部一致。规则依据 [CSS Text 3 的对齐与两端分布](https://www.w3.org/TR/css-text-3/#justification)。

保留制表符的行只把可用扩展空间分给最后一个制表符之后的文字；它之前的所有原始来源保持既有位置，避免分隔符和字距把列起点拉开。数字型 `tab-size` 使用最近块容器的空格推进量，包含该块的 `word-spacing` 与 `letter-spacing`；长度型值使用已解析的长度，0 不绘制制表推进。距离下一制表位不足 0.5ch 时跳到更后的制表位，这个最小距离按制表符所在文字的字体计算，支持原始 Run 和内联 TextBlock 的字号差异。浮动、缩进和多列投影保留各自的坐标基准，选择、公开命中、装饰和绘制共用同一布局。

上述制表分布参考 [CSS Text 3 的制表位规则](https://www.w3.org/TR/css-text-3/#white-space-phase-2) 与对应 WPT 用例；规范允许不拉伸保留空白的文字，本实现选择在固定制表位之后继续分布。当前安装的 Edge 和 WebKit 在所测拉伸用例中没有保持原制表位，其结果作为差异记录，不用来替代规范约束。无有效机会时沿用现有末行回退；完整 RTL 制表、无机会制表行的跨路径回退一致性、全部书写系统的机会分类、复杂双向和跨来源联合塑形、转换后一个源字素内部的扩展、垂直书写与完整分片仍需继续。当前覆盖不代表完整 CSS 或完整 WPT 验收。

## 行高、基线与字形溢出

CSS 的 `line-height` 按行框计算；行框可以小于字形，`0` 也合法。每个行内来源将行高与首选字体的 ascent / descent 之差平均分配到基线上下，差值可以为负。段落自身的字体与行高构成最小的基线框，再与 Run、Span、叶级文字和嵌套行内容器各自的框合并。空的行内 Span 也可以影响已有文字行的高度。

```css
.paragraph { display: block; font-size: 16px; line-height: 1.5; }
.paragraph .large { font-size: 32px; }
/* .large 继承数值 1.5，按自己的字号得到 48px。 */
.fixed { line-height: 150%; }
/* 在 16px 父级计算为 24px；后代继承该长度。 */
```

无单位数值继承后按每个来源自己的字号计算；长度和百分比继承已计算的长度。`normal` 使用平台字体及实际塑形的自然度量；固定行高不因回退字体的字形较高而自动增高。绘制和源文本选择坐标使用同一基线，超出行框的字形参加绘制边界与祖先裁剪检查；变换或动画偏移后也会刷新该边界。改变行高后保留原文、Run 对象和选择偏移。FlowDocument 的段落代理保留相同的继承语义；多列分片使用实际塑形行的边界，避免浮点误差切入整行。

原生本地 `LineHeight` 和显式本地 `LineStackingStrategy` 保留优先级；撤销后恢复 CSS。没有作者 CSS 行高的原生文字继续使用原有 MaxHeight 行堆叠和宿主测量余量。作者设置 CSS 行高的原生 TextBlock 仍保留宿主的 2 DIP 测量余量；显式 CSS 正文盒与共享段落按 CSS 行框贡献高度。

字体回退、平台字体版本和浏览器取整可以造成自然字体度量差异。当前覆盖水平行高、基线及相关溢出；完整垂直书写、复杂双向联合塑形和完整分片仍需继续实现。语义依据 [CSS 2.2 行高](https://www.w3.org/TR/CSS22/visudet.html#line-height) 与 [CSS Inline 3 的行内布局边界](https://www.w3.org/TR/css-inline-3/#inline-height)。

## 嵌套行内容器

Border、内置 Panel 和 ContentPresenter 可递归加入外层共享段落。带自身生成内容的 TextBlock 也可继续参加外层行框，其原始正文仍由原来的匿名正文适配器绘制。不同层的 Children、Child、Content、Run 和视觉父级保持原样；空容器不新增可选择字符、Tab 站点或可访问正文节点。

```css
.paragraph { display: flow-root; line-height: 1.8; }
.frame, .frame .content, .frame .body { display: inline; }
.frame { padding: 0 5px; border: 1px solid #468467; background: #def0e5; }
.frame.repeated { box-decoration-break: clone; margin: 0 3px; }
.frame .aside { float: inline-start; width: 50px; height: 55px; margin-inline-end: 8px; }
.frame .body::before { content: "【"; display: inline; }
.frame .body::after { content: "】"; display: inline; }
```

每层容器按其片段绘制背景、边框和圆角。slice 仅在首尾保留水平边缘，clone 在每行重复水平 margin、border 和 padding；父子层的边缘分别计入一次。非替换行内宿主的 width / height / min/max 不覆盖文字分配，百分比 padding 依据外层包含块解析。垂直 padding 和 border 可越出行框，不会把它们重复计入每一行的行高。vertical-align 根据容器及后代的基线与字号计算；相对定位移动整个子树，不改变后续来源的流式位置。

嵌套浮动进入同一外层排除区，既影响容器内文字，也影响它后面的行内来源；inline-block / inline-flex / inline-grid 继续保留独立的原子布局。片段包围矩形中的空白不参与点命中，包括带圆角的 Border 和 TextBlock 匿名正文；普通、过滤式视觉树和输入命中沿用各自的源元素。宽度缓存回放、内在测量、同尺寸内容更新、样式切换和重新挂载会恢复各层的片段与绘制缓存。

行内祖先包含块级后代时，会在外层块格式化上下文中拆开前后段落。所有相关行内祖先的背景与边框只绘制前后行片段，不绕住中间的块。块的 width、百分比尺寸、auto margin 和 RTL 放置使用外层包含块；行内祖先的 padding 不缩小它的包含块。前后为空时保留实际需要的空侧盒，没有文字和有效边缘的侧盒不会增加空白行。相邻块只隔着无装饰的可合并空白、浮动或绝对定位内容时，不额外创建文本行。

```css
.frame .block { display: block; margin: 12px 0 20px; }
.frame .block.independent { display: flow-root; }
.frame .block.centered { width: 70%; margin-inline: auto; }
```

块级后代复用普通块布局的 margin 合并、空块穿透、clear 和浮动排除区。正常块可借用外部浮动并把自己的浮动传给后续正文；flow-root 保持独立上下文。边距是否留在内部或从正常父块逃逸，取决于实际行盒、边缘、clearance 和父级上下文。行内祖先的 relative 偏移也移动块级后代，后续外部内容的流式位置保持不变。嵌套块的文字参加外层 text-group-align，原有 grid/flex 内容继续使用自身布局。切换块后代的 display、更新生成文字、改变宽度或进行内在测量后会重建相应段落与布局缓存。匿名段落不加入 CSS 文档树，结构选择器仍然读取原始子级。拆分与边距语义参考 [CSS 2.2 匿名块盒](https://www.w3.org/TR/CSS22/visuren.html#anonymous-block-level) 和 [margin 合并](https://www.w3.org/TR/CSS22/box.html#collapsing-margins)。

复杂自绘控件、原生 LiquidGlass / SuperEllipse Border 和未经适配的自定义容器保留原子布局。自定义纯内容宿主可通过 SupportsCssInlineContentLayout 选择片段布局，自己的绘制逻辑也需适配行片段。上述联合塑形、完整双向/垂直排版和连续复杂背景等边界仍然适用；表格、多列、分页分片及复杂定位包含块仍需继续补齐。语义依据 [CSS 2.2 行内格式化](https://www.w3.org/TR/CSS22/visuren.html#inline-formatting)、[行高与基线](https://www.w3.org/TR/CSS22/visudet.html#line-height) 和 [box-decoration-break](https://www.w3.org/TR/css-break-3/#break-decoration)。

## 浮动与共享行框

CSS 流式宿主中的文字可按浮动排除区逐行换行：支持 left / right、inline-start / inline-end、不同高度的左右浮动、来源中途出现的浮动、margin 与 clear。跨子级单词不因浮动插入而自动断开；可用宽度容不下首个不可断词段时，文字向下寻找可容纳它的位置；矩形排除区使用浮动底边，形状排除区使用实际轮廓。字号或 vertical-align 改变行高后会重新核对整行的排除区。

```css
.article { display: flow-root; line-height: 1.6; }
.article > .aside { float: inline-start; width: 80px; height: 90px; margin-inline-end: 12px; }
.article > .body, .article > .note { display: inline; }
```

普通 block 子级可承接祖先排除区，也会把自身浮动传给后续的父级正文；此行为适用于 Panel 及接入 CSS 内容布局的 Border、ContentControl、TextBlock 等宿主。flow-root 或其他独立格式化上下文把外部浮动排除在自身之外。TextBlock 的匿名原始正文、行内伪元素和浮动伪元素也共享此路径；通过普通 block 继承的外部浮动不会被 TextBlock 的行内专用路径绕过。

内在宽度测量同时计入左右浮动，即使整行只有浮动也会保留完整 max-content 宽度。排版缓存保留实际行带和源坐标；当片段在相同外接矩形内移动时，会刷新绘制缓存，避免切换浮动方向后重放旧坐标。正文引用、选择和普通点命中仍属于原始元素。语义参考 [CSS 2.2 的浮动模型](https://www.w3.org/TR/CSS22/visuren.html#floats)。

浮动支持 shape-outside 的基本形状与盒形：inset()（含 round）、xywh()、rect()、circle()、ellipse()、polygon()（含 round / fill-rule）、path()、shape()，以及 margin-box / border-box / padding-box / content-box。基本形状默认以 margin-box 为参考盒，也可在形状前后指定盒名；盒形包含实际 border-radius，margin-box 的圆角按规范调整。shape-outside: none 保持原始矩形排除区。

```css
.illustration {
    float: left;
    width: 96px; height: 96px;
    margin-right: 12px;
    shape-outside: circle(50%) border-box;
    shape-margin: 8px;
    clip-path: circle(50%);
}
```

shape-margin 使用向轮廓外侧的圆形距离偏移，支持非负长度、百分比和 math；百分比按包含块的 inline size（当前水平排版为宽度）解析，负的 math 结果在使用时限制为 0。形状及其间距最终裁到浮动的 margin box。文字按整行高度避让，支持退化竖线、加间距的水平线、凹轮廓及对向浮动间的开口；长词会寻找最早可容纳的位置。曲线与圆形间距复用几何扁平化，目标误差为 0.01 CSS px。浮动框的放置与堆叠、独立格式化上下文的避让、clear、内在宽度和容器高度仍按原始矩形外边距计算。文字前缀后出现浮动时，候选浮动与已有形状共同决定该前缀能否留在来源行。

shape-outside 只改变环绕，不裁剪元素的绘制或点命中；如需图文轮廓一致，可另设 clip-path。普通 block 传递的排除区保留形状坐标，变更轮廓、shape-margin、参考盒圆角或相同宽度下的内容会使相关段落重新排版。语义参考 [CSS Shapes 1](https://www.w3.org/TR/css-shapes-1/)。

shape-outside 也支持 url() 图片，以及 linear-gradient / radial-gradient / conic-gradient 和相应 repeating 形式。shape-image-threshold 接受数值、百分比和 number-percentage math，计算结果限制到 [0,1]；只有 alpha 严格大于阈值的像素参与环绕。默认值 0 忽略完全透明像素，阈值 1 产生空形状。透明空洞保持透明，shape-margin 使用圆形距离扩展并裁到浮动外边距；密集栅格的长词下移直接查询像素区间。

```css
.photo {
    float: left;
    width: 120px; height: 120px;
    object-fit: contain;
    shape-outside: url('../images/photo.png');
    shape-image-threshold: calc(10% + 0.05);
    shape-margin: 8px;
}
```

图片异步加载与解码期间，以及读取、解码或像素访问失败时，环绕按 none 的矩形规则处理；成功得到全透明图片时则产生空浮动区域。加载完成会通知仍使用该资源的元素重排，晚到的旧资源不会覆盖新样式。相对 URL 使用样式表的源地址；编译 CSS 也保留此地址，不需要 CSS 源文回退。源流由加载器释放。同一 URL 在一份编译声明中共享加载，阈值和宽度变化沿用已解码像素；动画栅格只使用第一帧。

图片以浮动的实际内容盒为基准。Image 的显示与形状使用同一 replaced-content 计算，遵守 object-fit / object-position 及本地 Stretch 的优先级；CSS 边框和 padding 计入内容盒，Image 的共享边框绘制与圆角内容裁剪同步生效。形状资源可以与实际 Image.Source 不同，不会改写 Source、Run、子级或选择范围。

环绕像素与自然元数据分别保留。SVG 缺少宽度、缺少高度、仅有 viewBox 比例和显式零长度，均按各自状态参与协商，不从墨迹范围或公开兼容宽高补出自然轴。Image 没有可用显示尺寸时，使用环绕源的独立自然维度计算对象矩形，避免把补齐的宽高误当成比例。contain 等规则改变对象矩形后，SVG 在该实际逻辑视口中重新提取透明度；viewBox、媒体条件和百分比坐标不会先在内容盒解析、再被第二次拟合。

`image()` 的空间片段先在源图像的自然坐标中选取，再映射到环绕输出。SVG 在源视口中提取片段，位图的 image-set 密度在片段之后换算 CSS 自然尺寸；整图方向镜像不除以自然宽高，零自然长度仍可由明确的正输出尺寸绘制。`cross-fade()` 的环绕元数据复用共同的自然尺寸权重计算，颜色、渐变、无自然维度的 SVG 和加载回退色不提供虚构尺寸；真实零长度仍参与计算。同一源 URL 的不同 image-set 候选共享解码，分别保留自己的分辨率，DPI 切换会更新轮廓。SVG 与生成图像不按像素候选密度缩小。

原生入口 `--css-shape-image-sizing`，尺寸、片段、实际文本换行、选区和命中的验证见[环绕图像尺寸记录](../artifacts/css-shape-image-sizing-20261022/README.md)。独立自然尺寸算法见 [CSS Images 3](https://www.w3.org/TR/css-images-3/#default-sizing)，混合元数据见 [CSS Images 4](https://www.w3.org/TR/css-images-4/#cross-fade-sizing)。普通盒与 Image 的图片环绕在 Chromium 对照中仍有尺寸 / 定位差异，具体范围保留在记录中，不能据此声称完整浏览器一致性或真实窗口 / VoiceOver 验收。

URL 复用 ICssResourceResolver，也可实现 ICssImageResourceResolver 提供专用图片请求。CssResource.ImagePixelAccessAllowed 用于声明像素访问是否已获允许：本地、data 和应用资源默认可信，HTTP 资源需要显式批准。默认网络图片请求采用原生视图的 opaque origin（Origin: null），不带 cookie 或认证信息，并在每一跳重定向上检查 Access-Control-Allow-Origin 为 * 或 null；自定义远程解析器应依据应用自己的来源规则设置批准结果。SVG 图片不导入外部子资源，支持框架已有的 SVG 绘图及嵌入栅格；文字通过本机字形轮廓提取透明度，macOS 使用 Core Text 的整行塑形和字体回退。

图片读取限制为 32 MB 编码数据与 16,777,216 个解码像素；渐变和 SVG 透明度栅格按实际取样视口生成，最长边上限为 2048 像素。缩小取样缓冲区不改变逻辑默认尺寸或缺失自然轴的回退值。不支持或超出资源能力的图片恢复矩形环绕。其他尚未实现的 CSS Images 形式、首字分片与字体组合、完整双向和垂直排版，以及仅有位图而没有轮廓的 SVG 字体仍需继续补齐；本节不代表 CSS Shapes 全部值或跨平台物理设备验收已完成。

`background-image`、`background` 和 `shape-outside` 共用 `image-set()`，也接受 `-webkit-image-set()` 别名。候选可以是 URL、字符串地址或已有的 CSS 渐变，不能嵌套 image-set；分辨率描述符支持 x / dppx / dpi / dpcm、calc 等数学函数及元素上下文，未指定时用于选择的分辨率为 1x。

```css
.density-image {
    background-image: image-set("photo.png" 1x, "photo@2x.png" 2x);
    background-repeat: no-repeat;
    shape-outside: image-set("mask.png" 1x, "mask@2x.png" 2x);
}
```

先移除 `type("MIME")` 声明的格式不可用的候选，再按计算后的分辨率保留第一个候选；格式标签用于选择，不强制图片字节采用该格式。选择策略取不小于当前 DPI 的最小分辨率，否则取最大分辨率。只读取选中的资源；选中资源读取或解码失败不会自动改选另一候选。零分辨率以及数学结果限制到零的候选是有效语法，但表示无效图片；字面负值则使整个声明无效。参考 [CSS Images 4](https://drafts.csswg.org/css-images-4/#image-set-notation)。

栅格的显式分辨率覆盖自然分辨率，例如 288 像素宽的 2x 候选按 144 CSS px 计算自动背景尺寸；SVG 和渐变保持自身的矢量/生成图像尺寸规则。背景 URL 使用样式表源地址和 ICssResourceResolver 异步读取，流在读取后释放，沿用 BitmapImage 的延迟解码及动画管线；解码完成、自然尺寸就绪和动画换帧会通知仍使用该资源的元素重绘。DPI、变量、currentcolor 和元素相关数学变化会重新选择，晚到的旧图片不会覆盖新样式；编译 CSS 无须保留 CSS 源文件。

格式能力由 `INativeImageDecoder.SupportsMimeType()` 提供，自定义解码器可覆盖此默认接口方法。默认原生解码器在 macOS 查询 Image I/O 注册格式，其他平台对可选 WebP、TIFF、ICO、HEIC/HEIF、AVIF 进行缓存的小图解码探测；未列入探测的可选格式仍需补齐能力查询。当前 image-set 接入范围包含背景、shape-outside、图片 content 与 list-style-image，其他图片消费位置仍需逐项接入，Windows/Linux/Android 的实际格式与 DPI 切换需要各平台验收。

## 颜色图像与多图混合

按 2026-10-08 的 CSS Images 4 编辑草案，`image(<color>)` 表示没有自然尺寸的颜色图像。支持 currentcolor、已实现的现代颜色函数和动态变量。URL 回退、方向注解和空间片段属于 Images 5 编辑草案；下面的扩展已接入现有图片消费者。

```css
.tint { background-image: image(rgb(0 0 255 / .5)), url("photo.png"); }
.preview { background-image: cross-fade(url("first.png") 25%, url("second.png")); }
.badge::before { content: cross-fade(image-set("icon.png" 1x, "icon@2x.png" 2x), currentcolor); }
.float { shape-outside: cross-fade(url("mask.png") 75%, transparent); }
```

`cross-fade()` 接受一个或多个图片或颜色，百分比可放在各项前面或后面。省略的百分比平分剩余权重；总和超过 100% 时归一化，小于 100% 时保留剩余透明度。字面量须在 0%–100% 内，数学表达式延迟到元素上下文计算并限制到该范围。直接嵌套的 cross-fade 会展开，相同图片项合并权重；image-set 仍禁止通过其他函数间接嵌套 image-set。

绘制使用预乘 sRGB 的颜色与透明度加权平均。只有具有自然尺寸的图片参与尺寸加权；颜色、渐变和剩余透明度不缩小已有图片的自然尺寸。每个输入缩放到混合图像的具体尺寸再混合。背景继续应用自身的尺寸、重复、定位、裁剪和圆角；内容及列表图像延续替换元素与标记规则。当前颜色、属性、变量、兄弟数量与 DPI 更新会重新计算，比例变化和等价 URL 会复用当前消费者已读取的资源，过时读取完成不会覆盖新声明。解码完成与动画帧会更新混合缓存。

shape-outside 共用权重算法，但始终使用经像素来源批准的静态首帧；远程图片仍须解析器明确批准像素访问，绘制图片的授权不会自动授权环绕读取。混合后的形状在浮点 alpha 上应用严格阈值。一般混合栅格最长边限制为 2048 像素；纯颜色混合使用常量像素，避免分配视口大小的缓冲区。

旧式 `-webkit-cross-fade()`、element()、paint() 等其余图片消费位置仍待补齐。本节未包含物理平台、真实跨屏或 VoiceOver 验收。语义依据 [CSS Images 4](https://drafts.csswg.org/css-images-4/#cross-fade-function)，不代表完整 CSS 已完成。

## 图片边框

已接入 `border-image-source`、`border-image-slice`、`border-image-width`、`border-image-outset`、`border-image-repeat` 和 `border-image` 简写。默认值依次为 none、100%、1、0、stretch。普通 `border` 简写重置这五项，单边、颜色、宽度和样式简写不重置图片。显式继承图片来源时，子元素会重新订阅资源并使用自己的绘制上下文；其他边框图片值也监听父级变化。

```css
.frame {
    border: 8px solid transparent;
    border-image: url("frame.png") calc(100% / 3) fill / auto / 4px round;
}
.preview {
    border-image-source: image-set("frame.png" 1x, "frame@2x.png" 2x);
    border-image-slice: 30%;
    border-image-width: calc(10% + 2px);
    border-image-repeat: round space;
}
```

切片的数字是源栅格像素或向量坐标；百分比以相应源图轴为基准，百分比数学表达式保留这一基准。每项截到源图尺寸；对边切片重叠时，对应中段为空。width 的数字乘对应普通边框宽度，百分比取包含 outset 的图片边框区域；auto 取切片自然尺寸，没有自然尺寸时用对应普通边框宽度。四边绘制宽度重叠时整体按比例缩小，包括极大的计算值。

stretch 延展对应片段；repeat 从区域中央开始平铺；round 调整尺寸以容纳整数块；space 只画完整图块，并平均分配前后及块间空隙，放不下一块时该区域为空。fill 保留中间片段。普通盒子的边和中心都随背景阶段绘制，位于正文与子元素后方。边框图片不按 border-radius 裁成圆角。可映射到同框装饰宿主的控件模板会转发绘制，模板与来源变化会刷新宿主。

图片尚在加载、解码失败或来源无效时，保留普通边框；有效的透明图片会替换普通边框。支持当前已接入的 URL、SVG、渐变、image-set、image() 注解和 cross-fade 图片管线。图片密度作用于自然尺寸与 auto 宽度，数字切片仍使用源像素。图像帧、采样提示、方向和 DPI 变化会使切片缓存失效，过时来源完成或旧动画帧不会恢复已移除的声明。

各切片先按源分辨率隔离，再缩放，避免线性滤波读入相邻切片。向量与生成图按可用绘制分辨率栅格化。缓存最多保留 16 个片段，每片最长边限制为 2048 像素。超过 4096 个图块的单元使用有界像素采样，避免记录海量绘制指令；其输入与输出最长边同样限制为 2048 像素。

outset 只扩展绘制、脏区和视口剔除范围，不改变布局、滚动尺寸和点命中区域。外扩与 outline / effect 共用既有绘制范围通道；来源与样式移除后会恢复普通边框与原正文。

原生文本、可拆分内容宿主及 TextBlock 中的文档 Span/Run 图片边框已接入跨行绘制。`box-decoration-break: slice` 按排版后的实际片段宽度拼接图片区域，再裁回每一行，保留平铺相位；`clone` 在每行独立计算九宫格。切口不额外绘制左右图片外扩。片段高度不同时，各自保持其块轴尺寸；水平首尾边缘与拼接方向使用父级行内方向，图片 `image(ltr/rtl …)` 注解仍依据图片消费者自身方向。

```css
/* 非可视文档节点使用现有文字区域，图片宽度可独立于普通边框。 */
Span.frame, Run.frame {
    border-image: image-set("frame.png" 1x, "frame@2x.png" 2x)
                  calc(100% / 3) fill / 4px round;
    box-decoration-break: slice;
}
```

文档片段沿用换行、行间距、截断和文本来源索引。图片及嵌套文档背景位于选择和字形下方，不创建额外文字节点或 Tab 目标。来源、显式继承和图片密度变化使用文档所属可视宿主；异步像素与动画帧只请求重绘，不作为正文内容修改。匿名正文同步外扩绘制范围，宿主带 CSS padding 或生成前后内容时仍保留坐标映射。

RichTextBox 托管的 FlowDocument 也已接入有排版片段的 Run/Span 背景与图片边框。原生段落路径读取原始 Run 对应的实际视觉片段，同一双向行上不连续的范围分别绘制，保留中间兄弟节点的空隙；相邻后代片段合并为所属 Span 的区域。背景及图片按祖先深度绘制，随后绘制选择背景与字形，不透明的自定义选择刷也不会遮住文字。原生控件自身的有效图片边框会抑制普通边框，移除声明后恢复。

```css
RichTextBox .frame {
    border-image: url("frame.png") calc(100% / 3) fill / 4px round;
    box-decoration-break: slice;
}
```

编辑器按未滚动的全部行片段缓存连续图片区域，再平移和裁剪到内容视口；首个片段滚出视口不会重置后续图案，外扩进入视口的装饰仍可绘制。Section/List 的绘制、选择、命中与 IME 几何共用相同的层级缩进与段后间距。正文编辑、字体或布局变化重建片段；异步像素和动画帧只重绘，保留已塑形段落。现有普通 Run 布局路径也使用同一绘制器，其自身的布局能力范围保持原状。

FlowDocumentScrollViewer、FlowDocumentPageViewer、FlowDocumentReader 的单页、双页和滚动模式，以及 FlowDocument 的两个分页器入口，现已保留原始段落与 Run/Span 的字体、文字装饰、背景和图片边框。查看器借用原始 Inlines 集合，不重新挂载文档，不复制或截断 Run；同一文档可同时用于编辑器和多个查看器。CSS 应附加在原文档或其已有逻辑祖先上；查看器不是共享文档的新逻辑父级。

公共文档接口和查看器共用同一个稳定的分页器实例，样式刷新不会在两个实例之间切换并释放当前页。分页读取 TextBlock 已排版的行及来源索引，替代按平均字符宽度估算和截取字符串的路径；基础分页不会切开一行或字素，保留段落分隔符、空段落和显式的 Paragraph.BreakPageBefore。滚动视图按可用视口宽度换行，缩放作用于完整排版表面，字体和装饰一起缩放。Section/List 层级的边距、段落 padding、普通实色边框与背景参与布局；透明边框角落不会重复叠色。

共享文档仍只有一份计算样式。image-set 按仍持有文档的显示视图中的最高 DPI 选择候选，各视图的 CSS DIP 尺寸不变；解除高 DPI 视图的文档关联后重新选择候选。这不等同于每个视图各自计算样式、祖先选择器或媒体查询。异步像素和动画资源通知会使各视图重绘，不作为正文修改或分页失效；文本、样式和字体变化则重排。页面是 DocumentPageView 的实际可视子级，同页样式或 DPI 刷新保留正文的可视父级并更新页面几何；真正换页时释放旧视图，页面请求被回调改写时重新校验并重试。查看器保留原文搜索和跨段落选择，双页搜索也显示高亮；正文提供只读 Text Pattern、来源文字、选择与已排版的范围几何，并可请求滚动或跳页。内部滚动条不增加正文的 Tab 停靠点。

FlowDocument 的分页布局现已使用 `break-before`、`break-after`、`break-inside`、`orphans` 和 `widows`。断点基于已塑形的真实行和原始 Paragraph/Section/ListItem 层级，避免规则会将能够完整放下的内容移到下一页。强制分页优先于避免分页和首尾行数量；空间不足、约束无法同时满足时依次放宽首尾行和避免分页限制，保证排版继续前进。`orphans` 是断点之前保留的最少行数，`widows` 是之后保留的最少行数，均继承、初始为 2，接受正整数与按整数规则计算的数学表达式。

```css
.chapter { break-before: recto; }
.heading { break-after: avoid-page; }
.keep-together { break-inside: avoid; }
Paragraph { orphans: 3; widows: 2; }
```

`left` / `right` 指定物理页侧，`recto` / `verso` 随文档页序方向变化；必要时插入有实际空白视觉内容的页面，来源位置导航跳过这些零长度页。首个内容的页侧决定起始页侧，不生成开头空页；末尾强制断点不生成额外内容页。相邻强制断点合并，页侧冲突采用流顺序中较后的值；第一或最后一个子块的断点可传到相应祖先边界。强制断点保留后续块的上边距，非强制断点丢弃该边距，祖先和块本身的 padding / border 留白仍参与布局。

旧属性 `page-break-before` / `page-break-after` 的 `always` 映射到现代属性的 `page`，其 `auto` / `avoid` / `left` / `right` 共用现代存储和级联；`page-break-inside` 接受 `auto` / `avoid`。它们也支持 CSS 全局关键字。`column` / `region` 及对应的避免值不会在仅有页面的上下文中错误触发分页；普通控件与文档根级多列见下文，区域布局仍待接入。没有直接覆盖的 CSS 声明时，原生 `BreakPageBefore`、`KeepWithNext`、`KeepTogether` 和非零的段落首尾行提示继续使用同一规划器；段落自身的 CSS 声明可覆盖这些提示。运行时与编译 CSS、`@supports` 和编写元数据共用属性定义。语义依据 [CSS Fragmentation 3](https://drafts.csswg.org/css-break-3/)。

文档表格已接入原始 `Table` / `TableRowGroup` / `TableRow` / `TableCell` / `TableColumn` 的行列几何，供滚动查看器、分页查看器、Reader 和公共分页器使用。同一行共享高度，同一列共享宽度；原生像素列、Auto / Star 列、ColumnSpan 和 RowSpan 参与约束，跨行范围止于所属行组。单个原生 ColumnSpan 的布局上限为 1000，越界或非正跨度按有界值处理，不丢弃来源单元格。自动布局读取已塑形文字的最小与最大内容宽度；固定布局读取显式列宽和首行的宽度约束，后续正文不会反向扩大固定列。文档表格默认使用可用行宽，CSS `width` 可指定尺寸或包含块百分比，显式 content-box / border-box 会计入相应的外层 padding 和 border。表格 `height` 作为最小高度，多余空间分给未折叠的行。普通控件的 CSS 表格格式化见下一节。

```css
Table { table-layout: fixed; border-spacing: 4px 8px; }
TableCell { padding: 8px; border: 1px solid #9ab8a8; vertical-align: top; }
TableCell.summary { width: 25%; vertical-align: middle; }
Table.compact { border-spacing: 0; empty-cells: hide; }
```

`table-layout` 接受 `auto` / `fixed`，不继承；`border-spacing` 接受一或两个非负长度，分别指定水平、垂直间距，不接受百分比；`empty-cells` 接受继承的 `show` / `hide`。间距也继承，CSS 初始值为 0；没有作者声明时继续使用原生 Table.CellSpacing。显式的 0、继承、变量、相对单位、全局关键字和声明移除会重新排版。没有可见内容且设置 hide 的单元格不绘制自身和下方的列、行组、行背景；整行均为空且隐藏时折叠该行及重复间距。含空的块元素的单元格仍属于有内容的格。单元格支持已有的实色背景、普通实色边框和图片边框；透明格下依次显示行、行组、列与表格背景，格间空隙显示表格背景。

单元格的 padding / border 进入轨道与行高，百分比 padding 以表格行宽为基准；本地原生 Padding 值保留框架既有优先级。基线来自第一条已塑形文字行，空段落使用有限的内容底边；`vertical-align:top/middle/bottom/baseline` 参与共享行高，原生单元格的行高和文字对齐可作为段落默认值。RTL 改变列序。列宽、行组、行、格及原始 Run 的变更通知会传到文档，使已有视图和分页器更新。纯代码宿主无需加载 XAML 即可匹配实际元素及其基类的类型选择器。

并排单元格保持逻辑来源顺序和各自的物理文字位置，命中同时比较 X / Y，避免右列误选左列。TextPointer、范围文字、全文选择与布局共用单元格 tab 和行分隔符；嵌套表格与表后正文也保留精确位置。分页按实际物理行带选断点，各段落的首尾行约束独立计算，来源导航按文字所在页定位；长单元格的逻辑顺序不再被误当作页面顺序。无法在一个页面放下的单个行带会垂直续页，避免剩余绘制内容被丢弃。该路径也用于过高的普通文字行。这里的表格轨道、边框分离模型与对齐参考 [CSS 2.2 Tables](https://www.w3.org/TR/CSS22/tables.html)。

文档 Span/Run 尚无完整的普通 CSS border/padding/margin 盒模型；当前图片区域使用已有文字片段。图片 width 的显式长度、百分比和有自然尺寸的 auto 可绘制，数字乘数仍以普通边框初始宽度 0 计算；不会凭图片边框为文字增加布局留白。无文字片段的空内容、查看器中列表标记的完整分片与双向组合、嵌入 UI 与文档节点的完整交互 CSS、普通元素的其余表格边界、原生文档 caption 和重复表头/表尾、RichTextBox 表格排版与编辑、查看器的完整表格可访问结构与单元格键盘导航、多栏、复杂跨页重排和装饰盒片段、其余平台的完整段落塑形、垂直排版、连续复杂背景、无法映射外框的复杂模板，以及物理平台与 VoiceOver 验收仍待补齐，这些缺口继续属于完整 CSS 目标。合并边框的当前范围见下文。语义依据 [CSS Backgrounds and Borders 3](https://drafts.csswg.org/css-backgrounds-3/#border-images) 与 [CSS Fragmentation 3](https://drafts.csswg.org/css-break-3/#break-decoration)。

## 内容尺寸关键字

`width`、`height`、各自的 `min-` / `max-` 属性及 `inline-size` / `block-size` 别名支持 `min-content`、`max-content`、`fit-content`、`stretch` 和 `fit-content(<length-percentage>)`。`flex-basis` 及 `flex` 简写也接受这些值。固有尺寸从实际文字、嵌套块、Flex、Grid、表格及图片内容测量；最小和最大尺寸继续参与最终约束，冲突时最小值优先。

`fit-content` 把可用空间限制在最小和最大内容尺寸之间；函数形式使用指定长度或百分比作为限制。`stretch` 填充包含块扣除 margin 后的空间，绝对定位还扣除指定的 inset。无可用确定尺寸时退回自动尺寸。关键字本身按边框盒测量，不会因 `box-sizing:content-box` 重复添加 padding 和 border；函数参数这样的数量值仍按所选盒计算。作者指定的 `aspect-ratio` 同样按所选盒计算。

这些计算值支持变量、`@supports`、显式继承和尺寸样式查询；查询比较计算值，容器的最终像素宽度不代替关键字。本地及动画属性仍优先，清除后恢复 CSS。内容、包含块和样式变化会重新测量，来源节点与文字 Run 保持原样。

尺寸语义依据 [CSS Sizing 3](https://www.w3.org/TR/css-sizing-3/#sizing-values) 和 [CSS Sizing 4](https://www.w3.org/TR/css-sizing-4/)。函数形式等较新语法在本机浏览器中的支持有差异，浏览器观察与规范用例分别记录。垂直书写模式、完整碎片化、复杂百分比确定性与跨平台物理验收仍属于后续工作，不能据此认定已支持完整 CSS。

## 循环百分比与尺寸确定性

内容尺寸测量与最终百分比分配分开进行。例如容器使用 `width:min-content`、子级使用 `width:50%` 且包含 80px 原始内容时，容器的内容贡献为 80px，最终子级宽度为 40px。嵌套的明确宽度会打断循环；变量、原生及动画属性仍保留各自的优先级和计算值。

固有尺寸贡献中的非替换元素将循环百分比首选尺寸和最大尺寸按初始值处理；最小尺寸、margin 和 padding 将循环百分比部分按零计算，`calc()` 中的长度部分保留。替换元素的最小内容贡献对首选/最大百分比按零计算，并考虑通过宽高比传递的另一轴最小尺寸。最终分配再使用容器尺寸解析百分比，容器不会因子级随后溢出而循环增长。普通内容高度仍保持不确定；明确高度、Flex 明确 basis、stretch 分配等按各自布局规则提供百分比基准。

普通块、浮动和 inline-block 的自动高度，以及 `height:min-content` / `max-content` / `fit-content`，不会仅因测量后获得有限像素值就成为确定的高度基准。例如 20px 内容的 `min-height:calc(100px + 50%)` 在此类父级中保留 100px 长度部分；`min()`、`max()`、`clamp()` 和含 `0%` 的表达式也保留对应长度。父级只有 `min-height` / `max-height` 不会使自动高度变为确定；明确高度经过约束后的内容盒、Grid 或 Flex 的确定分配则使用各自的实际基准。Grid 自动行在轨道计算中忽略循环百分比，最终子项可按已分配轨道重新解析并溢出。

横向 Flex 的有限交叉轴分配与百分比高度基准分别保留。自动高度容器中的百分比首选高度仍按自动处理；stretch 项的后代可以使用拉伸后高度。原生 `StackPanel` / `Border` 没有指定 CSS display 时仍提供其原生 Arrange 包含块；需要上述普通 CSS 自动高度语义时显式指定 `display:block`。

相关实现共用 [CSS Sizing 3 的循环百分比规则](https://www.w3.org/TR/css-sizing-3/#cyclic-percentage-contribution) 与 [Flexbox 的确定尺寸规则](https://drafts.csswg.org/css-flexbox-1/#definite-sizes)。选取的几何用例、浏览器差异与回归记录单独保存；不代表所有尺寸、书写模式或碎片化组合均已完成。

### 百分比 gap 与不定高度

`row-gap` / `column-gap` 的数学表达式在百分比基准未定时保留长度部分。自动高度的纵向 Flex 使用 `row-gap:calc(20px + 10%)`、包含两个 20px 项时，容器高度为 60px，第二项从 40px 开始。同样的 Grid 在轨道测量阶段保留 20px 长度间距，最终再以 60px 内容高度解析百分比，第二项从 46px 开始；最终溢出不会再次扩大容器。高度明确为 100px 时两者均使用 30px 间距。横向内容尺寸确定后的百分比 column-gap 使用最终内容宽度。

原生 Flex/Grid spacing 的本地及动画值优先于 CSS gap，清除后恢复百分比；即使原生值与 CSS 长度部分相同，来源变化仍会重新布局。原生容器的间距兼容映射在没有百分比基准时保留表达式长度项。规则参考 [CSS Gaps 1 的百分比间距](https://www.w3.org/TR/css-gaps-1/#gap-percent)。该节覆盖水平书写的 Flex/Grid，未覆盖完整多列 gap、垂直书写和碎片化组合。

## Flex 项目的主轴尺寸

原生 `FlexPanel` 与 `display:flex` 宿主共用主轴分配规则。`width` / `height` 提供首选尺寸，`flex-basis` 提供基础尺寸；最终的 `ActualWidth` / `ActualHeight` 使用 Flex 解算后的尺寸。两个宽度为 400px、200px 的无边框项目放入 300px 容器时，默认收缩为 200px、100px。原生本地宽高仍优先决定输入的首选尺寸，属性值与来源节点不因分配而改写；原生 min/max 继续限制分配，清除本地值后恢复 CSS。

基础尺寸与经过 min/max 约束的假设尺寸分别保留。换行使用假设尺寸，增减空间从原始基础尺寸计算并按违规约束冻结项目。收缩权重使用内容区的基础尺寸；padding、border 和 margin 占用空间，但不充当可收缩的正文。小于 1 的 flex 因子保留其比例对应的剩余空间。长度、百分比与数学表达式共享包含块尺寸，百分比 padding 继续以容器内容宽度为参照。

```css
.records { display:flex; width:300px; gap:12px; }
.record { min-width:0; flex:0 1 auto; }
.record.compact { flex:0 0 content; }
.record.equal { flex:1 1 0px; }
```

`flex-basis:content` 忽略该项目主轴上的首选尺寸，使用内容及相关宽高比约束；`auto` 读取首选尺寸，没有首选尺寸时再读取内容。`flex:0 0 content`、`flex:content 0 0` 和单独的 `flex:content` 可使用内容基础尺寸。grow 与 shrink 必须相邻，`flex:0 content 0` 之类的无效声明整条忽略，不覆盖已有层叠值。简写省略基础尺寸时保留规范中的零长度；显式百分比基础尺寸与零长度分别处理。运行时样式与构建编译器生成的 CSS 资源共用属性解析规则。

未指定最小尺寸或显式使用 `auto` 时，非滚动 overflow 的 Flex 项保留基于内容的自动最小尺寸；`min-width:0` / `min-height:0` 可关闭该自动限制，`hidden` / `auto` / `scroll` 使用零自动最小尺寸。宽高比通过交叉轴约束参与内容建议值，图片等替换内容与普通块分别使用对应规则。显式的原生零最小值也会覆盖 CSS auto；原生 Basis 设置为与默认值相同的 NaN 后使用 auto，清除本地值可恢复 CSS content。

表格仍保留网格内容本身的自然最低尺寸，即使显式零最小值或较小 max-size 也不会使原文轨道消失；表格指定高度不额外成为 Flex 最小高度。已覆盖原始文本、图片尺寸、RTL / 反向、边距与盒模型、反复缩放及测量异常后的布局恢复。规则参考 [CSS Flexbox 主轴解算](https://www.w3.org/TR/css-flexbox-1/#resolve-flexible-lengths)、[自动最小尺寸](https://www.w3.org/TR/css-flexbox-1/#min-size-auto)和[简写语法](https://www.w3.org/TR/css-flexbox-1/#flex-property)。

### Flex 的内在尺寸与不定主轴

Grid 轨道、自动表格和行内/浮动的收紧宽度布局查询 Flex 的最小、最大内容宽度时，使用项目的内容贡献，而不是将零基础尺寸直接当作容器宽度。项目贡献包含内容尺寸与首选尺寸中的较大者，再按照不能增长/不能收缩的基础尺寸及 min/max 约束修正；外边距与间隔也参与计算。横向单行容器对贡献求和；换行容器的最小内容宽度使用最大单项贡献，最大内容宽度仍包含全部项目及间隔。折叠项目和绝对定位项目不增加内在主轴尺寸。

```css
.summary { display:grid; grid-template-columns:max-content; }
.summary-group { display:flex; gap:12px; }
.summary-item { flex:1 1 0px; min-width:0; }
```

纵向 Flex 的内在宽度保留子项的内容贡献；多列换行时，最小内容宽度取能容纳任一列的最大贡献，最大内容宽度包含各列及间隔。图片等替换内容参与最小内容查询时保留自然尺寸与有效宽高比，不因查询使用的零宽度而缩至零。普通自动高度仍通过正常 Flex 布局计算，不使用横向内在宽度查询替代。

Flex 作为 RTL Grid 项目时，默认 `justify-self:normal` 仍允许自动宽度填充网格区域；明确宽度的项目继续按逻辑起始方向对齐。切换方向不会将自动宽度项目误缩为未经增长的基础尺寸。

当主轴包含块不定时，百分比 `flex-basis`（包括含百分比的数学表达式）的使用值是 `content`，会忽略该项目的主轴首选尺寸。举例：自动高度的纵向容器中，项目 `height:160px;flex:0 0 50%` 包含 40px 高的内容时，基础尺寸为 40px；`auto` 基础尺寸仍可读取 160px 首选高度。给容器设置确定高度后，百分比重新按该高度计算；恢复自动高度后又使用内容。省略基础尺寸的简写继续保留规范中的零长度，与显式 `0%` 分别处理。

内在贡献采用 [Flexbox 编辑草案 §9.9.1.2](https://drafts.csswg.org/css-flexbox-1/#intrinsic-main-sizes) 的浏览器兼容算法及 [§9.9.3](https://drafts.csswg.org/css-flexbox-1/#intrinsic-item-contributions)。部分已安装浏览器仍与该草案中的伸缩贡献限制存在差异；保留具体用例对照，不将局部结果当作全部浏览器互操作验收。全局尺寸属性的完整内在关键字、复杂跨轴二次排版、百分比后代的全部确定性规则、垂直书写与分页分片仍需继续完善，完整 CSS 目标保持进行中。

## 普通控件的 CSS 表格格式化

普通 Panel、Border、ContentControl、ContentPresenter、TextBlock 和已选择加入 CSS 内容布局的宿主可使用 `display:table` / `inline-table`（也接受 `block table` / `inline table`）。内部角色包括 table-row、table-cell、table-row-group、table-header-group、table-footer-group、table-column、table-column-group 和 table-caption。内部角色作为 flex/grid 项、浮动或绝对定位盒时按块盒使用。

```css
.matrix { display: table; width: 100%; table-layout: fixed; border-spacing: 4px 6px; }
.matrix .head { display: table-header-group; background: #dceee4; }
.matrix .body { display: table-row-group; }
.matrix .row { display: table-row; }
.matrix .cell { display: table-cell; padding: 8px; border: 1px solid #9ab8a8; vertical-align: middle; }
.matrix .column { display: table-column; width: 25%; }
.matrix.compact { border-spacing: 0; empty-cells: hide; }
```

此路径复用文档表格的轨道约束，原始控件保留 VisualParent、逻辑父级、公开 Children 和结构选择器顺序。Grid.ColumnSpan / Grid.RowSpan 提供宿主跨度，跨行先截到所属行组再占用列；第一个 header/footer 行组分别移到表格首尾，其余同角色行组按普通行组排列。缺失行或单元格的内容使用私有匿名盒；普通流中的连续、错误嵌套的表格内部兄弟使用匿名 table 补全父级。匿名盒不参与作者选择器和自动化节点。

自动宽度在 CSS 块流中按最小/最大内容宽度收缩；有明确宽度的固定布局读取列定义和首行约束。百分比列以扣除外侧和内侧 border-spacing 后的可分配宽度为基准，原生像素列保持最低长度，Star 列按权重接收余量。table 的显式宽高作为最小尺寸，外层 padding/border 只计一次。行和行组不使用 padding、单元格不使用 margin；内容的 padding/border 参与单元格行高。支持首行文字基线及 top/middle/bottom 对齐，inline-table 按原子行内盒换行，RTL 反转列位置。

列定义及其子树不绘制，也不参与命中和焦点；其列背景仍在表格下面绘制。分离边框模型按 table、列组、列、行组、行、cell 的顺序叠加实色背景，间距只显示 table 背景；跨行/列区域采用其来源行/列的底层颜色。empty-cells:hide 隐藏空格的背景、边框和底层行列背景；仅含已折叠匿名空白的格也可隐藏，preserve 空白仍属于内容。原始节点变更、列宽或间距切换、模式撤销会更新几何并释放布局分配，焦点继续留在原始控件上。

当前实现使用水平表格，分离与合并边框模型分别见本节与下文；行列折叠见下文；仍需补齐复杂错误嵌套、完整混合跨列最小/最大内容传播、跨跨度的连续图片定位和复杂模板适配；普通 CSS 表格尚未接入完整分页分片。原有按钮等语义不因视觉表格自动变成 table/grid 可访问结构，完整表格语义、单元格方向键导航、物理输入、VoiceOver 和其他平台实机验收继续保留为缺口。规则依据 [CSS 2.2 Tables](https://www.w3.org/TR/CSS22/tables.html)、[CSS Tables 3 宽度分配](https://www.w3.org/TR/css-tables-3/#width-distribution) 与 [CSS Display 3](https://www.w3.org/TR/css-display-3/)。本节不代表完整 CSS 已完成。

### 自动表格百分比列宽

普通控件与文档表格共用自动列宽约束。单元格、列与列组的百分比先保留为比例；计算内在尺寸时，不把它们提前解析成父级宽度的固定像素。每列采用最大的百分比贡献，并按来源列顺序将总预算限制在 100%。跨列单元格新增的百分比贡献分配给尚无百分比的列，按非跨列最大内容宽度加权，全为零时均分。

自动表格的最大内容宽度同时考虑百分比列容纳自身内容，以及非百分比列在剩余比例中容纳内容所需的表宽。若两列最大内容宽度分别为 20px、60px，首列为 25%、50%、75% 时，未受包含块限制的表宽分别为 80px、120px、240px。可用宽度会限制该最大值，但不压缩各列的自然最小宽度；包含块宽度未定时不以循环百分比制造确定尺寸。原生 Border、ScrollViewer 等宿主在 Arrange 中给出更宽槽位时，CSS auto 表格也按内容宽度收缩。

分配从所有列的最小宽度开始，依次增长百分比列、CSS 指定长度列、自动列，向各阶段目标按缺额比例插值。达到最大内容宽度后，余量优先按最大内容宽度比例分给自动列，再退回指定长度列与百分比列；原生 Star 权重继续有效。CSS 指定长度可在自然最小宽度以上压缩，原生本地像素尺寸保留其最低约束。列定义的 `0%` 按 auto 处理，单元格的 `0%` 则保留单元格百分比请求。

自动布局的单元格百分比作用于列的边框盒；实际 padding / border 仍参与内容最低需求，不额外重复加到百分比上。固定布局中显式 content-box 的单元格百分比另计这些边。百分比 `max-width` 可限制百分比贡献，本地原生最大宽度（含默认的正无穷）可以覆盖并在清除后恢复它。列组宽度作为每个未指定宽度的子列的默认值，不把组宽平均拆给子列。

长度与百分比的线性 `calc()` 延迟到分配宽度已知时解析；相互抵消的百分比保留其常数宽度，同列的多个表达式分别保留自己的系数和常数，不把不同表达式的最大值拼在一起。有限网格也会解析 `clamp()` 等非线性表达式。原生 `TableColumn.Width` 的 Pixel / Auto / Star、本地附加 Width 的 `NaN` 和默认值，以及清除未改变数值的尺寸来源，都会更新原始查看器和分页缓存。折叠列在完整分配之后投影，不重新排版保留列的原文。

上述行为参考 [CSS Tables 3 列度量与宽度分配](https://www.w3.org/TR/css-tables-3/#computing-column-measures) 和 [Blink 的列宽实现](https://chromium.googlesource.com/chromium/src/+/main/third_party/blink/renderer/core/layout/table/table_layout_utils.cc)。数学表达式、百分比 max-width 等在浏览器间存在差异；完整非线性内在贡献、复杂重叠跨列约束和嵌套自动表格的全部百分比贡献仍需完善，不表示完整浏览器一致性。

### 自动表格跨列内容约束

跨列单元格也复用列宽的分阶段分配：先满足各列自然最小宽度，再增加百分比列、指定长度列、自动列，剩余空间按内容比例分配。跨列的内在最小／最大内容宽度会扣除覆盖范围内的内部间距，外侧间距仍由表格计算。例如两列原始内容宽度为 20px、60px，跨两列内容需要 160px 时，得到 40px、120px，不把 80px 缺额均分成 60px、100px。

指定跨列宽度提供最大宽度目标，原生本地像素宽度还保留最低约束；自然最小宽度继续约束可压缩范围。自动跨列单元格的最大内容贡献若只覆盖指定长度列，不强行扩张这些列；有自动列时优先分配给自动列，只有百分比列可增长时按比例分配。百分比贡献在该跨列单元格的内容扩张之前计算，避免内容本身改变比例权重。

先处理较短的跨度，相同跨度按逻辑起始列处理，再保留相同起始列的来源顺序，因此重排起始列不同、跨度相同的两条记录不会改变列宽。普通控件和文档表格共用这些约束；样式、本地尺寸、内容变化及列折叠都会重新计算，折叠仍在完整分配后投影。

这一分配路径参考 [Blink 的跨列自动布局实现](https://chromium.googlesource.com/chromium/src/+/main/third_party/blink/renderer/core/layout/table/table_layout_utils.cc)；[CSS Tables 3 列度量草案](https://www.w3.org/TR/css-tables-3/#computing-column-measures) 与现有引擎在百分比、重叠跨度及空列上仍有差异。本节不代表所有浏览器结果一致，完整空列合并、非线性内在百分比、所有复杂重叠跨列和嵌套表格的内在尺寸仍需验证。

### 嵌套表格的内在尺寸

文档表格估算内层表格时，与实际排版共用放置结果、单元格内在尺寸和列定义，不再取每行宽度之和的最大值。例如内层两行的内容宽度分别为 20px / 100px、100px / 20px，两列各自都需要 100px，因此向外层贡献 200px，而不是单行最大值 120px。跨行／跨列、CSS 列宽、原生 Pixel / Auto / Star、额外声明的列及分离间距参与相同的网格计算。

内层的 padding、border、margin 与 content-box 尺寸在对应盒层计入一次；合并边框使用冲突解析后的半边。指定宽度与 min/max-width 会限制可分配尺寸，但不能抹掉网格的自然最小需求。列折叠在完整分配后投影，其内在贡献也随可见列收缩，保留列的原文不重排。多层嵌套、内层内容、样式和列定义变化会沿原始节点更新查看器与分页缓存。

内层表格依赖外层单元格的宽度，因此计算它对外层的内在贡献时，百分比列不会反向放大最大内容估值；自身循环百分比宽度、padding 和 margin 也使用未定包含块的规则。外层分配确定后，实际内层排版再解析这些百分比，可能保留内容最低需求或产生溢出。语义参考 [CSS Sizing 3 的循环百分比规则](https://www.w3.org/TR/css-sizing-3/#cyclic-percentage-contribution) 与 [Blink 的表格百分比贡献开关](https://chromium.googlesource.com/chromium/src/+/main/third_party/blink/renderer/core/layout/table/table_node.cc)。

本节覆盖水平内在宽度传播；完整空列合并、非线性内在百分比、所有混合跨度和模板组合、垂直书写及普通 CSS 表格的完整分页仍需验证，不代表完整浏览器一致性。

### 表头与表尾行组

普通 CSS 表格与 FlowDocument 的 TableRowGroup 都按角色排列：首个 `table-header-group` 放在其他行之前，首个 `table-footer-group` 放在其他行之后。首个身份由原始来源顺序确定；后续同类行组保留在正文中的相对顺序，不会因为真正的表尾移到末端而被误认成主表尾。空行组同样保留其首个身份；在已有行轨道的表格中，零高度的表头、正文组和表尾仍占完整网格宽度，并定位到所属视觉边界。`visibility:hidden` 保留空间，`visibility:collapse` 使用既有的轨道投影，不会把身份转交给后续同类组。

行组高度分配使用这个有效身份：有正文组时，剩余空间仍优先分给正文组，额外的表头／表尾按正文组处理。固定布局的首行约束来自第一条视觉行；合并边框、跨行上限与单元格位置也使用同一视觉网格。跨行仍止于所属来源行组。

文档的原始 RowGroups、Rows、Cells、Paragraph 和 Inlines 不重排、不复制。文字代理按来源顺序消费内容，再投影到视觉轨道，保持源偏移、选择与文字身份；分页、可见范围、原文点选和按来源跳页使用实际位置。切换角色、删除与恢复行组会更新已有查看器与分页缓存。上述角色规则依据 [CSS 2.2 表格模型](https://www.w3.org/TR/CSS22/tables.html#table-display)。逐页重复表头／表尾、完整 display 重建、完整表格可访问关系和其他分页组合仍待补齐。

### 空表格、空行与保留空间

没有行时，尚未使用的列定义及其间距不再撑大表格的内在宽度。明确的表格宽高继续保留；显式 CSS `width:auto` 的空网格不贡献文字或列宽，只计入表格自身的 padding、border、最小宽度及普通控件 caption 的最小需求。分离边框与合并边框继续使用各自的盒尺寸规则，`box-sizing` 和本地宽度覆盖的优先级保持不变。

空行及空行组使用表格实际内容区的宽度，不会因为没有单元格而得到零宽。行组的矩形只包含所跨轨道和内部间距，不包含表格两侧的 border-spacing。没有行时，零高度行组盒定位到网格起点，不能因为表格有明确高度而被推到预留空间末端。插入单元格后，同一列定义重新参与正常宽度分配；删除与恢复内容会同步更新内在尺寸、原文位置和分页缓存，不重建或复制原始 Run。

原生 Table 的列、行组、行和单元格集合在清空、范围删除及替换时，完成集合与父级关系更新后再通知现有查看器。带有文字选择的查看器可以继续重新分页，避免在删除中途读到仍留在集合中的已脱离节点。

空表格的明确高度登记为可跨页的版面空间。分页器不会将它当作过大的前导间距跳过，表格后面的原文仍在正确页面继续；空白片段不生成虚假段落、可见文字范围或文字命中。

尺寸参考 [CSS Tables 3 草案的空表格算法](https://www.w3.org/TR/css-tables-3/#table-layout-algorithm)；内部盒的位置参考[表格盒定位规则](https://www.w3.org/TR/css-tables-3/#bounding-box-assignment)。浏览器对照明确区分 `CSS1Compat` 与 `BackCompat`，使用标准模式复核。安装的 WebKit 在仅有空行组时仍把明确高度算为零，本实现采用草案中的明确尺寸规则，保留作者高度。复杂 HTML 列定义及未使用尾列的兼容行为、垂直书写和逐页重复表头／表尾仍需继续完善。

### 隐式列归并与缺失单元格

自动布局将没有显式列盒、且被同一组单元格跨越的相邻隐式列归并为一列；任意一行中的单元格起止边界都会阻止该处归并。布局使用调整后的列起点与跨度计算内在尺寸、间距、宽度、背景和合并边框。固定布局保留原有列；显式 CSS `width:auto` 继续使用自动算法。原生 TableColumn 的星号宽度约定继续保留。归并只修改布局数据，不改写 `Grid.ColumnSpan`、`TableCell.ColumnSpan` 或来源集合。

确定网格后，未被原始单元格及其跨行区域覆盖的位置按匿名空单元格处理。普通控件和文档路径都在这些位置绘制列、行组与行的背景，保留格间空隙，并服从 RTL、列折叠及 `empty-cells`。匿名格继承所属行的 `empty-cells`；仍然显示的匿名格会阻止一整行因其唯一的来源空单元格被隐藏而错误折叠。合并边框模型继续忽略 `empty-cells:hide`。这些格只产生布局和绘制区域，不插入控件、TableCell、Paragraph 或 Run，不增加文字代理、来源偏移或焦点目标。

文档中缺失格占用的行空间参与分页，补齐、收起和恢复记录会更新已有查看器与分页缓存。CSS 内在文字宽度按字形推进量计算，不再计入原生 TextBlock 为墨迹预留的 2 DIP；普通原生测量仍保留该预留量。

规则参考 CSS Tables 3 草案的[网格归并](https://www.w3.org/TR/css-tables-3/#dimensioning-the-row-column-grid--step2)和[缺失格修复](https://www.w3.org/TR/css-tables-3/#missing-cells-fixup)，以及 [CSS 2.2 的表格背景与空格规则](https://www.w3.org/TR/CSS22/tables.html#table-layers)。安装的 WebKit 与草案及 WPT 断言在部分空内容、固定跨度和 COL 定义用例上存在差异；当前结果不代表完整 HTML 表格算法、全部浏览器兼容或完整 WPT 验收。

### 自动表格高度与包含块

普通 CSS 表格的 `height:auto` 按行、单元格、格间距、内边距和边框决定高度。默认原生 Grid、Border、水平 StackPanel 和物理 ScrollViewer 提供的较高安排区域不会自动拉高表格；后续原文与命中区域使用表格的实际高度。原生本地 `VerticalAlignment`、CSS Grid / Flex 的拉伸分配及 `ScrollViewer.FillViewport` 继续生效，显式原生 Stretch 与默认值相同时也会重新布局。Grid 的 normal / stretch 对齐受到 max-height 限制后，余量留在表格下方。

`height` 和 `min-height` 可增加表格行的分配空间；`max-height` 限制作者尺寸，但不截断行与单元格内容的自然最低需求。普通控件和原生文档 Table 共用这套高度边界，保留本地高度优先级与显式 content-box / border-box 的外框计算。普通控件的顶部、底部 caption 加在网格高度之外。

```css
.records { display: table; height: auto; min-height: 120px; }
.half-height { height: 50%; }
.tall-records { height: calc(200% + 10px); }
```

物理 ScrollViewer 使用真实视口计算百分比高度，即使测量方向允许无限滚动；超过 100% 的表格会增加滚动范围，反复调整视口不会重复放大高度。Flex 与 absolute / fixed 定位分别传递容器的尺寸，避免用已经求出的子项尺寸再次解析百分比；原生边距仍由子元素自身处理。文档百分比高度继续要求确定的父级内容高度，PageHeight 仅控制分页。

高度语义参考 [CSS 2.2 表格高度](https://www.w3.org/TR/CSS22/tables.html#height-layout)、[CSS Tables 3 高度计算草案](https://www.w3.org/TR/css-tables-3/#computing-the-table-height)和 [CSS Box Alignment](https://www.w3.org/TR/css-align-3/#align-self-property)。本轮以标准模式 WebKit 对照已列出的普通流、Grid、Flex、定位及滚动用例；完整 HTML 表格兼容、全部百分比与 flex-shrink 组合、垂直书写、普通表格分页、完整表格可访问关系及物理平台验收仍需继续完善。

显式 `content-box` 的百分比高度继续沿用[通用盒模型的长度与百分比解释](https://www.w3.org/TR/css-sizing-3/#box-sizing)：200px 的确定包含块中，`height:50%` 加上下共 24px 的外框得到 124px，普通表格与文档 Table 一致；合并边框模型中同类 2px 外框得到 102px。本机 WebKit 在这两个用例中均得到 100px，此项表格互操作差异仍保留在验收记录中。

## 分页文字定位与可见范围

分页正文的文字矩形按当前页实际分配的内容片段裁剪，同时考虑 PagePadding、表格单元格和多列片段。短页的内容区可能小于纸张高度；下一页文字即使位于本页纸张范围内，也不会成为本页的搜索、选择或 Text Pattern 文字矩形。跨页超高行只返回当前片段可见的部分，字体的光学偏移仍沿用实际文字排版。

页内点选只在当前片段可见的原文中查找最近插入位置。拖动到页外时限制到最近的可见部分，避免落到同一原始段落的下一页行。具有分配高度但没有可见文字的表格空白页，以及 recto / verso 等规则插入的空白页，不返回文字命中或虚假的可见范围。

文字矩形、可见行与点选查询会先刷新原文布局，因此样式、边距和 Run 内容变更不会沿用旧的文字位置；页面片段的重新分配仍由分页器更新。搜索与选择使用原始来源偏移，不复制、截断或重新挂载 Paragraph / Run / Inlines。可见矩形契约参考 [UI Automation GetBoundingRectangles](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-itextrangeprovider-getboundingrectangles)；这些接口检查不等同于物理键盘、VoiceOver 或其他平台的实机验收。

## 表格 caption 与外层包装盒

普通控件可以使用 `display:table-caption`，`caption-side` 接受可继承的 `top` / `bottom`，初始为 top。它们与运行时样式、组件资源的编译 CSS、`@supports`、变量和全局关键字共用注册及编写元数据。多个说明按各自一侧的源顺序排列，原始控件、公开 Children、结构选择器、Tab 顺序和自动化名称保持来源关系。

```css
.matrix .caption { display: table-caption; line-height: 1.6; margin-bottom: 10px; }
.matrix .note { display: table-caption; caption-side: bottom; margin-top: 10px; }
.matrix.below { caption-side: bottom; }
```

表格的包装盒包含上方说明、数据网格与下方说明。说明宽度以网格外框宽度为包含块，百分比宽度和 padding 使用该宽度；auto 水平边距可以居中说明。说明自身的最小内容贡献会参与表格的最小宽度。说明可以换行，拥有自己的 padding、border、背景和边距；同侧相邻说明的垂直边距会折叠，负边距可以产生重叠。

table 的 height 和自身 padding/border 仍作用于数据网格，包装盒再加入说明占用的高度，反复测量不会把说明高度重复分配给行。inline-table 的基线以第一行网格为准，并加入上方说明的偏移，下方说明不会压低基线。错误放在普通流中的连续说明与行可以生成匿名 table；`::before` / `::after` 生成的说明也使用同一布局。

表格的背景、实色边框、圆角、border-image 和 outline 使用网格矩形；说明区绘制自己的装饰。默认绘制顺序为上方说明、网格及其内容、下方说明，负边距重叠也遵循这个顺序。网格的 CSS overflow 圆角裁剪不截断说明，绘制和命中共用对应的裁剪；显式原生 Clip/ClipToBounds 仍保留框架优先级。超出包装盒的网格装饰会保留脏区域留白。

本节描述水平、普通控件的 caption，尚未扩展到原生 FlowDocument 文档节点的 caption 排版或普通 CSS 表格的分页分片。复杂层叠上下文、table 自身的 box-shadow / filter / backdrop 与说明的组合、复杂模板、完整混合百分比和垂直书写等边界仍需逐项完善。原有按钮和文字语义不会自动建立完整表格可访问关系；物理输入、VoiceOver 和其他平台实机验收尚未完成。语义依据 [CSS 2.2 Tables](https://www.w3.org/TR/CSS22/tables.html#model) 与 [CSS Tables 3 caption 定位](https://www.w3.org/TR/css-tables-3/#caption-side)。

## 表格合并边框

`border-collapse` 接受可继承的 `separate` / `collapse`，初始为 separate；普通控件表格与 FlowDocument 表格使用同一个边框冲突解析器。运行时样式、编译组件 CSS、`@supports`、变量、全局关键字和编写元数据共用注册。

```css
.matrix { display: table; border-collapse: collapse; table-layout: fixed; }
.matrix .cell { display: table-cell; padding: 8px; border: 2px solid #9ab8a8; }
.matrix .priority { border-inline-end: 6px solid #146342; }
Table { border-collapse: collapse; }
TableCell { border: 2px solid #9ab8a8; padding: 8px; }
```

每段共享边接收 cell、row、row-group、column、column-group 和 table 的候选边。hidden 会抑制其他候选；none 优先级最低；之后依次比较宽度、线型、来源类型和源列/行位置。等宽线型顺序为 double、solid、dashed、dotted、ridge、outset、groove、inset；同宽同线型优先 cell，其后依次是 row、row-group、column、column-group、table。同类颜色冲突由逻辑首列、再由较上方来源胜出，RTL 对应物理右侧。inset / outset 在此模型中分别按 ridge / groove 绘制。

胜出宽度的一半参与相邻单元格的内容尺寸；原生 BorderThickness 值保持原来的属性来源。表格自身不使用 padding 和 border-spacing，empty-cells:hide 不再隐藏空格。跨行/跨列格内部的网格线不会穿过内容；显式宽高、caption 包装盒、inline-table 基线、嵌套表格和匿名父级继续使用对应轨道。空表格仍保留明确尺寸及边框。较后行的粗外边框可以溢入表格边距，普通控件路径会为它保留脏区域留白。

共享线复用已有实线、点线、虚线、双线和立体线型绘制；交点划分绘制区域，避免同一透明边框反复叠加。合并模型忽略网格及单元格圆角。图片边框保留自身的半宽内容尺寸，中心先绘制，格的图片边缘在共享线之后绘制；切回 separate 或移除图片声明后重新使用相应的普通边框。列与行等结构盒的图片边框不参与这条路径。属性变化、继承变化和撤销声明会重新测量，并保留原始内容、父级、命中与焦点来源。

FlowDocument 的滚动与分页视图共用合并后的格和文字几何，嵌套表格的边框跟随父格内容偏移。分页会裁剪同一份连续边框；小数行高在不同页面像素原点下可能产生抗锯齿差异，不能把纯色像素数量直接当作几何守恒。

冲突规则以 [CSS 2.2 合并边框](https://www.w3.org/TR/CSS22/tables.html#collapsing-borders) 为基础；图片边框与半宽尺寸参考仍在讨论中的 [CSS Tables 3](https://www.w3.org/TR/css-tables-3/#border-collapsing)。这里尚未覆盖其完整的跨跨度传递协调提案、复杂定位/层叠与效果组合、完整连续背景和复杂模板边界，也不代表完整 CSS 或物理平台/VoiceOver 验收已经完成。

## 表格行列的可见性与折叠

普通 CSS 表格与 FlowDocument 表格均支持 `visibility:collapse` 的行列语义。先用完整的源表格计算轨道约束，再移除折叠行、列及对应的重复间距；剩余轨道保留原来的宽高，不因隐藏来源文字而重新分配。表格的使用尺寸随之缩小，包括指定宽高和最小尺寸。行组和列组的 collapse 覆盖其中的轨道，即使某一行或列声明 visible 也不会重新占位；普通控件的 caption 随可见网格重新排版。

```css
.auxiliary-column { display: table-column; visibility: collapse; }
.optional-row { display: table-row; visibility: collapse; }
TableColumn.auxiliary { visibility: collapse; }
TableRow.optional { visibility: collapse; }
```

跨格内容保留折叠前的排版尺寸，实际单元格缩短并裁剪内容；LTR 保留左上部分，RTL 保留右上部分。跨行格继承到 collapse 时与 hidden 一样不可见；显式 visible 的来源或后代仍可在未被完全折叠的区域显示。一个格覆盖的全部行或全部列均被折叠时，整格及后代不绘制、不命中，也不参与 Tab。单元格自身的 collapse 不移除轨道，允许显式 visible 的后代重新显示。hidden 行保留行高；列和列组的 hidden 不隐藏对应的格。

合并边框按折叠后的相邻关系重新解决绘制冲突，完全折叠的格不再贡献图片边框。动态切换、列跨度变化和撤销声明会更新几何、绘制、命中与可访问裁剪，原始父级和选择器顺序不变。匿名文本盒沿用源元素的 CSS 可见性，内部显示开关不覆盖它。

文档表格保留原文、源单元格和选择偏移；不可见段落不成为点选候选。跨格文字及嵌套表格的装饰受相同的可见格区域裁剪，完全裁掉的行带不进入分页，滚动和分页视图共用相同的收缩几何。尚未覆盖文档 Span/Run 的完整可见性覆盖、复杂定位和效果组合、完整表格可访问关系及物理输入/VoiceOver 验收。语义参考 [CSS 2.2 动态行列效果](https://www.w3.org/TR/CSS22/tables.html#dynamic-effects) 与 [CSS Tables 3 可见性折叠](https://www.w3.org/TR/css-tables-3/#visibility-collapse)；完整 CSS 目标仍未完成。

## 普通内容宿主的多列排版

普通 CSS block、flow-root、table-cell、table-caption 及原生内容宿主可通过 `column-count` 或 `column-width` 建立多列布局。两个值都为 `auto` 时恢复连续布局；`columns` 简写重置省略的一项。flex、grid 和 table 自身继续使用各自的格式化算法，内部的普通内容宿主可以建立多列。

```css
.article {
    display: block;
    columns: 16rem 3;
    column-gap: 1.5em;
    column-fill: balance;
    column-rule: 1px solid currentColor;
}
.article .heading { display: block; column-span: all; margin: 12px 0; }
.article .together { break-inside: avoid-column; }
```

`column-count` 接受 `auto` 或正整数，`column-width` 接受 `auto` 或非负长度，不接受百分比和非零的无单位长度。列宽与列数共同受可用宽度约束；`column-gap: normal` 在多列中使用一倍字体大小，grid/flex 的默认间距不变。支持相对长度、长度数学表达式、百分比间距、变量、CSS 全局关键字、`@supports`、编译 CSS 和编写元数据。

默认 `column-fill: balance` 平衡内容高度；`auto` 在明确的高度或最大高度内依次填列，超出的内容继续进入横向溢出列，无明确高度时连续留在首列。`balance-all` 在当前未分页的多列上下文中与 `balance` 相同。最小高度可延长列盒和分隔线。`column-span: all` 的普通块标题跨越整行，标题前的列组始终先平衡，再开始标题后的列组；正的标题上下边距在整行上保留。跨列标题建立独立格式化上下文，移除多列后仍保留这一边界。

文字复用已有塑形结果，按完整行换列；按钮、图片、复杂自绘控件及独立布局子树保持原子盒。支持列前后强制断开、`avoid` / `avoid-column`、`orphans` / `widows`，过高的原子盒保持完整并溢出。普通内容框的背景和边框按原盒切片绘制；`column-rule-width` / `style` / `color` 和 `column-rule` 简写只影响绘制，不占用间距或改变列宽，`inset` / `outset` 分别按 `ridge` / `groove` 绘制。显式 `inherit` 跟踪父级列属性的动态计算值；继承的 `currentColor` 保持符号值，在子元素上用其自身文字颜色解析，遵循 [CSS Color 4](https://www.w3.org/TR/css-color-4/#resolving-other-colors)。

分列不复制文字或控件，也不修改 `Children`、`Child`、`Inlines`、父级、源偏移或 Tab 顺序。命中、点选和无障碍可见区域使用实际列片段；间距不变成文字命中区域。RTL 从右侧首列开始，动态修改文本、宽度、方向或撤销多列会重新排版。横向溢出列纳入脏区扩展。资源保护将使用列数上限设为 1000、单组溢出片段上限设为 16384，最后一个片段保留剩余内容。

这仍不是完整多列实现。垂直书写、完整内在尺寸规则、负边距与复杂边距折叠、固定高度下无空间跨列标题的降级、跨列浮动的重新环绕、复杂定位/变换/效果、以及 `box-decoration-break: clone` 的重复边距预留仍需继续补齐。自动化焦点与坐标检查不能代替物理输入或 VoiceOver 验收。语义依据 [CSS Multi-column 1](https://www.w3.org/TR/css-multicol-1/) 与 [CSS Fragmentation 3](https://www.w3.org/TR/css-break-3/)；完整 CSS 目标保持进行中。

## FlowDocument 块宽度与相对间距

文档中的 Paragraph、Section、List、ListItem 和 Table 使用同一份 CSS 长度与盒模型计算。`margin` / `padding` 的物理和逻辑边支持百分比、相对单位及长度数学表达式；上下百分比边距和内边距也以包含块的宽度为参照。嵌套列中的块使用当前列的内容宽度，单元格内的块使用分配后的单元格内容宽度。原始 Paragraph、Run 和父级身份保持不变。

```css
FlowDocument .article {
    width: 80%;
    max-width: 34rem;
    margin: 5% auto;
    padding: 3%;
    box-sizing: border-box;
}
FlowDocument .article Paragraph {
    width: 90%;
    margin-inline: auto;
    padding-inline: calc(2% + 4px);
}
```

支持 `width` / `min-width` / `max-width` 及对应的水平逻辑尺寸；最小宽度高于最大宽度时以最小值为准。指定的 `content-box` 尺寸另加边框和内边距，`border-box` 尺寸至少容纳这些边。沿用 Jalium 原有的默认盒尺寸约定，作者可以显式设置 `box-sizing`。自动水平边距在约束后的宽度上分配剩余空间；约束冲突时由包含块的方向决定调整哪一侧边距。零宽度和解析后为负的尺寸数学表达式保留零宽度盒与可见文字溢出，选择和命中仍使用原文几何。

段落和列表的原生 10px 段后间距由属性元数据提供，CSS 可以覆盖，移除 CSS 后恢复。显式本地值和动画保持优先；清除一个等于默认值的本地尺寸或间距也会更新使用值。只改变相对长度或变量的样式同样通知各个文档视图与分页器。重新分页保留查找选中范围的连续视图位置。

列表项会绘制自己的背景、边框并使用间距和宽度约束，原有 20px 列表缩进位于 inline-start。普通与合并边框表格保留各自边框占用，合并模型继续忽略表格自身的内边距；单元格内边距按表格内容宽度计算。表格的内在宽度计算中，循环依赖的后代百分比间距先贡献零，获得确定的单元格内容宽度后再解析。

表格先分配共享列轨道，再以实际轨道宽度放置外框和原文。内容最小宽度超过声明宽度时，外框同步扩展；自动边距在实际宽度上重新分配，包含块的方向决定溢出时的边距调整。折叠列的自动边距按可见外框重新计算，原生非自动边距继续保留原有 inline-start 放置。单元格百分比内边距使用最终的未收缩表格宽度，折叠列不改变保留单元格的原文排版约束。

显式 CSS `width:auto` 按内在最小、最大宽度及包含块约束收缩，仍遵守作者的最小和最大宽度，表格自身的内容最小值可以撑开外框。没有 CSS 宽度声明时保留原生表格的拉伸约定；原生本地宽度继续优先。自动宽度下 `table-layout:fixed` 回到自动算法。空表格保留作者的最小宽度和高度。

内在尺寸关键字、完整垂直边距折叠、复杂跨列与非线性百分比贡献、浮动和垂直书写仍需继续补齐。上述行为参照 [CSS 2.2 宽度与边距](https://www.w3.org/TR/CSS22/visudet.html#blockwidth) 和 [CSS Sizing 3](https://www.w3.org/TR/css-sizing-3/)，不代表完整 CSS 已完成。

### 文档块高度

`Paragraph`、`Section`、`List` 和 `ListItem` 使用 `height`、`min-height`、`max-height` 以及对应的 `block-size` 属性决定外框和后续块的位置。长度、百分比与 `calc()` 复用共享尺寸解析；内容盒与边框盒计入各自的内边距和边框，最小高度在冲突时优先。原生本地或动画高度继续优先于 CSS 高度，显式设置原生 `NaN` 可保持自动高度，清除本地值后恢复 CSS。

百分比高度以直接包含块的确定内容高度为基础，包括父级明确高度经过最小和最大约束后的结果。父级 `height:auto` 不会仅因设置 `min-height` 或 `max-height` 就获得百分比高度基础；此时后代百分比高度按自动处理，百分比最小和最大高度分别退回零和无上限。原生文档的 `PageHeight` 控制分页区域，不会替自动高度的章节或列表项创建高度基础。

块的外框高度与原文排版高度相互独立。普通可见溢出的文字不因较小的高度或最大高度而截断，仍参与滚动范围、点选、搜索和文本提供程序的范围查询；后续块按使用高度推进，因而可能与溢出内容重叠。固定或最小高度产生的空白作为可分片的布局空间保留，单独的空章节也可跨页；该空白不会被当成文字行计入孤行和寡行约束。表格行折叠时会同步移除该行的保留空白，祖先多列布局也会保留后代块的高度占用。

当前仍需补齐完整百分比行轨道的高度分配，以及这些能力与复杂分页、浮动和垂直书写的全部组合。独立多列容器的高度行为见下文。上述普通块行为参照 [CSS 2.2 高度](https://www.w3.org/TR/CSS22/visudet.html#the-height-property)、[最小与最大高度](https://www.w3.org/TR/CSS22/visudet.html#min-max-heights) 和 [CSS Sizing 3 确定尺寸](https://www.w3.org/TR/css-sizing-3/#definite)。

### 表格单元格内部的百分比高度

文档表格先按未定的单元格高度排版内容，再分配行高。若单元格本身具有长度高度，或所属表格具有长度/百分比高度，其直接子块会按最终分配的单元格内容高度重新解析百分比 `height`、`min-height` 和 `max-height`；内容高度扣除实际内边距与边框。表格百分比高度可使用确定的父级内容高度；即使该百分比因父级高度未定而按自动处理，它仍使单元格符合第二轮内容解析条件。

```css
FlowDocument Table.schedule { height: 240px; }
FlowDocument Table.schedule TableCell { padding: 10px; }
FlowDocument Table.schedule Section.chapter {
    height: 50%;
    columns: 2;
    column-gap: 20px;
}
```

单独的行高或兄弟格的长度高度不会使自动高度单元格获得该基础；仅有最小、最大高度也不算明确高度。自动高度章节内的后代仍保持高度未定，明确的百分比章节则继续向后代传递自身的确定内容高度。跨行单元格先使用完整行分配的跨度，再应用折叠行的几何投影和裁剪。

第二轮保留首轮行尺寸与基线，百分比溢出不反复撑大行高，后续文档块仍按行框推进。溢出原文继续参与滚动、查找与分页；重新排版会替换首轮嵌套表格、多列、边框和保留空白片段，保留原始 Paragraph、Run 与父级。CSS 单元格内容盒高度另计内边距与边框；原生附加高度保持边框盒含义，本地 `NaN` 和清除值的优先级继续生效。

行与单元格指定百分比高度的分配见下一节。可滚动子块首轮高度为零的特殊规则、垂直书写和所有分页组合仍需补齐。语义参考 [CSS Tables 3 单元格内容第二轮排版](https://www.w3.org/TR/css-tables-3/#cell-layout-second-pass)。

### 表格百分比行高与剩余空间

普通 CSS 表格与 FlowDocument 表格共用行高计算。先满足各行的内容、长度高度与基线最低需求，再以表格网格高度解析行和单行单元格的百分比高度。同一行采用最大的请求；每个行组的百分比预算按来源行顺序最多分配 100%，不足以满足全部请求时按缺口比例分配剩余空间，保留内容与固定尺寸的最低需求。

```css
Table.schedule, StackPanel.schedule { height: 240px; }
.first-row { height: 20%; }
.last-row { height: 48px; }
/* 中间的 auto 行接收其余空间 */
```

百分比请求之后，剩余空间优先按当前高度比例分给未指定高度的非空行；没有这类行时，空的自动行可接收空间，最终退回非空行的比例分配。跨行内容先满足其覆盖范围的最低高度，优先扩展未指定高度的行；跨行单元格的百分比不会替代各独立行的百分比请求。分配完成后再投影折叠行，保留其他行原有的分配高度。边框间距参与网格高度，CSS 内容盒单元格的指定高度另计实际内边距与边框，本地原生值及清除值继续保持原有优先级。

文档行框中的留白按可分片的空间分页，不再放大最后一个字形行的高度。样式变化后立即查找也会将文字代理放回最终行位置与页面留白坐标。以上行为参考 [CSS Tables 3 高度分配](https://www.w3.org/TR/css-tables-3/#height-distribution) 和 [Blink 的表格高度分配实现](https://chromium.googlesource.com/chromium/src/+/main/third_party/blink/renderer/core/layout/table/table_layout_utils.cc)。草案与浏览器在超额百分比、数学表达式和自动行分配上仍有差异，当前结果按本节规则确定；跨行的全部基线组合及完整浏览器互操作仍需完善。

### 表格行组高度

普通 CSS 的 `table-row-group` / `table-header-group` / `table-footer-group` 与原生 `TableRowGroup` 支持 `height` 长度、百分比、`calc()` 和 `auto`。指定高度作为组的最低需求，组内原始行、单元格内容和跨行约束可以继续撑大该组；没有来源行的空组不产生占位行。行组不使用 padding，组内的边框间距只计一次。

```css
TableRowGroup.summary, .summary-group { height: 80px; }
TableRowGroup.chapter, .chapter-group { height: 50%; }
/* 普通控件还需为组、行、格指定相应 display 角色 */
.chapter-group { display: table-row-group; }
```

先根据组内自然行高与组的长度部分确定最低需求。表格有余量时，组百分比以扣除外侧和组间间距后的可分配网格高度解析；所有百分比组按距离目标的缺额比例分配，不使用独立行的来源顺序百分比上限。其后优先扩展正文组，再按 auto、长度、百分比组的顺序选择接收余量的类别，并按当前组高比例分配，全为零时均分。没有正文组时，表头、表尾组也可接收余量。每类首个 header/footer 组享有该角色，后续同类组作为正文组参与分配。

组内行继续使用完整表格网格高度作为百分比参考，并受该组最终分配空间约束；自然尺寸和固定尺寸不缩小。自动高度表格没有额外空间时，不因组百分比自行放大。明确行组高度本身不使自动单元格的百分比后代获得确定高度；已有明确表格或单元格高度的第二轮内容排版继续生效。

本地原生 Height（包括 `NaN` 和默认值 0）、样式替换和清除、原始内容及行组集合变化都会重算几何与缓存分页。折叠行组在完整分配后投影，不把其空间重新分给其余组，恢复时保留原始节点。原生 `TableRowGroup` 保留集合顺序；普通 CSS 表格继续使用其首个表头、表尾组排序。该分配参考上述 Blink 实现，但行百分比参考和数学表达式等仍按本文规则处理，不表示与全部浏览器或当前草案完全一致。

### 文档根盒尺寸与页面区域

`FlowDocument` 根节点使用相同的 `width` / `min-width` / `max-width`、`height` / `min-height` / `max-height`、水平逻辑尺寸、`margin`、`padding` 和 `box-sizing` 计算。根盒位于原生 `PagePadding` 留出的页面内容区内；CSS 间距与原生页面留白分别计入。水平百分比尺寸和所有百分比边距以内含页面内容区宽度为基础，自动水平边距按最终约束后的根宽度分配。

```css
FlowDocument.article {
    width: 80%;
    max-width: 40rem;
    height: 75%;
    margin: 8px auto;
    padding: 10px;
    border: 2px solid #146342;
    box-sizing: border-box;
}
FlowDocument.article Paragraph.lead { height: 50%; }
```

滚动视图的根百分比高度使用缩放前的确定视口高度，再扣除原生上下 `PagePadding`；分页视图使用每页的内容区高度。根的明确高度为后代百分比高度提供确定内容高度，`height:auto` 及仅有最小、最大高度的根仍不提供该基础。没有确定高度的滚动视图保持百分比高度未定，不以自身内容高度形成循环。原生 `PageWidth` / `PageHeight` 控制页面，CSS 根尺寸不改写纸张尺寸。

根外框、内外边距与原始文字几何共同参与连续滚动范围；LTR 右侧的可见溢出可通过启用水平滚动访问，反复布局和查找继续使用视口宽度解析百分比。CSS 根内边距和边框在连续根盒上消耗一次，分页按该盒的连续几何裁剪，固定空白高度及大于一页的前置内边距保留空白页；原生 `PagePadding` 仍逐页应用。根级多列读取根的内容宽度和确定高度，保留其自身的内边距偏移。

根附加的原生本地、动画尺寸和 `FrameworkElement.Margin`、`Control.Padding`、`Control.BorderThickness` / `BorderBrush` 值保持优先；清除默认值或 `NaN` 同样更新所有查看器和缓存分页器。根的背景继续沿用文档画布背景行为；混合边样式、圆角与完整背景层、RTL 左侧溢出的滚动访问、复杂跨列/分页组合和垂直书写仍需补齐。当前范围不代表完整 CSS 支持。

## FlowDocument 根级多列与分页

`FlowDocument` 根节点现可使用相同的列数、列宽、间距、填充和分隔线声明。连续滚动视图与两个分页器入口、单页/双页阅读器都复用原始段落的塑形行，不复制、截断或重新挂载 Paragraph、Run、Span。CSS 应附加在原始文档上。

```css
FlowDocument.article {
    columns: 14rem 3;
    column-gap: 1.5em;
    column-rule: 1px solid currentColor;
    column-fill: balance;
}
FlowDocument.article .heading { column-span: all; margin: 8px 0; }
FlowDocument.article .together { break-inside: avoid-page; }
```

分页时使用页面扣除 `PagePadding` 后的宽高，填完一页内的列后再换页；`balance` 只平衡最后一页，`balance-all` 平衡每一页，`auto` 依次填列。连续视图没有页面高度约束，`auto` 连续留在首列。文档根级 `column-gap: normal` 跟随当前字体大小。原生 `ColumnWidth`、`ColumnGap`、`ColumnRuleWidth`、`ColumnRuleBrush` 的本地值优先；清除本地值后恢复 CSS，固定原生列宽可通过 `IsColumnWidthFlexible = false` 保留列组旁的剩余空间。

普通 Paragraph 或 Section 可使用 `column-span: all` 跨越根级各列；普通 Section 内的跨列标题也参与根级布局。标题前先平衡列组，标题后重新开始。`break-before` / `break-after` 区分列、页和左右页；跨列标题前后的强制分页同样生效。`avoid-page` 约束整页列组，仍允许其中换列；`avoid-column`、段落 `orphans` / `widows` 约束列断点。空间不足时放宽可避免的约束，超高的不可分行带继续在后续页面裁剪呈现。

分列后的选择、命中、查找滚动、页面定位和文本提供程序使用原文偏移与实际片段。段落背景、富文本图片装饰、普通及合并表格边框共用相同投影和裁剪，列间空隙不会变成文字可见区域。宽度、字体、方向、列声明与原文变更会重新排版；撤销列声明时分页器按当前自然文字高度恢复首行位置。

Section、Paragraph、List、ListItem 和 TableCell 现也可建立自己的列上下文。内层按自身内容宽度计算列宽、间距、填充与分隔线，再组合外层片段坐标；标题的 `column-span: all` 只作用于最近的列上下文。原生 Table 本身继续使用表格算法，单元格中的多列不改变行列或源父级。普通及合并边框保留自然绘制几何，文字、列线和格裁剪随最终放置一起移动。

```css
FlowDocument.article { columns: 2; column-gap: 20px; }
FlowDocument.article Section.local { columns: 2; column-gap: 16px; }
FlowDocument.article Section.local .heading { column-span: all; }
FlowDocument.article TableCell.notes { columns: 2; column-rule: 1px solid currentColor; }
```

内层列在分页或外层列边界继续生成列组，每个外层片段的实际可用高度按顺序反馈到内层规划，包含已有内容占用的首片段和经过平衡的较短末页。平衡时回收内层换列产生的行尾空白，保留文字行框、跨列标题间距和表格行占用的空间；`break-after: column` 留在内层，强制页和左右页断点传播到页面上下文。原文查找、选择与文本提供程序使用组合后的同一份几何。移除内层列声明会恢复外层流，撤销全部列声明后恢复原生连续布局。平衡高度的浮点反馈向上规范化，边界误差不会生成宽度跨越另一列的细小伪片段。

独立的 `Paragraph`、`Section`、`List` 和 `ListItem` 列上下文会读取自身的高度约束：明确高度限定内容区的列容量，`max-height` 作为上限而不把较短内容撑到最大值，`min-height` 保留外框和分隔线的最小占用。边框盒、内容盒与原生高度优先级沿用普通文档块规则；容器自身的百分比高度以确定的父级内容高度为基础。

连续阅读时，内容超过列高就继续进入行内方向的溢出列，局部 RTL 会反转列的推进方向。没有溢出列时遵循 `auto` 或平衡填充；产生溢出列时使用受限容量依次填列。短内容不会缩小明确的外框高度，分隔线延续至使用的列盒高度。跨列标题前先平衡，标题及前一列组消耗本容器高度，后一列组使用剩余容量。

分页中同时考虑作者列高与祖先片段容量，超出的原文继续到后续片段；后续块按列上下文的完整分片占用推进。明确高度留下的空白及边框、内边距会保留，空列容器同样可以跨页。表格单元格中的定高列会贡献行高度，折叠和恢复该行时同步处理列、原文和高度空间。上述规则依据 [CSS Multi-column 1 的填充与溢出](https://www.w3.org/TR/css-multicol-1/#overflow)。

文档块的固定 `margin` 和逻辑边距写入该块自身的属性，百分比与自动边距保留到布局时解析，遵循原生本地值优先和撤销 CSS 后恢复默认值的规则。文字代理保留原始继承的 `text-align` 与末行对齐，已有塑形行的两端对齐随列投影一起保留；默认回退不会冒充作者在段落上设置的本地对齐值。清除一个等于默认值的原生对齐、列间距或列线属性也会重新判断 CSS 优先级。

容量反馈轮次按独立列上下文的深度计算，下限八轮、上限六十四轮；当前回归覆盖十二层嵌套，以及首片段与末页高度均变化的组合。这仍未覆盖任意深度的全部嵌套平衡、所有匿名列盒的百分比高度组合、完整浮动与垂直书写、复杂跨列标题边距、完整边距折叠和定位/效果组合；极端作者尺寸、溢出列的横向滚动范围和复杂表格分片也需要更多覆盖。截图与自动化检查覆盖 macOS Metal 的 Impeller / Vello、窄宽视口、局部 RTL 与 2x DPI；物理输入、VoiceOver 和其他平台实机未作验收。语义依据 [CSS Multi-column 1](https://www.w3.org/TR/css-multicol-1/) 与 [CSS Fragmentation 3](https://www.w3.org/TR/css-break-3/)。

## 图片回退、方向注解和片段裁剪

`image()` 现在接受 URL 或字符串引用，以及可选的回退色和 `ltr` / `rtl` 标签，适用于背景、普通元素替换内容、伪元素、列表图片和 `shape-outside`。也可放在 `image-set()` 和 `cross-fade()` 内。

```css
.preview { background-image: image("photo.png", currentcolor); }
.icon::before { content: image(ltr "sprites.png#xywh=32,0,32,32", transparent); }
.row { list-style-image: image(ltr url("arrow.png"), currentcolor); }
.crop { content: image("photo.png#xywh=percent:25,0,50,100", #dceee4) / "裁剪图片"; }
.float { shape-outside: image(ltr "mask.png#xywh=20,0,80,100", transparent); }
```

加载或解码未完成、读取失败、图片无效时，回退色生成没有自然尺寸和比例的图片。内容默认采用 300×150，列表标记采用一倍字体尺寸；作者指定的尺寸和 object-fit 仍照常应用。成功解码后重新布局，改用有效图片的自然尺寸；透明像素显示透明区域，回退色不会垫在有效图片下面。没有回退色时，加载或失败状态继续是无效图片，列表改用 list-style-type。元数据读取后才请求首个栅格，避免单像素缩略图污染 sprite 的首个可见片段。

空间片段支持 `xywh=x,y,width,height`、显式 `pixel:` 和 `percent:`，使用四个非负整数且宽高大于零。百分比范围须落在原图内；百分比坐标向下取整、尺寸向上取整。像素裁剪超出右/下边界时截到图片边缘，起点完全在原图外时按 Media Fragments 忽略该片段。多个 xywh 使用最后一个有效项，支持 URI 百分号编码。无法识别或无有效空间项的片段代表无效图片，可用回退色；不会将未理解的片段静默画成整张图。多分辨率 ICO/CUR 忽略像素片段，百分比片段仍可裁剪。加载使用去掉片段的 URL，修改片段、回退色或方向时复用驻留资源。

裁剪依据平台解码后的自然方向与坐标，EXIF 方向由平台图片解码器处理；本次在 macOS 上验证了 EXIF 6 的 JPEG。SVG 使用自身自然视口，raster 的 image-set 密度在裁剪之后换算 CSS 尺寸，SVG 不被该密度缩小。`ltr` / `rtl` 与元素方向不同时，在当前支持的水平行内轴上翻转裁剪图片；继承方向变化会更新绘制缓存和环绕形状，未加标签的图片保持自身方向。image-rendering 的采样提示也会使缓存失效。

图片绘制继续跟随动画帧；形状只读取经来源批准的首帧 alpha，裁剪与翻转后在浮点 alpha 上应用阈值。加载、失败或权限拒绝时可使用作者指定的回退色，但不会从显示图片借用未获批准的像素。变更声明后，旧的完成通知和动画不会覆盖当前图片。源文本、选择、命中、键盘焦点和可访问替代内容沿用生成内容的既有规则。注解生成栅格最长边限制为 2048 像素。

其他片段语法（包括 SVG 命名片段）、垂直书写轴、其他平台的 EXIF 与物理设备/VoiceOver 验收仍需后续覆盖。以上语义参照 [CSS Images 5 编辑草案](https://drafts.csswg.org/css-images-5/#image-notation) 和 [Media Fragments URI](https://www.w3.org/TR/media-frags/#naming-space)，不代表完整 CSS 已完成。

## 计数器和引号

`counter-reset`、`counter-increment`、`counter-set` 可以同时操作多个大小写敏感的名称。默认初始值和设定值为 0，默认增量为 1；先 reset，再 increment，最后 set。`none` 取消本条属性的操作。相同名称的 reset 只取最后一项，重复 increment 累加，重复 set 最后一项生效。整数在有符号 32 位范围内饱和，支持整数 `calc()`、变量和类型化 `attr()`。

```css
.chapters { counter-reset: chapter; quotes: "“" "”" "‘" "’"; }
.chapter { counter-increment: chapter; }
.chapter::before { content: "第" counter(chapter, cjk-decimal) "章 "; }
.chapter::after { content: open-quote attr(Tag, "摘要") close-quote; }
.chapter .chapters { counter-reset: chapter; }
.chapter .chapter::before { content: counters(chapter, ".") " "; }
```

计数器按文档顺序继承身份和数值；祖先创建的同名计数器形成嵌套，兄弟上的同名 reset 替换同层实例。`counter()` 输出最内层值，`counters()` 用指定字符串连接各层；未创建的计数器在第一次使用时从 0 开始。插入、移除、移动、属性和样式变化会重算，CSS flex `order` 不改变编号源顺序。`display: none`、原生 Collapsed 和未生成的伪元素不参与；`visibility: hidden` 仍参与。

`counter-reset: reversed(chapter)` 自动按作用域内的增量及首个 set 计算初始值，也可显式指定起点。通用计数器的默认增量仍是 +1；倒数请使用 `counter-increment: chapter -1`。`display: list-item` 的隐式编号遵循下面的列表规则。

当前表示法包括 decimal、decimal-leading-zero、lower/upper-alpha（及 latin 别名）、lower/upper-roman、lower-greek、平假名/片假名及 iroha 顺序、disc/circle/square/disclosure 符号、cjk-earthly-branch/cjk-heavenly-stem，以及 arabic-indic、persian、bengali、cambodian/khmer、cjk-decimal、devanagari、gujarati、gurmukhi、kannada、lao、malayalam、mongolian、myanmar、oriya、tamil、telugu、thai、tibetan 数字。表示法不自带列表标点；超出字母/罗马适用范围或未定义的名称回退 decimal。其他复杂预定义表示法仍待补齐。

`quotes` 支持 `auto`、`none`、`match-parent` 和一组或多组开闭字符串，默认继承。`open-quote`、`close-quote` 依据整棵文档的引用深度选择字符串，超出已给层级时重复最后一组；`no-open-quote`、`no-close-quote` 仅改变深度，额外闭引号被忽略。`none` 不输出字符，但仍改变深度。`auto` 按父级内容语言选用引号，目前对日、法、德、俄使用对应默认值，其他语言使用弯双/单引号；`match-parent` 复用父级实际采用的引号系统。

`content: counter(chapter) / "第" counter(chapter) "章"` 可用计数器形成可访问替代说明。计数器与引号的显示继续受上面的生成盒宿主范围限制。语义依据 [CSS Lists and Counters 编辑草案](https://drafts.csswg.org/css-lists-3/#creating-counters) 和 [CSS Generated Content](https://www.w3.org/TR/css-content-3/#quotes)，不代表完整 CSS 已验收。

## 自定义计数器样式

```css
@counter-style chapter-id {
    system: extends decimal;
    pad: 3 "0";
    negative: "(" ")";
    range: -999 999;
    fallback: lower-roman;
}
.chapter::before { content: counter(chapter, chapter-id); }
.binary::before { content: counter(chapter, symbols(numeric "〇" "一")); }
```

`@counter-style` 支持 cyclic、fixed（可指定起始整数）、symbolic、alphabetic、numeric、additive、extends 七种系统。可设置字符串或标识符 `symbols`、按权重严格递减的 `additive-symbols`、`negative`、`range`、`pad`、`fallback`、`prefix` 和 `suffix`。错误或 `!important` 描述符不覆盖之前的有效值；需要符号却没有有效符号的定义不生效。描述符内不支持动态 `var()`、`attr()`。`extends` 继承未指定的描述符，缺失基类或继承环使用 decimal；回退环只有在沿环找不到可表示当前值的样式时才回退 decimal。

定义在当前文档内可用，彼此独立的根文档隔离；同名规则按层叠和源顺序整条替换。支持 `@import`、layer、media、supports 及预编译构建路径，媒体偏好、视口、样式更换和挂载变化会更新选择。自定义名称区分大小写，规范预定义名称忽略 ASCII 大小写。decimal、disc、circle、square、disclosure-open、disclosure-closed 不能被重新定义。

`pad` 按字素簇计数，负数的负号也计入总宽度；例如十进制 `pad: 3 "0"` 将 -5 表示为 -05。字符数过大的表示会采用 fallback，以避免 symbolic、additive 或巨量补位分配无界文本。`counter()` 和 `counters()` 只返回表示本身，不添加 `prefix`、`suffix`；默认列表标记会添加所指定样式的这两个描述符，即使数字表示来自 fallback。

`symbols()` 支持字符串符号与 cyclic、fixed、symbolic、alphabetic、numeric 系统，省略类型时为 symbolic；numeric/alphabetic 至少需要两个字符串。它可在 `counter()`、`counters()` 的样式参数中使用，也可参与变量、动态属性和可访问替代文本。匿名函数中的符号必须加引号。

图像符号和 `speak-as` 的辅助技术语音输出仍待实现；目前只保留 speak-as 描述符。相关规则依据 [CSS Counter Styles Level 3](https://www.w3.org/TR/css-counter-styles-3/)。

## 大小写字形变体

`font-variant-caps` 接受 normal、small-caps、all-small-caps、petite-caps、all-petite-caps、unicase、titling-caps。当前接入字体自带的 smcp／c2sc、pcap／c2pc、unic 和 titl 特性，计算值保持原关键词。可继承，参与变量、全局关键字、级联层、重要声明、font／all 重置、离散动画和 style() 查询。font-feature-settings 在同名标签上覆盖高层选择。源文字、节点和公开字体族不因选字形而改变。

```css
.caption { font-variant-caps:small-caps; }
.acronym { font-variant-caps:all-small-caps; }
.legacy { font:italic small-caps 600 20px/1.5 Seravek; }
```

font 简写的 CSS2 变体分组仅接受 normal／small-caps，其他大小写变体通过长属性设置；省略时恢复 normal，系统字体简写也重置此值。显式字号后的 small-caps 是普通族名。重复或互斥前缀使整条简写无效。

petite-caps 在所选字体未定义 pcap 时按 small-caps 请求；all-petite-caps 在 pcap／c2pc 任一缺失时整组按 all-small-caps 请求，同时关闭原 petite 请求，避免同一字体混用两种大小。判断基于字体 GSUB 特性列表，不以当前字符有没有替换判断。每个明确选择的字体及 CSS 后备字体分别查询；计算值仍是作者指定的 petite 关键词，style() 查询不会因实际使用 small 字形而改变。字体描述符先应用，caps 选择随后应用，元素的 font-feature-settings 最后覆盖。

渲染标识保留 caps 请求，在创建实际字体格式时选择特性，编译 CSS 不捕获编译机的字体能力。原有普通字体标识保持兼容。原生查询提供 GSUB／GPOS 分表选择，caps 仅查询 GSUB；旧载荷或后端没有查询能力时保留原请求并报告转换边界。当前已验证的混合字体与绘制路径为 macOS，其他平台运行仍需验收。

macOS Metal／Software 已接入缺少字形时的合成：small-caps 将小写字母转为大写并以原字号的 80% 绘制，all-small-caps 同时处理大写字母；petite 回退复用这一分支。缺少 unic 时，仅将原有大写字母按小型大写处理，原有小写保持原字形；有 smcp 时使用真实小型大写，否则缩小合成。转换保留所选字面、原始字体度量与基线，中文、数字和 emoji 等无大小写内容不缩小。

`font-synthesis-small-caps` 支持 auto／none，初始为 auto，可继承、参与变量、层、全局重置、离散动画和 style() 查询。none 只禁止合成，字体已有的真实特性仍可用。字体缺少 smcp 时，元素的 `'smcp' off` 也不会取消 small-caps 的合成请求；字体具有 smcp 时，关闭该特性恢复普通字形。此属性不随 font 简写重置。

渲染标识同时保留语言与合成许可。原生大小写使用与托管 CSS 相同版本的 Unicode 18 数据：完整映射、Lowercase、Uppercase／Titlecase、Cased 及上下文属性，不依赖系统预定义字符类别。覆盖罗马数字、带圈字母、组合字符、非 BMP 字母、词末 Sigma、土耳其语／阿塞拜疆语删点与立陶宛语重音规则。同一字素中的字符分别保留字体和绘制属性，组合重音跟随合成基字，源字素仍作为完整映射单元。

原生段落转换使用整段源上下文；普通 TextBlock 中有不同 caps 样式的多个 Run 也复用共享排版，使同一行的测量、绘制、选择与光标使用相同转换。已覆盖 `ß → SS` 的扩展和 `I + DOT ABOVE → i` 的收缩；文字、节点和公开族名不改变，命中、选择、IME 范围、字形遮罩及预览仍使用源 UTF-16 索引，换行不会把一次源字素扩展切在中间。

macOS 普通 TextBlock 的共享片段排版现在保留片段之外的六种 Unicode 上下文谓词，测量、绘制、选择和命中共用它们。40px、break-all 下的 `Ο + unicase(ΣΑ)` 已修复为 1＋2 个源字符；单一文本、混合 Run、重音、可忽略标点、硬换行、间距、反复调整宽度和样式恢复已加入验证。边界谓词为 64 种，组合字符的首部大小写操作为 3 种，大小写上下文部分最多 192 种，不把全文放入字体缓存键；已有普通、caps 和语言／合成标识保持兼容。新原生接口独立于既有可选接口，旧后端可返回不支持。含 Sigma 的上下文 unicase 使用 CSS 行框基线，覆盖普通控件及文档投影。

同一 CSS 行内段落中的多个 TextBlock、嵌套行内框及显式 Run 行内盒也已接入逻辑上下文。段落在逐行拟合前建立共享表，原始源位置保持不变；内边距、边框、外边距及垂直对齐形成字形塑形边界时，Unicode 大小写上下文继续传递，替换对象与块级边界则中断上下文。已验证跨来源的词中／词末 Sigma、硬换行、重音、可忽略标点、间距、宽度往返、文字编辑、重排、移除、样式恢复与脱离父级后的上下文清理。

跨来源的组合字符现在从完整逻辑段落查询实际字体解析后的大小写操作，再传给以组合附加符开头的片段；附加符保留自身字体和样式，遵循基字的合成或原生 smcp 操作。已验证 tr／az-Latn 的 `I + U+0307`、lt 的 `i／j + U+0307` 和 `I + U+0301`，包括连续来源、1px 内边距形成的塑形边界、混合字体、禁止合成及语言子标签。转换后完全不可见的附加符仍保留源 UTF-16 长度、行度量、光标和零宽选择范围；跨来源的同一转换字素由包含其最后源代码单元的来源绘制一次，避免重复或遗漏。宽度往返和恢复 normal 后会清理转换状态。

其他平台尚未接入这套合成。上述组合字符用例通过不代表所有字体、语言和组合路径均已覆盖；裁剪／省略、制表符、字体拉伸和改变字符数的文本变换仍需继续统一及验收。实际系统输入也仍需验收。普通直接绘制与共享排版之间的个别小数基线偏移仍需后续统一。Unicode 数据符合性不等同于所有字体、语言、绘制及交互路径已完整支持。不能据此声明此属性或完整 CSS 已完成。Unicode 数据的[许可副本](../src/native/thirdparty/UNICODE-LICENSE.txt)随源码保留，生成器可从固定 UCD 输入离线重现。

宿主入口 `--css-font-caps` 验证字体已有特性，`--css-font-caps-fallback` 验证 petite 回退，`--css-font-caps-synthesis` 验证合成，`--css-font-caps-boundaries` 验证普通控件的换行上下文、像素与源范围，`--css-shared-caps-context` 验证共享行内来源，`--css-caps-combining-context` 验证跨来源组合字符。依据 [CSS Fonts 大小写字形](https://www.w3.org/TR/css-fonts-4/#font-variant-caps-prop) 与 [合成许可](https://www.w3.org/TR/css-fonts-4/#font-synthesis-small-caps)。记录见 [已有特性](../artifacts/css-font-caps-20261019/README.md)、[字体能力和 petite 回退](../artifacts/css-font-caps-fallback-20261019/README.md)、[合成及源索引验证](../artifacts/css-font-caps-synthesis-20261019/README.md)、[Unicode 大小写和上下文验证](../artifacts/css-font-caps-unicode-20261019/README.md)、[换行片段上下文验证](../artifacts/css-caps-context-boundaries-20261020/README.md)、[共享来源上下文验证](../artifacts/css-caps-shared-context-20261020/README.md) 与 [组合字符上下文验证](../artifacts/css-caps-combining-context-20261020/README.md)。

## 上标与下标字形

`font-variant-position` 接受 normal、sub、super，默认 normal 并继承；`font-synthesis-position` 接受 auto、none，默认 auto 并继承。属性支持变量、全局关键字、font／all 重置、编译 CSS、style() 查询、离散过渡与关键帧。font 简写重置 position，而不重置合成许可。

macOS Metal／Software 复用 CoreText 的真实字体与回退字面解析。sub／super 分别请求 subs／sups；当连续文字段的每个非空白字素都有对应变体时使用原生字形。一旦某个字素缺失，整段使用原字形的缩小合成，避免数字与字母、不同字体或颜色之间混合两套不齐的上下标；none 禁止这种合成，但不禁用已有的原生变体。显式低层特性仍可关闭已有字形选择。

显式的 `font-feature-settings` 属性最后应用：既有原生变体和整段合成都会保留作者的 subs／sups 请求，合成不再用内部的 0 覆盖作者显式开启的特性。每个来源分别保留精确选择值；字体特性替换失败不改变旧状态。unicase 使用小型大写回退时也保留显式 smcp 数值。font-face 描述符仍低于高层变体，属性级低层设置仍最高。

绘制元数据使用 64 位有符号存储容纳完整的原生 32 位无符号请求，避免截断及缓存键混同。具体字形依赖字体和平台：本机 CoreText 对测试夹具的 subs／sups／smcp 正值按开启处理，即使 GSUB 字表包含多个备选，本轮也未统一这些标签的自定义多索引字形选择。它与真正按索引选择的 salt 分开记录；salt 的 0／1／2／3 已与独立字面参考比对。规范只对超出字体实际支持范围的值不规定确切字形，不能把所有大于 1 的请求都归为这种情况。源范围、整段决策和高层合成许可不因此改成低层开关。

合成保留原始字面、文字、节点、源 UTF-16 索引与行框基线，不改变公开字号。20px 及以上文字使用原字号的 65%；较小字号逐步放宽到 80%，提高可读性。上标的绘制基线提高 0.35em，下标降低 0.2em；不依赖可能不准确的 OS/2 上下标度量。嵌套相同请求不累计缩小或位移。完整逻辑段落先解析选择结果，再将有界模式交给换行片段与共享来源；来源字体、颜色或 1px padding 边界本身不会让前半段误用原生变体。

普通 TextBlock、Run／Span、共享 CSS 行内来源已验证实际字形与源范围；文档投影、标记及公开使用端也有自动检查。直接字体 API 保留合成前的光标行高与起点。旧原生库缺少语义接口时，会报告有损转换并请求仍可用的 subs／sups 特性；完整合成与段落元数据需要匹配的新版原生库。窄宽度、1×／2× DPI、Metal Impeller／Vello、Software Auto、文字编辑、宽度往返与 normal 恢复的范围见 [上下标验证记录](../artifacts/css-font-variant-20261020/README.md)，宿主入口为 `--css-font-position`。低层优先级补充入口为 `--css-font-feature-priority`，证据与平台边界见 [字体特性优先级验证](../artifacts/css-font-feature-priority-20261020/README.md)。

这仍不等于完整 CSS。emoji 呈现与总简写的新增实现见下节；全部装饰／裁剪／制表符／字体拉伸组合、复杂双向与竖排、所有字体及其他平台还需继续统一和验收；离屏绘制与程序化选择不替代可见窗口、VoiceOver、物理输入或真实系统 Writing Tools 会话。语义依据 [CSS Fonts 4 的上下标字形](https://www.w3.org/TR/css-fonts-4/#font-variant-position-prop)与[合成许可](https://www.w3.org/TR/css-fonts-4/#font-synthesis-position)。

## Emoji 呈现与 font-variant 总简写

`font-variant-emoji` 接受 normal（初始值）、text、emoji、unicode，按计算值继承。normal 使用排版器的普通选择；后三者只影响 Unicode Emoji Presentation Participating Code Points：text 如同追加 VS15，emoji 如同追加 VS16，unicode 根据 Unicode 的 Emoji_Presentation 默认值选择 VS15／VS16。该集合也包含数字、#、*，并非所有 emoji 字符都在集合内。原文已有的 VS15／VS16 优先，包括跨 Run／Span、共享来源及物理内联边界的情况。

macOS Metal／Software 在绘制文本中插入选择符，公开文本、节点及 UTF-16 光标／命中／选区索引保持原文。软换行片段只携带有界的尾随选择符上下文；非参与字符不会因该属性切换普通排版路径。Unicode 18.0.0 的参与集合与默认呈现表由固定 SHA-256 的官方数据生成，可通过 `tools/generate-css-emoji.py --data-dir <本地 UCD emoji 目录>` 离线重现。呈现仍取决于选定字体、回退字面及其可用字形；text 不保证每个字体都提供单色设计。

```css
.label { font-variant-emoji: text; }
.status { font-variant-emoji: emoji; }
.figures { font-variant: small-caps oldstyle-nums; }
.script { font-variant: super tabular-nums text; }
```

`font-variant` 现展开 ligatures、position、caps、alternates、numeric、east-asian、emoji 七个长属性。各组可交错，复用长属性对重复、互斥和函数参数的校验；非法声明整体丢弃。normal 将七项重置为 normal；none 将 ligatures 设为 none，其余六项重置为 normal。普通组合中省略的长属性同样重置；不会重置 font-kerning、font-feature-settings 或合成许可。font 简写会重置 emoji。变量、CSS 全局关键字、层叠、重要声明、编译 CSS、style() 查询及离散过渡／关键帧沿用同一份计算值。

宿主入口 `--css-font-emoji`。实际原生渲染、字面选择符参考、宽度往返、重复样式切换、文档与标记继承、直接／编译 CSS 使用端和数据检查见 [emoji 与总简写验证](../artifacts/css-font-variant-emoji-20261020/README.md)。WPT 语法示例仅核验输入是否接受，未实现或验证浏览器 CSSOM 序列化。旧原生库与其他后端缺少该可选接口时报告有损转换。制表符、裁剪省略、非正常字体拉伸、复杂双向及竖排的完整组合，以及其他平台、实体输入和 VoiceOver，仍需后续验收。依据 [CSS Fonts 4 emoji 呈现](https://www.w3.org/TR/css-fonts-4/#font-variant-emoji-prop)和[总简写](https://www.w3.org/TR/css-fonts-4/#font-variant-prop)。

## 数字字形与 OpenType 特性

`font-variant-numeric` 支持 normal，以及三组选一的 lining-nums／oldstyle-nums、proportional-nums／tabular-nums、diagonal-fractions／stacked-fractions，后两项 ordinal、slashed-zero 可独立组合。关键词顺序不影响含义；重复或互斥组合使整条声明无效。属性可继承，initial、unset、inherit、revert 和 all 沿用正常级联；font 简写将它重置为 normal。变量、编译 CSS、Run／Span、文档视图和标记使用同一份映射。

```css
.amount { font-variant-numeric: lining-nums tabular-nums; }
.body-figures { font-variant-numeric: oldstyle-nums proportional-nums; }
.fraction { font-variant-numeric: diagonal-fractions; }
.serial { font-variant-numeric: slashed-zero; }
```

字体特性进入测量、字形绘制、段落和命中的内部字体标识；公开 FontFamily.Source 和原始 UTF-16 文本不改变。样式切换更新现有排版缓存，回到 normal 不保留上一组特性。字体宽度、私有字体、按字符范围拆分的字体与回退顺序继续保留。CoreText 在复制特性时使用原始图形字体，避免把未安装的私有字体按名称替换为系统字体。

原生接口为可选的 OpenType 特性能力，覆盖 CoreText、DirectWrite，以及共享自研 GSUB 路径。字体没有对应字形时按字体默认行为显示；旧原生载荷或不具备此能力的后备后端保留普通字形并报告有损转换。共享自研路径仍有复杂上下文替换、字体专有定位等限制，Windows／Linux／Android 的实际运行须分别验证，不能由当前主机结果推定。

检查入口为 `--css-font-numeric`，输出使用 `JALIUM_CSS_WRITING_TOOLS_OUTPUT`；通过 `JALIUM_CSS_NUMERIC_FONT` 指向测试资源中的 FontWithFancyFeatures.otf。固定版本 WPT 字体及其参考字形用于验证八种特性；原生对照、构建、编译路径、场景和验证边界见 [数字字形验证](../artifacts/css-font-numeric-20261018/README.md)。语义参考 [CSS Fonts 数字排版](https://www.w3.org/TR/css-fonts-4/#font-variant-numeric-prop)。这不代表完整 CSS／WPT 一致性或物理输入、AX、VoiceOver 验收。

### 东亚字形与宽度

`font-variant-east-asian` 接受 normal，或以下三组的任意非空组合：jis78／jis83／jis90／jis04／simplified／traditional 中的一项、full-width／proportional-width 中的一项，以及独立的 ruby。组间次序任意；重复、同组互斥值及 normal 与其他值混用使整条声明无效。属性按完整计算值继承，支持变量、全局关键字、级联层、font／all 重置、allow-discrete 过渡和离散关键帧。

```css
.japanese { font-variant-east-asian: jis04 proportional-width; }
.chinese { font-variant-east-asian: simplified full-width; }
.annotation { font-variant-east-asian: ruby; }
```

各值分别请求 jp78、jp83、jp90、jp04、smpl、trad、fwid、pwid、ruby 特性；ruby 只选择字体中的注音字形，不创建注音布局。字体没有相应字形时保留默认显示。Run、Span、文档、共享文本及列表标记使用相同特性组合，元素 font-feature-settings 可覆盖同名标签。源文本、公开 FontFamily.Source 和节点归属保持原有值。未完整实现的 font-variant 总简写仍不开放。

`style(font-variant-east-asian: jis04 full-width ruby)` 按完整计算值比较，组间次序不影响结果；字形刚好相同不代表计算值相同。font-kerning、font-variant-ligatures、font-variant-numeric、font-feature-settings 同样可作样式查询，继承变化会重新评估查询。

检查入口 `--css-font-east-asian`，使用 `JALIUM_CSS_WRITING_TOOLS_OUTPUT` 指定输出，`JALIUM_CSS_EAST_ASIAN_FONT` 可指定固定版本 WPT 测试字体。验证记录见 [东亚字形验证](../artifacts/css-font-east-asian-20261019/README.md)，语义依据 [CSS Fonts 东亚文本](https://www.w3.org/TR/css-fonts-4/#font-variant-east-asian-prop)。当前主机结果不能替代其他平台运行、物理输入或 VoiceOver 验收。

### 命名字形与字体专属替代

`font-variant-alternates` 接受 normal、historical-forms，或后者与 stylistic(name)、styleset(name, ...)、character-variant(name, ...)、swash(name)、ornaments(name)、annotation(name) 的组合。各类最多出现一次；stylistic、swash、ornaments、annotation 各接收一个名称，styleset 和 character-variant 可接收多个名称。函数名不区分大小写，名称区分大小写，支持 CSS 标识符转义。整组计算值继承，支持变量、全局关键字、font／all 重置、allow-discrete 过渡、离散关键帧及 style() 查询；未定义名称保留在计算值中，选择字形时忽略。

```css
@font-feature-values Brand Serif, "Brand Serif Fallback" {
  font-display: swap;
  @stylistic { title: 2; }
  @styleset { editorial: 1 3; }
  @character-variant { alternate-a: 1 2; }
}
.title {
  font-family: Brand Serif, "Brand Serif Fallback", serif;
  font-variant-alternates: stylistic(title) styleset(editorial);
}
```

`@font-feature-values` 只接受具名字体族，可声明多个族和重复特性块；定义按字体族、特性种类、区分大小写的名称分别合并。同名定义按级联层和文档顺序取胜，不同名称保留并集，支持导入及条件规则。无效声明与未知块按 CSS 错误恢复规则跳过。整数值非负，styleset 可列多个索引；character-variant 接收一个索引或索引与选择值。OpenType 的 ss／cv 索引 1～99 转成相应标签，0 或大于 99 不启用标签；其他特性使用给定选择值。historical-forms 请求 hist；stylistic 请求 salt；swash 同时请求 swsh／cswh；ornaments 请求 ornm；annotation 请求 nalt。后出现的 character-variant 名称可覆盖同一标签的选择值，参数次序参与计算值比较。

名称按实际选用的字体族解析，包括 CSS 字体别名和字符范围子集；回退到其他字体不会继承另一字体的编号。字体描述符特性先应用，再合并变体，显式 font-feature-settings 最后覆盖。原生设置的 FontFamily 继续选择原生字体，可使用其字体族定义，不因同名 CSS 字体别名被替换。文档排版代理使用原始文档的字体作用域，保留空格、基线、换行、命中与 IME 几何；字体加载完成和定义替换会更新已显示的文档。

font-display 设置整个字体族的默认显示策略，仅对没有有效显式 font-display 的 @font-face 生效；显式 auto 仍保持 auto。实际 CLI 编译保留省略信息；旧 FontFace builder 重载保留显式 display 参数的语义，新增重载及新编译器可表达省略。

检查入口 `--css-font-alternates`，通过 `JALIUM_CSS_ALTERNATES_FONT` 指定测试字体，使用 `JALIUM_CSS_WRITING_TOOLS_OUTPUT` 输出回读。依据 [CSS Fonts 字体替代](https://www.w3.org/TR/css-fonts-4/#font-variant-alternates-prop) 和 [命名特性规则](https://www.w3.org/TR/css-fonts-4/#font-feature-values)。构建、固定 WPT 参考字形、编译路径及实际验证边界见 [命名字形验证](../artifacts/css-font-alternates-20261019/README.md)。这些结果不代表完整 CSS／WPT 一致性或其他平台、物理输入、VoiceOver 验收。

### 通用字体特性

`font-feature-settings` 接受 normal，或逗号分隔的四字符 OpenType 标签和可选整数／on／off。标签区分大小写，限 U+0020～U+007E；CSS 字符串转义可表示引号、反斜线等可打印字符。省略值等于 1，off 等于 0；重复标签以最后一个值为准，计算值按完整映射继承，子元素的一项设置取代整张父映射。整数和 calc 沿用共享数值解析及元素上下文；font 和 all 可重置本属性。

```css
.amount { font-variant-numeric: tabular-nums; font-feature-settings: "tnum" 0; }
.alternate { font-feature-settings: "salt" 2; }
@font-face {
  font-family: InvoiceText;
  src: local(Seravek);
  font-feature-settings: "tnum" 1;
}
```

`@font-face` 的同名描述符为被选中字体设定默认特性；字符范围和回退的其他字体保留各自默认值。优先级为字体描述符、元素数字／东亚／命名替代／连字变体和字偶距、非零 letter-spacing 的可选连字抑制、元素 font-feature-settings。normal 不额外覆盖较低层特性，显式 0 可关闭同名特性。测量、字形绘制、换行、段落、命中及文档标记共用相同结果；样式撤销保留原始文本和节点。实际 CLI 生成的 CSS 同样保存字体描述符，旧 FontFace builder 重载保留二进制兼容。

单个特性映射的实现上限为 4096 项，整数为 0～2147483647；合并后的原生调用上限为 16384 项。共享整形器增加 GSUB AlternateSubst 的一基索引选择，字体没有对应索引时保留原字形；其他复杂上下文替换、GPOS 类型和跨平台运行边界仍须分别验证。检查入口 `--css-font-feature-settings`，参见 [通用字体特性验证](../artifacts/css-feature-settings-20261018/README.md)；本阶段不代表完整 CSS／WPT 或物理可访问性验收。语义依据 [CSS Fonts 特性与优先级](https://www.w3.org/TR/css-fonts-4/#feature-precedence)。

## 列表项与文本标记

Panel 及已接入内容格式化协议的宿主（例如 Border）可以用 `display: list-item` 建立流式列表项。支持与 `block`、`inline`、`flow`、`flow-root` 合法组合，不接受与 flex/grid 的组合：

```css
@counter-style steps { system: extends decimal; pad: 2 "0"; suffix: ". "; }
.list { counter-reset: list-item; padding-left: 40px; }
.item { display: list-item; list-style: steps outside; }
.item::marker { color: #146342; font-weight: 600; }
.compact .item { list-style: symbols(cyclic "甲" "乙" "丙") inside; }
.compact .body { display: inline-block; }
.reversed { counter-reset: reversed(list-item); }
```

`list-style-type` 接受具名计数器样式、`symbols()`、字符串和 `none`，默认是 disc；`list-style-position` 默认为 outside，可设为 inside。两者继承，`list-style` 简写会重置遗漏的子属性。`list-style-image` 接受 `none`、URL、已有渐变和 image-set，继承并参与 `list-style` 简写；简写中重复位置、重复图片或无法分配的 none 使整条声明无效。有效图片优先于 list-style-type，并在后面添加一个空格；加载中或失败时回退文字样式，list-style-type: none 时无回退文字。渐变等无自然尺寸的标记默认使用 1em 正方形，字体、颜色、DPI 和图片状态更新后会重新布局。

列表项隐式递增 `list-item`，作用域为 reversed 时隐式递减。`counter-increment` 显式包含 `list-item`（包括 0）时取代隐式增量；设置 `none` 或只操作其他计数器不会抑制隐式编号。处理顺序是 reset、增量、set；标记读取该列表项的编号后，才处理 `::before` 和正文。插入、移除、重排、隐藏和样式切换会重算。

`::marker` 在 `content: normal` 时使用列表样式；显式内容可使用图片、字符串、变量、动态属性、计数器和可访问替代说明，覆盖 `list-style-type: none`。`content: none` 隐藏标记；普通非列表项的 marker 内容计算为 none。字体、颜色和已实现的可继承文本属性可作用于标记；宽高、盒间距、背景、定位和 transform 等盒属性不影响标记，包括关键帧中的这些属性。默认保留空白、禁用 text-transform，并使用 `font-variant-numeric: tabular-nums`；作者可覆盖或用 revert 回到这组 UA 默认值。`unicode-bidi` 的作者属性及隔离默认值已接入，跨来源标记的统一段落排版范围见后文。

outside 标记不占正文宽度，在 inline-start 侧与第一行基线对齐；RTL 放在右侧，作者需为其保留外侧空间。inside 标记是格式化子级的第一项，排在 `::before` 之前。`inline flow list-item` 的 outside 按 inside 使用；`inline flow-root list-item` 保留块容器的外侧标记。TextBlock 自身的正文与行内生成文字、流式父级中的叶级 TextBlock 和已适配的嵌套行内容器可跨共享行框碎分并绕过矩形浮动排除区，遇到块级后代时拆分段落；其他未适配宿主仍需补齐。

生成标记不加入公开 `Children`，不改变结构选择器、文档编号顺序或 Tab 顺序；命中归属列表项，可访问文本延续生成内容协议。其他自绘正文和尚未接入宿主的标记、`::before::marker` / `::after::marker`、marker-side、完整双向和垂直书写布局仍需补齐。语义参考 [CSS Lists and Counters 编辑草案](https://drafts.csswg.org/css-lists-3/#markers)。

### FlowDocument 与 RichTextBox 的列表标记

原生 `List` / `ListItem` 在文档查看器和 CSS 编辑呈现中复用同一套生成内容、计数器、`::marker` 级联及标记图片管线。没有作者 `list-style-type` 时使用原生 `MarkerStyle`；没有作者 `counter-reset` 时按 `StartIndex` 初始化当前列表。嵌套列表分别保存起始编号，`counters(list-item, '.')` 可读取原始祖先计数器。显式作者声明仍按既有级联规则计算。

```css
List.steps { list-style-type: decimal-leading-zero; }
List.steps > ListItem::marker { color: #146342; font-weight: 600; }
List.steps ListItem ListItem::marker { content: counters(list-item, '.') ' '; }
```

每个查看器拥有自己的标记视觉对象，选择器和计数器使用原始文档树；共享文档不会因多个视图而重复递增，也不改写列表、段落、Run 或可编辑文本范围。新增、移除、改变起始值、样式撤销及图片状态更新会刷新标记。`display:none` 子树不计数、不贡献标记宽度；`visibility:hidden` 保留计数与占位。

普通流中的原生列表保留至少 20 DIP 的行首编号列，宽标记与原生 `MarkerOffset` 可增加它。该列独立于 List 的 CSS padding，ListItem 的百分比尺寸与间距仍基于扣除编号列后的可用宽度；外层文档切换到 Grid/Flex 时也保留此约定。作者把 List 自身改为 Flex/Grid 等其他格式化器时，原始 ListItem 直接参与该格式化器，保留 `order` 等项目属性，外侧标记空间由作者的 padding 等布局声明预留。显式取消 ListItem 的 list-item 角色也取消生成标记。outside 标记与项目首行基线对齐，RTL 放在右侧；inside 使用格式化器的行内标记。没有段落的可见标记项目仍保留一行，修改编号再恢复不会丢失原始计算行高。

原生检查入口为 `--css-document-list-markers`，输出目录由 `JALIUM_CSS_WRITING_TOOLS_OUTPUT` 指定，固定源码与验证范围见 [文档列表标记记录](../artifacts/css-document-markers-20261018/README.md)。此处接入所列原生来源与共享格式化路径；任意文档伪元素、完整跨页／跨列和双向／垂直组合、marker-side、原生 AX 事件及 VoiceOver 仍需继续验收，不能据此推定完整 CSS 一致性。

## 固有尺寸计算与插值

已接入 FrameworkElement 的 `width`、`height`、`min-*`、`max-*`、对应逻辑尺寸别名以及 Flex 的 `flex-basis` / `flex` 简写：

```css
.record {
  height: calc-size(auto, size + 24px);
  overflow: hidden;
  transition: height 200ms ease;
}
.record.closed { height: calc-size(any, 0px); }
.opt-in { interpolate-size: allow-keywords; }
.opt-in .panel { transition: height 200ms ease; }
.opt-in .panel.closed { height: 0; }
.opt-in .panel.open { height: auto; }
```

`calc-size(basis, calculation)` 保留 basis 的布局行为，再将该基准的使用尺寸代入第二个参数中的 `size`。支持相应属性允许的 `auto`、min/max-content、fit-content、stretch、fit-content()、长度/百分比计算和嵌套 calc-size；`content` 仅用于 flex-basis，max-* 不接受 auto。`any` 不依赖固有基准，第二个参数不能使用 size。两个参数中的数值计算都需要长度/百分比类型，零长度写作 `0px`。calc-size 不是普通长度，不能放入 calc()/min()/max() 或 padding、gap 等未声明接收它的属性。

计算遵循 content-box/border-box 数量空间；嵌套结果不会提前截断负数。auto 基准保留普通流、Grid stretch、绝对定位双侧 inset、宽高比、Flex 自动最小尺寸等对应行为。纵向 Flex 以不定的 auto 基准决定换行，再按实际主轴尺寸分配伸缩空间。清除原生本地尺寸或动画值后会恢复当前 CSS 表达式，原始内容对象保持不变。

`interpolate-size` 继承，默认 numeric-only，本身不可动画。allow-keywords 允许尺寸关键词与数值插值；不同固有关键词之间仍为离散变化。显式 calc-size 可与兼容基准、any 或普通数值进行 replace 型过渡/关键帧插值。插值前展开嵌套基准、替换长度基准，并将百分比基准统一为 100%；中间值继续观察实时内容尺寸，支持中断后继续过渡。嵌套解析上限为64层，插值替换有4096节点预算，超出时退回离散采样。

尺寸继承保留计算表达式及其字体单位上下文；尺寸 style() 查询比较计算值的基准、百分比依赖和算式，不以当前像素大小代替计算值。已覆盖线性算式和结构相同的非线性表达式，不承诺任意数学等价式的化简。

依据 [CSS Values 5 §11](https://drafts.csswg.org/css-values-5/#calc-size) 当前草案，第二个参数在不定包含块中的百分比按零计算，基准中的百分比保留对应布局语义。已安装 Chromium 对部分不定百分比基准仍采用整项 auto 的行为，与本实现的使用值求解存在差异；这些用例单独记录，未算作浏览器一致。该草案及相关 WPT 仍在演进。

上述验证范围是普通元素尺寸通道。原生 FlowDocument 的独立尺寸实现见下一节；完整动画组合、完整 WPT、垂直书写/分片及跨平台物理和辅助技术验收仍需继续。`column-width` 当前语法为 auto 或非负长度，不接受 calc-size()；不能把尺寸属性允许的函数直接扩展到列宽语法。完整 CSS 支持目标保持进行中。


## FlowDocument 固有尺寸与计算

原生 `FlowDocument` 根节点、`Paragraph`、`Section`、`List` 和 `ListItem` 的独立排版现接入固有宽高、最小/最大尺寸与 `calc-size()`。根、段落和分节支持 min/max-content、fit-content、fit-content()、stretch、数值/百分比基准与嵌套计算；列表沿用既有20DIP缩进。表格的固有/计算优先宽度、单元格的定值计算宽度以及表格/单元格的定值计算高度，复用原有轨道分配路径。

```css
FlowDocument.article { interpolate-size: allow-keywords; }
FlowDocument.article Section.record {
  height: calc-size(auto, size + 24px);
  padding: 12px;
  box-sizing: content-box;
}
FlowDocument.article Paragraph.note {
  width: calc-size(min-content, size + 20px);
  max-width: 100%;
}
```

`auto` 的自然尺寸来自原文档的真实行盒；内容修改后重新求值，分列平衡和分页缓存使用相同的结果，不复制或重新挂载原始 Paragraph、Run、Span。普通段落也按真实行盒推进，不再将独立 TextBlock 控件的光学测量余量重复计入段落高度。`box-sizing` 在计算式使用的数量空间中生效，嵌套表达式保留中间负值，到最终盒尺寸才限制为非负并保留边框/内边距。

固有最大高度会让循环百分比后代保持 auto；固有最小高度增大原本确定的高度时，先以不定自身高度取得自然贡献，再用最终高度解析后代百分比，避免将溢出反复反馈为更大的固有高度。stretch 的高度基于可用边距盒空间。显式 inherit 保留父节点的已计算字体单位；原生本地或动画尺寸仍优先，清除后恢复 CSS；上下界覆盖也会替换其后代百分比定性。即使计算式不使用 size，例如 `calc-size(max-content,80px)`，仍保留基准的固有尺寸行为。样式查询及已有 `@keyframes` 采样读取非视觉节点的带类型表达式，保留实时内容依赖。

段落代理的原生 Wrap 现在作为回退值，CSS `white-space`、`overflow-wrap` 和 `word-break` 可以控制换行与固有宽度。普通英文单词的 min-content 不会被原生紧急换行拆成单字母；anywhere/break-all 可缩小固有最小宽度，break-word 不缩小。运行时添加或清除换行声明会刷新现有查看器，清除后恢复原生文档回退。

本节验证覆盖所列节点的选择用例、滚动阅读器、两个共享呈现视图、分页缓存、根/嵌套列、暂停关键帧、样式查询和编译组件 CSS。非视觉节点的自动 transition 声明、复杂表格固有高度与循环百分比组合、文档 overflow 裁剪、完整动画组合与 WPT、垂直书写和完整分片仍需继续。实际 Metal 画面检查不等于物理键盘、VoiceOver、所有查看器模式或跨平台实机验收。


### 行尾保留空白与固有尺寸

普通 CSS 文本盒与共享行内段落现在分别保留物理文字范围和参与行宽计算的 advance。`white-space:pre-wrap` 的软换行悬挂整个行尾空格或制表符序列；在强制换行和段落结束处，只悬挂放不下的部分，能放下的空白仍参与对齐。它的 min-content 排除悬挂空白，max-content 保留段末空白。`pre`、`break-spaces`、`normal` 和 `pre-line` 保持各自规则；NBSP 与 EM SPACE 不会被当作 ASCII 行尾空格统一裁掉。

预留空白跨多个原始行内来源时，`pre-wrap` 在整段空白之后换行，`break-spaces` 仍允许逐个空格换行。物理片段、原始文本、Run 实例及选择偏移保持完整；Flex、Grid、表格及 fit-content 的固有尺寸消费同一套行宽贡献。macOS 的简单 RTL CSS 文本行复用现有原生 paragraph，同时提供绘制、选区和点击定位，并将原生负坐标规范到完整物理行内盒。

所选 CSS Text 3/4 与 WPT 来源保存在 `artifacts/css-hanging-whitespace-20261017`，另有本机 Edge/WebKit 字体测量对照、编译 CSS 消费者、同程序集修复前后对照和 Metal 截图。这里的几何比较区分字体自身的度量与 CSS 规则；没有运行完整 WPT harness。复杂富文本、混合来源的段落级双向重排、其他平台的 RTL、原生字体 strut/行高与浏览器的完整一致性、垂直书写、分片和实机辅助功能验收仍需继续，不能由这些选例推定完整 CSS 一致性。

## 普通元素的多行摘要截断

普通 CSS 块容器现在接入 `line-clamp`、`max-lines`、`block-ellipsis` 和 `continue:collapse`。同一块格式化上下文中的真实行盒共用行数，包括嵌套普通块和纯行内控件组成的行；独立格式化上下文内的文字不消耗外层行数。自动高度排除被抑制的内容，保留的浮动元素在容器内容末端裁剪。原始 TextBlock、Run、文本和选择偏移保留，省略号是匿名排版内容；点击省略号定位到仍显示的原文边界。

```css
.summary { line-clamp: 3; }
.summary.more { line-clamp: 3 "… 阅读更多"; }
.summary.by-height { line-clamp: auto; max-height: 96px; }
.summary.both { line-clamp: 3 auto; max-height: 96px; }
.summary.expanded { line-clamp: none; }

/* 兼容旧语法需要同时具备这三项声明。 */
.legacy-summary {
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 3;
}
```

语法跟随 [CSS Overflow 4 当前草案](https://drafts.csswg.org/css-overflow-4/#line-clamp)：`max-lines` 接受正整数、`auto` 或二者，整数可由计算式产生；`block-ellipsis` 接受 `no-ellipsis`、`ellipsis` 或字符串。空字符串等同于没有省略文本。只有 `block-ellipsis` 继承，`max-lines` 和 `continue` 不继承。`line-clamp:none` 同时重置三个长属性；CSS 全局关键字和普通元素的 `style()` 查询沿用这些长属性的计算值。

省略文本只在实际存在截断点时出现，恰好排满且没有后续内容的段落不会自动加省略号。为给省略文本腾出空间，使用正常软换行机会回退，忽略 `overflow-wrap` 额外产生的紧急断点。先确定截断前缀，再对该前缀均衡换行；较大字号被重新分配而增加高度时，重新评估自动截断点。收起内容继续保留键盘焦点资格与可访问的原始名称，符合 `continue:collapse` 的视觉抑制语义。

原生 `FlowDocument` 根、`Paragraph`、`Section`、`List`、`ListItem` 和 `TableCell` 也已接入同一截断语义。嵌套普通文档块共用外层行数；表格、独立格式化上下文和内层截断容器按整体布局，不消耗外层行数。列容器自身忽略 `line-clamp`，但其原始段落可先截断、再投影到列中，原生 `ColumnWidth` 与 CSS 列声明遵循相同规则。

文档布局保留原始节点、`Run`、`Inlines` 集合和文本指针；隐藏段落仍属于逻辑全文和辅助技术文本范围，其可见矩形为空。各查看器分别保存截断端点，原文编辑、列表项替换、宽度改变和取消截断会重新排版，分页只使用仍显示的行和保留的盒空间。`auto` 按文档盒的高度、最小/最大高度、边框和内边距选择截断点；显式固定高度子级的可见溢出仍遵循其原有盒规则。均衡换行针对已保留的前缀，后续几何查询不会重新排出隐藏文字。

这仍不是完整 CSS 或完整 WPT 验收。`continue:discard` 虽可解析，其移除输入/辅助技术内容、多列溢出和区域分片语义尚未完成；原生文档的完整 CSS `display` 盒生成、浮动和定位布局也未由这项截断适配补齐。旧 WebKit 语法的完整父子使用值规则、复杂形状浮动与滚动溢出、定位元素的包含块链、混合双向文本、垂直书写、完整动画及其他平台实机验收仍需继续。普通元素证据保存在 `artifacts/css-line-clamp-20261018`，本次原生文档的测试、编译 CSS 消费者和画面对照保存在 `artifacts/css-document-line-clamp-20261018`；物理键盘、VoiceOver 与完整 AppKit 宿主验收须分别完成。

## 原生文档的 display:none

`display:none` 现已作用于原生 `FlowDocument` 的根、段落、分节、列表／列表项、表格／行组／行／单元格，以及原始行内 Run、Span 和 LineBreak。整棵隐藏子树不再生成布局和绘制内容，也不计入固有宽度、列和页的行带或外层截断行数；后代声明 `display:block` 或 `visibility:visible` 不会穿透隐藏祖先。表格移除隐藏行列的布局参与后，重新计算放置、跨度和折叠边框；隐藏列的作者宽度和背景不再作用于其对应的匿名列，原始单元格仍保留。

原始文档节点、集合、文本和 TextPointer 保持完整。查看器的辅助技术文本、范围查询和点击定位使用显示文字与原文之间的偏移映射，隐藏内容不再出现在该文本范围或文字名称中；原始文档／TextRange API 仍反映原文。取消隐藏、改变类名或编辑隐藏文字后，原来的对象重新参与排版。`display:none` 与保留逻辑辅助技术全文的 `continue:collapse` 分别处理。

```css
Section.details { padding: 12px; background: #eef5f0; }
Section.details.is-hidden { display: none; }
Run.private-note { display: none; }
```

这项实现没有把原生文档的完整 CSS 盒生成视为完成：表格 `display:contents` 的后续进展见下文；任意 display 的原生盒转换、完整生成内容、浮动、定位、混合双向和垂直排版仍需继续。当前证据位于 `artifacts/css-document-display-20261018`，完整 WPT、物理键盘／IME、VoiceOver 和其他平台验收须分别完成。盒生成语义参照 [CSS Display 3 §2.5](https://www.w3.org/TR/css-display-3/#box-generation)。

## 普通内容宿主的 display:contents

普通 CSS 内容宿主现在接受 `display:contents`：自身不生成盒，子项、原始文字和前后伪元素在最近生成盒的祖先中参加 flow、flex、grid 或表格排版。匿名表格修复、子项浮动与定位、子网格借用和分栏沿用现有算法。容器自己的尺寸、边距、内边距、边框、背景、变换、透明度和裁剪不会套到提升后的子项上；后代自己的盒属性仍生效。

```css
.reading-list { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
.reading-list > .group { display: contents; color: #233c32; }
.group > .entry { padding: 16px; background: #eef5f0; }
```

原始集合、视觉父节点、选择器、继承和事件路径保留。键盘导航跳过不生成盒的容器，继续按原始顺序访问可聚焦后代。动态插入、删除、编辑和切回普通显示值会重新排版，提升后的绘制也会清理原始容器的脏标记，让下一次子项更新继续传播。根元素的计算显示值为 block；替换图像等不能由 CSS 内容布局控制的元素计算为 none。`contents` 不能与 inline、flow 或 list-item 等关键词组合。

这项接入针对普通内容宿主，原生 `FlowDocument` 的段落／分节／列表 `contents` 连续排版见下一节；原生表格自身的匿名盒修复、任意 display 转换和 Run／Span 的全部盒生成语义仍需继续实现。原生控件模板的全部转换、完整 WPT、复杂双向与垂直排版、各平台物理输入及辅助技术也不能由本轮通用宿主测试代替。证据和复现入口见 [本轮记录](../artifacts/css-contents-20261018/README.md)。规范依据为 [CSS Display 3 的盒生成与根元素规则](https://www.w3.org/TR/css-display-3/#box-generation)。


## 原生文档的 display:contents 连续排版

原生 `FlowDocument` 的 `Paragraph`、`Section`、`List` 和 `ListItem` 现在可以使用 `display:contents`。这些节点保留原始集合、样式继承、选择器与文档指针，自身不再生成尺寸、间距、背景、边框、独立分栏或分页边界。相邻的盒外段落文字复用共享行排版器，在最近生成盒的祖先中连续折行；正常块会结束这一组匿名行，`display:none` 子树不会制造新的段落。

```css
FlowDocument.article { font-size: 18px; line-height: 28px; }
FlowDocument.article Section.joined,
FlowDocument.article Section.joined Paragraph { display: contents; }
```

匿名行的固有宽度跨越原始段落接缝计算，包含跨节点的同一单词和内部空白，也用于原生表格单元格的宽度分配。页、列和外层 `line-clamp` 计数使用实际的共享行；无盒段落自己的分页与分栏声明被忽略，匿名段落的孤行／寡行约束来自最近生成盒的父节点。省略号及其命中位置随列投影保留。`FlowDocument` 根节点仍生成根盒，遵循根 `contents` 的 block 规则。

原生 `GetText()`、`TextRange` 和 `TextPointer` 继续表示原始文档。文档文本提供程序及复制内容省略无盒段落的隐式分隔符，并将可见范围、跨段选择和文字命中映射回原始偏移。编辑原始 Run、多个视图同时显示、修改宽度及切回普通显示值会重新排版，原始代理和内容节点保持不变。

Run／Span 的全部盒生成语义、任意 display 的原生盒转换、完整生成内容、浮动定位、复杂双向与垂直书写仍需继续。完整 CSS 目标继续进行；完整 WPT、各平台实机、物理键盘／IME、VoiceOver 和原生 AX 通知须分别验收。复现与证据见 [连续排版记录](../artifacts/css-document-contents-20261018/README.md)。

## 原生文档表格的 display:contents 匿名盒修复

`Table`、`TableRowGroup`、`TableRow`、`TableCell` 和 `TableColumn` 支持 `display:contents`。排版先提升这些节点的子内容，再按 [CSS 2.2 匿名表格盒规则](https://www.w3.org/TR/CSS22/tables.html#anonymous-boxes) 补齐缺失的表格、行与单元格：相邻无盒行的单元格可合成一行，相邻无盒单元格的内容可合成一个匿名单元格。全部表格节点都无盒时，原始块进入文档普通流；段落也无盒时，原始文字可以连续排版。

无盒节点的尺寸、间距、边框、背景、列宽、跨度、独立分栏与分页边界不转移给匿名盒。匿名盒从实际盒父级继承样式；原始后代仍沿原始文档树继承并匹配选择器。行组移除后，跨行范围、表头／表尾提升、边框折叠与可见性折叠使用修复后的表格结构。仅可折叠空白参与匿名盒修复时会被丢弃，保留空白仍可形成内容。

```css
Table.reading, Table.reading TableRowGroup { display: contents; }
Table.reading TableCell.joined { display: contents; }
```

原始行、单元格、列定义、段落与 Run 集合不重建或改父级。文本提供程序省略无盒行、单元格和段落的隐式分隔符，可见范围、跨接缝选择、命中与分页继续映射原始文档偏移。多视图、原始文字编辑和切回普通显示值会重算盒投影；百分比单元格第二轮排版会释放第一轮共享文字分配。

本节限于原生表格节点的 `contents` 语义；任意 display 转换、完整生成内容与浏览器互操作仍未完成。可复现验证入口为 HostSmoke 的 `--css-document-table-contents`，证据见 [表格修复记录](../artifacts/css-document-table-contents-20261018/README.md)。

## 原生文档块使用共享 CSS 格式化器

`FlowDocument` 根、`Section`、`List`、`ListItem`、`Paragraph`、`Table`、`TableRowGroup`、`TableRow` 和 `TableCell` 的显示角色转换已接入普通元素使用的 flow、flex、grid、行内及匿名表格格式化器。例如，把原始段落排成响应网格，或让段落在共享行中连续阅读：

```css
Section.reading {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 12px;
}
Section.reading Paragraph { display: block; min-width: 0; }
Section.reading.continuous { display: block; }
Section.reading.continuous Paragraph { display: inline; }
```

各查看器构建私有盒树，复用原有段落代理与行内内容。原始文档父级、集合、Run、样式继承、结构选择器和文本指针保持原样；插入节点后，结构选择器按实际集合顺序重新匹配。不同宽度的共享视图分别保存布局约束。原始 CSS 层级与原生局部属性的优先级保留，flex 容器声明也可存储在非视觉文档节点上。

文本范围、命中、可见行带、裁剪和分页映射回原始偏移；空的固定高度盒仍可跨页。表格中的原始单元格保留可查询的行列与跨度。转换后的盒复用普通元素的分栏与截断，截断标记由所属格式化器绘制一次。CSS 正文使用排版行高，不再加上独立 TextBlock 控件的光学余量；深层 display 改动会使格式化祖先重新测量。

本节是所列角色与组合的接入，不能推定完整 CSS 或浏览器一致性。Run／Span 的进一步组合（见下节）、原生文档其余生成伪元素（列表标记见前节）、未发生显示转换时的完整浮动与定位、原生列宽回退的全部组合、复杂双向与垂直书写、完整分片、动画组合、完整 WPT 和各平台实机验收仍需继续。测试比较的是同一套 CSS 格式化器与独立原始行内来源；普通字符串与富文本的全部原生双向绘制差异不在本节的像素一致性结论中。复现入口为 `--css-document-box-formatters`，记录见 [共享格式化器验证](../artifacts/css-document-box-formatters-20261018/README.md)。盒树与根元素使用值规则参照 [CSS Display 3](https://www.w3.org/TR/css-display-3/#box-generation)。


## Run 与 Span 的私有 CSS 盒

普通 `TextBlock.Inlines` 和只读 `FlowDocument` 查看器中的 `Run`／`Span` 现在可参与共享的 CSS 盒排版。已覆盖 `inline`、`block`、`inline-block`、`flow-root`、`flex`、`inline-flex`、`grid`、`inline-grid`、`table`、`inline-table`、`table-row`、`table-cell`、`contents` 和 `none`，包括嵌套 Span 中的块级拆行。原始 Run、Span、父级、集合、文本和指针偏移保留；每个视图自行创建视觉盒，文字叶子只持有原文片段。

```css
Span.emphasis {
    display: inline;
    padding: 2px 4px;
    background: #eef5f0;
    border-bottom: 1px solid #aac6b6;
}
Span.emphasis.expanded {
    display: block;
    margin: 10px 0;
    padding: 12px;
    border: 1px solid #aac6b6;
    border-radius: 6px;
}
```

尺寸、盒模型、边框与圆角、继承文字样式、相对位移及 `box-decoration-break` 沿用已有格式化器。父级改为 Flex/Grid 后，子项重新计算块化使用值；无盒节点在嵌套共享行中保持正确坐标。只改变图片边框或背景而不需要新盒的内容继续复用原有文字绘制路径。

原生与 CSS 行内文字装饰仍从原始节点取值，文字叶子按原始范围绘制。文本范围、选区绘制和命中映射回原文；样式移除、等长文字修改、插入节点后的结构选择器及多视图分别分配布局均有回归覆盖。分栏保留行内装饰的片段范围，尺寸探测不再把暂时未放置的原子盒发布为截行隐藏状态。本节的文字修改指文档模型更新；编辑面的接入与边界见下节。

复现入口为 `--css-inline-boxes`，验证记录见 [行内盒验证](../artifacts/css-inline-boxes-20261018/README.md)。该增量仍不代表完整 CSS：InlineUIContainer、全部浮动与绝对定位组合、复杂双向／垂直书写、全部截行标记与分片组合、跨来源文字装饰 inset 与原子盒传播等组合、完整 WPT 及跨平台实机验收仍需继续。盒与原始节点的区分参照 [CSS Display 3](https://www.w3.org/TR/css-display-3/#box-generation)。

## RichTextBox 的 CSS 文档排版

`RichTextBox` 中需要 CSS 布局的文档复用查看器的文档排版器。文档、段落、Run／Span 及其父级仍是原始来源；私有视觉树不进入作者的结构选择器或辅助功能子节点。`inline`、`block`、原子行内盒、Flex、Grid、表格、`contents` 和 `none` 以及盒模型沿用共享格式化器，编辑器仍使用原有文字修改、段落拆分、格式快照和撤销／重做实现。

仅使用已有原生文字、背景、文字装饰及图片边框的文档继续沿用成熟的原生段落绘制，保留双向片段、图片连续相位、加载与动画缓存。其他需要排版的属性转入共享格式化器；移除最后一项布局样式后会释放私有视图并返回原生路径。

```csharp
Css.SetStyleSheet(editor, """
    FlowDocument { font-family: "PingFang SC"; line-height: 28px; }
    Paragraph { display: block; margin: 0 0 12px; }
    Span.emphasis { display: inline; background: #eef5f0; }
    Span.emphasis.expanded {
        display: block;
        margin: 10px 0;
        padding: 10px;
        border: 1px solid #aac6b6;
    }
    """);
Css.SetClass(emphasis, "emphasis expanded");
```

绘制、文字命中、插入点、可视行导航、选择范围与滚动使用同一份排版几何；软换行末尾保留后向插入方向。辅助功能文本排除 `display:none` 子树，并把选区、命中和编辑请求映射回原始偏移。左右箭头及单字／删词操作跨过隐藏来源；删词覆盖多个可见片段时合并为一个撤销单元，隐藏文字保留在原始模型中。公开文档模型与 IME 周边文字仍使用原始 UTF-16 偏移。

编辑内容视口为根文档百分比高度提供基准，再扣除文档页面留白；子盒的排列使用同一个包含块，避免再次按自身已使用高度计算百分比。实际使用值为 `inline` 或 `contents` 的文档内容节点由私有匿名视口建立格式化上下文，提升的块后代仍贡献正文高度；`contents` 不绘制自身间距、边框或背景，`none` 不绘制文档画布背景。组合输入的插入框沿用 CSS 行高，原生水平命中仍由现有文字系统提供。

这不代表完整编辑或 CSS 验收。嵌入 UI、复杂混合双向与竖排、全部分片与定位组合、大文档性能、完整 WPT／浏览器一致性仍需继续；物理键盘、IME 候选窗口、VoiceOver、原生 AX 通知及跨平台实机须分别验收。原生宿主入口为 `--css-richtext-boxes`，测试与画面记录见 [编辑排版验证](../artifacts/css-richtext-boxes-20261018/README.md)。

### 文档画笔的动态更新与追加

原始文档节点上已被绘制消费的可变 `Brush` 现在与视觉控件使用同一套弱所有者机制。原地修改颜色、透明度、变换或渐变停止点时，沿原始文档树通知实际消费它的视图；共享文档的多个视图分别刷新。只更新画笔不改动原文、原始父级、选择范围或行尺寸。替换画笔时按属性释放旧关联，释放视图后停止通知它；普通数据属性中的画笔不会成为绘制依赖。

自行观察文档变化的编辑器和分页宿主也注册绘制消费关系，因此文档根背景不会因没有普通内容订阅而保留旧帧。空文档和只含空 Section 的文档也保留根节点订阅，替换文档后释放旧关联。普通 CSS 颜色／背景声明、变量更新和 Run／Span／块的样式替换仍沿用原有级联与排版路径。

`RichTextBox.AppendText()` 从原始文档逻辑末尾追加，复用富文本编辑事务，保留文档实例、既有节点及 CSS；换行沿用现有段落编辑规则。它不覆盖当前选择，程序化追加仍可用于只读控件；空字符串和 null 不改动文档或撤销队列。追加、撤销、重做使用同一份格式快照，隐藏尾部仍留在原始容器中。公开入口继续声明在 TextBoxBase 上，普通 TextBox 的行为保留。容器末尾插入的做法参照 [WPF TextBoxBase.AppendText 的实现](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/Primitives/TextBoxBase.cs)。

原生检查入口为 `--css-document-paint-refresh`，输出目录使用 `JALIUM_CSS_WRITING_TOOLS_OUTPUT`；固定来源、独立新建场景的像素对照、追加及撤销／重做记录见 [文档绘制刷新验证](../artifacts/css-document-paint-refresh-20261018/README.md)。这覆盖上述绘制消费关系和追加路径，不能推定所有图像、效果、动画、复杂结构编辑或完整 CSS 一致性；物理输入、Apple Intelligence、原生 AX 与 VoiceOver 仍需分别验收。

## 跨块行内内容的截行

外层截行会继续统计同一块格式化上下文中的后代行。行内 Section 或普通行内内容宿主包住块后代时，块前、块后的行内片段仍按实际顺序参与预算；保留的块不会因自身没有行内片段而被隐藏。早期保留片段继续显示，只有真正截断的末行获得省略号及源文字端点，后续被丢弃的块保持不绘制。

```css
FlowDocument.note {
    width: 90px;
    line-height: 24px;
    line-clamp: 2 no-ellipsis;
}
Section.note-emphasis {
    display: inline;
    box-decoration-break: clone;
    padding: 2px;
    border: 1px solid #146342;
}
```

`balance`、`pretty` 与 `avoid-short-last-line` 的重排沿用相同预算。绘制、命中和范围几何使用保留片段；原始文档、节点、文字与指针不因截行而被删除，移除截行后可恢复。原生文档编辑的可见前缀修改及撤销／重做有回归覆盖。

此前 `CSS-RICH-001` 的行内 Section 截行正文消失问题在本轮修复；来源与验证见 [跨块截行记录](../artifacts/css-inline-clamp-20261018/README.md)。这仍不是全部分片组合、WPT／浏览器或跨平台实机验收。

## CSS 文档的 Writing Tools 字形呈现

macOS Writing Tools 的公开文本范围先映射回原始文档，再映射到段落、行内来源和实际绘制的 UTF-16 字形范围。`display:none`、空白折叠、大小写扩展、全角变换、字间距、换行和分片沿用现有来源映射；不修改原始节点或文字。呈现层复用 Metal／Software 的字形范围接口，需要与托管程序集匹配的原生库。

隐藏动画只隐藏目标字形及其装饰线、文字阴影；正文背景、边框和编辑选区继续绘制。透明预览只包含所选文字及其文字装饰，保留斜体越界和装饰线端点。省略号等没有原始文字范围的生成标记不会进入所选文字预览。不可编辑文字的灰色截图在进入文字叶节点的绘制保护之前生成，避免重入时漏掉自身。

编辑器私有文字投影在绘制时重新读取呈现状态，保留排版和文字测量缓存；阅读器和独立 `TextBlock` 的既有绘制缓存策略不变。多范围的准备、结束、清理以及文字、显示状态、视口变化使用同一份原始模型和公开范围映射。

原生检查入口为 `--css-writing-tools`，通过 `JALIUM_CSS_WRITING_TOOLS_OUTPUT` 指定输出目录。独立场景像素对照、固定源码和实际运行边界见 [Writing Tools 验证记录](../artifacts/css-writing-tools-20261018/README.md)。真实 Apple Intelligence 会话、物理输入及 VoiceOver 尚需单独验收；动态画笔与文档追加的当前范围见前节，大文档性能及全部 CSS 组合仍有后续工作。此节描述呈现接入，不代表完整 CSS 或完整 Writing Tools 编辑协议验收。

## Writing Tools 的 CSS 可见偏移编辑

服务请求按辅助功能文本的可见 UTF-16 范围校验，再映射到原始节点。隐藏前缀、段内隐藏片段及其间断范围不会被算入替换；隐藏接缝处的插入使用可见来源。列表和表格内部的文字编辑仍保留原有容器，段落、列表与表格的结构记录按可见范围裁剪，同时保留原始表格坐标、跨行／跨列和列表编号。

完整可见兄弟块在原集合中替换，保留其间及两端的隐藏块；富文本结果沿用既有样式导入，整个操作合为一个撤销单元。请求中途失效或编辑结果破坏可见前后缀、删除／暴露隐藏内容时恢复原图；恢复前检查节点所有权，不从其他宿主收回节点。

跨隐藏段落的部分结构合并尚未实现，当前拒绝此类无法安全合并的请求并保留原文。完全隐藏文档也不接受可见文本插入。原生检查入口为 `--css-writing-tools-editing`，输出目录仍由 `JALIUM_CSS_WRITING_TOOLS_OUTPUT` 指定；来源、撤销及独立场景像素对照见 [可见偏移编辑验证](../artifacts/css-writing-tools-editing-20261018/README.md)。这些是独立于真实 Apple Intelligence 会话与物理输入的自动检查，完整 CSS 支持仍在继续。


### 字偶距与连字变体

`font-kerning` 接受继承的 auto（初始值）、normal、none。normal 启用字体度量中的 kern，none 关闭；auto 由原生排版器决定，目前横排 CoreText 使用其默认字偶距。没有字偶距数据的字体不会出现人为间距。

`font-variant-ligatures` 接受 normal、none，或以下四组各最多一个关键字，组间次序任意：

- common-ligatures / no-common-ligatures：liga 与 clig。
- discretionary-ligatures / no-discretionary-ligatures：dlig。
- historical-ligatures / no-historical-ligatures：hlig。
- contextual / no-contextual：calt。

normal 不额外覆盖字体描述符；默认引擎启用普通／上下文连字与上下文替代，历史与酌情连字需要启用。none 关闭上述五项，保留必需的 rlig。属性按完整计算值继承，支持变量、全局关键字、层叠、重要声明和样式撤销；font 简写重置两项属性。font-variant 总简写现已接入七项变体长属性，见 emoji 呈现与总简写一节。

非零 letter-spacing 在特性合并时抑制 liga/clig/dlig/hlig；显式 font-feature-settings 最后合并，能再次启用或关闭同名特性。macOS 的普通水平文字现保留整行 CoreText 塑形，再移动完整字形簇；测量、绘制、光标、选区和命中共用不可变的段落对象。AV 等字偶距保留；显式强制的连字不拆成独立字形，额外内部间距留在连字的字符单元内。普通可选连字在非零字距下关闭，组合附加符和 ZWJ emoji 不增加内部字距。行首尾不增加字距，相邻内联值在视觉边界取半值之和，零宽格式字符不额外增加间隔。已覆盖的连笔文字保留连接及自然宽度，空格和非连笔文字仍可增加字距。声明移除或改回 0 恢复对应字体塑形，保留源文本及节点。

这条路径覆盖普通 TextBlock、不同样式的 Run、文档阅读及 RichTextBox 投影和文字标记，以及普通换行后的单行片段。它要求实色文字和正常字体拉伸；共享 CSS 行内格式化的未覆盖组合（见下节）、改变字符数的文字变换、保留制表符、原子行内盒、裁剪省略及部分两端对齐组合继续走已有排版路径，尚不能宣称这些组合保留了完整跨字符塑形。连笔文字识别仍使用现有脚本范围，尚未覆盖完整 Unicode 脚本分类与复杂语言定制。letter-spacing 百分比及动画范围见下节。竖排 vkrn、完整上下文 GSUB/GPOS 和各非 macOS 原生后端的实际运行须继续验证。

检查入口 `--css-shaped-spacing`，源代码、独立 CoreText 参考、原生渲染及回归证据见 [保留塑形的字距验证](../artifacts/css-shaped-letter-spacing-20261018/README.md)。这些证据不代替实体设备键盘、VoiceOver 或其他平台的验收。

### 字距百分比与计算值

`letter-spacing` 现接受 `normal`、长度、百分比及合法的长度百分比计算。百分比原样继承，在使用时按当前文字的字号解析；`em` 等字体单位保留声明元素的已计算字体上下文。`calc(.1em + 5%)` 因而可以同时含有来自父级的绝对长度和依赖当前 Run 字号的百分比。相邻字体大小不同的文字仍各贡献半个字距，行边缘不增加字距。既有编译 CSS 的绝对数值继续可用。

普通 transition 和 `@keyframes` 可在字距长度与百分比之间按计算值插值；改变字号会重新解析仍保留的百分比。混合计算及非线性数学端点沿用各自的计算上下文，反复中断线性过渡会归并长度与百分比项。字距与词距的 `style()` 查询比较计算值：当前恰好都为 2px 的 `10%` 和 `2px` 仍不同，`normal` 按零长度比较。

容器上的继承字体变化会使后代文字的测量与绘制缓存失效，避免字号属性已更新但共享行框仍保留旧字形。macOS 对保留字形的位移使用与普通行绘制一致的坐标空间，减少分数位移下彩色 emoji 的采样差异。制表符、原子盒及省略路径已接入使用字距的解析；这些路径的完整上下文塑形仍受前述边界限制。

检查入口 `--css-percentage-spacing`。计算、继承、动画、查询、直接／编译 CSS 和原生画面证据见 [字距百分比验证](../artifacts/css-letter-spacing-percentage-20261019/README.md)，语义依据 [CSS Text 4 字距](https://www.w3.org/TR/css-text-4/#letter-spacing-property)。这些检查不代替实体输入、VoiceOver、完整 AppKit 应用包和其他平台验收。

### 跨来源的行内塑形

macOS 的共享 CSS 行框现可先对同一行的相邻文字完成整段塑形，再把绘制、光标、命中和选区分配给原始 TextBlock／Run／Span。没有影响字形的颜色或装饰变化，以及无间隔的嵌套行内框，不再自行切开字偶距、连字和组合字符。相邻字距仍取两侧半值之和；换行候选使用同一份塑形宽度。

非零行内 margin、border、padding 和非 baseline 的 vertical-align 会切开塑形；相邻来源的方向或 Writing Tools 宿主不同也保持独立。一个字形簇跨越来源接缝时只由最后一个贡献来源绘制一次，保留完整字形与源字符的几何，避免重复叠墨。其颜色归属属于 [CSS Text 4 的元素边界规则](https://www.w3.org/TR/css-text-4/#characters-and-letters) 允许的处理；普通塑形边界遵循 [跨元素塑形](https://www.w3.org/TR/css-text-4/#boundary-shaping)。

此路径要求实色文字、正常字体拉伸和可直接映射的源字符。改变字符数的变换、保留制表符、原子盒和裁剪省略继续使用既有格式化路径；不能据此宣称这些组合已具有完整跨节点塑形。兼容来源的两端对齐范围见下节。单个连笔词没有内部扩张机会，切换至两端对齐的居中回退时仍保持同一塑形。RTL 容器中含强 LTR 字符的跨来源组合暂保留既有盒级推进；包含物理边界、原子盒的完整段落 bidi 重排仍需统一。

检查入口 `--css-shared-shaping`，原生逐字形参照、自动化结果与验证边界见 [共享行内塑形验证](../artifacts/css-shared-shaping-20261018/README.md)。实体输入、VoiceOver 和其他平台仍需实机验收；完整 CSS 目标继续进行。

### 保留塑形的词距

macOS 的普通文字与共享行内格式化现将 `word-spacing` 作为完整塑形之后的字符单元宽度调整，词距不再阻止相邻来源共享字偶距、连字和连笔形态。普通 Run 的颜色和透明度也在塑形后按来源绘制，不切开连字；显式的零词距同样保留这份塑形。词距只作用于空白处理后保留的词分隔符，两侧各分配一半，使用分隔符所属 Run／来源的计算值；百分比仍按该元素的字体大小解析。负词距将分隔符的宽度减至零为止，零宽分隔符不凭空增加间隔，固定宽度空格不额外获得词距。可与 `letter-spacing` 组合，并继续尊重显式字体特性。

同一个不可变对象提供使用宽度、字形位置、双向光标和源选区；普通换行会去除折叠行尾空格的宽度及其词距，保留空格按现有 white-space 规则计入。撤销与反复修改词距不会改写源文本或节点。原生端增加独立的单元宽度接口，已有段落及字距 ABI 保持兼容；不支持的后端与上述未覆盖组合继续使用原路径。自动间距的接入范围见下节。文档视图和省略标记复制的默认值不视为作者显式设置的零词距，避免重排或恢复时无故切换绘制路径。

检查入口 `--css-word-shaping`，独立 CoreText、Metal／软件渲染及回归证据见 [词距塑形验证](../artifacts/css-inline-advances-20261019/README.md)。规则依据 [CSS Text 4 词距](https://www.w3.org/TR/css-text-4/#word-spacing-property)。这些证据不代替实体键盘、VoiceOver 或其他平台验收。


### 保留塑形的自动间距

macOS 的普通 TextBlock、Run 与共享行内文字现将 `text-autospace` 接入整行 CoreText 塑形后的宽度调整。中西文、数字及法语标点的间距不会切开原有字偶距和显式连字；自动间距、词距和字距共用绘制、源范围、光标和命中几何。间距依视觉相邻字符在 bidi 重排之后计算；文字中的显式双向控制符保留源偏移，边界光标按所属字形运行解析。

跨元素间距由包含该边界的最内层共同元素决定，并使用该元素的字体度量。间距计入行的推进量，但不扩张不包含它的子元素背景和源选区。`insert` 尊重已有空格，`replace` 调整符合条件的 U+0020；原始字符串、复制结果和节点归属保持不变。行边缘不增加自动间距，换行后的已替换空格按所在行重新处理。样式和语言变化会重新生成使用几何。

本路径覆盖实色、正常字体拉伸、可一一映射的字符、同一行内的兼容来源及已适配的文档投影。非零边框／内边距／外边距和非基线垂直对齐继续构成塑形边界；原子盒、制表符、改变字符数的变换和裁剪省略仍需继续统一。兼容文字的两端对齐已接入下述路径；RTL 容器中含强 LTR 字符的跨来源盒推进与竖排尚未由此项工作补齐；`unicode-bidi` 的后续接入范围见文末。

检查入口 `--css-contextual-shaping`；独立 CoreText 字形对照、1×/2× DPI、窄宽视口、文档与 Writing Tools 自动检查见 [自动间距塑形验证](../artifacts/css-contextual-advances-20261019/README.md)。规则依据 [CSS Text 4 自动间距](https://www.w3.org/TR/css-text-4/#text-autospace-property)。这些自动检查不代替实体输入、VoiceOver、完整 AppKit 宿主及其他平台验收，完整 CSS 目标继续进行。

### 保留塑形的两端对齐

macOS 的普通文字与兼容的共享行内来源现先完成整行 CoreText 塑形，再在词分隔符或字间边界分配剩余宽度。字偶距、必需连字与显式启用的可选连字保留；字间对齐可关闭普通可选连字，显式 `font-feature-settings` 仍优先。扩张在已有词距、字距和自动间距之后计算；词分隔符两侧各获得一半，字间机会分配至相邻文字单元的两侧，源选区和光标包含各自的一半。无内部扩张机会的连笔词使用现有末行对齐回退。

绘制、源范围、光标与命中共用同一份扩张后的段落对象；跨节点完整字形簇仍只绘制一次，复制与源节点保持原样。修改对齐、宽度和固有尺寸会重建相应几何，释放塑形缓存时同时使对齐计划失效。行内／块显示方式变化也会更新祖先面板的绘制顺序，避免后绘制的行内背景遮住跨节点字形。普通换行、列投影及已适配的文档文字使用同一计算路径。

当前共享路径要求整行由兼容文字组成。含原子盒、制表符、物理边界、偏移基线、字符数变化或裁剪省略的行仍采用既有对齐；这些组合的完整上下文塑形，以及跨来源 RTL／强 LTR 混排、完整段落 bidi、竖排和其他平台仍待补齐。

检查入口 `--css-justified-shaping`；独立旧段落 API 的字形对照及回归证据见 [两端对齐塑形验证](../artifacts/css-shaped-justification-20261019/README.md)。规则依据 [CSS Text 4 两端对齐](https://www.w3.org/TR/css-text-4/#justification) 与 [字距](https://www.w3.org/TR/css-text-4/#letter-spacing-property)。自动渲染和几何检查不代替实体输入、VoiceOver 或完整 AppKit 宿主验收，完整 CSS 目标继续进行。


上述两项属性以及数字变体、font-feature-settings 支持显式 allow-discrete 过渡：使用整份计算值，在经缓动的进度中点切换；普通 transition 不尝试插值字体枚举或特性映射。过渡的字体状态与测量、重排和绘制使用同一套属性值。

检查入口 `--css-font-ligatures`，阶段证据见 [字偶距与连字验证](../artifacts/css-font-ligatures-20261018/README.md)。依据 [CSS Fonts 字偶距](https://www.w3.org/TR/css-fonts-4/#font-kerning-prop)、[连字](https://www.w3.org/TR/css-fonts-4/#font-variant-ligatures-prop) 与 [CSS Text 字距](https://www.w3.org/TR/css-text-4/#letter-spacing-property)。


## 字体宽度的连续动画与 font 简写行高

`font-width` 与旧名称 `font-stretch` 共用同一个级联属性，计算值为百分比。关键词先换算成对应百分比；`calc()`、`min()`、`max()`、`clamp()` 可用于宽度。负的字面百分比无效；数学表达式计算出的负值按允许范围收敛到 0%。CSS 保留精确百分比，公开 FontStretch 继续提供原有九档原生值。

宽度的 transition 与 @keyframes 现通过同一个百分比通道插值，支持两个名称混用、同档位内变化、跨档位关键词、继承、缺失端点、重要声明、样式查询及撤销恢复。原生本地 FontStretch 值继续优先。测量、字体选择、绘制、源命中及 IME 使用当前宽度；ch 等字体单位和依赖样式查询随动画失效重算。macOS 通过现有原生字体宽度接口选择实际字体或变量轴，不将普通字体机械地按百分比缩放。其他后端仍保留既有宽度能力，尚未取得这些后端的运行验收。

```css
.label { font-width:83.2%; transition:font-width 240ms linear; }
.label:hover { font-stretch:92.8%; }
@keyframes width { from { font-width:condensed; } to { font-stretch:expanded; } }
.caption { font:italic 600 condensed 32px/normal 'SF Pro'; }
.body { font:32px/calc(1em + 6px) 'SF Pro'; }
```

`font` 的可选行高复用 line-height 的类型：normal、无单位倍数、长度、百分比及数学表达式；无单位 1.5 保留倍数含义。前缀 normal 受字体简写四组语法数量限制，重复或互斥的 style、weight、width 和 CSS2 caps 分组仍使声明无效。简写中的宽度仍限于 CSS 3 关键词，百分比宽度应单独设置。small-caps 分支的已有特性与合成边界见大小写字形节；系统字体简写见下节，font-variant 总简写的七项计算值、重置与验证边界见 emoji 呈现与总简写一节。

宿主入口 `--css-font-width-animation`，回读位置使用 `JALIUM_CSS_WRITING_TOOLS_OUTPUT`。依据 [字体宽度](https://www.w3.org/TR/css-fonts-4/#font-width-prop) 与 [字体简写](https://www.w3.org/TR/css-fonts-4/#font-prop)，构建、解析／CLI 使用端、原生截图和验收范围见 [连续字体宽度验证](../artifacts/css-font-width-animation-20261019/README.md)。离屏渲染与程序化几何结果不替代物理输入、VoiceOver 或完整 WPT 一致性。

## 系统字体简写

`font` 接受单独的 caption、icon、menu、message-box、small-caption、status-bar，关键词不区分大小写并支持标识符转义和注释。整条声明同时选择字体族、字号、字重和样式，宽度与行高恢复 normal，其他已支持的 font 子属性也恢复初始值。color、文字装饰、字距和词距独立级联；后续长属性可覆盖系统字体对应分量，原生本地值继续优先。支持变量、全局关键字、重要声明、层、编译 CSS、Run／Span、文档、列表标记和关键帧。

```css
.menu-label { font:menu; }
.window-title { font:caption; color:currentcolor; }
.compact { font:small-caption; font-weight:600; }
.literal-name { font:18px menu; } /* 显式字号后的 menu 为普通族名 */
```

系统关键词只有单独位于简写初始位置时才选择平台角色；不能加 italic、字号、斜杠行高或回退列表。普通 font-family 属性仍须将这些保留族名加引号，例如 `font-family:'menu'`。解析与 CLI 编译只保留角色名，实际应用时再读取当前主机；一次样式计算使用同一代完整字体快照，字体偏好刷新会使相关 CSS 节点和字体单位重新求值。

Windows 复用 NONCLIENTMETRICS／图标字体及系统 DPI 换算，Linux 复用现有桌面字体设置；macOS 的显式 CSS 请求使用 CoreText UI 字体角色，标题、工具窗口标题、菜单、系统细节、消息分别对应平台角色。CoreText 没有独立图标标签角色，icon 使用平台系统字体作为替代。此分支不会改写原生控件已有的默认字号或公开字体资源身份。未由平台提供的分量按 CSS 初始值处理。

依据 [CSS Fonts 系统字体与 font 简写](https://www.w3.org/TR/css-fonts-4/#font-prop)。宿主入口 `--css-system-fonts`，回读位置使用 `JALIUM_CSS_WRITING_TOOLS_OUTPUT`；QA 可通过 `JALIUM_CSS_SYSTEM_FONT_ORACLE` 提供独立 CoreText 程序生成的角色参数。构建、原生回读、编译使用端和实际检查范围见 [系统字体验证](../artifacts/css-system-fonts-20261019/README.md)。本轮主机证据不代表其他平台、物理系统偏好切换、键盘／指针输入、VoiceOver 或完整 CSS／WPT 一致性验收。

## 可变字体的轴坐标

`font-variation-settings` 接受 `normal` 或逗号分隔的四字符轴标签和数值，例如 `"wght" 650, "wdth" 87.5, "slnt" -12`。标签区分大小写，须为 U+0020–U+007E 的四个字符；数值可为负数、小数和 number 类型的数学表达式。重复轴采用最后一个值，计算值去重并按标签排序。非法声明整体丢弃；计算值保留作者的数值，各实际字体实例分别将它限制到支持范围，忽略自身没有的轴。

此属性继承整组设置，支持变量、全局关键字、重要声明、层叠和 `style()` 查询；`font` 简写重置它，`font-variant` 简写保留它。相同轴集合的过渡和关键帧按坐标连续插值，次序不影响对应关系；不同集合使用离散切换，过渡须允许离散属性。缓存标识包括精确坐标，字体相关单位 `ch`、`ex`、`cap`、`ic` 等与绘制使用相同实例，继承、样式更换和动画会重新计算。

`@font-face` 同名描述符设置该字体的默认轴；属性设置按轴覆盖描述符，也覆盖字体匹配属性隐含的坐标。`normal` 不会抹掉描述符。轴设置不影响选择哪一种字体，也不为缺失轴合成字形。编译 CSS 保留描述符，支持移走源 CSS 后运行。

macOS 的 Metal 和 Software 后端已接入原生变量字体实例，覆盖普通控件、Run／Span、共享行内排版、文档与字体回退。显式 `opsz` 同时设置 CoreText 的光学字号控制，取消后恢复原值。设置字符范围和回退字体时保留实际字体数据及矩阵，防止私有可变字体被重新匹配成系统字体。macOS 的 `font-tech(variations)` 与 `src: ... tech(variations)` 使用这一能力；其他平台或旧原生载荷缺少可选接口时保留普通字形并报告有损转换。

高层自动光学字号见下一节。此节不代表完整 CSS、所有字体组合或跨平台验收已完成；实体输入、可见窗口焦点及 VoiceOver 也没有由离屏像素比较代替。原生使用可选扩展接口，已有后端虚函数表保持兼容。固定 WPT 字体、来源许可、重现命令与验收范围见[可变字体验证](../artifacts/css-font-variations-20261020/README.md)。

依据：[CSS Fonts 4 轴坐标](https://www.w3.org/TR/css-fonts-4/#font-variation-settings-def)及[特性与轴设置优先级](https://www.w3.org/TR/css-fonts-4/#feature-precedence)。

## 字体光学字号

`font-optical-sizing` 接受 `auto` 和 `none`，初始值为 `auto`，可继承。`auto` 根据使用的字号选择字体自带的 `opsz` 轴；`none` 取消自动选择，使用该字体的轴默认值。字体没有此轴时不合成光学字形，也不因此更换字体。坐标逐字体限制到实际支持范围。

自动坐标沿用浏览器及 WPT 的 CSS 像素语义：`24px`、`18pt`、`.25in` 解析后均使用 `24`；不会再乘以屏幕 DPI。改变字号会重新选择光学坐标，单纯的绘制缩放或变换保留布局时的选择。`@font-face` 描述符的 `opsz` 低于 `auto`；`none` 保留显式描述符。元素的 `font-variation-settings` 显式 `opsz` 最后覆盖两种策略，其他轴继续独立生效。

该属性支持变量、重要声明、全局关键字、`style()` 查询、编译 CSS 及离散动画；过渡须指定 `allow-discrete`。`font` 简写重置为 `auto`，`font-variant` 简写保留原值。测量、字体相关单位、段落、Run／Span、共享行内排版和文档使用相同字体实例。字体请求编号只标识加载来源；相同已选字体不会因 Run 的请求编号不同而丢失跨 Run 的字偶距。

macOS 的 Metal 和 Software 后端通过独立可选原生接口同时设置轴坐标及 CoreText 光学字号控制。旧载荷可用已有轴接口处理 `auto` 和显式 `opsz`；无法恢复默认光学轴的 `none` 会报告有损转换。`font-size-adjust` 在下节说明。其他平台的光学字号运行实现及完整 CSS／WPT 一致性仍待补齐。离屏图像和程序化选区检查不代表实体键鼠、可见窗口焦点或 VoiceOver 验收。入口 `--css-font-optical`，重现与实际检查范围见[光学字体验证](../artifacts/css-optical-sizing-20261020/README.md)。

依据：[CSS Fonts 4 光学字号](https://www.w3.org/TR/css-fonts-4/#font-optical-sizing-def)、[轴设置优先级](https://www.w3.org/TR/css-fonts-4/#feature-precedence)及 [WPT 光学字号参考](https://github.com/web-platform-tests/wpt/blob/master/css/css-fonts/variations/variable-opsz-ref.html)。


## 回退字体的视觉字号

`font-size-adjust` 支持 `none`、非负数和 `from-font`，数值前可选 `ex-height`、`cap-height`、`ch-width`、`ic-width` 或 `ic-height`，省略度量时使用 `ex-height`。初始值为 `none`，可继承；`font` 简写重置它，`font-variant` 保留它。数值是字体度量与字号的比值，不是百分比或像素长度。计算式产生的负数在计算时限制到零，直接写负数无效。

```css
.heading {
    font-family: "Brand Text", "PingFang SC", sans-serif;
    font-size: 32px;
    font-size-adjust: ex-height .52;
}
.caption { font-size-adjust: from-font; }
.ideograph { font-size-adjust: ic-height 1; }
```

`from-font` 在当前元素上读取首个可用字体的度量并计算为数值；子元素继承该数值，重新声明 `from-font` 才读取子元素自己的字体。字体资源加载、字体列表或轴坐标变化会重新计算。每个实际选中的字体独立应用 `使用字号 = CSS 字号 × 目标比值 / 该字体实际比值`；显式回退、系统回退及大小写／字形呈现转换后的回退字体均参与。无法取得该字体定义的度量时保留字号，不使用其他字体的估算度量；此时 `from-font` 计算为 `none`。

CSS `font-size` 的计算值与 `em/rem` 不变，数值 `line-height` 继续乘以 CSS 计算字号；`ex/cap/ch/ic` 等字体度量使用调整后的实例。`ic-height` 读取字体纵向 advance，包含 `VVAR`、索引映射、`avar` 与 `avar2` 坐标变化。`font-optical-sizing:auto` 使用调整后的字号，显式 `opsz` 仍有最终优先级；合成大小写和上下标保留相对比例。

该属性支持变量、重要声明、全局关键字、`style()` 查询及直接／编译 CSS。相同度量的数值可连续动画，不同度量或 `none` 的变化为离散动画；过渡需要 `allow-discrete`。注册自定义属性使用调整后的字体单位，其与 `font-size-adjust` 构成的依赖循环会回退到初始／继承值。

当前实际运行覆盖 macOS Metal 和 Software；旧原生载荷及其他尚未实现的后端会报告有损转换。平台实例的极端字号限制到 1,048,576 CSS 像素，计算比值仍保留原值。可取得所选度量时，零比值绘制无墨迹，CoreText 内部以极小的正字号表示零；缺失度量时仍保留原字号。`@font-face` 的缩放与行高度量描述符见下节；其他平台运行实现及完整 CSS／WPT 一致性仍待补齐。离屏渲染、选区和光标检查不代表实体键鼠、可见窗口焦点或 VoiceOver 验收。入口 `--css-font-size-adjust`，证据和已发现的分数基线差异见[视觉字号验证](../artifacts/css-font-size-adjust-20261020/README.md)。

依据：[CSS Fonts 5 font-size-adjust](https://www.w3.org/TR/css-fonts-5/#font-size-adjust-prop)、[OpenType VVAR](https://learn.microsoft.com/en-us/typography/opentype/spec/vvar)、[OpenType avar](https://learn.microsoft.com/en-us/typography/opentype/spec/avar)。


### 字体资源的缩放与行高度量覆盖

`@font-face` 支持 `size-adjust`、`ascent-override`、`descent-override` 和 `line-gap-override`。这些是每个字体资源的描述符。直接 CSS 和 CLI 编译 CSS 均保留它们；旧 `FontFace` builder 重载继续可用。

```css
@font-face {
    font-family: "Aligned Fallback";
    src: local("Arial");
    size-adjust: 96%;
    ascent-override: 100%;
    descent-override: 25%;
    line-gap-override: 0%;
}
.caption {
    font-family: "Aligned Fallback", sans-serif;
    font-size: 24px;
    line-height: normal;
}
```

`size-adjust` 初始为 `100%`，接受非负百分比，缩放字形、前进宽度和字体度量，包括来自该字体的 `ex`／`ch` 等单位。它保留计算后的 `font-size`、`em` 和数值 `line-height`。`font-size-adjust` 属性在描述符之后应用：可以取得所选字体度量时，固定比值会抵消描述符的缩放；`from-font` 则读取描述符调整后的首选字体比值。自动光学字号使用最终实际字号，元素显式 `opsz` 仍有最高优先级。

三个行高度量描述符初始为 `normal`，分别保留字体原有上升、下降和行间隙度量；百分比乘以最终实际字号。它们影响 `line-height:normal` 和行内基线，保留字形墨迹、原始文本、计算后的字号及字形字体单位。数值或长度 `line-height` 继续使用指定的行高和有符号半行距。零是有效覆盖值：逻辑行框可以为零，墨迹仍允许溢出。省略或恢复样式会重新计算字体和行框。

度量覆盖可写一至两个值，例如 `ascent-override: 90% normal`；第二值用于直立竖排，省略时为 `normal`。当前解析、计划和编译结果保留两轴值，实际渲染验收限于 macOS 横排；完整竖排布局仍未实现。描述符不能作为元素属性继承，缺失字形所选的系统回退字体也不会继承另一个字体的描述符。无效声明保留同名描述符较早的有效值，描述符不接受变量或 `!important`，计算式产生的负百分比钳制为零。

macOS 的 Metal／Software 使用可选原生接口处理逐字体缩放、行框、段落、选区和墨迹范围；旧原生载荷或尚未实现的其他后端会报告有损转换。实际字号和覆盖度量在平台边界限制到 1,048,576 CSS 像素。实体输入、可见窗口焦点、VoiceOver 和其他操作系统未由离屏检查证明。入口 `--css-font-face-metrics`，复现、回归及既有分数基线限制见[字体资源度量验证](../artifacts/css-font-face-metrics-20261020/README.md)。

依据：[CSS Fonts 5 size-adjust](https://www.w3.org/TR/css-fonts-5/#descdef-font-face-size-adjust)、[行高度量覆盖](https://www.w3.org/TR/css-fonts-5/#font-metrics-override-desc)。


## CSS 图像遮罩

普通 `FrameworkElement` 的 `mask-image`、`mask-mode`、`mask-repeat`、`mask-position`、`mask-origin`、`mask-clip`、`mask-size`、`mask-composite` 以及 `mask` 简写已接入绘制。遮罩作用于元素及其子树合成后的图像，包含模板、背景、边框、文字与后代；不会改变布局尺寸、源节点、命中测试或键盘顺序。

```css
.fade {
    mask: linear-gradient(to right, white, transparent);
}
.cutout {
    mask-image: image(white), radial-gradient(white 50%, transparent 51%);
    mask-size: 100% 100%, 40px 40px;
    mask-position: 0 0, center;
    mask-repeat: no-repeat;
    mask-composite: subtract;
}
.stencil {
    mask: url("stencil.png") center / contain no-repeat alpha;
    mask-origin: padding-box;
    mask-clip: content-box;
}
```

各长属性支持逗号列表，较短列表按图像层数循环使用，第一幅图像位于最上层。最底层忽略自己的合成运算；其余层使用 `add`（source-over）、`subtract`（source-out）、`intersect`（source-in）或 `exclude`（xor）。`none` 在混合列表中仍占据一个透明层；全部图像均为 `none` 时不应用遮罩。

`alpha` 使用图像透明度，`luminance` 使用非预乘 RGB 的亮度乘以透明度。普通图片和渐变的 `match-source` 使用 alpha。图片选择、密度、渐变、`image()`、`image-set()`、`cross-fade()` 和位图采样复用现有 CSS 图像实现。尚未加载、解码失败或来源不获许可的图像层按透明黑色处理。HTTP 图像需要 `ICssImageResourceResolver` / `ImagePixelAccessAllowed` 的像素访问许可；后续帧和解码发布会使现有遮罩缓存失效，已撤销的源不会继续使元素重绘。

定位、显式尺寸、百分比、`contain` / `cover`、`repeat` / `no-repeat` / `space` / `round` 使用背景图像的布局规则。普通布局盒中的 `fill-box` 对应 content box，`stroke-box` 和 `view-box` 对应 border box；`mask-clip:no-clip` 包含后代溢出及既有额外墨迹范围。`mask-position` 与可插值的 `mask-size` 支持可重复列表过渡和关键帧，保留百分比与计算长度；不可插值的关键词过渡需要 `allow-discrete`。列表插值的展开上限为 65,536 项，单个遮罩栅格上限为 16,777,216 像素，超大绘制区域在这一预算内降低栅格分辨率。

整帧录制会发布独立的 A8 像素快照，回放及 Writing Tools 的文字预览转发同一组遮罩操作。固定定位后代保留原来的包含块计算，同时进入最近的遮罩绘制组。原生 Metal 使用专用捕获和设备像素合成，避免旋转或错切边缘重复抗锯齿；Software 的捕获隔离父级透明度与裁剪，再在组完成后统一应用。

这仍不是完整 CSS Masking 模块：文档 Run / Span 遮罩尚未实现并产生有损诊断。`mask` 简写同时把六个 mask-border 长属性重置为初始值；需要组合两种遮罩时，先写 `mask`，再声明 `mask-border`。图像样式查询目前比较图像表达式，URL、颜色及等价表达式的完整规范化、外部图像遮罩的完整动画仍需补齐。普通布局盒以外的 SVG 几何、复杂层叠 / 变换边界和其他渲染后端也需要继续实现与验证。SVG 遮罩引用的支持范围见下节。

当前运行证据来自 macOS Metal（Impeller / Vello）和 Software 的离屏路径；不支持组遮罩的上下文会报告有损转换。离屏截图与托管焦点检查不代表可见窗口、实体输入、VoiceOver 或其他操作系统已经验收。标准依据 [CSS Masking 1](https://drafts.csswg.org/css-masking-1/) 和 [SVG luminance-to-alpha](https://www.w3.org/TR/SVG11/filters.html#feColorMatrixElement)。入口 `--css-masks`，运行证据和复现见[遮罩验证记录](../artifacts/css-mask-20261020/README.md)。完整 CSS 目标仍包含竖排、混合模式、三维变换及其他尚未覆盖的能力。


### SVG 遮罩引用

普通布局盒的直接 `mask-image:url("masks.svg#soft")` 引用可以读取 SVG `<mask id="soft">`。URL 按样式表源地址解析，读取资源时去掉片段；片段 ID 支持 URI 转义，XML 和 gzip SVG 使用同一解析路径。不存在的 ID、错误的元素类型、空遮罩、读取失败或未获像素访问许可的资源形成透明层。外部 XML 禁用 DTD / 实体解析，限制为 8,000,000 个字符、65,536 个节点和 256 层嵌套。

```css
.soft {
    mask-image: url("masks.svg#soft");
    mask-mode: match-source;
}
```

```xml
<svg xmlns="http://www.w3.org/2000/svg">
  <mask id="soft" maskContentUnits="objectBoundingBox" mask-type="alpha">
    <rect width=".5" height="1" fill="white" opacity=".5"/>
  </mask>
</svg>
```

`maskContentUnits` 默认为 `userSpaceOnUse`，`objectBoundingBox` 将内容映射到引用元素的几何盒。`maskUnits` 默认为 `objectBoundingBox`，控制显式 x / y / width / height 的坐标和百分比；只要写出其中一项，其余项按 -10% / -10% / 120% / 120% 补齐。四项均省略时不额外裁剪，允许遮罩覆盖既有溢出。SVG 遮罩源使用自身几何，忽略该层的 CSS mask-position / size / repeat / origin / clip；普通图片层继续使用 CSS 图像布局。

`match-source` 读取 `<mask>` 的 `mask-type`，初始为 `luminance`；显式 `mask-mode:alpha` 或 `luminance` 覆盖它。亮度按非预乘 RGB 的 0.2125 / 0.7154 / 0.0721 系数乘以透明度计算；`color-interpolation:linearRGB` 先转换色彩空间。`mask-type` 接入属性元数据、变量、CSS-wide 值、离散过渡与 `style()` 查询，不被 `mask` 简写重置；普通布局盒上的该属性不会改变外部 SVG 定义。

遮罩内容复用现有 SVG 图形、渐变、变换、裁剪、`use`、嵌入栅格及文字轮廓绘制。遮罩及其祖先的继承属性参与内容绘制，引用元素的填色不进入外部定义；`use` 实例从实例元素继承。遮罩根自身的 display / opacity / filter 不抑制遮罩内容，`<mask>` 不直接绘制。SVG 文档中的单个本地 `mask="url(#id)"` 可应用于图形或组；循环引用形成透明结果。组透明度和组裁剪在子内容完成合成后统一应用，避免重叠内容重复累积。

嵌入图片的像素发布会刷新正在使用的遮罩，撤销的源不再使原元素重绘。SVG 内嵌样式表与变量的当前支持范围见下节；完整 SVG CSS、DOM / SMIL 动画、SVG 内部跨文档遮罩、多层 mask 简写、嵌套视口及滤镜仍待补齐。嵌入栅格沿用现有静态解码路径，不能据此声称嵌入 GIF 已支持动画。`image()`、`image-set()`、`cross-fade()` 里的 URL 仍按图像处理，SVG 图像片段视图不等于 `<mask>` 引用。

入口 `--css-svg-masks`，本机离屏验证与复现见 [SVG 遮罩记录](../artifacts/css-svg-mask-20261020/README.md)。本节不代表完整 CSS、可见窗口或其他平台验收已完成。几何及亮度规则参照 [CSS Masking 1](https://drafts.csswg.org/css-masking-1/)。

### SVG 内嵌 CSS

SVG 图片、引用遮罩及裁剪路径可以使用 SVG / HTML 命名空间中的 `<style>` 和元素 `style` 属性。它们通过隔离的 XML 样式节点复用 CSS 解析、选择器、层叠排序和变量计算，不进入应用的实时界面树，也不继承外部页面 / 应用的样式表。

XML 类型选择器只匹配源元素名称，不使用框架 CLR 基类回退。带尾逗号的无效虚线列表不覆盖更早的有效声明；SVG 虚线的绘制范围与路径长度校准见下节。

```xml
<svg xmlns="http://www.w3.org/2000/svg" width="160" height="80">
  <style>
    :root { --ink: #146342; }
    @layer base, accent;
    @layer base { .tile { fill: var(--ink); } }
    @layer accent { .tile { opacity: .5; } }
    .caption { font: 700 20px Arial; fill: white; }
  </style>
  <rect class="tile" width="100%" height="100%"/>
  <text class="caption" x="16" y="48">Jalium / 样式</text>
</svg>
```

类型、ID、class、属性、命名空间、组合关系、结构及函数选择器使用原 XML 树。呈现属性作为低优先级提示，再按样式表、层、具体度、作用域接近度、顺序、行内声明与 `!important` 选择属性；无效的普通声明不覆盖先前有效值，计算后无效的变量结果按 unset 处理。`all` 保留自定义属性，CSS-wide 和 revert / revert-layer / revert-rule 使用共享层叠逻辑。`@media`、`@supports`、`@scope`、样式元素的 media 属性和共享语法中的嵌套规则参与匹配。

普通自定义属性、回退、循环和 `@property` 注册的类型 / 初始值 / 继承规则参与计算。`<use>` 保持原节点的选择器身份，同时从实例元素继承变量与呈现值，多个实例分别计算。变量替换保留的 token 分隔符在普通属性解析前统一处理，避免 `currentColor` / `none` 回退被当成透明颜色或无效填色。

目前接入的绘制属性包括 fill / stroke / color、相关透明度与填充规则、线宽 / 虚线 / 端点与连接、display / visibility、已有文字字体及 font 简写中的 family / size / style / weight、渐变 stop-color / stop-opacity、color-interpolation、二维 transform、clip-path、本地单层 mask 引用及其 mode / type，以及当前图形适用的 x / y / width / height / cx / cy / r / rx / ry。呈现几何属性按 SVG 元素类型应用，不把 text 的坐标列表或渐变的几何属性当作普通盒长属性。

数值、单位、数学、现代颜色与色彩函数沿用现有 CSS 解析器，`#RGBA` 使用末尾 alpha。字体简写的字号关键字与 SVG 的 16px 初始字号一致，注册长度可依赖元素自己的计算字号。应用 viewBox 后，几何百分比使用用户坐标中的宽高；媒体查询保留文档 CSS 视口。遮罩的内嵌样式按引用视口计算；现有 SVG 图片仍在解析时建立静态绘图，不能据此声称拉伸图片时已实现动态媒体重排。

XML 样式树限制为 65,536 个元素、256 层，另对选择器匹配设置 1,000,000 步共享预算。超限使该 SVG 加载 / 遮罩解析失败；不继续无限匹配。字体、子图像和外部样式资源的加载能力仍以现有图像模式为准，本节没有启用 SVG 内部的外部 link / @import、远程字体、脚本、DOM 或 SMIL 动画。完整 SVG 文字布局、全部绘制属性与值、嵌套视口、滤镜、多层 SVG 遮罩及其他 CSS 模块仍待继续，不能把本节作为完整 CSS / SVG 一致性声明。

原生离屏入口 `--css-svg-styles`，运行与源码证据见 [SVG CSS 记录](../artifacts/css-svg-cascade-20261020/README.md)。规则依据 [SVG 2 样式](https://www.w3.org/TR/SVG2/styling.html) 与 [CSS Cascade 5](https://drafts.csswg.org/css-cascade-5/)。

### SVG 虚线与路径长度

SVG 图形的 `stroke-dasharray`、`stroke-dashoffset`、`stroke-width` 和 `stroke-linecap` 连接现有软件与原生 Pen 绘制。SVG 的初始端帽为 `butt`，它同时用于路径端点与每段虚线；不会沿用通用 Pen 的方形虚线端帽。奇数列表重复为偶数序列，全零列表与 `none` 绘制实线，圆 / 方端帽下的零长度 on 项可绘制点。

长度列表使用共享 CSS 单位和数学解析器，支持变量与注册自定义属性。字体相对长度在声明元素上计算后继承，百分比仍保留到使用时，以当前 SVG 用户视口的归一化对角线为基准。负的普通列表项无效，不覆盖更早有效声明；有效数学表达式的负结果按非负范围归零。大长度与偏移先定位原始周期中的位置，再裁限到足够覆盖整条路径的有限距离，避免将溢出值交给原生 float 接口。

`path-length` 支持 `none` 或非负 CSS 长度，不继承；SVG 数字 `pathLength` 属性映射为该属性的呈现提示，因此样式、变量和 CSS-wide 值能覆盖或清除它。路径长度校准影响虚线长度与偏移，也影响现有 textPath 的非百分比 startOffset。百分比的基准仍为视口 / 被引用路径，不因作者长度改变。零作者长度按无限缩放处理并在有限路径内求可见结果；目前接入仿射长度、百分比及 min / max / clamp 组合，其余混合非线性数学极限仍待补齐。

虚线裁切使用更细的曲线采样，避免端帽因粗折线的切线误差倾斜；软件位图先合并描边覆盖区域再计算抗锯齿，避免线段与端帽之间发白。原生软件路径保留零长度项及 DPI 下的端帽精度，共享解析描边器保留细分曲线的浅角连接。绘制回放缓存保留纯色画刷透明度，反复录制不会将半透明填充合并为不透明颜色。

软件图片的缓存缩放与仿射采样均按预乘颜色进行过滤，再恢复直通 BGRA 用于混合，透明像素下的 RGB 不再污染 SVG 位图边缘。

本节不表示完整 SVG 描边一致性：全部零长度子路径行为、非缩放描边及复杂数值极限仍需继续。原生离屏入口 `--css-svg-strokes`；源码、实际截图、验证记录和浏览器对照差异见 [SVG 虚线记录](../artifacts/css-svg-stroke-20261020/README.md)。规则依据 [SVG 2 描边](https://www.w3.org/TR/SVG2/painting.html#StrokeDashing) 与 [SVG 2 路径长度](https://www.w3.org/TR/SVG2/paths.html#PathLengthAttribute)。

### SVG 文字描边与绘制顺序

SVG 内的 CSS、呈现属性与行内样式支持 `paint-order: normal | [fill || stroke || markers]`，默认继承。省略的操作按 fill、stroke、markers 的默认顺序补在末尾，因此 `stroke` 等价于 `stroke fill markers`。重复关键字、混用 normal 或逗号列表无效；共享层叠、变量、CSS-wide、`@supports` 和 use 实例继承参与计算。绘制器按该顺序组合填充、描边和下述 marker 图形，元素自身的透明度、裁剪与遮罩作用于组合结果。

```xml
<svg xmlns="http://www.w3.org/2000/svg" width="320" height="90">
  <style>
    text { font: 48px sans-serif; fill: white; stroke: #146342;
           stroke-width: 5px; paint-order: stroke; }
  </style>
  <text x="16" y="62">描边文字</text>
</svg>
```

text、tspan 与 textPath 的描边复用原生字体轮廓和共享 Pen 路径，`fill:none` 保留描边。线宽、颜色、透明度、渐变、虚线、端点及连接沿用 SVG 的解析与绘制能力。相邻片段改变有效描边属性时保留独立样式；带 mask 的片段不合并到外部文字。无描边的普通矢量文字也复用布局阶段取得的轮廓，此时修改描边属性或 paint-order 不会拆开连续字形的整形与字距计算。

同一文字块中连续且绘制样式兼容的轮廓组成等价路径，先统一填色、再统一描边，或按 paint-order 反向操作；绝对 x / y 开启新文字块。逐字位置、旋转、textLength 与 textPath 的现有布局结果进入轮廓绘制，textLength 的字形拉伸也作用于描边。混用不同字形拉伸与旋转的轮廓仍按兼容组分别绘制，复杂跨组重叠不作为完整文字绘制一致性声明。SVG 几何专属的 pathLength / path-length 不校准文字的虚线。

Metal 与原生软件后端按其实际矩阵约定组合嵌套变换；封闭的软件路径统一合并描边覆盖，再应用画刷透明度，避免文字轮廓和相交部分反复加深。直接绘制、SVG 图片、图片画刷和录制回放的当前主机验证见 [文字绘制记录](../artifacts/css-svg-text-paint-20261021/README.md)，离屏入口 `--css-svg-text-paint`。完整 SVG 文字布局、marker、跨后端和浏览器边缘抗锯齿一致性仍需继续。规则依据 [SVG 2 绘制顺序](https://www.w3.org/TR/SVG2/painting.html#PaintOrderProperty) 与 [SVG 2 文字绘制](https://www.w3.org/TR/SVG2/text.html#TextRenderingOrder)。

### SVG 普通文字图片

`SvgImage.FromSvgString` 中仅填充的文字会携带布局阶段生成的真实字形轮廓，用于直接绘制、图片、`ImageBrush`、CSS 图片资源及录制回放。单个兼容片段保留 `GlyphRunDrawing` 的文字与字体信息，边界使用实际墨迹；同一文字块内兼容的多个片段合并填充覆盖，避免半透明重叠处重复加深。轮廓与其字体签名一起保存，冻结与克隆后的绘图可以在工作线程栅格化。

文字定位和字形旋转进入轮廓坐标，`userSpaceOnUse` 的线性与径向渐变不再因额外的原点平移而偏移；字形拉伸沿用等价路径的共同变换。渐变、裁剪和遮罩仍使用同一 SVG 用户坐标与既有绘制能力。

当前画刷和原点的依赖属性修改会更新图像版本；改变字体、字符范围格式或布局约束后不会复用旧轮廓。普通 `FormattedText` 的字段修改通知、任意 `GlyphRun` 的软件文字绘制及只有位图的彩色字形仍待补齐。没有兼容轮廓的图片文字会报告有损转换诊断，不能据此声称通用文字或完整 SVG 字体支持已完成。

原生离屏入口 `--css-svg-text-images`，当前主机的源码、实际截图和验收范围见 [普通文字图片记录](../artifacts/css-svg-text-image-20261021/README.md)。本节沿用既有 SVG 文字布局与字体加载范围，规则依据 [SVG 2 文字绘制](https://www.w3.org/TR/SVG2/text.html#TextRenderingOrder) 与 [SVG 2 填充](https://www.w3.org/TR/SVG2/painting.html#FillProperty)。

### SVG 顶点标记

SVG 文档内的 `marker-start`、`marker-mid`、`marker-end` 与 `marker` 简写支持 `none` 或本地 `url(#id)`，参与呈现属性、样式表、行内样式、变量、继承、CSS-wide 与 `@supports`。首尾标记对应整条路径的首尾，其他顶点使用中点标记；多子路径、闭合路径和零长度线段保留顶点关系，曲线使用解析端点切线。自动朝向支持 `auto` 与 `auto-start-reverse`，固定朝向支持角度及无单位度数。

`markerUnits` 支持 `strokeWidth` 和 `userSpaceOnUse`；`markerWidth`、`markerHeight`、`refX`、`refY`、`viewBox` 与 `preserveAspectRatio` 决定实例的视口和锚点。标记默认裁剪到自身视口，作者可用 `overflow: visible` 放开。定义中的样式来自原始定义树，普通 fill / stroke 不从引用路径继承；`context-fill` 和 `context-stroke` 显式读取引用图形或 use 实例的绘制值，渐变保留来源坐标及对象边界。marker 自身与其子元素的裁剪、遮罩和组透明度进入现有绘图管线。循环标记引用停止该循环分支；文档标记实例数具有资源上限。

标记装饰参与绘制边界，但不扩大供 `objectBoundingBox` 裁剪与遮罩使用的对象几何边界。直接绘制、矢量图片、图片画刷、延迟 SVG 遮罩与录制回放复用同一绘图树。非等比变换下的 CPU 几何填充使用折线路径覆盖，避免以单个距离缩放系数柔化矩形的一侧。

上下文线性渐变按仿射坐标换算其投影，仍可表示为轴对齐椭圆的径向渐变直接换算中心、焦点和半径；其余径向变换走现有矢量图片画刷。原生软件后端在采样渐变前应用当前绘制变换的逆矩阵，使平移、旋转、错切、镜像、DPI 和变换栈弹出后的颜色坐标一致。

原生渐变栅格缓存包含变换，避免同一包围盒中的镜像复用错误颜色；软件椭圆填充按逆变换判定，描边复用居中的路径描边。下节补充渐变颜色插值的显式模式；标记坐标阶段的原始验证仍沿用当时的原生线性光颜色约定。

椭圆折线分段向上对齐四个象限，保持原有误差上限，并使四分之一圈旋转与交换半径得到相同覆盖，避免图片画刷在不同 DPI 下因采样相位出现轮廓变化。

本轮接入静态文档内的引用；跨文档 marker、SVG DOM / SMIL 动态更新及完整跨平台验收仍待继续。入口为 `--css-svg-markers`，源码与实际验收见 [SVG 标记记录](../artifacts/css-svg-markers-20261022/README.md)。规则依据 [SVG 2 顶点标记](https://www.w3.org/TR/SVG2/painting.html#Markers) 与 [路径方向](https://www.w3.org/TR/SVG2/paths.html#PathDirectionality)。

### 渐变颜色插值

SVG 渐变的 `color-interpolation` 支持 `sRGB`、`linearRGB` 和 `auto`，初始为 `sRGB`，沿定义所在的原始文档树继承，参与呈现属性、样式表、行内声明、变量、全局关键字及 `@supports`。使用 href 模板的渐变以被 fill / stroke 直接引用的定义自身计算值为准；模板提供停止点和几何属性，不替代该定义的 CSS 继承来源。`auto` 当前选择 sRGB。

`GradientBrush.ColorInterpolationMode` 的默认值 `SRgbLinearInterpolation` 对 sRGB 编码分量进行线性插值；`ScRgbLinearInterpolation` 在线性光 RGB 分量之间插值后再编码为显示颜色。透明度独立线性插值，画刷透明度应用一次。scRGB 原始分量保留到插值完成后才限制到显示范围，避免先把扩展值压成字节；模式、原始 scRGB 分量及透明度进入缓存内容标识。

软件位图和矢量图片共用停止点采样，重合位置采用原始顺序中最后的颜色。Software 与 Metal 的原生渐变显式接收模式；Vello 色带缓存区分模式。D3D12 / Vulkan 的既有 sRGB 管线使用保留原始停止点的自适应色带，核对直通与预乘颜色的误差，再复用现有绘制路径；模式变更从原始停止点重新生成，不反复转换已处理的色带。资源预算或精度要求无法满足时报告失败。

本节接入渐变颜色计算，未声明 SVG 元素背景混合、颜色动画或滤镜的颜色空间已完整实现，也不扩展现有几何、渐变坐标变换和图像模式的其他能力。原生离屏入口 `--css-gradient-interpolation`；当前主机的运行、截图、公共 CSS 与 AOT 范围见 [渐变插值记录](../artifacts/css-gradient-interpolation-20261022/README.md)。依据 [SVG 2 color-interpolation](https://www.w3.org/TR/SVG2/painting.html#ColorInterpolationProperty) 与 [GradientBrush.ColorInterpolationMode](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.gradientbrush.colorinterpolationmode)。其他平台的物理渲染仍需单独验收。

### 渐变坐标与画刷变换

SVG 的 `objectBoundingBox` 渐变先在单位边界内计算颜色，再应用 `gradientTransform` 和对象边界映射；非正方形边界下，线性渐变的颜色带不一定垂直于映射后的端点连线。`userSpaceOnUse` 使用当前视口的用户坐标，省略端点、圆心和半径时，百分比默认值按视口轴或归一化对角线解析。`gradientTransform` 作为 `transform` 的呈现属性参与 CSS 层叠，支持样式表覆盖、变量和显式 `none`。本地 `href` 模板可沿链查找几何、单位、变换与最近的 stop 子元素，检测循环，链长上限为 256。

画刷的 `RelativeTransform` 在单位边界内应用，随后应用绝对 `Transform`，最后进入绘制上下文变换。WPF 风格的 `LinearGradientBrush` 保留先换算实际端点、再投影颜色的行为；CSS 角度渐变保留物理方向。软件位图、矢量图像、原生直绘与录制重放共用坐标计算，SVG 标记的 context paint 将原始画刷坐标映射到标记局部空间。SVG 对象边界不包含描边宽度，零宽或零高的对象边界及不可逆画刷变换不产生渐变墨迹。

画刷变换的原位修改刷新内容标识，录制快照复制两种变换，按实际尺寸解析 CSS 渐变描边与点绘制，批量直线分别解析各自的边界；原生缓存同时区分对象边界、上下文偏移和画刷内容。CSS 图像按实际尺寸解析时保留画刷变换与透明度。解析结果持有变换值的快照，按源画刷弱关联缓存最近的尺寸与画刷状态，复用未改变的结果，过时版本可回收。新增 C API `jalium_brush_set_paint_transform` 通过独立能力接口设置渐变到用户空间的二维仿射矩阵；无效矩阵返回错误并保留原值，旧画刷工厂沿用单位矩阵。

离屏验证入口为 `--css-gradient-transforms`，运行记录见 [渐变坐标记录](../artifacts/css-gradient-transforms-20261022/README.md)。此范围不覆盖 SVG 2 的完整径向焦锥、`fr`、动画 DOM、外部模板、三维变换、完整色彩管理及各平台的全部文字和描边渐变路径。规范依据：[SVG 2 渐变坐标](https://www.w3.org/TR/SVG2/pservers.html)、[WPF 画刷变换](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/brush-transformation-overview)及 [WPF 线性渐变源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/WpfGfx/core/resources/lineargradient.cpp)。


### SVG 径向渐变的焦点圆

`radialGradient` 解析 `fr`，默认值为零；百分比在用户坐标下以当前视口的归一化对角线为基准，在对象边界坐标下保留单位边界语义。`fr`、圆心、焦点和终止半径沿本地 `href` 模板链查找。渐变几何属性不是 CSS 呈现属性，不能通过行内样式中的 `cx`、`r` 或 `fr` 声明覆盖。负的起止半径不产生绘制。

SVG 画刷按起止两个圆的圆族求颜色，选择半径为正且参数最大的交点；支持扩张、收缩、终止半径为零、外部焦点与非正方形对象边界。圆锥外和完全相同的两圆保持透明。`fr=0` 的焦点恰好位于终止圆上时，Pad 扩展终止颜色，Repeat / Reflect 使用停止点按偏移加权的平均颜色，平均值在当前插值颜色空间计算，透明度独立积分。内切圆族的线性退化状态在转换到原生 float 前保留，避免舍入误填透明区域。嵌套圆族的零半径端点使用连续极限，避免焦点恰落在像素中心时出现透明点；收缩 Repeat 从有效半径一侧接近停止点边界。普通 WPF 风格径向画刷沿用既有焦点行为。

焦点半径与 SVG 圆族模式进入画刷内容标识、克隆、冻结和录制快照，marker 上下文画刷同步换算焦点半径。新增 C API `jalium_brush_set_radial_geometry` 使用独立能力接口，不改变旧画刷工厂与 Brush 虚表；无效参数保留原状态。Metal 使用 v7 shader 标记；顶点颜色引擎复用路径覆盖与逐像素颜色兼容路径，Vello 对 SVG 特有的退化边界复用该覆盖路径。

入口为 `--css-radial-cones`，当前主机的运行范围见 [径向焦锥记录](../artifacts/css-radial-cones-20261022/README.md)。这不表示完整 CSS、SVG 动态 DOM、外部模板、任意精度的亚像素重复周期或所有平台的渲染验收已完成。规则依据 [SVG 2 径向渐变](https://www.w3.org/TR/SVG2/pservers.html#RadialGradients) 与 [Canvas 双圆算法](https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createradialgradient)。

### SVG 恒定描边

SVG 文档内的 `vector-effect` 支持 `none` 与 `non-scaling-stroke`，初始为 `none`，不自动继承。呈现属性、样式表、行内声明、变量、CSS-wide、层叠和 SVG 内的 `@supports` 使用同一套级联；显式 `inherit` 可从父元素或 `use` 实例取得该值。其他 vector-effect 关键字尚未实现，不报告支持。普通 XAML 元素的 CSS 属性表仍与 SVG 文档内的属性表分别检查能力。

`non-scaling-stroke` 在最外层 SVG 视口中构造描边，支持非等比缩放、倾斜、旋转与镜像，保持线宽、虚线、端帽和连接的视口度量。`pathLength` 先按原路径长度标定虚线。渐变仍在原用户坐标中着色，包括对象边界渐变和径向焦点圆；填充、paint-order、裁剪、透明度及遮罩保持元素组合语义。`markerUnits="strokeWidth"` 标记使用恒定描边的视口度量，并按变换后的方向放置；`userSpaceOnUse` 标记保留用户坐标变换。

根和嵌套 SVG 的 viewBox、preserveAspectRatio、x/y 与矩阵变换进入同一根视口；嵌套视口按 overflow 裁剪。SVG 图像的目标逻辑尺寸与栅格像素密度分别传递：100px SVG 放大到 200px 时，4px 恒定描边仍为 4 个逻辑像素，在 2 倍像素密度下覆盖 8 个设备像素。图像画刷按实际完整图像矩形确定逻辑视口，缓存同时区分逻辑尺寸和像素尺寸。直接 Drawing 外部的变换及 DrawingImage 的整体缩放保留绘图语义。内部组变换原位修改后重新计算描边空间；冻结克隆与录制回放保留结果。

恒定描边的 CPU 位图与原生 Software 直绘复用矢量栅格器的轮廓并集覆盖，避免短线段分别取 SDF 最大值产生分段相关的边缘。原生 Software 的暂时栅格使用既有按帧淘汰的图片缓存管理生命周期与预算，单边最多 4096 像素；Metal 直绘继续使用原生矢量路径。曲线按根视口误差约 0.001 的目标展开，并受已有展平和资源预算约束；非有限、不可逆变换不生成恒定描边。数值极限、退化零长度子路径、动态 SVG DOM / SMIL、完整文字塑形、其他 vector-effect 值和其他平台实际验收仍待继续。离屏入口为 `--css-vector-effects`，具体证据与未验收范围见 [恒定描边记录](../artifacts/css-vector-effects-20261022/README.md)。规则依据 [SVG 2 vector-effect](https://www.w3.org/TR/SVG2/coords.html#VectorEffects)、[描边坐标](https://www.w3.org/TR/SVG2/painting.html#NonScalingStroke)及 [markerUnits](https://www.w3.org/TR/SVG2/painting.html#MarkerUnitsAttribute)。

### 组合几何、裁剪与变换

CSS 绘制、裁剪、遮罩及矢量图片共用的几何路径会在展开时应用子几何的平移、缩放、旋转、镜像和错切，顺序为先子、后父。框架保留局部坐标约定：顶层几何自身的 Transform 由绘制或裁剪入口另应用一次，避免重复变换。原生与软件路径复用同一展开实现；返回的折线保留填充、闭合、描边段及平滑连接标记，并可独立修改。

组合几何的边界包含子变换，贝塞尔和椭圆弧在仿射变换后计算极值；填充命中使用整体绕向规则。开放路径在填充和命中时隐式闭合，描边仍保留开放端点，适用于 `clip-path: path(...)` 与 `shape(...)`。子 Transform 内部属性修改会通知几何、绘图与图片缓存。图片固有边界也包含几何顶层变换。

`Geometry.Combine` 与 `CombinedGeometry` 的并、交、差和异或区域使用托管 [Clipper2 2.0.0](https://www.nuget.org/packages/Clipper2/2.0.0) 运算，先应用操作数变换、再按各自填充规则解析孔洞和自交，最后应用结果变换。面积按实际填充区域计算，不重复累计重叠轮廓。该包随 Jalium.UI.Managed 作为传递依赖分发；许可为 BSL-1.0。

曲线先按容差展开，缩放和错切会参与容差换算。布尔运算在平移后的坐标系内处理以减少大偏移下的精度损失，普通 UI 坐标最多保留八位小数，位数随范围调整；局部半径超过 `1e14` 或非有限多边形坐标会拒绝运算，不退回包围盒冒充几何。极大动态范围、完整数值极限及其他平台的实际渲染仍需继续验证。

原生离屏入口 `--css-geometry-transforms`，当前主机的源码、截图、回归及兼容边界见 [组合几何记录](../artifacts/css-geometry-transforms-20261021/README.md)。规则依据 [SVG 2 坐标系统](https://www.w3.org/TR/SVG2/coords.html) 与 [CSS Masking 裁剪路径](https://www.w3.org/TR/css-masking-1/#clipping-paths)。这不表示完整 CSS 或全部几何 API 兼容性已完成。

### SVG 图片的实际视口

`Image` 先按既有 Stretch / object-fit 规则确定绘制矩形，再用该矩形的逻辑尺寸建立 SVG 视口。CSS URL 图像用于背景和图像遮罩时，视口使用最终图片尺寸，包含 background-size / mask-size、平铺与 round 调整。SVG 内的媒体条件和 vw / vh 使用该逻辑视口，百分比几何使用最近的 viewBox 用户坐标；像素桶、窗口 DPI 和祖先缩放不改变媒体条件的 CSS 像素尺寸。根 viewBox 按 preserveAspectRatio 的九种对齐、meet / slice 或 none 映射，保留空白、非零原点和图片边界裁剪。无 viewBox 时，固定根尺寸保留固有用户空间，百分比和 auto 根尺寸则从实际视口解析。

CSS 背景与图像遮罩分别保留 SVG 的自然宽度、自然高度和自然比例。根 width / height 的 auto、缺省值和百分比不提供自然长度；有效 viewBox 提供比例，不自动提供两个绝对尺寸。auto、contain、cover 和单轴 auto 先使用可用的自然维度与比例，再以背景或遮罩定位区域作为默认尺寸。框架公开的 SvgImage.Width / Height 仍保留既有兼容行为。裁剪 ImageBrush 的绝对 Viewbox 时保留 SVG 的小数用户坐标，避免把整数栅格桶尺寸当成源坐标而漏掉边缘。

每个渲染 Dispatcher 缓存最近四个尺寸的解析绘图，尺寸相同时复用；源重新加载或显式修改 Drawing 时失效。修改后的 Drawing 保持为后续绘制的内容，不再由旧 XML 重建。各尺寸共用原始图像的原生缓存槽和绘制时间标记，并以实际绘图身份与内容代次检查缓存。直接 DrawingContext.DrawImage(SvgImage, …) 以及普通程序创建的 ImageBrush 保留基于固有绘图尺寸的画布缩放行为；CSS 资源图片使用实际视口。

离屏入口为 `--css-svg-viewports`，证据见 [SVG 图片视口记录](../artifacts/css-svg-viewports-20261022/README.md)。其他图像上下文的自然尺寸规则见下一节；动态 SVG DOM、外部 SVG 子资源、完整文字塑形和其他平台的实际原生验收仍待继续。当前主机的离屏截图不代替窗口键盘、VoiceOver 或实体设备验收。尺寸规则参考 [SVG 2 intrinsic sizing](https://www.w3.org/TR/SVG2/coords.html#IntrinsicSizing)、[CSS Images 3 default sizing](https://www.w3.org/TR/css-images-3/#default-sizing)及 [CSS background-size](https://www.w3.org/TR/css-backgrounds-3/#background-size)。

### 图像自然尺寸与替换内容

自然宽度、自然高度和自然比例分别参与尺寸协商。SVG 的缺省值、`auto` 和百分比不是自然长度；显式 `0` 是自然长度。固定宽高均存在时，仅两个正长度提供比例；否则有效 viewBox 可以提供比例。图形的墨迹范围不替代缺失的自然尺寸，`SvgImage.Width / Height` 的公开兼容值也不作为 CSS 的自然元数据。

`content` 图片及 `image()` 的默认对象尺寸为 300×150，列表图片标记沿用 1em 的上下文；有自然维度时先使用该维度。以常规替换内容为例：

| SVG 根属性 | 没有指定 CSS 宽高时的默认尺寸 |
| --- | --- |
| `viewBox="0 0 24 24"` | 150×150 |
| `width="40"` | 40×150 |
| `height="20"` | 300×20 |
| 没有尺寸与 viewBox | 300×150 |
| `width="0" height="20"` | 0×20 |

显式设置 CSS `object-fit` 的 `Image` 与 `content` 图片复用替换内容的测量规则；移除后 `Image` 恢复原有 Stretch 测量。指定单轴尺寸时按可用比例推导另一轴，`aspect-ratio:auto <ratio>` 优先使用真实自然比例，独立指定的比例则遵循 box-sizing；自然比例始终约束内容区域。最小 / 最大尺寸、内边距与边框参与测量。加载失败时不凭空获得默认自然尺寸，仍保留明确的 CSS 尺寸与比例约束。重新加载后，即使补齐的默认尺寸相同，自然轴或比例改变也会重新测量。

`object-fit` 的绘制与元素测量分开。`none` 使用内容盒作为默认对象尺寸补齐缺失轴，`contain / cover` 使用真实自然比例，`scale-down` 比较 none 与 contain；`object-position` 再定位。明确的正 CSS 宽高可覆盖自然零尺寸。`image()` 保留未裁剪源的自然元数据，空间片段先在源图像坐标中裁剪，再缩放到输出矩形；整图 RTL 镜像不依赖除以自然宽高。

`cross-fade()` 在使用者的默认对象尺寸中补齐各源尺寸后，按图像权重计算结果；颜色和完全没有自然维度的图像不提供自然尺寸权重。背景与遮罩使用自己的定位区域，边框图像先以边框图像区域协商源尺寸。`border-image-width:auto` 分轴使用可用的自然切片长度，缺失所需自然轴时回退到该边的 border-width；mask-border 复用这套切片规则。这些规则依据 [CSS Images 3](https://www.w3.org/TR/css-images-3/#default-sizing)、[CSS Images 4 cross-fade sizing](https://www.w3.org/TR/css-images-4/#cross-fade-sizing)与 [CSS Backgrounds 3 border-image-width](https://www.w3.org/TR/css-backgrounds-3/#border-image-width)。

入口 `--css-image-intrinsic`，实际验证和 Chromium 对照差异见 [图像尺寸记录](../artifacts/css-image-intrinsic-20261022/README.md)。当前实现按上述规范处理单轴 `object-fit:none` 与缺少自然轴的边框 auto；不能据此声称与所有浏览器逐像素一致。shape-outside 的独立元数据与取样视口见前文环绕章节；浏览器尺寸 / 定位差异、极端数值与亚像素采样、动态 SVG 内容及跨平台原生运行仍需继续；完整 CSS 目标尚未完成。

### 九宫格边框遮罩

普通布局元素支持 `mask-border-source`、`mask-border-slice`、`mask-border-width`、`mask-border-outset`、`mask-border-repeat`、`mask-border-mode` 和 `mask-border` 简写。九宫格划分、宽度重叠缩减、图像密度、自动宽度及平铺几何与 `border-image` 共用实现，但边框遮罩作用于整个元素及后代的合成结果，不绘制新的彩色边框，也不增加布局或墨迹范围。

```css
.frame {
    mask: linear-gradient(to right, white, transparent);
    mask-border: url("stencil-frame.png") 20 fill / 12px / 4px round alpha;
}
.frame:hover { mask-border-width: 16px; }
```

初始值依次为 `none`、`0`、`auto`、`0`、`stretch`、`alpha`。未写 `fill` 时，中间区域保持完全不透明；写了 `fill` 才按中间图像的像素遮罩。该规则也适用于零切片。有效但完全透明的源与加载中、解码失败或没有可用 image-set 候选的源不同：失败的源使整个边框遮罩透明，包括中间区域；`none` 则不应用边框遮罩。

`alpha` 使用源透明度，`luminance` 使用源亮度乘以透明度，`mask-mode` 不影响边框遮罩。它与普通 `mask-image` 分别完成遮罩后相乘，`mask-composite` 只控制普通图像遮罩层。`mask-border-outset` 可让边框图像区域超出盒子，用于保留已有的后代、滤镜或边框图像溢出；区域外的源墨迹被遮掉，不因 outset 产生新的墨迹。边框图像区域也是有限遮罩栅格的边界，避免仿射变换下把末端像素混入其他溢出空间。

六项均支持变量、CSS-wide 关键字、层叠及直接 / 编译 CSS；`mask` 和 `mask-border` 简写中省略的相关项均重置。`mask-image:none` 这个长属性保留已声明的边框遮罩。`style()` 查询按计算后的边框度量比较，比较源图像时不加载查询值；图像表达式的完整等价规范化仍待补齐。

切片、宽度和 outset 按兼容的数值 / 长度 / 百分比类型连续过渡和关键帧插值；相同的 `auto` 侧保留自动值，类型或 `fill` 改变时离散处理。源图像、mode 和 repeat 使用离散动画，过渡需要 `allow-discrete`。图像解码和 GIF 帧更新刷新遮罩及九宫格切片缓存；在离散切换前完成加载的源会在开始绘制时重新绑定，撤销的源不再使元素重绘。

图像像素许可、资源和栅格预算、录制回放及验收平台范围沿用普通图像遮罩。文档 Run / Span 以及 SVG 几何上的 mask-border 仍待实现。当前验证和复现见[边框遮罩记录](../artifacts/css-mask-border-20261020/README.md)，完整 CSS 及实体输入 / VoiceOver 验收仍未完成。


## 原生后端的渐变文字

软件渲染现在可用线性、径向画刷为原生字形覆盖率着色，沿用画刷色标、透明度、Pad / Repeat / Reflect、颜色插值及画刷变换。文字使用当前元素的仿射变换、DPI、裁剪与字形范围；CSS 字距、词距、强调标记及选区仍共用原有排版。

```csharp
text.Foreground = new LinearGradientBrush(
    Color.FromRgb(20, 99, 66), Color.FromRgb(32, 83, 122),
    new Point(0, 0), new Point(1, 0));
Css.SetStyle(text, "letter-spacing: .04em; word-spacing: .2em;");
```

这是 `Foreground` 画刷与 CSS 排版的组合示例。尚未原生测量的文本片段会先测量绘制副本，渐变范围使用真实字形度量，调用者和已录制命令的测量状态保持原样。半透明色标与元素透明度各合成一次；纯色字体仍沿用原有绘制路径。

macOS Metal 的 Impeller / Vello 文字路径同样使用原生字形遮罩采样线性或径向渐变。文字缓存只保存覆盖率，每次绘制重新应用色标、画刷变换、颜色插值及透明度，因此切换画刷时不会沿用旧颜色。设备坐标中的字形遮罩保持 1:1 采样，旋转、斜切和 DPI 不会重复变换。纯色与彩色字形继续沿用原有纯色绘制路径；渐变画刷按彩色字形的覆盖率着色。

macOS 原生软件载荷已验证 CoreText 路径；共享字形图集、LCD、变换字形及 GDI 分支也接入同一覆盖率着色，其他平台运行仍需分别验收。已支持的渐变文字可进入完整帧及逐视觉缓存的不可变录制；图像／自定义文字前景及可变媒体未纳入这项回放保证。完整 CSS 及各平台验收仍未完成。

原生像素入口是 `--css-software-gradient-text`；CMake 软件测试还提供 `jalium.native.software.apple.gradient-text`。冻结源码、独立原生遮罩对照、输入场景及载荷来源见[软件渐变文字记录](../artifacts/css-software-gradient-text-20261023/progress.json)。官方 AppKit 主机的工具链限制和实体输入 / VoiceOver 验收范围沿用前述说明。

Metal 的独立原生对照入口是 CMake 测试 `jalium.native.metal.gradient-text`；实际像素、输入控件和 JIT / NativeAOT 证据见[Metal 渐变文字记录](../artifacts/css-metal-gradient-text-20261023/progress.json)。该项不代表所有平台或完整 CSS / WPT 已完成。


## 渐变文字的不可变录制

录制文字时会捕获前景画刷和分段格式中的画刷。线性／径向渐变沿用现有 `Freezable.CloneCurrentValue` 机制，复制当前动画值、色标、透明度、插值、坐标模式及嵌套变换，再冻结后交给回放。普通纯色文字继续复用冻结的值池；分段格式不再按只有标量字段的键合并，避免下一次录制复用旧的分段颜色。

CSS 生成渐变仍保留实际绘制尺寸的解析能力，同时按值捕获字体、视口、容器单位、兄弟序号／数量和像素对齐所用 DPI，脱离原有元素树。改变绘制宽度时可继续解析百分比；回放不会重新读取后续树结构或计算依据。未改变的画刷及计算依据复用同一快照，源值或依据改变时生成新快照。

macOS Metal（Impeller / Vello）及 Software 已通过原生工作线程回放对照：绘制目标在主线程创建、开始／结束与读回，录制命令在独立线程执行。覆盖整帧、逐视觉缓存、嵌套录制，以及文本、自动补全、数字、密码、显示密码和富文本输入；修改原值、字号、字距、画刷和树结构，并回收缓存后，旧录制仍保留原像素，新录制使用当前值。该检查不代表可见 AppKit 窗口、实体输入、VoiceOver 或其他操作系统的线程模型已验收。

原生入口为 `--css-gradient-recording` 与 `--css-gradient-input-recording`；完整证据和剩余范围见[渐变录制记录](../artifacts/css-gradient-recording-20261023/progress.json)。


### 背景按文字轮廓裁剪

`background-clip: text`（包括 `-webkit-background-clip` 别名）将 CSS 背景裁剪到真实字形的透明度。它可以用于背景颜色、渐变、图像及多层背景；每层独立选择 `text`、`border-box`、`padding-box` 或 `content-box`。`background-origin` 继续只接受三个盒区域，简写中的 `text` 只设置裁剪区域。例如：

```css
.title {
    background: linear-gradient(115deg, #14784f, #9b286d);
    background-clip: text;
    color: transparent;
}
```

遮罩包含现有文字控件的字形、装饰线和强调标记，以及普通流和浮动视觉后代中的文字；绝对和固定定位后代不参与。背景仍受自身绘制区域、圆角及正常裁剪限制，因此首行伪元素的背景只覆盖其首行盒区域。文字颜色、选区背景、插入光标和文字阴影不会扩展字形遮罩，正常前景及编辑器选区继续按原顺序绘制。语义依据 [CSS Backgrounds 4](https://drafts.csswg.org/css-backgrounds-4/#background-clip)。

字形使用独立的无窗口 Software 原生目标生成，不改变当前绘制后端或当前 `RenderContext`；不要求创建 `NSView`。遮罩按根视觉 DPI 和变换缩放栅格化，并对齐像素网格，避免分数行盒边缘造成重复采样。大容器先估计实际文字与装饰范围；连续大面积文字受 16384 边长和 16777216 像素预算限制，必要时降低遮罩分辨率并报告诊断。录制保存独立的透明度数组，工作线程回放不访问实时视觉树；后代文字、布局或变换变化会使祖先文字背景缓存失效，尚未附着窗口的控件也会正确更新缓存。

当前原生支持范围为 macOS Metal（Impeller / Vello）、Software 与 Vulkan。Vulkan 将遮罩组的内容隔离到透明 CPU 子帧，完成内部重叠、嵌套与滤镜后统一乘以 A8 覆盖率，再以位图提交到父 Vulkan 绘制流；父组本身处于 CPU 效果捕获时继续在该组内合成。父层裁剪和透明度只在组完成时应用一次。该兼容路径暂按整个目标分配子帧，目标超过 16777216 像素时返回不支持，不代表遮罩已经使用 GPU 着色器。Vulkan 的局部变换先于父变换组合，局部平移随根 DPI 一同缩放。Linux 容器、lavapipe 的实际结果与范围见[Vulkan 遮罩记录](../artifacts/css-vulkan-alpha-mask-20261023/progress.json)。

D3D12 尚未接入分组透明度遮罩；不支持的上下文会报告诊断并省略该裁剪层。自定义 `GlyphRun` 的真实轮廓尚未接入，不能用其推进矩形代替字形。`border-area` 及其与 `text` 的联合值仍待实现。

验证入口为 `--css-background-text-clip` 和 `--css-background-text-clip-scenes`；源码、真实原生像素、录制回放、NativeAOT 与全套回归的实际结果见[文字背景裁剪记录](../artifacts/css-background-clip-text-20261023/progress.json)。离屏绘制证据不替代可见窗口、实体输入、VoiceOver 或其他操作系统的运行验收。


## 共享自研后端的双向文字基础

Linux / Android 共享文本实现采用固定版本的 SheenBidi 3.0.0（Unicode 17.0.0 数据）解析段落。OpenType 塑形保留逻辑字符顺序及原始范围，换行完成后再按同一段落的嵌入级别重排每行；不再按 RTL 字符数量反转整段，因此阿拉伯文或希伯来文旁的 `CSS` 与数字保持自身的视觉顺序。已有 Unicode 隔离符、嵌入／覆盖控制符、成对括号镜像及行尾空白重置进入该路径。方向控制符不产生可见字形，也不并入前一个字形的源范围。

CSS `direction` 通过控件的 FlowDirection 传到普通 FormattedText 绘制及其录制快照。原生格式缓存和文字值缓存区分方向，修改源对象不会改变已有录制。可选原生接口 `jalium_text_format_set_direction` 接受 0（LTR）、1（RTL）和 2（首个强方向，默认 LTR）；不改变 TextFormat 基础虚表，其他未实现的格式返回 NOT_SUPPORTED。自研格式的命中和光标查询按逻辑边缘解释 RTL 字形，保留 C API 的 UTF-16 偏移，并应用水平、垂直对齐。应使用同一源码重建的整套原生载荷，避免混用跨模块 C++ 类型的不同布局。

SheenBidi 源码、许可证及固定提交随项目保存，C unity 源直接编入 text 模块，也包含在静态文本归档内；消费端不需要另加一个原生库。Interop 与 Android 包的许可证条目包含 Apache-2.0 与 Unicode 许可，原生陈旧性检查也跟踪该依赖。原生动态／静态对照、完整托管回归和实际像素见[共享双向文字记录](../artifacts/css-native-bidi-20261023/progress.json)。算法依据 [UAX #9](https://www.unicode.org/reports/tr9/)，CSS 段落方向与作者控制的规则见 [CSS Writing Modes 4](https://www.w3.org/TR/css-writing-modes-4/#unicode-bidi)。本次数据固定为 Unicode 17，未据此宣称覆盖后来 Unicode 版本的新增字符。

本轮运行范围为 Linux x64 容器中的 Vulkan / Software，以及 macOS 上的便携结构检查和托管回归。CSS `unicode-bidi` 作者属性及统一字体文本的整段上下文已接入，范围见下节；跨来源／原子盒的统一重排、复杂排版组合及编辑控件的完整方向键／选择交互仍需继续。自研 OpenType 引擎的阿拉伯连笔、复杂上下文替换、组合标记定位及连字内部光标尚未由此补齐。Software 与 Vulkan 的字形覆盖率仍有差异，文字背景裁剪沿用独立 Software 字形遮罩；同一参考通过不代表跨后端抗锯齿一致性、实体输入或辅助技术验收。


### `unicode-bidi` 作者范围与段落上下文

`unicode-bidi` 接受 `normal | embed | isolate | bidi-override | isolate-override | plaintext`。初始值为 `normal`，不自动继承；显式 `inherit`、变量、层回退、编译 CSS 与普通计算样式查询沿用级联规则。`all` 不重置它，该属性和 `direction` 均不能用于 CSS 过渡或关键帧动画；`::first-line` 不接受此属性。`::marker` 的默认声明包含 `unicode-bidi:isolate`，跨来源行内格式上下文中的实际标记隔离仍需下面列出的统一排版工作。

对行内范围，嵌入、隔离、覆盖、隔离覆盖及 `plaintext` 分别采用 CSS Writing Modes 规定的 Unicode 控制等价形式；这些范围在硬段落边界关闭并重新开始。非行内盒上的 `embed` 和 `isolate` 不建立额外范围；块根上的覆盖影响根行内内容，块根的 `plaintext` 按每段首个强方向字符确定段落方向。此过程不会改写作者文本，也不会把布局控制符暴露到 `Text`、`SelectedText` 或 UTF-16 来源位置。

普通来源树中的 `TextBlock` 对不同字体族、字号、字重、样式及实色、线性和径向渐变前景使用一份完整、不可变的文本投影，供绘制、选区、文档文字边界与点击命中使用。CSS 已决定的软换行以布局专用的 U+2028 保留在同一段落中，继续行不会重新猜测 `plaintext` 的方向。macOS 使用保留的 CoreText 段落；Linux 的 Software 与 Vulkan 使用共享原生段落按来源范围过滤已塑形字形。Vulkan 文字位图缓存区分过滤模式和完整范围，并在栅格化之前归一化过滤行的纵向原点，保留分数行高。超预算缓存条目在转移所有权前拒绝插入，超长行按视口限制暂存位图；物理像素对齐且 1:1 映射的文字保留原始覆盖率，变换后的文字继续使用插值。录制保留不可变布局和范围，后续样式变化、布局释放及工作线程重放不会改写该帧的方向信息。

尚未由此项补齐：图片等其他非实色前景、跨来源行内盒和原子控件的统一 bidi 段落、下述接入范围以外的字距与词距组合、长度变化的文字变换、折叠空白的绘制映射、保留制表符、软连字符、裁剪省略及复杂对齐组合，以及行内范围改变塑形时的精确测宽和换行判定；这些组合仍需继续接入共享排版。已进入该分支的未接入组合会产生 `LossyConversion` 诊断；跨来源匿名盒的范围传递和完整诊断也尚待接入。`plaintext` 与每段行框方向及对齐的后续接入范围见文末专节。Linux 的段落查询已接入下述共享原生对象；大量样式跨度的绘制批次与复杂排版仍需继续验收。此阶段不代表竖排、复杂脚本塑形、编辑器逻辑／视觉方向键、实体输入、VoiceOver 或真实 Windows 验收完成。

实现规则依据 [CSS Writing Modes 4 的双向文字章节](https://www.w3.org/TR/css-writing-modes-4/#unicode-bidi)。逐阶段结果、源码与实际原生模块身份保存在[作者 bidi 验证记录](../artifacts/css-unicode-bidi-20261023/progress.json)；只有记录中明确完成的验证才属于本阶段验收范围。

### 共享原生段落与字符边界

Linux 的共享文本引擎现在实现原有不可变段落接口。创建时保存字体与格式快照，保留完整段落的双向上下文，再建立行、字体跨度、字素单元和视觉边缘索引；光标、命中与选区查询不再每次传入整段文本或重新塑形。绘制复用保存的排版结果，并使用绘制目标的字形图集，因此文字遮罩可以在另一个 Software 上下文绘制同一段落。格式、样式或布局所有者释放后，已经保留的录制帧继续拥有其字体与排版数据。

原生字符边界采用 [Unicode 17 的 UAX #29](https://www.unicode.org/reports/tr29/tr29-47.html)，覆盖 emoji、组合字符、Hangul 与 Indic 序列的扩展字素规则。固定版本的属性文件、官方测试及许可证随源码保存；`tools/generate-css-grapheme-unicode.py` 校验输入哈希后生成原生属性表，不增加 Unicode 运行库依赖。字素边界完整性不代表复杂脚本塑形或字体 GDEF 连字内部光标已完成。

原生段落接受不同字体族、字号、字重与实色跨度，并保留 UTF-16 来源位置。单行输入缓存同时区分文字渲染、格式化与提示选项；作者 bidi 和单行输入用私有输入格式捕获这些选项，避免改写测量缓存中的共享格式。绘制范围覆盖完整超宽行，布局宽度继续决定换行与对齐，单行输入滚动时不会因原始宽度丢失后面的文字。

CSS 作者 bidi 的 `TextBlock` 已接入不同字体、实色和下述渐变跨度。原生字体跨度能力不等同于跨来源行内盒、原子控件、图片前景、字距与词距、裁剪省略、完整 UAX #14 换行及复杂脚本已统一；大量样式跨度的绘制批次、托管与原生 Unicode 版本的一致性、竖排及实体输入也仍需继续。独立文字格式对照、录制与缓存失效、滚动长行、并发查询、官方字素测试、消毒器与来源核验的实际结果见[共享段落验证记录](../artifacts/css-shared-native-paragraph-20261023/progress.json)。容器中的 lavapipe 证据不替代实体 GPU、真实 Windows、IME 或 VoiceOver 验收。

### 作者 bidi 的字体与实色跨度

普通 `Run`／`Span` 来源树中的作者 bidi 现在保留各段字体族、字号、字重、样式及实色前景。字体跨度完整覆盖投影插入的方向控制符和软行分隔符，不会把控制符复制回作者文本。测宽先建立整段上下文及按来源位置索引的推进表，候选换行继续使用原有 CSS 规则；最终绘制段落再保留已经选择的软换行。方向、字体、渲染选项或布局变化会使缓存失效。

颜色是绘制属性，不再把同一字体的连续塑形切开。颜色边界跨过一个字素或连字时，该字形归最后一个参与来源的颜色，避免半透明前景重复绘制同一字形；可见性过滤仍按完整字形与请求范围的交集决定包含或排除。该边界策略属于 [CSS Text 3 对跨元素排版字符的允许处理](https://www.w3.org/TR/css-text-3/#characters)。录制保存已塑形布局、源范围和当时的颜色，修改源画刷、字体或文本并释放布局后，旧帧仍可回放。

这一接入仍要求普通来源树、正字号、正常字体宽度及支持的前景画刷。CSS 盒片段、原子控件、图片前景、下述接入范围以外的字距／词距及制表符组合、大小写或 emoji 特殊塑形、软连字符和裁剪省略等组合继续使用已有路径并保留未接入诊断；托管与原生 Unicode 字素版本的一致性、GDEF 连字内部光标、复杂脚本与完整 UAX #14 仍需继续。实色阶段的测宽、逐字体原生参考、源几何、遮罩、录制、反向对照及完整回归结果保存在[字体跨度验证记录](../artifacts/css-styled-bidi-paragraph-20261023/progress.json)，仅记录中明确完成的项目属于对应阶段验收。

### 作者 bidi 的渐变前景

`TextBlock.Foreground` 及普通 `Run`／`Span` 的线性、径向渐变现在直接绘制同一份保留段落的字形，不再改回分段塑形或代表色。相邻来源共享同一画刷时合并绘制；跨边界连字仍只归一个来源，包含／排除过滤仍选择完整字形。相对渐变按当前来源跨度的视觉推进包围盒及行盒高度定位，可见性过滤不改变该画刷坐标。画刷变换、透明度和渐变停止点沿用现有画刷规则；录制同时保留坐标及不可变画刷快照，释放布局后不再查询旧布局的几何。

原生新增可选的段落画刷绘制接口，保持已有段落和目标接口的虚表不变。Software 和 Metal 用真实字形覆盖率采样渐变；Linux／Android 的 Vulkan 文字位图路径保存不含画刷的覆盖率，再逐像素着色，因此单个字形内部也有连续渐变。该 Vulkan 渐变遮罩使用灰度覆盖率，实色文字保留原有 ClearType 路径；画刷或其坐标变化不会复用旧的着色结果。Android 的实现参与相同源码编译分支，实际设备结果仍待验收。

这里的渐变入口是宿主 `Foreground` 画刷能力；CSS `color` 仍接受颜色值，CSS 渐变背景填充文字继续使用 `background-clip:text`。图片画刷、跨来源行内盒、特殊塑形组合和物理输入范围未由此扩大。新增像素公式对照、混合字体、完整字形过滤、录制和平台回归的实际结果见[段落画刷验证记录](../artifacts/css-bidi-brush-20261023/progress.json)。


多行段落按字体的自然度量塑形，CSS 行高与基线随后定位每一行；首次查询哪一行不会改变后续段落的字形或渐变。每个来源范围的渐变上边缘与高度锚定其所在的 CSS 行盒，混合字号、硬换行与自动折行分别保留各行坐标。零行高和紧凑行高沿用 CSS 允许字形溢出行盒的行为，录制快照保存当时的行盒坐标。查询顺序、逐行坐标、修改后重放及实际后端的验证范围见[多行画刷记录](../artifacts/css-bidi-multiline-20261024/progress.json)。


### 作者 bidi 的字距与词距

普通 `TextBlock` 及 `Run`／`Span` 的 `letter-spacing`、`word-spacing` 已接入保留的整段 bidi 排版，包含混合字体、逐来源间距、实色和前述线性／径向渐变。间距按视觉顺序分配，相邻来源的字距取两侧值的平均；行首、行尾没有额外半份字距，折叠的行尾空格不获取字距或词距。候选换行从整段自然字形推进量计算，再应用该候选行的内部间距，避免把未换行段落的边缘半间距带入测宽。格式控制符不获取间距，也不会复制回作者文字。

原生段落的已有独立推进量接口现支持多行：CoreText 保留原始字形并逐行定位；Linux 的共享引擎复用已塑形字形、字体和来源索引，更新字形偏移、光标、命中、选区与片段几何。不会用逐字符重新塑形替代整段上下文。组合字符与代理对保持完整单元，草书文字沿用不拆断连接的间距抑制，负间距沿用现有非负推进量限制。录制保留这些结果，后续样式与字体变化不改写旧帧。

自动间隔的接入范围见下文。此接入仍不包含 CSS 行内盒／原子控件、大小写特殊塑形、下述范围以外的制表符组合、软连字符和裁剪省略等组合，也不代表复杂脚本、完整 WPT 或实体输入已验收。规则依据 [CSS Text 3 字距](https://www.w3.org/TR/css-text-3/#letter-spacing-property)及[词距](https://www.w3.org/TR/css-text-3/#word-spacing-property)；实际构建、像素、几何、录制与回归结果见[字距与 bidi 验证记录](../artifacts/css-bidi-spacing-20261024/progress.json)。

### 作者 bidi 的保留制表符

普通 `TextBlock` 与 `Run`／`Span` 的保留制表符现使用整段 bidi 字形和源范围，不再因为含有 `\t` 而退回逻辑顺序绘制。`pre`、`pre-wrap` 与 `break-spaces` 的候选测宽和最终行均按块内容边缘的制表尺推进；缩进先参与制表位计算，文字对齐随后定位整行。硬换行、空行和自动折行分别重置行内推进。制表符依 UAX #9 L1 采用所在段落的方向，`plaintext` 的不同段落可以采用不同方向。

该保留段落接入当前覆盖 macOS Metal／Software 与 Linux Vulkan／Software；其他平台继续沿用已有路径与未接入诊断。

`tab-size` 的数字值使用块容器的空格推进量及其字距／词距，长度值直接给出步长，行内来源可以设置自己的制表尺寸。下一制表位距离不足 `0.5ch` 时，只再前进一个制表位；极小步长不会反复跳至半个 `ch`。零尺寸制表符不产生水平推进，作者文字中的制表符与 UTF-16 源位置仍然保留。正字距和下一字形自身的前侧间距参与制表位置，后续字形起点落在选定制表位；本实现将接触制表符的负字距限制为零，其余文字保留已有的负间距限制。

原生端复用原有推进量接口及保留字形，更新制表符单元的推进量并同步绘制、光标、命中和选区；不会拆成独立字符重新塑形。字体、制表尺寸、画刷或作者文字的后续修改不改写已录制帧。此范围包含混合字体与前述前景画刷；作者 bidi 的两端对齐见下节。CSS 行内盒／原子控件、裁剪省略等组合、完整 WPT、复杂脚本与实体输入仍待继续验证。规范依据 [CSS Text 制表位](https://www.w3.org/TR/css-text-3/#white-space-phase-2)与 [Unicode L1](https://unicode.org/reports/tr9/#L1)，实际构建和验证范围见[制表符与 bidi 记录](../artifacts/css-bidi-tabs-20261024/progress.json)。

### 作者 bidi 的两端对齐

普通 `TextBlock` 与 `Run`／`Span` 来源树中的作者 bidi 已把 `text-align:justify`、`text-align-last` 和 `text-justify` 接入保留的整段排版。自然测量与候选换行保持原有推进量；最终行在 bidi 重排后按视觉顺序分配剩余空间，保留字体跨度、原始 UTF-16 来源、字形及渐变行盒。绘制、光标、公开命中和选区使用同一份扩展推进量。

`inter-word` 扩展词分隔符，`inter-character` 使用字素边界，`auto` 使用已支持的词分隔符及中日韩文字边界，`none` 不产生扩展机会。词分隔符读取所属来源的设置，相邻字符边界读取最近共同祖先的设置。现有可选连字规则参与逐字对齐；连写连接和必须保留的字形不会被拆成逐字符绘制。这不增加底层引擎尚未支持的复杂脚本塑形能力，Linux 的部分阿拉伯文形态仍有限制。

自动折行按非末行规则对齐；硬换行和段落末行读取 `text-align-last`。没有可扩展机会时采用末行对齐回退，回退值仍为 `justify` 时居中。保留制表符的行只扩展最后一个制表符之后的文字，之前的字段位置和制表尺保持不变；行尾悬挂空白仍保留自身推进。`pre-wrap` 行扩展时按原生可见来源的推进量计算剩余空间，行尾空格不挤占可见文字的对齐宽度；RTL 行同时保留悬挂至左侧的空格与正确裁剪范围。切换 CSS 对齐、设置等于默认值的原生 `TextAlignment` 本地值或清除此值，都会重新布局并更新保留段落，已录制帧保持原样。

此接入当前覆盖 macOS Metal／Software 与 Linux Vulkan／Software，包含字距、词距、混合字体、实色与已支持的渐变。Windows／Android 的保留段落接入、CSS 行内盒／原子控件、特殊大小写／emoji 塑形、软连字符、裁剪省略、竖排及完整 WPT 仍需继续。规范依据 [CSS Text 3 两端对齐](https://www.w3.org/TR/css-text-3/#text-justify-property)与[末行回退](https://www.w3.org/TR/css-text-3/#text-align-property)；独立原生推进量参考、像素、几何、录制、后端与回归的实际验证范围见[作者 bidi 两端对齐记录](../artifacts/css-bidi-justification-20261024/progress.json)。本轮原生渲染场景不代替实体键盘、IME、VoiceOver 或设备验收，完整 CSS 目标继续进行。

### 作者 bidi 的自动间隔

普通 `TextBlock` 和 `Run`／`Span` 来源树中的 `text-autospace` 已接入保留的作者 bidi 段落。候选行测宽和最终绘制复用现有自动间隔规则，在 bidi 视觉排序后判断相邻字符；中西文及数字间距使用最近共同来源的 `0.125ic`，法语标点沿用语言与字体对应的窄不换行空格／不换行空格宽度。全角字符、其他空格和隔离范围参与判断，字距与词距继续叠加；插入的间隔不改变作者文本或复制内容。

`insert` 保留已有空格；`replace` 在合适边界替换 U+0020 的推进量，保留其 UTF-16 来源位置，其他空格不被替换。替换间隔在软换行边缘不留下额外推进，宽度变化会重新计算每行的有效邻接关系。带制表符的行先合成自动间隔与字距，再按块制表尺定位字段；已支持的逐词／逐字两端对齐随后扩展剩余空间。不同字体、实色与渐变、选择和命中使用同一份推进量，后续文本或样式修改不改写已录制帧。

行起点与悬挂裁剪读取实际保留段落；不会把旧逻辑片段因重排、镜像字形或间隔产生的宽差当成溢出。`plaintext` 的 RTL 行尾空格可以悬挂至物理左侧，同时保留作者原有的 `FlowDirection`；每段行框方向与 `start`／`end` 回退对齐的后续接入范围见下节。异常 UTF-16 输入沿用替换字形分类，原始文本仍供选择复制使用。

此路径当前覆盖 macOS Metal／Software 与 Linux Vulkan／Software，并保留现有脚本分类、字体和原生推进量限制。CSS 行内盒／原子控件、其他绘制文本变换、特殊大小写／emoji 塑形、软连字符、裁剪省略、竖排、复杂脚本及完整 WPT 仍需补齐；实体键盘、IME、VoiceOver 和 Windows／Android 实机验收未由这些原生渲染场景代替。规则依据 [CSS Text 4 自动间隔](https://www.w3.org/TR/css-text-4/#text-autospace-property)，当前工作草案中的 `normal` 和 `replace` 语法分别解析，组合替换应写为 `ideograph-alpha ideograph-numeric replace`。实际源码、独立原生参考、反向对照和平台结果见[自动间隔与 bidi 验证记录](../artifacts/css-bidi-autospace-20261024/progress.json)。完整 CSS 目标继续进行。

### plaintext 的段落行框方向与对齐

普通 `TextBlock` 和 `Run`／`Span` 来源树的 `unicode-bidi:plaintext` 现在按段落方向定位行框。首个 L／R／AL 强方向字符决定逻辑 `start`／`end`；隔离范围内部的字符不决定外层方向，软折行投影内部插入的 U+2028 保留所属段落的方向。没有外层强方向的数字、标点和空段沿用前一行，首段则采用控件的 `direction`。此过程不改写公开 `FlowDirection` 或作者文本。

行框方向同时参与 `text-align-last`、不能扩展的两端对齐回退、`text-indent`、来源行内边距及悬挂标点的物理侧转换。`left`／`right` 保持物理含义；原生 `TextAlignment` 本地值沿用既有优先级，清除后恢复 CSS 对齐。改变方向或对齐值来源会重新定位行与源几何，旧录制帧继续保留原来的文字及位置。自动间隔、字距和词距先决定推进量，再应用行框对齐。

过长的 plaintext 行按行起点定位并保留水平溢出；TextBlock 内部绘制裁剪不再吞掉这些字形，显式 `overflow:hidden` 和外层裁剪继续生效。选区、命中和复制仍映射到原始 UTF-16 来源；空行的光标位置使用继承的行框方向。

强方向分类来自固定的 Unicode 17 `DerivedBidiClass`，包含未分配码点的默认规则，生成器可离线重现；数据许可位于 `tools/unicode-bidi-17/UNICODE-LICENSE.txt`。原生 bidi 重排和塑形继续由现有引擎完成。此项的早期行框绘制验收采用 LF／CRLF 与内部软折行投影，覆盖 macOS Metal／Software 与 Linux Vulkan／Software；作者源分隔符的后续接入见下节。共享 CSS 行内盒／原子控件、任意长度变化的绘制映射、特殊大小写／emoji、软连字符、省略、竖排和复杂脚本的完整组合仍需继续。实体键盘、IME、VoiceOver、Windows／Android 及完整 WPT 未由本轮渲染测试代替。

规则参见 [CSS Text 3 行框方向](https://www.w3.org/TR/css-text-3/#bidi-linebox)、[末行对齐](https://www.w3.org/TR/css-text-3/#text-align-last-property)及 [Writing Modes 的 plaintext](https://www.w3.org/TR/css-writing-modes-3/#valdef-unicode-bidi-plaintext)。实际输入、消费者反向对照、Unicode 数据比较、像素、几何、录制及回归范围见[plaintext 段落对齐记录](../artifacts/css-plaintext-alignment-20261024/progress.json)。完整 CSS 目标继续进行。


### 作者源 Unicode 分隔符与空白转换

普通 `TextBlock` 的 U+2028（LS）、U+2029（PS）与 U+0085（NEL）现在生成实际行框，后续行的绘制、选区、源几何和命中保持原始 UTF-16 位置。连续或结尾的分隔符保留空行与光标位置；CRLF 继续只分行一次。LS 结束当前行并保留 bidi 段落与方向范围，PS／NEL 开始新段落并关闭、重新打开作者方向范围；`plaintext` 的逻辑对齐、逐行缩进及末行对齐使用相应行框方向。已选择的软折行在源分隔符之间继续参与整段塑形，重新调整宽度会重建来源映射。

Jalium 的 Unicode 文本来源把 NEL 视为 segment break：`pre`、`pre-wrap`、`pre-line`、`break-spaces` 保留换行，`normal`／`nowrap` 按空白规则折叠，`preserve-spaces` 转为空格，`discard` 移除。LS／PS 保留强制换行，邻接空白按当前模式处理。这个约定适用于 Jalium 文本来源；[CSS Text 的 segment break 定义](https://www.w3.org/TR/css-text-3/#white-space-processing)取决于文档语言，不能据此声称 HTML DOM 或其他控制字符的行为已完成。

可见字符一一对应的空白转换，以及使用零宽 FEFF 源占位表示的隐藏空白，现可继续使用保留的 bidi 段落。每个位置都核对实际绘制偏移，避免把总长度碰巧相同的扩展或任意映射误认为一一对应。零宽 FEFF 占位不把普通拉丁文字判为阿拉伯呈现字符，也不因此切换 Writing Tools 的普通绘制路径。字形、渐变和来源位置使用同一段落；`word-space-transform` 在空白转换之后判断相邻字符是否仍为强制换行，复制内容保留作者原文。

此阶段验证范围见[源 Unicode 分隔符记录](../artifacts/css-unicode-line-separators-20261024/progress.json)，涵盖独立原生参考、空行、方向覆盖与隔离、字距、空白模式、窄宽度反复折行、选区命中及修改后重放。未列入记录的任意文字扩展、共享 CSS 行内盒、复杂脚本及完整 WPT 仍待继续；实体键盘、IME、VoiceOver 与 Windows／Android 实机未由离屏渲染替代。规则另参见 [Writing Modes 的段落与嵌入范围](https://www.w3.org/TR/css-writing-modes-3/#bidi-algo)。完整 CSS 目标继续进行。

### 共享 CSS 行内来源的双向段落

具备高级原生段落接口时，同一个 CSS 行内格式上下文内的原始 `TextBlock`、嵌套行内容器和普通原子盒可以参加统一的双向段落。普通行内来源边界保持透明；块方向和 `unicode-bidi` 范围投影为布局控制，软折行保留段落上下文，源强制分隔符沿用上一节的段落规则。来源字符串、父级、计算方向和复制内容保持原样，字形绘制、来源选区和命中使用同一个保留的段落。同一物理行上的视觉片段在文档接口中合并为一行，原生行身份保留不同段落和不同栏的边界。

双向重排可以把一个背景／边框拆成不相邻的片段。物理左右边缘按视觉顺序分配，`box-decoration-break: clone` 为每块保留边缘，嵌套容器按对应片段确定边界。留白、边框或垂直位移形成的实际塑形边界使用布局专用断开符，不把整个双向段落拆开。拆开的渐变文字沿用原来源行的渐变范围；已经录制的字形和笔刷范围不会随原来源修改而变化。Vulkan 的筛选字形暂存图保留整数像素边缘，非整数位置和仿射绘制使用确定的双线性采样，使来源拆分不会改变字形覆盖率；文本自身的裁剪保留一个设备像素的抗锯齿边缘，作者设置的外层裁剪仍然生效。

本阶段接入普通字体伸展、可保留来源映射、实色及受支持渐变的水平排版，包含字体跨度、字距、词距、普通中性原子盒、强制与软折行以及末尾空行。共享制表位、自动间距和扩展两端对齐的后续统一接入见下一节；软连字符显现、截断标记、任意长度变化、替换元素的特殊嵌入规则、复杂脚本和竖排组合仍需继续。单个 `TextBlock` 已有的高级能力不能由此推定为全部跨来源组合已完成。

规则依据 [Writing Modes 的行内盒重排](https://www.w3.org/TR/css-writing-modes-3/#bidi-box-model)。实际完成的消费者对照、源码与载入模块身份、像素、几何和回归结果见[共享行内 bidi 记录](../artifacts/css-shared-inline-bidi-20261024/progress.json)。该记录区分 macOS／Linux 后端结果与未验证的平台和实体输入；完整 CSS 与完整 WPT 目标继续进行。


### 共享双向段落的制表位、自动间距与两端对齐

共享行内段落现在在双向重排之后计算保留制表符、自动中西文／法文间距和可扩展的两端对齐。制表位沿块容器的内容边缘建立，数值 `tab-size` 使用块容器字体的空格宽度及其字距、词距；行内字体只影响制表符自身的半个 `ch` 阈值。首行缩进、浮动可用带及行内物理边缘参与制表位置。最后一个有效制表符之前的内容保持其制表锚点，其后的对齐机会按行框方向扩展。

自动间距继续按共同样式来源决定归属。祖先拥有的间距保留在来源盒外，子来源的背景、选区、点击边界和渐变前景都使用排除该间距的来源范围；同一来源内部的间距保留在自身绘制范围内。零宽制表符仍能复制并保留源位置，点击相邻可见字符时不会选中共享同一光标坐标的制表符末尾。macOS 的原生段落也补齐了关闭双向控制符前被 CoreText 省略的制表符尾侧光标。

普通从左到右的间距继续复用已有的共享原生塑形规划；需要双向上下文的段落在统一重排路径内合并间距。候选行复用不需要改写的自然段落；中性原子盒的原始宽度在无相邻字距贡献时缓存，避免生成图片混排时为每个候选重复测量同一个原子槽。缓存不改变段落、来源或录制帧的所有权。

规则见 [CSS Text 3 的制表符处理](https://www.w3.org/TR/css-text-3/#white-space-processing)和[对齐](https://www.w3.org/TR/css-text-3/#justification)，以及 [CSS Text 4 的自动间距](https://www.w3.org/TR/css-text-4/#text-autospace-property)。本次实际执行的源码、载荷、参考、反向消费对照与测试范围见[共享行内间距记录](../artifacts/css-shared-inline-spacing-20261024/progress.json)。软连字符显现、省略、长度变化的文字变换、竖排、复杂脚本及完整 WPT 仍需后续实现和验收；本节不表示完整 CSS 已完成。

## 共享行内段落的条件连字符

水平共享行内 bidi 段落可以保留 U+00AD 软连字符，并在选用该换行机会时显示 `hyphenate-character`。显示字符串可以为空或包含多个字素；字形使用软连字符所在 Run 的字体和前景样式，源文本仍保留单个 U+00AD。未使用的软连字符不生成字形或推进宽度，`hyphens:none` 不采用该断点。

共享段落保留之前选定的软折行和显现字符串；RTL、`unicode-bidi:plaintext`、override 和行内 isolate 继续使用原段落上下文。原生显示字素分别提供几何边界，源选区与命中结果映射回原始软连字符；宽度改变和文字修改不把生成的连字符写入 Run 或复制文本。

条件连字符的跨来源词界限计数见下一节。这项接入没有补齐语言词典、语言特有拼写变化、连字符内部混合方向的全部组合、截断标记、任意长度变化的文本转换、竖排或复杂脚本连接形式，也不表示完整 CSS 或完整 WPT 已通过。实体键盘、IME、VoiceOver 和其他平台的设备验收仍须分别完成。

规范依据：[CSS Text 3 的连字符与源内容要求](https://www.w3.org/TR/css-text-3/#hyphens-property)、[CSS Text 4 的显示字符串](https://www.w3.org/TR/css-text-4/#hyphenate-character)。

## 共享流中的连字符词界限与字数

`hyphenate-limit-chars` 的总字数、断点前字数和断点后字数使用共享流中的完整逻辑单词。拆分 TextBlock、Run、普通行内盒和带内边距的行内盒不会缩短单词；空行内盒、`display:none` 内容和流外内容不增加单词字数。真实空白、强制换行、块边界和原子／替换元素终止当前词的上下文。

计数按 Unicode 标量进行，非间距组合标记（Mn）、词内标点（P*）和软连字符自身不计数。字数和 `hyphens` 规则取自条件连字符所在的位置，其他节点的严格规则不改变该断点的限制。内容、样式、宽度重新布局时会重算上下文；原始 Run 和文本范围保持不变。普通 LTR 和 bidi 段落都保留条件连字符的显示映射。

共享流为包含条件连字符的段落扫描一次词上下文，换行候选复用已计算的断点资格；不再在每次候选测量时重复扫描每个源节点中的片段。可运行的源码对照、Unicode、结构边界、继承与录制回放用例见 [CssHyphenationWordContextTests](../tests/Jalium.UI.MacOS.Tests/CssHyphenationWordContextTests.cs)。词的字符分类沿用当前运行时策略；语言词典、特殊首字下沉、复杂脚本、文本变换与各类断词选项的完整组合仍需继续验证。

规则依据：[CSS Text 3 的跨行内边界词处理](https://www.w3.org/TR/css-text-3/#hyphens-property)和 [CSS Text 4 的字数限制](https://www.w3.org/TR/css-text-4/#hyphenate-limit-chars)。

## 变长文本转换与双向段落映射

双向段落保留原始 UTF-16 源位置，并独立构建显示文本。一个源 Unicode 标量可对应零个、一个或多个显示标量，涵盖 `ß → SS`、连字展开、`İ → i + ◌̇`、立陶宛语附加标记和土耳其语点号删除，以及补充平面字符。字体与 bidi 作用域边界、手动软连字符和已选定的换行点继续使用源位置；原文和纯文本复制内容不被改写。

字距、自动间距、制表位和两端对齐按转换后的显示字素分配，多个显示字素仍归属于同一源范围。普通 TextBlock、Run 与共享流使用这份映射绘制、选择、命中和录制回放；两端对齐生成的整源范围行也可接入保留段落。变长转换触发该路径时，普通 LTR 段落无需额外加入 RTL 内容。普通 LTR 的变长转换、保留制表符与扩展两端对齐（`text-align: justify` / `text-align-last: justify`）保持最后一个有效制表符之前的制表尺锚点，仅扩展制表符后的后缀，并保留源 UTF-16 光标与选区映射。

当 `word-break: break-all`、`line-break: anywhere` 或 `overflow-wrap: anywhere` 在同一个一对多展开字符（如 `ß → SS`、`ﬃ → FFI`）的显示字素之间产生软换行时，投影按 `(SourcePosition, DisplayOffset)` 软显示断点拆行并裁剪每行字形范围，使同一源字符可跨多行返回多个片段几何，而不会把单个显示字素（如 `İ → i + ◌̇`）或 UTF-16 代理对拆开，也不会在复制时重复输出源字符。`text-overflow: ellipsis`、`TextTrimming`、`line-clamp`（含自定义 `block-ellipsis`）、`::first-letter` 与 `::first-line` 均接入变长文本转换与语言上下文，生成省略标记不写回正文或 `SelectedText`。

可运行的字形、几何、复制、样式往返和组合用例见 [CssLengthChangingBidiTests](../tests/Jalium.UI.MacOS.Tests/CssLengthChangingBidiTests.cs)，映射边界约束见 [CssBidiShapingProjectionTests](../tests/Jalium.UI.Tests/CssBidiShapingProjectionTests.cs)。竖排及复杂脚本仍需继续实现与验证，离屏软件/GPU 渲染测试也不替代可见窗口、物理输入、VoiceOver 或 Windows/Linux 实机验收，不表示完整 CSS 或完整 WPT 已通过。

转换顺序与复制规则依据 [CSS Text 3 的文本转换](https://www.w3.org/TR/css-text-3/#text-transform-property)及[文本处理顺序](https://www.w3.org/TR/css-text-3/#order)。

## 跨节点语言上下文与自动断词

共享 CSS 行内格式化上下文将相邻的 `TextBlock`、`Run`、`Span`、匿名文本盒及行内 `::before`／`::after` 生成文本纳入同一份跨节点词与语言上下文窗口，同时保留各来源自身的语言标记、样式、对象身份与 UTF-16 源位置。普通行内边界（含非零行内 `padding`／`border`／`margin`）与空行内盒、`display:none` 内容、浮动／绝对定位流外节点不会无故截断单词上下文；真实空白、硬换行、块边界与 `inline-block`／替换原子盒则会结束当前单词上下文。

- **跨节点大小写与特殊语言规则**：`text-transform: capitalize` 跨越相邻行内节点追踪前导词状态（如 `<span>super-</span><span>cali</span>` 第二段保持小写 `cali`，荷兰语 `<span>i</span><span>jsvrij</span>` 跨节点合成为 `IJsvrij`）；希腊语末尾 sigma（`ΛΟΓ` + `ΟΣ` → `λογος`，含 `::before`／`::after` 拼接）、立陶宛语 `More_Above` 组合附加符（`Į` + `\u0307A`）与土耳其语／阿塞拜疆语 `Before_Dot` 跨越普通行内边界向前／向后扫描相邻可见字符，并在相邻节点文字、显示状态或语言改变时自动使受影响伙伴失效。
- **离线自动断词与正字法拼写变化（`hyphens: auto`）**：基于编译期内嵌、SHA-256 校验的 Liang／libhyphen 离线词典（`tools/hyphenation-dictionaries/` + `tools/generate-css-hyphenation-data.py`，覆盖 `en-US`、`de-1996`、`de-1901`、`nl`、`hu`、`ca`、`fr`、`es`，零联网依赖），在单控件与跨节点共享流中统一支持自动断词及语言特有断行拼写变化（传统德语 `de-1901` 的 `ck → k-k` 如 `Zucker → Zuk-ker`，荷兰语 `nl` 分音符消除 `reëel → re-eel` 与元音缩减 `cafeetje → café-tje`，匈牙利语 `hu` 双合字母折叠翻倍 `asszony → asz-szony`，加泰罗尼亚语 `ca` 飞点消除 `paral·lel → paral-lel`）。
- **显式软连字符优先与断词限制**：同一逻辑词内存在显式 `U+00AD` 时优先于自动词典断点；完整逻辑词统一遵循 `hyphenate-character`、`hyphenate-limit-chars`、`hyphenate-limit-lines`、`hyphenate-limit-last` 与 `hyphenate-limit-zone`。未折行时不产生拼写变化或连字符推进；缺少词典或校验失败时安全回退为不断词，不损坏原文、`SelectedText` 或命中偏移。

可运行用例与截图导出见 [CssHyphenationWordContextTests](../tests/Jalium.UI.MacOS.Tests/CssHyphenationWordContextTests.cs)。竖排、复杂脚本连接形式及跨平台实机与辅助技术验收仍需继续完成。

## 复杂脚本、字体变体组合与原生保留段落

macOS 原生（CoreText／Metal／Software）保留段落现已打通复杂脚本连接、双向重排、高级字体变体与排版组合，不再因启用大小写塑形、上下标或非 100% 字宽而退回逐片段拆分或合成兜底：

- **双向段落中的 `font-variant-position`、`font-variant-caps` 与 `font-width`**：单控件 `TextBlock`（含多 `Run`／`Span`）、带行内边框／内边距的行内盒（`HasCssInlineBoxes`）以及跨兄弟 `TextBlock` 的共享行内双向段落，均支持原生 OpenType `sups`／`subs`／`smcp`／`c2sc` 与合成大小写／上下标的逐字符回退（含一对多大小写展开 `ß → SS`、`İ → İ` 与逐范围 `PositionRunAt` 切片），并保留精确百分比 `font-stretch`／`font-width`（如 `83.2%`）。原生 `CreateApplePositionText` 将双向隔离控制符（`U+2066..U+2069`、`U+202A..U+202E`、`U+200E..U+200F`、`U+061C`）与软连字符（`U+00AD`）视为空白／格式控制符，避免误触发合成上下标回退。
- **跨来源复杂脚本连接、连字内部光标与组合标记**：跨不同颜色或样式的 `Run`、行内盒与兄弟 `TextBlock` 保持阿拉伯文连写形式（`مرحبا`）及显式 `ZWJ`／`ZWNJ` 连接控制，同时保留拉丁连字（`ffi`）的连字内部比例光标与组合附加符（`a + \u0301`）的扩展字素簇边界。
- **行内盒与文档视图中的完整字体状态传播**：行内盒代理、`::first-line`、`::first-letter` 与多列／FlowDocument 段落视图通过 `CopyCssFontState` 与 `DocumentFontSource` 完整继承 `FontStretchPercentage`、`FontStyleComputed`（`oblique <angle>`）、`FontFamilyComputed`、`font-optical-sizing`、`font-size-adjust` 以及文档作用域内的 `@font-face`（含 `ascent-override`、`descent-override`、`line-gap-override`、`size-adjust`、`unicode-range`）与 `@font-feature-values`。
- **行内盒与隔离边界下的选区、命中与录制回放**：`HasCssInlineBoxes` 激活时，`GetCharacterRect`、`GetSelectionGeometry` 与 `GetPositionFromPoint(point, snapToText: false)` 均按共享双向段落的真实字素几何与盒距离优先级解析，工作线程录制回放与主线程渲染逐像素一致。

可运行用例与截图导出见 [CssComplexShapingFontCombinationsTests](../tests/Jalium.UI.MacOS.Tests/CssComplexShapingFontCombinationsTests.cs)。Linux 自研文本引擎的复杂阿拉伯连写与 GDEF 光标、竖排以及 Windows／Linux 实机与辅助技术验收仍需继续完成。

## 尺寸确定性与布局边界

水平书写模式下的块流、浮动、Flex、Grid／Subgrid、绝对定位与单子级内容宿主现已统一尺寸确定性（Definite vs. Indefinite Sizing）与外边距折叠边界：

- **垂直外边距逃逸与 `min-height`／`max-height`／内在高度边界**：按 CSS 2.2 §8.3.1 与 §10.7，仅当父块为 `height: auto`（非 `min-content`／`max-content`／`fit-content` 内在首选高度）且试探内容高度未被 `min-height` 或 `max-height` 钳制时，最末流内子项的底部折叠外边距才逃逸为父级下外边距；一旦触发 `min-height`／`max-height` 钳制或父级声明了内在首选高度，尾部外边距截断在父盒内部。前置自折叠空兄弟的顶部外边距在后续兄弟因 `clear` 产生实际清除偏移时仍照常逃逸至父级顶部（无论是否处于浮动上下文中）。
- **循环百分比、`calc-size()` 与 `fit-content(<length-percentage>)`**：当父容器在对应轴上按内在尺寸探测（`CssIntrinsicPercentage.Cyclic`）或块轴高度未确定时，子项在 `calc-size(any, size + 50%)` 或 `fit-content(50%)` 中的百分比基准遵循 CSS Sizing 3／4 循环百分比规则：`min-width`／`min-height` 中的循环百分比按 `0px` 解析，首选尺寸与 `max-*` 中的循环百分比回退为内在内容尺寸，消除测量顺序依赖与循环膨胀。
- **Flex 跨轴确定性与拉伸二次排版**：多行（`flex-wrap: wrap`）或交叉轴不定高的行向 `FlexPanel` 在求取项目假想交叉尺寸时不把容器可用高度或 `max-height` 误作子项的确定百分比基准；单行确定高容器或显式高度子项保持确定高度。拉伸对齐（`align-self: stretch`）的 Flex 子项在交叉轴尺寸确定后执行 Flexbox §9.8 二次测量，使其内部百分比高度子项与绝对定位后代获得拉伸后的确定高度；列向 `FlexPanel` 被父级拉伸时同步激活主轴百分比基准并在包含块约束往返时精确失效缓存。
- **Grid 跨轨内在最小分配与 Subgrid 双遍缓存**：跨多轨项目的内在最小贡献在同时跨越有限上限轨（如 `minmax(auto, 40px)`）与 `auto` 轨（`minmax(auto, auto)`）时，先将有限上限轨封顶于其最大轨道上限，再将其余需求分配给 `auto` 轨；`subgrid` 在主网格列宽确定后的第二遍纵向测量中保留第一遍采集的 `ColumnContributions`／`RowContributions`。
- **绝对定位上下 inset 确定高度与单子级原生宿主统一**：在确定高度包含块中同时指定 `top` 与 `bottom` 的 `position: absolute | fixed` 元素具有确定高度，其内部百分比高度后代与自身 `min/max` 钳制按最终包含块 padding box 解析。`CssDisplayMode.Native` 下的 `Border`、`ContentControl`、`ContentPresenter` 与模板化 `Control` 与多子级面板共享同一套流外跳过、绝对定位包含块（padding box）及不定高百分比阻断语义。

可运行用例、约束往返、录制回放与离屏截图核验见 [CssSizingDeterminismBoundariesTests](../tests/Jalium.UI.MacOS.Tests/CssSizingDeterminismBoundariesTests.cs)。竖排逻辑轴（T4）及跨平台实机验收仍需继续完成。

## 表格约束与表格宿主

普通控件（`display: table`／`inline-table`）、`FlowDocument` 原生 `Table` 与 `RichTextBox` 中的文档表格共享同一套轨道约束与装饰规则，同时保持各自的宿主语义：

- **复杂错误嵌套、递归匿名盒与可折叠空白省略**：当普通元素混排产生错误嵌套（如 `table` 下直接放置 `table-cell`、普通块与嵌套 `table-row`）时，`CssTableLayout` 自动生成并递归维护 `CssTableAnonymousBox` 与内嵌匿名表格，不修改公开 `Children` 或文档节点；表格部件之间的纯可折叠空白文本按 CSS Table §2.1 自动省略，不会生成多余空列或空行；动态切换 `display` 或重新挂载时自动释放并重建匿名结构。
- **列与列组 `min-width`／`max-width`、重叠百分比跨度与非线性函数**：`display: table-column`（`<col>`）、`display: table-column-group`（`<colgroup>`）与 `FlowDocument` `TableColumn` 的 `min-width`／`max-width` 同时约束有单元格列、空列与尾随列；跨多列百分比单元格（含所跨列均已有百分比请求的场景）与 `clamp()`／`min()`／`max()` 非线性百分比在内在宽度（不动点二分求解）与使用宽度下均收敛一致，且嵌套表格在父级内在探测期间保持循环百分比隔离。
- **显式高单元格／表格双遍百分比重排与 `rowspan > 1` 基线对齐**：当表格或行／单元格具有确定高度时，单元格内容在行高确定后按单元格未偏移内容盒高度执行第二遍测量，使内部百分比高度与 `stretch` 子项获得准确高度并正确应用 `vertical-align: middle | bottom`；`rowspan > 1` 单元格仅在对齐方式为 `baseline` 且包含真实首行文本基线时参与起始行的基线撑高。
- **Caption 独立几何与跨轨连续装饰**：`table-caption` 在 `FlowDocument` 中记录独立表格几何并可与表体独立分页；表格自身的 `box-shadow`（含 `inset`）、`outline`、`Effect` 与 `BackdropEffect` 锚定在不含 caption 的表格网格框（`GridBounds`）上；跨多列或多行的 `colgroup`、`thead`、`tbody`、`tfoot`、`tr` 背景、`border-image` 与折叠边框跨所占轨道连续绘制。
- **三类表格宿主的可访问性与编辑边界**：普通控件通过 CSS `display: table*` 布局时保留各自控件的 `AutomationPeer`，不会自动伪造原生 `Table` 可访问结构；`FlowDocumentPresentation` 与 `RichTextBox` 完整保留 `FlowDocument` 原生 `Table`／`TableRowGroup`／`TableRow`／`TableCell` 结构与实时编辑同步。

可运行用例、录制回放与离屏截图核验见 [CssTableConstraintsAndHostsTests](../tests/Jalium.UI.MacOS.Tests/CssTableConstraintsAndHostsTests.cs)。

## 多列、分页与打印样式

多列容器（`column-count`／`column-width`）与文档分页／打印布局（`FlowDocumentPresentation`、`IDocumentPaginatorSource`）现已贯通 `@page` 规则、命名页、重复表头表尾与装饰分片：

- **`@page` 规则、页边盒、命名页与 `@media print`**：支持 `@page`、命名页（`@page wide` + 元素级 `page: wide`）、伪页选择器（`:first`、`:left`、`:right`、`:blank`）及 16 个页边盒（`@top-left`、`@top-center`、`@bottom-right` 等），支持 `size`（`a4`、`letter`、自定义宽高、`portrait`／`landscape`）、`page-orientation`（`upright`、`rotate-left`、`rotate-right`）、`margin`、`bleed`、`marks`、页盒背景／边框以及页边盒中的 `counter(page)`／`counter(pages)`。元素 `page` 名称变化时自动触发分页，`break-before: recto | verso | left | right` 需要跳页时自动生成应用 `:blank` 样式的空白页。CSS Regions 属性（`flow-into`、`flow-from`、`region-fragment`）明确标记为 `Unsupported`，不与多列或文档分页混算。
- **任意深度列平衡、匿名列盒百分比高度与 `column-span: all` 空间不足处理**：确定高度多列容器的直接子项以匿名列盒高度作为百分比 `height`、`min-height`、`max-height` 的确定包含块基准；当当前页或列段剩余纵向空间不足以容纳 `column-span: all` 标题（或当标题声明 `break-after: avoid` 且剩余空间不足以同时容纳标题与后续块受 `orphans` 限制的最小首片）时，普通多列容器将该跨列块降级为列内常规流块，分页文档则将其整体推进至下一页／下一列段，避免负列高与内容重叠。
- **跨页与跨列重复表头表尾**：`FlowDocument` 表格中的 `display: table-header-group`（`thead`）与 `display: table-footer-group`（`tfoot`）在各自高度不超过分片容器高度 `25%` 且当前分片至少包含一行表体行时，跨页、跨列自动重复渲染表头与表尾；自然表尾与最后一行表体保持同页／同列绑定，且源 `Table.RowGroups`、`Rows`、`Cells` 集合与对象身份保持唯一、零克隆污染。
- **`box-decoration-break: slice | clone` 与跨分片裁剪**：跨列或跨页拆分的块盒支持 `slice`（默认，截断非首尾分片的连接边与圆角、按整体盒长连续偏移渐变背景、按开口裁剪阴影）与 `clone`（每个分片独立绘制完整四边边框、圆角、背景与 `box-shadow`）；列与页裁剪边界自动纳入 `box-shadow`、`outline` 与 `Effect` 的墨水外扩，并在增删文本或页面尺寸往返后精确更新分片缓存。

可运行用例、录制回放与离屏截图核验见 [CssMultiColumnPaginationAndPrintTests](../tests/Jalium.UI.MacOS.Tests/CssMultiColumnPaginationAndPrintTests.cs)。

## 定位、跨层级层叠上下文与 CSS 3D 变换

绝对定位、固定定位、层叠上下文（Stacking Context）与 CSS Transforms Level 2 三维变换现已统一贯通布局、跨层级绘制排序与逆投影命中测试：

- **任意宿主、自动 inset 静态位置、自动外边距居中与包含块链**：
  - `position: absolute` 与 `position: fixed` 支持全部单子级与多子级容器（`Border`、`ContentControl`、`ContentPresenter`、模板化 `Control`、`StackPanel`、`Grid`、`FlexPanel`、`DockPanel`、`WrapPanel`、`Canvas`、`UniformGrid`、`CssFlowLayout`、`CssGridLayout`、`CssTableLayout`、`CssMultiColumnLayout` 及行内上下文），流外子项不占用 `Canvas`／`UniformGrid` 等原生容器的常规槽位。
  - 当 `left`/`right` 或 `top`/`bottom` 为 `auto` 时，静态位置计入前置流内兄弟的累积尺寸、RTL 方向及 Flex／Grid 静态对齐；当 `left` + `right` + `width`（或 `top` + `bottom` + `height`）同时确定时，自动求解 `margin-left`/`margin-right`/`margin-top`/`margin-bottom: auto` 居中与单侧分配。
  - 包含块链完整遵循 CSS Positioned Layout 3 与 CSS Transforms 2：`position: absolute` 查找最近的定位祖先或建立固定定位包含块的祖先；`position: fixed` 查找最近的 `transform`、`perspective`、`transform-style: preserve-3d`、`filter`、`backdrop-filter` 或 `container-type: size | inline-size` 祖先（padding box），若无则锚定视口。绝对与固定定位后代自动越过包含块内部未定位祖先的 `overflow: hidden`／滚动裁剪，同时保留包含块自身及其上层祖先的裁剪与 `clip-path`。
- **跨层级层叠上下文与负 `z-index`**：
  - 跨越非层叠上下文中间节点（`position: static` 或 `position: relative; z-index: auto`）的定位后代统一提升至所属层叠上下文根（`PaintOwner`）参与排序与绘制，不同子树分支中的定位元素按各自的 `z-index` 与文档序比较前后关系。
  - 负 `z-index`（`z-index < 0`）后代在层叠上下文根自身的背景／边框之后、流内非定位块级子项之前绘制，并在命中测试中保持对称优先级。
  - 非 Flex／Grid 子项的 `position: static` 元素声明 CSS `z-index` 时不激活绘制重排或建立层叠上下文（`GetComputedStyle` 仍返回声明的整数值，XAML 本地 `Panel.SetZIndex` 保持兼容）。
- **CSS 3D 变换、透视、`preserve-3d`、背面剔除与逆投影命中**：
  - 完整支持 `perspective`、`perspective-origin`、`transform-style`（`flat | preserve-3d`）、`backface-visibility`（`visible | hidden`）、三值 `transform-origin`（`x y z`，默认中心 `50% 50% 0px`）以及全部三维变换函数（`translate3d`、`translateZ`、`scale3d`、`scaleZ`、`rotate3d`、`rotateX`、`rotateY`、`rotateZ`、`perspective()`、`matrix3d()`），底层直接复用 `Jalium.UI.Media.Media3D`（`Matrix3D`、`Quaternion`、`Point3D`、`Point4D`、`Vector3D`）。
  - `transform-style: preserve-3d` 跨层级收集三维平面并按相机空间 $Z$ 深度排序绘制，`transform-style: flat` 将子平面压平至宿主局部平面，`backface-visibility: hidden` 在变换后法线背向观察者（如 `rotateY(180deg)`）时自动剔除绘制与命中。
  - 三维过渡与动画通过 `Matrix3D` 极分解与四元数 `Slerp` 插值；`HitTest`、`InputHitTest`、`TransformToVisual`、`TransformToAncestor` 与 `TranslatePoint` 均接入射线—平面逆投影（`TryInverseProjectPoint`），确保透视变形后的视觉像素与点击命中、坐标转换一致。
  - 二维和三维消费者读取同一有效原点：原生变换保持框架默认 `(0,0)`，CSS 变换／关键帧／过渡在无作者原点时使用参考盒中心；本地值、当前值、绑定和原点动画继续优先。`content-box`／`fill-box` 包含不对称内边距、边框和百分比包含块宽度。相同变换对象只切换来源也会刷新；取消 CSS 过渡恢复当前基础值。实际范围及限制见 [变换原点验证](css-transform-origins-validation-20261028.md)。

可运行用例、双窗口模式（`native`／`withoutborder`）1x/2x 离屏截图与逆投影命中核验见 [CssPositionedStackingAnd3DTests](../tests/Jalium.UI.MacOS.Tests/CssPositionedStackingAnd3DTests.cs)。

## 垂直书写、逻辑轴、Ruby 注音与 CSS 锚点定位

- **书写模式、字形方向与縦中横（`writing-mode` / `text-orientation` / `text-combine-upright`）**：
  - 支持 `writing-mode: horizontal-tb | vertical-rl | vertical-lr | sideways-rl | sideways-lr`（含 SVG 1.1 别名 `lr`、`lr-tb`、`rl`、`rl-tb`、`tb`、`tb-rl`）、`text-orientation: mixed | upright | sideways | sideways-right` 与 `text-combine-upright: none | all | digits <integer>?`（默认 `digits 2`，范围 `2..4`）。
  - `CssLogicalBoxEdges` 的 36+ 项逻辑边、逻辑尺寸（`inline-size`／`block-size`／`min-*`／`max-*`）、逻辑 inset（`inset-block-*`／`inset-inline-*`）与逻辑圆角（`border-start-start-radius` 等）同时依据 `direction` 与 `writing-mode` 解析为物理属性，并支持逻辑坐标与物理坐标的双向转换。
- **Ruby 注音布局（`display: ruby*` / `ruby-position` / `ruby-align` / `ruby-merge`）**：
  - 当前真实消费者支持 `display: ruby | ruby-base | ruby-text | ruby-base-container | ruby-text-container`、`ruby-position`（`over`、`under`、`alternate`、`alternate over`、`alternate under`、`inter-character`）、`ruby-align`（`space-around`、`center`、`start`、`space-between`）与 `ruby-merge`（`separate`、`merge`、`auto`）；当前消费者按基字层行距和必要 leading 处理普通行间注音。
  - `merge` 在同一行内共享基字宽度，断行后保留原始配对；`auto` 在注音都能独立容纳时保持独立，否则合并未被隐藏项分隔的序列。相同原文的注音按原始文字内容自动隐藏；`merge` 禁用该隐藏及注音自身的 `visibility: collapse`，作者属性和原始文本不变。旧 C# `Collapse` 枚举别名保留，CSS `ruby-merge: collapse` 不再接受。
  - 普通基字／行间注音的内容盒使用字体 ascent＋descent，注音自身的 line-height 不撑大内容盒，作者属性仍保留；基字／宿主行距在必要时增加 leading。侧边 inter-character 作为基字层内的 inline-block 贡献完整高度，并保留自己的行高。结构 rbc／rtc 忽略 margin／padding／border 与不适用尺寸，普通行内 Ruby 不使用 CSS 宽高／min／max 替代文字内容；原生本地尺寸优先级另行保留。
  - 完整 Ruby 盒模型、rb／rt 边缘、intrinsic／百分比、全部基线、匿名／空白规则、复杂 leading／overhang、方向／分配、文档／编辑器及跨平台仍开放。行盒能力见 [内容盒与行距验证](css-ruby-box-lines-validation-20261028.md)，其默认原点命中缺陷的后续修复与当前验收见 [变换原点验证](css-transform-origins-validation-20261028.md)。
- **CSS Anchor Positioning Level 1（`@position-try` / `anchor()` / `anchor-size()`）**：
  - 支持 `anchor-name`、`anchor-scope`、`position-anchor`、`position-area`（及 `inset-area` 别名，支持 3×3 物理与逻辑网格跨度）、`position-try-fallbacks`、`position-try-order`、`position-try` 简写、`position-visibility`（`always`、`anchors-valid`、`anchors-visible`、`no-overflow`）、`@position-try --name { ... }` 规则以及 `anchor()` 与 `anchor-size()` 函数，并在预编译二进制样式表中完整保留往返语义。

可运行用例与双窗口模式 1x/2x 截图见 [CssWritingModesRubyAndAnchorTests](../tests/Jalium.UI.MacOS.Tests/CssWritingModesRubyAndAnchorTests.cs)。

## 文档盒投影、自绘宿主、多层高亮伪元素与结构化编辑

- **文档行内盒、自绘宿主与非视觉节点替换（D1）**：
  - `FlowDocument` 中的 `Run`、`Span` 与 `InlineUIContainer` 支持块级／Flex／Grid／Table `display`、`float`、`position: absolute | fixed`、`::before`／`::after` 生成内容及单图片 `content: url(...) / "alt"` 替换渲染，公开 `Inlines`、`Blocks`、`Child` 与 `Parent` 保持不变。
  - `AccessText`、`Label`、`GroupBox`、`StatusBarItem` 接入生成内容宿主协议；`::marker` 支持 `marker-side: match-self | match-parent`、复杂预定义计数器与 `@counter-style` 的 `speak-as` 无障碍朗读输出。
- **CSS Custom Highlight API、多层高亮伪元素与 WritingTools 编辑保护（D2）**：
  - 支持 `::selection`、`::target-text`、`::search-text`（含 `:current`）、`::spelling-error`、`::grammar-error` 与 `::highlight(<custom-ident>)` 及 `CssHighlightRegistry` 优先级层叠绘制，支持 SVG 几何 `fill`／`stroke`／`stroke-width` 高亮覆盖与替换内容选区半透明遮罩。
  - `RichTextBox` 在可见偏移编辑、WritingTools 改写、Undo／Redo、表格 `Tab`／`Shift+Tab` 导航与剪贴板导出中自动保护 `display: none` 隐藏节点、`visibility: collapse` 单元格与 `InlineUIContainer` 嵌入控件。

可运行用例与双窗口模式 1x/2x 截图见 [CssDocumentGeneratedContentAndHostsTests](../tests/Jalium.UI.MacOS.Tests/CssDocumentGeneratedContentAndHostsTests.cs) 与 [CssSelectionHighlightAndEditingTests](../tests/Jalium.UI.MacOS.Tests/CssSelectionHighlightAndEditingTests.cs)。

## 行内合成组、边框区背景裁剪、图像函数与动画时间轴

- **行内合成管线、`background-clip: border-area` 与图像源扩展（P1 / P2）**：
  - 文档 `Run`／`Span` 及跨行行内分片支持独立 `mix-blend-mode`、`filter`、`clip-path`、`mask-image`、`mask-border` 与 `box-decoration-break: slice | clone`；`background-clip` 支持 `border-area` 及 `border-area text` 联合值；`GlyphRun.BuildGeometry()` 提取真实字体轮廓。
  - `image-set()`（含 `type()` MIME 探测）、`cross-fade()`、`element(#id)`（`CssElementSourceRegistry`）与 `paint(name, ...)`（`CssPaintWorkletRegistry`）贯通至全部 CSS 图像消费属性；SVG `vector-effect` 支持 `non-scaling-size`、`non-rotation`、`fixed-position` 及跨文档 `<marker>` 引用。
- **非视觉文档节点过渡、滚动／视口时间轴、Motion Path 与视图转场（M1）**：
  - `Run`、`Span`、`Paragraph` 支持连续属性与 `transition-behavior: allow-discrete` 离散属性过渡；关键帧支持 `animation-composition: replace | add | accumulate` 与 `@starting-style`。
  - 支持 `animation-timeline`、`scroll-timeline`、`view-timeline`、`timeline-scope`、`animation-range` 滚动／视口进度驱动动画，`offset-path`／`offset-distance`／`offset-rotate`／`offset-anchor`／`offset-position` 运动路径，以及 `view-transition-name`／`view-transition-class` 与 `CssViewTransitionHost`。
  - CSS 布局盒的运动锚点使用 `transform-box` 参考矩形：`content-box`／`fill-box` 包含实际 padding 与 border 的内缩，`border-box`／`stroke-box`／`view-box` 使用边框盒。显式位置相对于该矩形解析，`auto` 跟随作者的有效变换原点；没有作者贡献时取参考盒中心。原生默认 `(0,0)` 属性值不被写成 CSS 默认中心，本地原点、当前值、绑定和动画仍按各自来源工作。
  - `offset-anchor`／`offset-position` 复用已有 `<position>` 语法，包括 `top`、`bottom right`、`right 10px bottom 20px` 与 `calc()`。显式位置之间的关键帧／过渡保留百分比和相对单位，尺寸、字体、容器或视口变化后按当前上下文解析；`auto` 与显式位置之间的关键帧保留离散语义。`offset-distance` 的混合单位使用实际路径长度。
  - `ray()` 可指定 `at <position>`，省略时使用有效 `offset-position`；圆／椭圆省略 `at` 也可使用显式 `offset-position`。圆／椭圆的等价路径从最右侧起步，沿顺时针方向前进，25% 在底部。当前椭圆使用 128 段弧长近似；全部路径坐标盒、SVG 路径引用、形状插值与跨平台验收仍需继续，详见 [运动路径参考盒验证](css-motion-reference-boxes-validation-20261028.md)。
  - macOS Software／Metal 的持续缓存帧按实际 DPI、物理像素相位及镜像方向管理捕获；整数物理像素位移可复用，分数相位变化重新捕获，负缩放保留字形方向。该行为同时覆盖普通原生变换和 CSS 路径。当前严格像素证据限定于已验证的透明目标及用例，不透明背景和原生 Metal 窗口的局部 RGB 差异仍开放，见 [连续帧缓存验证](css-retained-raster-fidelity-validation-20261028.md)。

可运行用例与双窗口模式 1x/2x 截图见 [CssPaintingImagesAndSvgTests](../tests/Jalium.UI.MacOS.Tests/CssPaintingImagesAndSvgTests.cs) 与 [CssAnimationsTimelinesAndMotionPathTests](../tests/Jalium.UI.MacOS.Tests/CssAnimationsTimelinesAndMotionPathTests.cs)。

## 滚动吸附与锚定、Containment 按需呈现与原生 UI 属性

- **CSS Scroll Snap、逻辑滚动边距与滚动锚定（R1）**：
  - 支持 `scroll-snap-type`、`scroll-snap-align`、`scroll-snap-stop`、物理与逻辑 `scroll-padding-*`／`scroll-margin-*`、`scroll-behavior`、`overscroll-behavior-*` 嵌套滚动链隔离，以及 `overflow-anchor: auto | none` 滚动锚定补偿。
- **CSS Containment 与 `content-visibility`（R2）**：
  - 支持 `contain: none | strict | content | [size || inline-size || layout || style || paint]`、`contain-intrinsic-size`（含 `auto <length>` 记忆尺寸）与 `content-visibility: visible | auto | hidden`（含后代获得焦点时按需显现与计数器作用域隔离）。
- **原生 UI 外观、强调色、缩放与 URL 光标（R3）**：
  - 支持 `appearance`（`none` 剥离默认边框外观）、`accent-color`、`color-scheme`、`resize`（含逻辑轴 `block`／`inline`）、`touch-action`（同步 `ScrollViewer.PanningMode`）、`forced-color-adjust`、`print-color-adjust`、`text-size-adjust`、`zoom` 布局缩放及 `cursor: url(...) x y, fallback` 热区与回退链。

可运行用例与双窗口模式 1x/2x 截图见 [CssScrollContainmentAndNativeUiTests](../tests/Jalium.UI.MacOS.Tests/CssScrollContainmentAndNativeUiTests.cs)。
