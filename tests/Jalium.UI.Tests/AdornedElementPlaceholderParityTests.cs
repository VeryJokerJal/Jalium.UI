using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public sealed class AdornedElementPlaceholderParityTests
{
    [Fact]
    public void SurfaceMatchesWpfChildAndMarkupContracts()
    {
        Assert.True(typeof(IAddChild).IsAssignableFrom(typeof(AdornedElementPlaceholder)));

        var childProperty = typeof(AdornedElementPlaceholder).GetProperty(
            nameof(AdornedElementPlaceholder.Child),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.NotNull(childProperty);
        Assert.True(childProperty!.GetMethod!.IsVirtual);
        Assert.True(childProperty.SetMethod!.IsVirtual);
        Assert.Null(childProperty.GetCustomAttribute<System.ComponentModel.DefaultValueAttribute>()!.Value);
    }

    [Fact]
    public void ChildOwnsOneVisualAndLogicalSlotAndCanBeReplaced()
    {
        var placeholder = new ProbePlaceholder();
        var first = new Border { Width = 30, Height = 20 };
        var second = new Border { Width = 40, Height = 25 };

        placeholder.Child = first;
        placeholder.Measure(new Size(100, 100));

        Assert.Equal(1, placeholder.VisualChildrenCount);
        Assert.Same(first, placeholder.GetVisualChild(0));
        Assert.Same(placeholder, first.VisualParent);
        Assert.Same(placeholder, first.Parent);
        Assert.Equal(new Size(30, 20), placeholder.DesiredSize);
        Assert.Equal(new object[] { first }, placeholder.GetLogicalChildren());

        placeholder.Child = second;

        Assert.Null(first.VisualParent);
        Assert.Null(first.Parent);
        Assert.Same(placeholder, second.VisualParent);
        Assert.Same(placeholder, second.Parent);
        Assert.Equal(new object[] { second }, placeholder.GetLogicalChildren());
        Assert.Throws<ArgumentOutOfRangeException>(() => placeholder.GetVisualChild(1));
    }

    [Fact]
    public void MarkupChildContractAcceptsOneElementAndOnlyWhitespaceText()
    {
        var placeholder = new AdornedElementPlaceholder();
        var addChild = (IAddChild)placeholder;
        var child = new Border();

        addChild.AddText("  \r\n");
        addChild.AddChild(child);

        Assert.Same(child, placeholder.Child);
        Assert.Throws<ArgumentException>(() => addChild.AddChild(new Border()));
        Assert.Throws<ArgumentException>(() => addChild.AddChild("not an element"));
        Assert.Throws<ArgumentException>(() => addChild.AddText("content"));
    }

    private sealed class ProbePlaceholder : AdornedElementPlaceholder
    {
        public object[] GetLogicalChildren()
        {
            var children = new List<object>();
            IEnumerator enumerator = LogicalChildren;
            while (enumerator.MoveNext())
            {
                children.Add(enumerator.Current!);
            }

            return children.ToArray();
        }
    }
}
