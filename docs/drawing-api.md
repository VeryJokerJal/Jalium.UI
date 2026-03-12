# Jalium.UI 绘图 API 文档

本文档基于当前仓库源码整理，主要对应以下实现：

- `src/managed/Jalium.UI.Media/DrawingContext.cs`
- `src/managed/Jalium.UI.Interop/RenderTargetDrawingContext.cs`
- `src/managed/Jalium.UI.Interop/RenderTarget.cs`
- `src/managed/Jalium.UI.Media/Brush.cs`
- `src/managed/Jalium.UI.Media/ImageSource.cs`
- `src/managed/Jalium.UI.Media/BackdropEffect.cs`
- `src/managed/Jalium.UI.Media/RenderTargetBitmap.cs`

适用场景：

- 自定义控件 `OnRender`
- 绘制基础图形、文本、图片
- 使用裁剪、透明度、变换
- 使用背景特效、液态玻璃、过渡着色器

## 1. API 分层

Jalium.UI 的绘图接口主要分三层：

### 1.1 `DrawingContext`

高层通用绘图上下文，也是控件在 `OnRender(object drawingContext)` 中最常接触到的 API。

特点：

- 面向控件绘制逻辑
- 支持画线、矩形、圆角矩形、椭圆、文本、几何、图片
- 支持 `PushTransform`、`PushClip`、`PushOpacity` 和 `Pop`
- 支持背景特效 `DrawBackdropEffect`

### 1.2 `RenderTargetDrawingContext`

`Jalium.UI.Interop` 中的 GPU 实现，继承自 `DrawingContext`，在高层 API 之外额外提供：

- 过渡捕获与 GPU 过渡着色器
- 液态玻璃效果
- 元素特效捕获
- 透明打孔
- 缓存管理

如果你只写普通控件，通常先用 `DrawingContext` 即可；需要高级 GPU 效果时，再判断并转成 `RenderTargetDrawingContext`。

### 1.3 `RenderTarget`

更底层的 native 渲染目标封装，直接对应原生互操作层。

特点：

- 更接近底层 D2D/D3D 调用
- 由框架或高级渲染流程使用
- 适合做渲染基础设施，不适合普通控件直接依赖

## 2. 基础使用方式

在自定义控件中，通常重写 `OnRender`：

```csharp
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;

public sealed class DemoBadge : Control
{
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(0, 0, ActualWidth, ActualHeight);

        dc.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromRgb(27, 94, 32)),
            new Pen(new SolidColorBrush(Color.FromRgb(200, 230, 201)), 1),
            rect,
            new CornerRadius(10));

        var text = new FormattedText("READY", "Segoe UI", 14)
        {
            Foreground = new SolidColorBrush(Color.White)
        };

        dc.DrawText(text, new Point(10, 6));
    }
}
```

## 3. `DrawingContext` 核心接口

### 3.1 图形绘制

| 方法 | 说明 |
| --- | --- |
| `DrawLine(Pen pen, Point point0, Point point1)` | 绘制直线 |
| `DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)` | 绘制矩形 |
| `DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)` | 绘制统一圆角矩形 |
| `DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, CornerRadius cornerRadius)` | 绘制四角可不同的圆角矩形 |
| `DrawContentBorder(Brush? fillBrush, Pen? strokePen, Rect rectangle, double bottomLeftRadius, double bottomRightRadius)` | 绘制底部圆角内容边框 |
| `DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)` | 绘制椭圆 |
| `DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)` | 绘制几何图形 |
| `DrawImage(ImageSource imageSource, Rect rectangle)` | 绘制图片 |
| `DrawText(FormattedText formattedText, Point origin)` | 绘制文本 |
| `DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius)` | 绘制背景特效 |

说明：

- `brush` 为填充画刷，传 `null` 表示不填充。
- `pen` 为描边，传 `null` 表示不描边。
- `DrawRoundedRectangle(..., CornerRadius)` 在源码中会自动处理：
  - 四角都为 0 时退化为 `DrawRectangle`
  - 四角一致时走统一圆角重载
  - 四角不一致时转为 `PathGeometry`

### 3.2 状态栈

| 方法 | 说明 |
| --- | --- |
| `PushTransform(Transform transform)` | 压入变换 |
| `PushClip(Geometry clipGeometry)` | 压入裁剪 |
| `PushOpacity(double opacity)` | 压入透明度 |
| `Pop()` | 弹出最近一次压入的 transform/clip/opacity |
| `Close()` | 关闭绘图上下文 |

使用约定：

- 每次 `Push*` 后都应配对 `Pop()`
- `PushOpacity` 也是通过统一的 `Pop()` 弹出
- `DrawingContext` 实现了 `IDisposable`，`Dispose()` 会调用 `Close()`

