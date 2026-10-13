using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class WindowBackdropDepthTests
{
    [Fact]
    public void BackdropBelowOldDepthLimitRequiresFullMetalReplay()
    {
        var root = Nest(new Border { BackdropEffect = new AcrylicEffect() });
        Assert.True(RequiresReplay(root));
        Assert.False(RequiresReplay(root, RenderBackend.D3D12));
    }

    [Fact]
    public void LiquidGlassBelowOldDepthLimitRequiresFullMetalReplay()
    {
        var root = Nest(new Border { LiquidGlass = true });
        Assert.True(RequiresReplay(root));
    }

    [Theory]
    [InlineData(Visibility.Hidden, 1d)]
    [InlineData(Visibility.Collapsed, 1d)]
    [InlineData(Visibility.Visible, 0d)]
    public void NonVisibleDeepAncestorKeepsPartialReplay(Visibility visibility, double opacity)
    {
        var ancestor = Nest(new Border { BackdropEffect = new FrostedGlassEffect() });
        ancestor.Visibility = visibility;
        ancestor.Opacity = opacity;
        Assert.False(RequiresReplay(Nest(ancestor)));
    }

    [Fact]
    public void DeepPlainTreeKeepsPartialReplay()
    {
        Assert.False(RequiresReplay(Nest(new Border())));
    }

    private static StackPanel Nest(UIElement leaf)
    {
        UIElement child = leaf;
        for (int depth = 0; depth < 512; depth++)
        {
            var parent = new StackPanel();
            parent.Children.Add(child);
            child = parent;
        }
        return (StackPanel)child;
    }

    private static bool RequiresReplay(Visual root, RenderBackend backend = RenderBackend.Metal) =>
        (bool)typeof(Window).GetMethod("RequiresFullReplayForBackdrop", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [backend, root])!;
}
