using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed class GroupBoxParityTests
{
    [Fact]
    public void GroupBoxUsesHeaderedContentControlContracts()
    {
        Assert.Equal(typeof(HeaderedContentControl), typeof(GroupBox).BaseType);
        Assert.Same(HeaderedContentControl.HeaderProperty, GroupBox.HeaderProperty);

        var method = typeof(GroupBox).GetMethod(
            "OnAccessKey",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            [typeof(AccessKeyEventArgs)],
            null);
        Assert.NotNull(method);
        Assert.True(method!.IsFamily);
        Assert.True(method.IsVirtual);
    }

    [Fact]
    public void HeaderAndContentRemainDistinctLogicalChildren()
    {
        var groupBox = new ProbeGroupBox();
        var header = new Border();
        var content = new Button();

        groupBox.Header = header;
        groupBox.Content = content;

        Assert.True(groupBox.HasHeader);
        Assert.Same(groupBox, header.Parent);
        Assert.Same(groupBox, content.Parent);
        Assert.Equal(new object[] { header, content }, groupBox.GetLogicalChildren());

        groupBox.Header = null;

        Assert.False(groupBox.HasHeader);
        Assert.Null(header.Parent);
        Assert.Equal(new object[] { content }, groupBox.GetLogicalChildren());
    }

    private sealed class ProbeGroupBox : GroupBox
    {
        public object[] GetLogicalChildren()
        {
            var result = new List<object>();
            IEnumerator enumerator = LogicalChildren;
            while (enumerator.MoveNext())
            {
                result.Add(enumerator.Current!);
            }

            return result.ToArray();
        }
    }
}