示例：

```csharp
if (drawingContext is DrawingContext dc)
{
    dc.PushClip(new RectangleGeometry(new Rect(0, 0, 120, 120)));
    dc.PushTransform(new TranslateTransform { X = 20, Y = 10 });
    dc.PushOpacity(0.7);

    dc.DrawRectangle(
        new SolidColorBrush(Color.FromRgb(33, 150, 243)),
        null,
        new Rect(0, 0, 80, 80));

    dc.Pop();
    dc.Pop();
    dc.Pop();
}
```

## 4. 常用参数类型

### 4.1 `Brush`

基础画刷类型位于 `Jalium.UI.Media`：

- `SolidColorBrush`
- `LinearGradientBrush`
- `RadialGradientBrush`
- `ImageBrush`
- `DrawingBrush`
- `VisualBrush`

其中最常用的是：

```csharp
var fill = new SolidColorBrush(Color.FromRgb(0, 120, 215));
var stroke = new SolidColorBrush(Color.White);

var gradient = new LinearGradientBrush
{
    StartPoint = new Point(0, 0),
    EndPoint = new Point(1, 1)
};
gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0, 120, 215), 0));
gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0, 188, 212), 1));
```

补充说明：

- `LinearGradientBrush` 和 `RadialGradientBrush` 默认使用 `BrushMappingMode.RelativeToBoundingBox`
- 即坐标默认是相对于绘制区域的 0 到 1
- 当前 `RenderTargetDrawingContext` 的 native 画刷转换明确支持：
  - `SolidColorBrush`
  - `LinearGradientBrush`
  - `RadialGradientBrush`
- `ImageBrush`、`DrawingBrush`、`VisualBrush` 类型在媒体层中已定义，但当前 GPU 绘制实现没有像前三者那样直接映射到 native 画刷

### 4.2 `Pen`

`Pen` 用来定义描边：

```csharp
var pen = new Pen(new SolidColorBrush(Color.Black), 2)
{
    StartLineCap = PenLineCap.Round,
    EndLineCap = PenLineCap.Round,
    LineJoin = PenLineJoin.Round,
    DashStyle = DashStyle.Dash
};
```

主要属性：

- `Brush`
- `Thickness`
- `StartLineCap`
- `EndLineCap`
- `DashCap`
- `LineJoin`
- `MiterLimit`
- `DashStyle`

### 4.3 `FormattedText`

文本绘制使用 `FormattedText`：

```csharp
var text = new FormattedText("Hello Jalium", "Segoe UI", 16)
{
    Foreground = new SolidColorBrush(Color.White),
    FontWeight = 700,
    MaxTextWidth = 200,
    MaxTextHeight = 40,
    Trimming = TextTrimming.CharacterEllipsis
};

dc.DrawText(text, new Point(12, 8));
```

主要属性：

- `Text`
- `FontFamily`
- `FontSize`
- `Foreground`
- `MaxTextWidth`
- `MaxTextHeight`
- `FontWeight`
- `FontStyle`
- `FontStretch`
- `Trimming`

测量结果由内部写回：

- `Width`
- `Height`
- `LineHeight`
- `Baseline`
- `LineCount`

### 4.4 `ImageSource`

图片绘制使用 `ImageSource`，常见实现为 `BitmapImage`。

常用创建方式：

```csharp
var image1 = BitmapImage.FromFile("Assets/logo.png");
var image2 = BitmapImage.FromBytes(bytes);
var image3 = new BitmapImage(new Uri("https://example.com/image.png"));
```

绘制：

```csharp
dc.DrawImage(image1, new Rect(0, 0, 128, 128));
```

说明：

- `BitmapImage` 支持文件路径、字节数组、HTTP/HTTPS URI
- HTTP/HTTPS 加载是异步的，加载完成后会触发 `OnImageLoaded`

### 4.5 `Geometry`

`DrawGeometry` 接收 `Geometry` 体系对象，常见类型包括：

- `RectangleGeometry`
- `EllipseGeometry`
- `LineGeometry`
- `PathGeometry`
- `StreamGeometry`
- `GeometryGroup`
- `CombinedGeometry`

路径几何适合复杂图形和不规则轮廓。

### 4.6 `Transform`

常用变换类型：

- `TranslateTransform`
- `ScaleTransform`
- `RotateTransform`
- `SkewTransform`
- `MatrixTransform`
- `TransformGroup`
- `CompositeTransform`

示例：

```csharp
dc.PushTransform(new RotateTransform
{
    Angle = 15,
    CenterX = 50,
    CenterY = 50
});
```

