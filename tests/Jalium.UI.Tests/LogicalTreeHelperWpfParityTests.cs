using System.Collections;

namespace Jalium.UI.Tests;

public sealed class LogicalTreeHelperWpfParityTests
{
    [Fact]
    public void GetChildrenExposesAllThreeWpfOverloads()
    {
        Assert.Equal(
            typeof(IEnumerable),
            typeof(LogicalTreeHelper).GetMethod(
                nameof(LogicalTreeHelper.GetChildren),
                [typeof(DependencyObject)])!.ReturnType);
        Assert.Equal(
            typeof(IEnumerable),
            typeof(LogicalTreeHelper).GetMethod(
                nameof(LogicalTreeHelper.GetChildren),
                [typeof(FrameworkElement)])!.ReturnType);
        Assert.Equal(
            typeof(IEnumerable),
            typeof(LogicalTreeHelper).GetMethod(
                nameof(LogicalTreeHelper.GetChildren),
                [typeof(FrameworkContentElement)])!.ReturnType);
    }

    [Fact]
    public void FrameworkAndContentElementsEnumerateTheirLogicalChildren()
    {
        var visualParent = new ProbeFrameworkElement();
        var visualChild = new ProbeFrameworkElement { Name = "visualChild" };
        visualParent.Attach(visualChild);

        var contentParent = new ProbeFrameworkContentElement();
        var contentChild = new ProbeFrameworkContentElement { Name = "contentChild" };
        contentParent.Attach(contentChild);

        Assert.Equal(new object[] { visualChild },
            LogicalTreeHelper.GetChildren((FrameworkElement)visualParent).Cast<object>());
        Assert.Equal(new object[] { contentChild },
            LogicalTreeHelper.GetChildren((FrameworkContentElement)contentParent).Cast<object>());
        Assert.Same(visualParent, LogicalTreeHelper.GetParent(visualChild));
        Assert.Same(contentParent, LogicalTreeHelper.GetParent(contentChild));
        Assert.Same(visualChild, LogicalTreeHelper.FindLogicalNode(visualParent, "visualChild"));
        Assert.Same(contentChild, LogicalTreeHelper.FindLogicalNode(contentParent, "contentChild"));
    }

    private sealed class ProbeFrameworkElement : FrameworkElement
    {
        public void Attach(object child) => AddLogicalChild(child);
    }

    private sealed class ProbeFrameworkContentElement : FrameworkContentElement
    {
        public void Attach(object child) => AddLogicalChild(child);
    }
}
