using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public sealed class ExpanderParityTests
{
    [Fact]
    public void ExpanderUsesHeaderedContentControlHeaderSurface()
    {
        Assert.Equal(typeof(HeaderedContentControl), typeof(Expander).BaseType);
        Assert.Same(HeaderedContentControl.HeaderProperty, Expander.HeaderProperty);

        var expander = new Expander { Header = "Details", Content = "Body" };
        Assert.Equal("Details", expander.Header);
        Assert.Equal("Body", expander.Content);
    }

    [Fact]
    public void ExpandedAndCollapsedCallbacksRaiseEvents()
    {
        var expander = new ProbeExpander();
        var expandedCount = 0;
        var collapsedCount = 0;
        expander.Expanded += (_, _) => expandedCount++;
        expander.Collapsed += (_, _) => collapsedCount++;

        expander.IsExpanded = true;
        expander.IsExpanded = false;

        Assert.Equal(1, expander.ExpandedHookCount);
        Assert.Equal(1, expander.CollapsedHookCount);
        Assert.Equal(1, expandedCount);
        Assert.Equal(1, collapsedCount);
    }

    private sealed class ProbeExpander : Expander
    {
        public int ExpandedHookCount { get; private set; }
        public int CollapsedHookCount { get; private set; }

        protected override void OnExpanded()
        {
            ExpandedHookCount++;
            base.OnExpanded();
        }

        protected override void OnCollapsed()
        {
            CollapsedHookCount++;
            base.OnCollapsed();
        }
    }
}
