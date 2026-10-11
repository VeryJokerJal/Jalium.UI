using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class MetalBackdropReplayTests
{
    private static bool RequiresReplay(Visual root, RenderBackend backend = RenderBackend.Metal)
    {
        var method = typeof(Window).GetMethod("RequiresFullReplayForBackdrop", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (bool)method.Invoke(null, [backend, root])!;
    }

    [Fact]
    public void VisibleNestedBackdrop_ReplaysMetalSceneToAvoidFeedback()
    {
        var backdrop = new Border { BackdropEffect = new AcrylicEffect() };
        var root = new StackPanel();
        var nested = new StackPanel();
        root.Children.Add(nested);
        nested.Children.Add(backdrop);
        Assert.True(RequiresReplay(root));
        Assert.False(RequiresReplay(root, RenderBackend.D3D12));
        backdrop.BackdropEffect = null;
        Assert.False(RequiresReplay(root));
    }

    [Fact]
    public void HiddenOrTransparentBackdrop_DoesNotDisablePartialReplay()
    {
        var root = new StackPanel { Opacity = 0 };
        var backdrop = new Border { BackdropEffect = new FrostedGlassEffect() };
        root.Children.Add(backdrop);
        Assert.False(RequiresReplay(root));
        root.Opacity = 1;
        backdrop.Visibility = Visibility.Collapsed;
        Assert.False(RequiresReplay(root));
        backdrop.Visibility = Visibility.Visible;
        Assert.True(RequiresReplay(root));
    }

    [Fact]
    public void LiquidGlassBorder_ReplaysWithoutABackdropEffectProperty()
    {
        var border = new Border { LiquidGlass = true };
        Assert.Null(border.BackdropEffect);
        Assert.True(RequiresReplay(border));
        border.LiquidGlass = false;
        Assert.False(RequiresReplay(border));
    }
}
