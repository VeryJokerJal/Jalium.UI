using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class WindowBackdropDepthTests
{
    [Fact]
    public void PlainTreeIsNotEnumeratedOnReplayChecks()
    {
        var root = new CountingPanel();
        root.Children.Add(Nest(new Border()));
        root.ChildCountReads = 0;
        for (int i = 0; i < 100; ++i) Assert.False(RequiresReplay(root));
        Assert.Equal(0, root.ChildCountReads);
    }

    [Fact]
    public void CandidateOwnershipSurvivesReparentingAndOverlappingKinds()
    {
        var left = new CountingPanel();
        var right = new CountingPanel();
        var leaf = new Border { LiquidGlass = true, BackdropEffect = new AcrylicEffect() };
        var branch = new StackPanel();
        branch.Children.Add(leaf);
        for (int i = 0; i < 4; ++i)
        {
            left.Children.Add(branch);
            Assert.True(RequiresReplay(left));
            leaf.BackdropEffect = null;
            Assert.True(RequiresReplay(left));
            left.Children.Remove(branch);
            left.ChildCountReads = 0;
            Assert.False(RequiresReplay(left));
            Assert.Equal(0, left.ChildCountReads);
            right.Children.Add(branch);
            Assert.True(RequiresReplay(right));
            right.Children.Remove(branch);
            leaf.BackdropEffect = new AcrylicEffect();
        }
        Assert.False(RequiresReplay(left));
        Assert.False(RequiresReplay(right));
    }

    [Fact]
    public void MutableEffectsAndAncestorVisibilityAreCheckedLive()
    {
        var effect = new ToggleBackdrop();
        var parent = new StackPanel();
        var root = new StackPanel();
        root.Children.Add(parent);
        parent.Children.Add(new Border { BackdropEffect = effect });
        Assert.False(RequiresReplay(root));
        effect.Enabled = true;
        Assert.True(RequiresReplay(root));
        parent.Opacity = 0;
        Assert.False(RequiresReplay(root));
        parent.Opacity = 1;
        parent.Visibility = Visibility.Hidden;
        Assert.False(RequiresReplay(root));
        parent.Visibility = Visibility.Visible;
        Assert.True(RequiresReplay(root));
        effect.Enabled = false;
        Assert.False(RequiresReplay(root));
    }

    [Fact]
    public void MetadataDefaultsAndNoBaseCallbackRetainBackdropCandidates()
    {
        Assert.True(RequiresReplay(new DefaultBackdropBorder()));
        Assert.True(RequiresReplay(new DefaultGlassBorder()));
        var root = new StackPanel();
        var child = new NoBaseBackdropBorder();
        root.Children.Add(child);
        child.BackdropEffect = new AcrylicEffect();
        Assert.True(RequiresReplay(root));
        child.BackdropEffect = null;
        Assert.False(RequiresReplay(root));
    }

    [Fact]
    public void RuntimeBlendChangesAndClearsAreTrackedOnEveryBackend()
    {
        var root = new CountingPanel();
        var leaf = new Border();
        root.Children.Add(leaf);
        var properties = typeof(Window).Assembly.GetType("Jalium.UI.Styling.CssCompositingProperties")!;
        var mix = (DependencyProperty)properties.GetField("MixProperty", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        foreach (var backend in new[] { RenderBackend.Metal, RenderBackend.D3D12, RenderBackend.Vulkan })
        {
            Assert.False(RequiresReplay(root, backend));
            leaf.SetValue(mix, CssBlendMode.Multiply);
            Assert.True(RequiresReplay(root, backend));
            leaf.Visibility = Visibility.Collapsed;
            Assert.False(RequiresReplay(root, backend));
            leaf.Visibility = Visibility.Visible;
            leaf.ClearValue(mix);
            root.ChildCountReads = 0;
            Assert.False(RequiresReplay(root, backend));
            Assert.Equal(0, root.ChildCountReads);
        }
    }

    private sealed class CountingPanel : StackPanel
    {
        internal int ChildCountReads;
        protected override int VisualChildrenCount
        {
            get { ++ChildCountReads; return base.VisualChildrenCount; }
        }
    }
    private sealed class ToggleBackdrop : BackdropEffect
    {
        internal bool Enabled;
        public override bool HasEffect => Enabled;
    }
    private sealed class DefaultBackdropBorder : Border
    {
        static DefaultBackdropBorder() => BackdropEffectProperty.OverrideMetadata(typeof(DefaultBackdropBorder),
            new PropertyMetadata(new AcrylicEffect()));
    }
    private sealed class DefaultGlassBorder : Border
    {
        static DefaultGlassBorder() => LiquidGlassProperty.OverrideMetadata(typeof(DefaultGlassBorder), new PropertyMetadata(true));
    }
    private sealed class NoBaseBackdropBorder : Border
    {
        protected override void OnBackdropEffectChanged(IBackdropEffect? oldValue, IBackdropEffect? newValue) { }
    }

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
