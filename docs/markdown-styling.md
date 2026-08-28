# Markdown 控件样式定制

`Markdown` 把解析出来的每个块渲染成一个带默认 `ControlTemplate` 的控件，而不是在
`OnRender` 里把外观画死。定制方式与 `ComboBoxItem` 一致：换 `Style`、换模板，或者只覆盖
几个属性。

对应实现：

- `src/managed/Jalium.UI.Controls/Markdown.cs`
- `src/managed/Jalium.UI.Controls/MarkdownBlockPresenters.cs`
- `src/managed/Jalium.UI.Controls/MarkdownTextPresenter.cs`
- `src/managed/Jalium.UI.Controls/MarkdownCodeTextPresenter.cs`
- `src/managed/Jalium.UI.Controls/MarkdownInlineStyle.cs`
- `src/managed/Jalium.UI.Controls/Themes/Controls/Markdown.jalxaml`

## 块与控件的对应关系

| Markdown 结构 | 承载控件 | 容器样式属性 |
| --- | --- | --- |
| 标题 `#`…`######` | `MarkdownHeadingPresenter` | `Markdown.HeadingStyle` |
| 段落 | `MarkdownParagraphPresenter` | `Markdown.ParagraphStyle` |
| 块引用 `>` | `MarkdownQuotePresenter` | `Markdown.QuoteStyle` |
| 围栏代码块 | `MarkdownCodePresenter` | `Markdown.CodeBlockStyle` |
| mermaid 代码块 | `MarkdownDiagramPresenter` | `Markdown.DiagramStyle` |
| 列表 | `MarkdownListPresenter` | `Markdown.ListStyle` |
| 列表项 | `MarkdownListItemPresenter` | `Markdown.ListItemStyle` |
| 表格 | `MarkdownTablePresenter` | `Markdown.TableStyle` |
| 表格单元格 | `MarkdownTableCellPresenter` | `Markdown.TableCellStyle` |
| 独占一行的图片 | `MarkdownImagePresenter` | `Markdown.ImageStyle` |
| 脚注定义 `[^n]:` | `MarkdownFootnotePresenter` | `Markdown.FootnoteStyle` |
| 分隔线 `---` | `MarkdownRulePresenter` | `Markdown.RuleStyle` |

它们都派生自 `MarkdownBlockPresenter`（本身是 `ContentControl`），块的正文放在
`Content` 里，由模板中的 `ContentPresenter` 呈现。行内文本由
`MarkdownTextPresenter` 排版，代码块正文由 `MarkdownCodeTextPresenter` 排版。

图片分两种落法：`![](shot.png)` 独占一行时提升成块级的 `MarkdownImagePresenter`
（按容器宽度自适应、可带图注）；与文字混排的 `文字 ![icon](i.png) 文字` 留在
`MarkdownTextPresenter` 里跟着文字排，由它内嵌一个 `Image` 元素承载。

## 三种定制入口

### 1. 隐式样式（作用于整个应用）

```xml
<Style TargetType="MarkdownHeadingPresenter">
    <Setter Property="Foreground" Value="{ThemeResource AccentTextFillColorPrimaryBrush}" />
    <Setter Property="SeparatorThickness" Value="0" />
</Style>
```

### 2. 容器样式属性（只作用于一个 `Markdown` 实例）

语义与 `ItemsControl.ItemContainerStyle` 相同。显式样式叠在主题样式**之上**，
所以只需要写想改的 setter，模板与其余默认值仍来自主题：

```xml
<Markdown Text="{Binding Article}">
    <Markdown.QuoteStyle>
        <Style TargetType="MarkdownQuotePresenter">
            <Setter Property="BorderBrush" Value="#F59E0B" />
            <Setter Property="BorderThickness" Value="3,0,0,0" />
            <Setter Property="Background" Value="Transparent" />
        </Style>
    </Markdown.QuoteStyle>
</Markdown>
```

需要按块动态分派时用 `Markdown.BlockContainerStyleSelector`；`SelectStyle` 收到的
`item` 与 `container` 都是待应用样式的 presenter，可以从
`MarkdownBlockPresenter.BlockKind` 或具体类型（如 `MarkdownHeadingPresenter.Level`）判断。
选择器返回非 `null` 时优先于固定的容器样式属性。

### 3. 整体替换模板

容器样式里给 `Template` 一个新的 `ControlTemplate` 即可。模板可用的部件属性见下节。

## 各控件的可模板化属性

- `MarkdownHeadingPresenter`：`Level`、`HasSeparator`、`SeparatorBrush`、`SeparatorThickness`
- `MarkdownCodePresenter`：`Code`、`CodeLanguage`、`ShowLineNumbers`、`LineNumberForeground`、
  `GutterBackground`、`GutterSeparatorBrush`、`CodePadding`
- `MarkdownCodeTextPresenter`（代码正文部件）：`CodeLineHeightRatio` 单独控制代码行距，
  与正文可继承的 `LineHeightRatio` 互不影响
- `MarkdownListPresenter`：`IsOrdered`、`StartIndex`
- `MarkdownListItemPresenter`：`Marker`（只读，算出来的标记文本）、`MarkerKind`、`ItemNumber`、
  `IsChecked`、`IsLastItem`、`BulletGlyph`、`NumberFormat`、`TaskCheckedGlyph`、
  `TaskUncheckedGlyph`、`MarkerWidth`、`MarkerMargin`、`MarkerForeground`
- `MarkdownTablePresenter`：`RowCount`、`ColumnCount`
- `MarkdownTableCellPresenter`：`IsHeaderCell`、`RowIndex`、`ColumnIndex`、`ColumnAlignment`
  （来自 GFM 分隔行里的冒号，默认模板把它绑到内容的 `HorizontalAlignment`）
