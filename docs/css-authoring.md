# CSS 属性与值元数据

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
