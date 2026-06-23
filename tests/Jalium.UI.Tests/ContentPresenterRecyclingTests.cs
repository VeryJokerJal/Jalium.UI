using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

/// <summary>
/// Covers the recycling fast path in ContentPresenter.OnContentChanged: when a container is
/// rebound to a new data item under the SAME ContentTemplate, the existing visual subtree is
/// reused (DataContext swap) instead of being torn down and rebuilt via LoadContent. This is
/// what makes VirtualizationMode.Recycling avoid a full per-row template rebuild + cold text
/// re-measure on every scroll.
/// </summary>
[Collection("Application")]
public class ContentPresenterRecyclingTests
{
    private static FrameworkElement? GetContentElement(ContentPresenter presenter)
    {
        var field = typeof(ContentPresenter).GetField("_contentElement",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (FrameworkElement?)field!.GetValue(presenter);
    }

    [Fact]
    public void RebindSameTemplate_ReusesSubtreeAndSwapsDataContext()
    {
        var template = new DataTemplate();
        template.SetVisualTree(() => new TextBlock());

        var presenter = new ContentPresenter { ContentTemplate = template };

        presenter.Content = "item-A";
        var first = GetContentElement(presenter);
        Assert.NotNull(first);
        Assert.Equal("item-A", first!.DataContext);

        presenter.Content = "item-B";
        var second = GetContentElement(presenter);

        // The subtree instance is reused (not rebuilt) and just rebound to the new item.
        Assert.Same(first, second);
        Assert.Equal("item-B", second!.DataContext);
    }

    [Fact]
    public void ChangingTemplate_RebuildsSubtree()
    {
        var first = new DataTemplate();
        first.SetVisualTree(() => new TextBlock());
        var second = new DataTemplate();
        second.SetVisualTree(() => new Border());

        var presenter = new ContentPresenter { ContentTemplate = first, Content = "x" };
        var firstElement = GetContentElement(presenter);
        Assert.IsType<TextBlock>(firstElement);

        // A template change must force a rebuild even though the data item is unchanged.
        presenter.ContentTemplate = second;
        var secondElement = GetContentElement(presenter);

        Assert.NotSame(firstElement, secondElement);
        Assert.IsType<Border>(secondElement);
    }

    [Fact]
    public void FrameworkElementContent_IsReplacedNotRebound()
    {
        var template = new DataTemplate();
        template.SetVisualTree(() => new TextBlock());
        var presenter = new ContentPresenter { ContentTemplate = template };

        var a = new Border();
        var b = new Border();

        presenter.Content = a;
        Assert.Same(a, GetContentElement(presenter));

        // A literal FrameworkElement cannot be rebound via DataContext — it must be replaced.
        presenter.Content = b;
        Assert.Same(b, GetContentElement(presenter));
    }

    [Fact]
    public void RebindManyItems_KeepsSingleSubtreeInstance()
    {
        var template = new DataTemplate();
        template.SetVisualTree(() => new TextBlock());
        var presenter = new ContentPresenter { ContentTemplate = template };

        presenter.Content = "row-0";
        var element = GetContentElement(presenter);
        Assert.NotNull(element);

        // Simulate a recycled container being rebound across many scrolled-in items: the same
        // realized subtree must persist throughout (no per-item LoadContent rebuild).
        for (int i = 1; i < 200; i++)
        {
            presenter.Content = $"row-{i}";
            Assert.Same(element, GetContentElement(presenter));
        }

        Assert.Equal("row-199", GetContentElement(presenter)!.DataContext);
    }
}
