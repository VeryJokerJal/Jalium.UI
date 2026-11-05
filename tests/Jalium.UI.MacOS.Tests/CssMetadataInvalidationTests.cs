using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssMetadataInvalidationTests
{
    [Fact]
    public void CachedSheetDependencies_KeepScopeAndNestedAttributeSelectorsReactive()
    {
        var root = new StackPanel { Tag = "closed" };
        Css.SetClass(root, "scope");
        var gate = new Border();
        Css.SetClass(gate, "gate");
        var child = new Border { Tag = "off" };
        gate.Child = child;
        root.Children.Add(gate);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
            Border.gate { width: 12px; }
            @scope (.scope[Tag='open']) {
                Border.gate:has(> Border[Tag='on']) { width: 64px; }
            }
            """));
        void Flush() => CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
        Flush();
        Assert.Equal(12, gate.Width);
        child.Tag = "on";
        Flush();
        Assert.Equal(12, gate.Width);
        root.Tag = "open";
        Flush();
        Assert.Equal(64, gate.Width);
        child.Tag = "off";
        Flush();
        Assert.Equal(12, gate.Width);
        child.Tag = "on";
        Flush();
        Assert.Equal(64, gate.Width);
        root.Tag = "closed";
        Flush();
        Assert.Equal(12, gate.Width);
    }
}
