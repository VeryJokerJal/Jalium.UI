using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// 2026-08-25 真机静默失效回归：
/// (A) CopyClrProperties 把 live 窗口的宿主管线属性（Handle/RenderTarget）覆盖成 parse 树的
///     零值 → 合成器按 Handle==0 当离屏窗口处理，patch 后整窗呈现冻结（值都变、屏幕永不变）。
/// (B) 主题隐式样式给 DP 挂的 StyleSetter 层 DynamicResource 订阅被宽松版
///     TryGetDynamicResourceKey 误报成"patch 声明了 {DynamicResource}"，字面量被丢弃、
///     属性被改挂主题资源（TextBlock.Foreground 全部变主题默认色）。离屏测试进程无主题，
///     故此前测试全绿也全盲。
public class HotReloadHostPlumbingAndThemeTests
{
    private static void CopyClrProperties(object target, object source)
        => typeof(HotReloadRuntime).GetMethod("CopyClrProperties",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { target, source });

    private static void CopyDependencyProperties(DependencyObject target, DependencyObject source)
        => typeof(HotReloadRuntime).GetMethod("CopyDependencyProperties",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { target, source });

    private sealed class FakeHostElement : ContentControl
    {
        public IntPtr Handle { get; set; }
        public object? RenderTarget { get; set; }

        public int MutableScalarSetCount { get; private set; }
        private string _mutableScalar = string.Empty;
        public string MutableScalar
        {
            get => _mutableScalar;
            set { _mutableScalar = value; MutableScalarSetCount++; }
        }
    }

    [Fact]
    public void A_CopyClrProperties_NeverOverwritesHostPlumbing()
    {
        var target = new FakeHostElement
        {
            Handle = new IntPtr(0x1234),
            RenderTarget = new object(),
        };
        var keptRenderTarget = target.RenderTarget;
        var source = new FakeHostElement();

        CopyClrProperties(target, source);

        Assert.Equal(new IntPtr(0x1234), target.Handle);
        Assert.Same(keptRenderTarget, target.RenderTarget);
    }

    [Fact]
    public void A_CopyClrProperties_SkipsEqualValues_NoSetterSideEffects()
    {
        var target = new FakeHostElement { MutableScalar = "same" };
        var source = new FakeHostElement { MutableScalar = "same" };
        var countBefore = target.MutableScalarSetCount;

        CopyClrProperties(target, source);

        Assert.Equal(countBefore, target.MutableScalarSetCount);
    }

    [Fact]
    public void B_StyleLayerDynamicResourceOnSource_DoesNotHijackLiteralCopy()
    {
        var target = new TextBlock
        {
            Text = "target",
            Foreground = new SolidColorBrush(Colors.Orange),
        };
        var source = new TextBlock { Text = "source" };
        source.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Colors.LimeGreen));
        // 模拟主题隐式样式：样式层（非 local）动态资源订阅挂在 source 的同一个 DP 上。
        DynamicResourceBindingOperations.SetDynamicResource(
            source, TextBlock.ForegroundProperty, "Jalium.Theme.NoSuchBrush",
            DependencyObject.LayerValueSource.StyleSetter);

        CopyDependencyProperties(target, source);

        var brush = Assert.IsType<SolidColorBrush>(target.Foreground);
        Assert.Equal(Colors.LimeGreen, brush.Color);
        Assert.False(DynamicResourceBindingOperations.TryGetLocalDynamicResourceKey(
            target, TextBlock.ForegroundProperty, out _));
    }

    [Fact]
    public void B_LocalDynamicResourceInPatch_StillTransfersSubscription()
    {
        var target = new TextBlock();
        var source = new TextBlock();
        DynamicResourceBindingOperations.SetDynamicResource(
            source, TextBlock.ForegroundProperty, "SomeThemeKey");

        CopyDependencyProperties(target, source);

        Assert.True(DynamicResourceBindingOperations.TryGetLocalDynamicResourceKey(
            target, TextBlock.ForegroundProperty, out var key));
        Assert.Equal("SomeThemeKey", key);
    }

    [Fact]
    public void B_ClearLocalDynamicResource_LeavesStyleLayerSubscriptionAlive()
    {
        var element = new TextBlock();
        DynamicResourceBindingOperations.SetDynamicResource(
            element, TextBlock.ForegroundProperty, "LocalKey");
        DynamicResourceBindingOperations.SetDynamicResource(
            element, TextBlock.ForegroundProperty, "StyleKey",
            DependencyObject.LayerValueSource.StyleSetter);

        DynamicResourceBindingOperations.ClearLocalDynamicResource(
            element, TextBlock.ForegroundProperty);

        Assert.False(DynamicResourceBindingOperations.TryGetLocalDynamicResourceKey(
            element, TextBlock.ForegroundProperty, out _));
        // 宽松版仍能看到样式层订阅——它必须活着，属性才能继续跟随主题。
        Assert.True(DynamicResourceBindingOperations.TryGetDynamicResourceKey(
            element, TextBlock.ForegroundProperty, out var styleKey));
        Assert.Equal("StyleKey", styleKey);
    }
}
