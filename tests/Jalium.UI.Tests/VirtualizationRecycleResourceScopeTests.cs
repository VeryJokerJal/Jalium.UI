using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public sealed class VirtualizationRecycleResourceScopeTests
{
    [Fact]
    public void SamePanelRecycle_SkipsRecursiveStyleRefreshAndResourceCacheFlush()
    {
        var parent = new StackPanel();
        var container = new Border
        {
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "title" },
                    new TextBlock { Text = "subtitle" }
                }
            }
        };

        var originalResolver = FrameworkElement.ThemeStyleResolver;
        var resolverCalls = 0;
        FrameworkElement.ThemeStyleResolver = _ =>
        {
            resolverCalls++;
            return null;
        };

        try
        {
            parent.Children.Add(container);
            Assert.True(resolverCalls > 1);

            resolverCalls = 0;
            var generationBeforeRecycle = ResourceLookup.CacheGeneration;
            container.PrepareForVirtualizationRecycle(parent);
            parent.Children.Remove(container);
            parent.Children.Add(container);

            Assert.Equal(0, resolverCalls);
            Assert.Equal(generationBeforeRecycle, ResourceLookup.CacheGeneration);
        }
        finally
        {
            FrameworkElement.ThemeStyleResolver = originalResolver;
        }
    }

    [Fact]
    public void ResourceMutationWhilePooled_FallsBackToFullRecursiveRefresh()
    {
        var parent = new StackPanel();
        var container = new Border
        {
            Child = new TextBlock { Text = "item" }
        };

        var originalResolver = FrameworkElement.ThemeStyleResolver;
        var resolverCalls = 0;
        FrameworkElement.ThemeStyleResolver = _ =>
        {
            resolverCalls++;
            return null;
        };

        try
        {
            parent.Children.Add(container);
            container.PrepareForVirtualizationRecycle(parent);
            parent.Children.Remove(container);

            // Any ResourceDictionary mutation advances the global resource generation.
            var resources = new ResourceDictionary
            {
                ["ChangedWhileContainerWasPooled"] = true
            };
            Assert.True(resources.Contains("ChangedWhileContainerWasPooled"));

            resolverCalls = 0;
            parent.Children.Add(container);

            Assert.True(resolverCalls > 1);
        }
        finally
        {
            FrameworkElement.ThemeStyleResolver = originalResolver;
        }
    }

    [Fact]
    public void DifferentPanelReattach_FallsBackToFullRecursiveRefresh()
    {
        var originalParent = new StackPanel();
        var newParent = new StackPanel();
        var container = new Border
        {
            Child = new TextBlock { Text = "item" }
        };

        var originalResolver = FrameworkElement.ThemeStyleResolver;
        var resolverCalls = 0;
        FrameworkElement.ThemeStyleResolver = _ =>
        {
            resolverCalls++;
            return null;
        };

        try
        {
            originalParent.Children.Add(container);
            container.PrepareForVirtualizationRecycle(originalParent);
            originalParent.Children.Remove(container);

            resolverCalls = 0;
            newParent.Children.Add(container);

            Assert.True(resolverCalls > 1);
        }
        finally
        {
            FrameworkElement.ThemeStyleResolver = originalResolver;
        }
    }
}
