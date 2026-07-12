using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public sealed class HierarchicalDataTemplateWpfParityTests
{
    [Fact]
    public void ChildContainerPresentationPropertiesMatchWpfSurface()
    {
        var template = new HierarchicalDataTemplate
        {
            AlternationCount = 3,
            ItemBindingGroup = new BindingGroup(),
            ItemContainerStyleSelector = new ProbeStyleSelector(),
            ItemStringFormat = "Node: {0}",
        };

        Assert.Equal(3, template.AlternationCount);
        Assert.NotNull(template.ItemBindingGroup);
        Assert.IsType<ProbeStyleSelector>(template.ItemContainerStyleSelector);
        Assert.Equal("Node: {0}", template.ItemStringFormat);
    }

    [Fact]
    public void ApplyingTemplateTransfersChildContainerPresentation()
    {
        var bindingGroup = new BindingGroup();
        var selector = new ProbeStyleSelector();
        var template = new HierarchicalDataTemplate
        {
            AlternationCount = 4,
            ItemBindingGroup = bindingGroup,
            ItemContainerStyleSelector = selector,
            ItemStringFormat = "Child: {0}",
        };
        var container = new TreeViewItem();

        TreeViewItem.ApplyHierarchicalDataTemplate(container, new object(), template);

        Assert.Equal(4, container.AlternationCount);
        Assert.Same(bindingGroup, container.ItemBindingGroup);
        Assert.Same(selector, container.ItemContainerStyleSelector);
        Assert.Equal("Child: {0}", container.ItemStringFormat);
    }

    private sealed class ProbeStyleSelector : StyleSelector;
}