## 5. `RenderTargetDrawingContext` 扩展接口

当需要高级 GPU 绘制能力时，可以先判断：

```csharp
if (drawingContext is Jalium.UI.Interop.RenderTargetDrawingContext rtdc)
{
    // 使用 GPU 扩展能力
}
```

### 5.1 过渡捕获与着色器

| 方法 | 说明 |
| --- | --- |
| `BeginTransitionCapture(int slot, Rect localBounds)` | 捕获过渡位图，`slot` 为 0 或 1 |
| `EndTransitionCapture(int slot)` | 结束捕获 |
| `DrawTransitionShader(Rect localBounds, float progress, int mode)` | 使用 GPU 过渡着色器绘制 |
| `DrawCapturedTransition(int slot, Rect localBounds, float opacity)` | 绘制已捕获的过渡位图 |

约定：

- `slot = 0` 表示旧内容
- `slot = 1` 表示新内容
- `mode` 当前源码注释为 `0-9`

### 5.2 液态玻璃

| 方法 | 说明 |
| --- | --- |
| `DrawLiquidGlass(Rect rectangle, float cornerRadius, ...)` | 绘制液态玻璃效果 |

主要参数：

- `cornerRadius`
- `blurRadius`
- `refractionAmount`
- `chromaticAberration`
- `tintR/tintG/tintB/tintOpacity`
- `lightX/lightY`
- `highlightBoost`
- `shapeType`
- `shapeExponent`
- `neighborCount`
- `fusionRadius`
- `neighborData`

这个接口当前已在 `Border` 控件中用于液态玻璃边框绘制。

### 5.3 元素特效捕获

`RenderTargetDrawingContext` 还实现了 `IEffectDrawingContext`：

| 方法 | 说明 |
| --- | --- |
| `BeginEffectCapture(float x, float y, float w, float h)` | 开始捕获元素内容 |
| `EndEffectCapture()` | 结束捕获 |
| `ApplyElementEffect(IEffect effect, float x, float y, float w, float h)` | 将元素特效应用到捕获结果 |

当前实现中 `ApplyElementEffect` 已分发支持：

- `Jalium.UI.Media.Effects.BlurEffect`
- `Jalium.UI.Media.Effects.DropShadowEffect`

### 5.4 其他扩展

| 方法 | 说明 |
| --- | --- |
| `PunchTransparentRect(Rect rectangle)` | 在当前目标上打透明孔 |
| `PopOpacity()` | 显式弹出透明度栈 |
| `ClearCache()` | 清空画刷、文本、位图缓存 |
| `ClearBitmapCache()` | 仅清空位图缓存 |
| `TrimCacheIfNeeded()` | 按阈值裁剪缓存 |

说明：

- 普通代码仍然推荐用统一的 `Pop()`
- `PopOpacity()` 主要是给实现层和接口适配层使用

## 6. 背景特效 `IBackdropEffect`

`DrawBackdropEffect` 依赖 `IBackdropEffect`，常见实现位于 `Jalium.UI.Media/BackdropEffect.cs`：

- `BlurEffect`
- `AcrylicEffect`
- `MicaEffect`
- `FrostedGlassEffect`
- `ColorAdjustmentEffect`
- `CompositeBackdropEffect`

简单示例：

```csharp
var effect = new AcrylicEffect(Color.White, tintOpacity: 0.35f, blurRadius: 24f);
dc.DrawBackdropEffect(
    new Rect(0, 0, ActualWidth, ActualHeight),
    effect,
    new CornerRadius(12));
```

常用属性：

- `BlurRadius`
- `BlurSigma`
- `BlurType`
- `NoiseIntensity`
- `Brightness`
- `Contrast`
- `Saturation`
- `HueRotation`
- `Grayscale`
- `Sepia`
- `Invert`
- `Opacity`
- `TintColor`
- `TintOpacity`
- `Luminosity`
- `HasEffect`

## 7. 底层 `RenderTarget` 能力概览

如果你在做渲染基础设施，可以直接关注 `Jalium.UI.Interop.RenderTarget`。它提供的能力包括：

- 绘制基础图元：
  - `FillRectangle`
  - `DrawRectangle`
  - `FillRoundedRectangle`
  - `DrawRoundedRectangle`
  - `FillEllipse`
  - `DrawEllipse`
  - `DrawLine`
  - `FillPolygon`
  - `DrawPolygon`
  - `FillPath`
  - `StrokePath`
  - `DrawBitmap`
  - `DrawText`
- 渲染状态：
  - `BeginDraw`
  - `EndDraw`
  - `Clear`
  - `Resize`
  - `PushTransform`
  - `PopTransform`
  - `PushClip`
  - `PushRoundedRectClip`
  - `PopClip`
  - `PushOpacity`
  - `PopOpacity`
