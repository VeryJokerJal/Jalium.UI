using System.Reflection;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class AutomationPeerBaseChainParityTests
{
    [Fact]
    public void PeersUseTheWpfBaseChainAndConstructorShapes()
    {
        AssertPeer<DocumentViewerAutomationPeer, DocumentViewerBaseAutomationPeer, DocumentViewer>();
        AssertPeer<GridSplitterAutomationPeer, ThumbAutomationPeer, GridSplitter>();
        AssertPeer<TextBoxAutomationPeer, TextAutomationPeer, TextBox>();

        Assert.False(typeof(DocumentViewerAutomationPeer).IsSealed);
        Assert.False(typeof(GridSplitterAutomationPeer).IsSealed);
        Assert.False(typeof(TextBoxAutomationPeer).IsSealed);
        Assert.False(typeof(ThumbAutomationPeer).IsSealed);

        Assert.True(typeof(TextAutomationPeer).IsAbstract);
        ConstructorInfo textBaseConstructor = Assert.Single(
            typeof(TextAutomationPeer).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.True(textBaseConstructor.IsFamily);
        Assert.Equal(typeof(FrameworkElement), textBaseConstructor.GetParameters().Single().ParameterType);
    }

    [Fact]
    public void GridSplitterExposesTheCanonicalTransformProvider()
    {
        var peer = new GridSplitterAutomationPeer(new GridSplitter());

        Assert.Contains(typeof(ITransformProvider), peer.GetType().GetInterfaces());
        Assert.Same(peer, peer.GetPattern(PatternInterface.Transform));

        var transform = (ITransformProvider)peer;
        Assert.True(transform.CanMove);
        Assert.False(transform.CanResize);
        Assert.False(transform.CanRotate);
        Assert.Throws<ArgumentOutOfRangeException>(() => transform.Move(double.NaN, 0));
        Assert.Throws<InvalidOperationException>(() => transform.Resize(10, 10));
        Assert.Throws<InvalidOperationException>(() => transform.Rotate(90));
    }

    [Fact]
    public void TextBoxRetainsItsValueAndTextPatternsAfterTheBaseMigration()
    {
        var textBox = new TextBox { Text = "automation" };
        var peer = new TextBoxAutomationPeer(textBox);

        Assert.Same(peer, peer.GetPattern(PatternInterface.Value));
        Assert.NotNull(peer.GetPattern(PatternInterface.Text));
        Assert.Equal("automation", ((IValueProvider)peer).Value);
    }

    private static void AssertPeer<TPeer, TBase, TOwner>()
    {
        Assert.Equal(typeof(TBase), typeof(TPeer).BaseType);
        ConstructorInfo constructor = Assert.Single(typeof(TPeer).GetConstructors());
        Assert.Equal(typeof(TOwner), constructor.GetParameters().Single().ParameterType);
    }
}
