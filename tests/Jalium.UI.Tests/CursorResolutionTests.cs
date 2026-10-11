using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class CursorResolutionTests
{
    [Fact]
    public void UnspecifiedCursor_LeavesTheDefaultArrowFallbackAvailable()
    {
        var child = new FrameworkElement();
        var parent = new Border { Child = child };
        Cursor? queriedCursor = Cursors.Cross;
        child.QueryCursor += (_, e) => queriedCursor = e.Cursor;

        var cursor = FrameworkElement.ResolveEffectiveCursor(child);

        Assert.Null(queriedCursor);
        Assert.Null(cursor);
        Assert.Same(Cursors.Arrow, cursor ?? Cursors.Arrow);
    }

    [Fact]
    public void ClearingInheritedCursor_RestoresTheDefaultFallback()
    {
        var child = new FrameworkElement();
        var parent = new Border { Child = child, Cursor = Cursors.Hand };

        Assert.Same(Cursors.Hand, FrameworkElement.ResolveEffectiveCursor(child));

        parent.ClearValue(FrameworkElement.CursorProperty);

        Assert.Null(FrameworkElement.ResolveEffectiveCursor(child));
    }

    [Fact]
    public void ExplicitNone_RemainsAHiddenCursor()
    {
        var element = new FrameworkElement { Cursor = Cursors.None };

        Assert.Same(Cursors.None, FrameworkElement.ResolveEffectiveCursor(element));
    }

    [Fact]
    public void QueryHandler_CanSelectOrExplicitlyHideTheCursor()
    {
        var element = new FrameworkElement();
        Cursor? selected = Cursors.IBeam;
        element.QueryCursor += (_, e) => e.Cursor = selected;

        Assert.Same(Cursors.IBeam, FrameworkElement.ResolveEffectiveCursor(element));

        selected = null;

        Assert.Same(Cursors.None, FrameworkElement.ResolveEffectiveCursor(element));
    }
}