- 特效与高级功能：
  - `DrawBackdropFilter`
  - `DrawTransitionShader`
  - `BeginTransitionCapture`
  - `EndTransitionCapture`
  - `DrawCapturedTransition`
  - `BeginEffectCapture`
  - `EndEffectCapture`
  - `DrawBlurEffect`
  - `DrawDropShadowEffect`
  - `DrawLiquidGlass`
  - `PunchTransparentRect`
  - `CaptureDesktopArea`
  - `DrawDesktopBackdrop`

这层接口更适合封装渲染后端，不建议在普通控件里直接操作。

## 8. `RenderTargetBitmap` 注意事项

`RenderTargetBitmap` 是软件位图渲染路径，可用于把视觉对象渲染到位图，但当前实现能力有限。

当前源码状态：

- 已支持：
  - 矩形
  - 线条
  - 椭圆
  - 基础变换
- 仍是占位或简化实现：
  - 文本绘制
  - 图片绘制
  - 几何精确绘制
  - 背景特效
  - 裁剪
  - 透明度
  - 可视树完整 `OnRender` 调用链

也就是说：

- 如果你要做真正完整的控件渲染截图，优先使用实际窗口/GPU 渲染路径
- `RenderTargetBitmap` 目前更适合作为基础能力或测试桩，而不是完整截图方案

## 9. 实现细节与注意点

### 9.1 坐标单位

高层绘制 API 使用 DIP（device-independent pixels）。

### 9.2 像素对齐

当前 `RenderTargetDrawingContext` 在多种绘制调用中会对原点或边缘做 `Math.Round` / `Floor` / `Ceiling` 处理，以减少亚像素抖动和边框厚薄不一致问题。

这意味着：

- 同一控件多帧重绘时，边线通常会更稳定
- 某些极细线条或非常依赖亚像素定位的效果，结果会更偏向“对齐像素”而不是“完全保留浮点位置”

## 10. 实战建议

### 10.1 普通自定义控件

直接用 `DrawingContext`：

- 背景、边框、文本、图片都够用
- 用 `PushClip` / `PushTransform` / `PushOpacity` 管理局部绘制

### 10.2 要做 GPU 高级特效

先判断并转成 `RenderTargetDrawingContext`：

```csharp
protected override void OnRender(object drawingContext)
{
    if (drawingContext is not DrawingContext dc)
        return;

    var rect = new Rect(RenderSize);

    dc.DrawRoundedRectangle(
        new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
        null,
        rect,
        new CornerRadius(16));

    if (drawingContext is Jalium.UI.Interop.RenderTargetDrawingContext rtdc)
    {
        rtdc.DrawLiquidGlass(
            rect,
            cornerRadius: 16,
            blurRadius: 10,
            refractionAmount: 55);
    }
}
```

### 10.3 管理状态栈

建议遵守这几个规则：

- `Push*` 和 `Pop()` 数量必须匹配
- 尽量让 `Push`/`Pop` 在同一个方法中成对出现
- 如果渲染逻辑复杂，可在 `OnRender` 里 push，在 `OnPostRender` 里 pop

## 11. 一份最小参考清单

最常用的组合通常是：

1. `DrawRectangle` / `DrawRoundedRectangle`
2. `DrawText`
3. `DrawImage`
4. `PushClip`
5. `PushTransform`
6. `PushOpacity`
7. `Pop`
8. `DrawBackdropEffect`
9. `RenderTargetDrawingContext.DrawLiquidGlass`

如果你是准备给业务同学一个“够用版”心智模型，可以直接记成：

- 画什么：`Draw*`
- 局部控制：`Push*` + `Pop`
- 高级效果：`RenderTargetDrawingContext`

## 12. 相关源码位置

- `src/managed/Jalium.UI.Media/DrawingContext.cs`
- `src/managed/Jalium.UI.Media/Brush.cs`
- `src/managed/Jalium.UI.Media/ImageSource.cs`
- `src/managed/Jalium.UI.Media/Transform.cs`
- `src/managed/Jalium.UI.Media/BackdropEffect.cs`
- `src/managed/Jalium.UI.Interop/RenderTargetDrawingContext.cs`
- `src/managed/Jalium.UI.Interop/RenderTarget.cs`
- `src/managed/Jalium.UI.Media/RenderTargetBitmap.cs`
- `src/managed/Jalium.UI.Controls/Border.cs`
- `src/managed/Jalium.UI.Controls/Shapes/Rectangle.cs`
- `src/managed/Jalium.UI.Controls/Primitives/ResizeGrip.cs`