- `MarkdownImagePresenter`：`Source`、`Alt`、`ImageTarget`、`Caption`、`CaptionForeground`、
  `HasSource`、`HasCaption`（后两个是只读的，默认模板用它们在图片与替代文本之间切换、决定要不要显示图注行）
- `MarkdownFootnotePresenter`：`Number`、`Label`、`Marker`（只读）、`MarkerForeground`
- 所有块：`IsNested`（是否嵌在列表项/引用里，默认样式据此收紧间距）

换掉无序列表的项目符号不需要改模板，改字形就行：

```xml
<Style TargetType="MarkdownListItemPresenter">
    <Setter Property="BulletGlyph" Value="→" />
    <Setter Property="MarkerWidth" Value="20" />
</Style>
```

## 行内文本样式

行内部分分两层。

**主题色**用可继承的画刷属性表达，能正常参与 `{ThemeResource}` 的深浅色切换：

- `LinkForeground`
- `InlineCodeForeground`
- `InlineCodeBackground`
- `MonospaceFontFamily`
- `LineHeightRatio`
- `SelectionBrush`

这些属性定义在 `MarkdownTextPresenter` 上，并被 `Markdown` 与
`MarkdownBlockPresenter` 共享，所以既能在 `<Style TargetType="Markdown">` 里一处设置，
也能在单个块的样式里覆盖。

**形态**用 `MarkdownInlineStyle` 表达，通过 `BoldStyle`、`ItalicStyle`、
`InlineCodeStyle`、`StrikethroughStyle`、`LinkStyle` 提供（同样可继承）：

```csharp
markdown.InlineCodeStyle = new MarkdownInlineStyle
{
    CornerRadius = new CornerRadius(9),
    Padding = new Thickness(7, 2, 7, 2),
    FontSizeRatio = 0.92,
};

markdown.LinkStyle = new MarkdownInlineStyle
{
    Decorations = MarkdownTextDecorations.None,
    FontWeight = FontWeights.SemiBold,
};
```

`MarkdownInlineStyle` 的每个属性都带“未设置”语义（引用类型与可空值类型为 `null`，
`FontSizeRatio` 为 `double.NaN`），未设置的部分沿用块继承下来的排版值，
因此只写想改的项即可。实例按不可变配置使用：赋值之后再改它的属性不会触发重排。

字号缩放走 `FontSizeRatio`（相对继承字号的倍数）而不是绝对字号，标题正是靠它按级别放大，
同时跟随 `Markdown.FontSize` 的基准值。

## 字体与前景怎么流下来

`MarkdownTextPresenter` 的 `FontFamily` / `FontSize` / `FontWeight` / `FontStyle` /
`Foreground` 全部是可继承的依赖属性，与 `TextElement` 共享。因此在块级样式里设置字体，
就直接作用到块里的文字：

```xml
<Style TargetType="MarkdownHeadingPresenter">
    <Setter Property="FontFamily" Value="Georgia" />
    <Setter Property="FontSizeRatio" Value="1.8" />
</Style>
```

## 需要注意的优先级

默认样式用 `Style.Triggers` 按 `Level` 驱动标题的字号、行高与分隔线开关。样式引擎里
trigger 高于普通 setter，所以要关掉一级标题的分隔线，用
`<Setter Property="SeparatorThickness" Value="0" />`（trigger 不碰它），
或者在自己的样式里写同样条件的 trigger 覆盖。

## 从旧 API 迁移

旧的零散画刷属性已从 `Markdown` 上移除，改由容器样式或可继承的行内属性承担：

| 旧属性 | 现在的写法 |
| --- | --- |
| `Markdown.LinkForeground` | 同名属性仍在，改为可继承（也能在块级样式里覆盖） |
| `Markdown.CodeBackground` | 行内代码用 `InlineCodeBackground`；代码块用 `MarkdownCodePresenter` 的 `Background` |
| `Markdown.CodeLineNumberForeground` | `MarkdownCodePresenter.LineNumberForeground` |
| `Markdown.CodeGutterBackground` | `MarkdownCodePresenter.GutterBackground` |
| `Markdown.QuoteBackground` | `MarkdownQuotePresenter.Background` |
| `Markdown.QuoteBorderBrush` | `MarkdownQuotePresenter.BorderBrush` |
| `Markdown.HeadingSeparatorBrush` | `MarkdownHeadingPresenter.SeparatorBrush` |
| `Markdown.TableBorderBrush` | `MarkdownTableCellPresenter.BorderBrush`、`MarkdownRulePresenter.Background` |
| `Markdown.TableHeaderBackground` | `MarkdownTableCellPresenter` 上 `IsHeaderCell` 触发器的背景 |

`MarkdownCodeBlockView` 更名为 `MarkdownCodeTextPresenter` 并公开，属性
`Text` / `Language` 更名为 `Code` / `CodeLanguage`。

## 滚动

滚动视图是模板的一部分（`PART_ScrollViewer`），所以应用级的 `<Style TargetType="ScrollViewer">`
对它同样生效，滚动条的自动隐藏、惯性、覆盖式样式等行为与应用里其它 `ScrollViewer` 完全一致。

`Markdown` 上只有两个滚动相关属性：`HorizontalScrollBarVisibility`（默认 `Auto`，
因为宽表格和长代码行没法换行，横向滚动是它们唯一的出路）与 `VerticalScrollBarVisibility`
（默认 `Auto`）。要换掉整个滚动行为就替换模板。

## 支持的语法

解析器对齐 CommonMark 与 GFM，细节见 [markdown-syntax.md](markdown-syntax.md)。
