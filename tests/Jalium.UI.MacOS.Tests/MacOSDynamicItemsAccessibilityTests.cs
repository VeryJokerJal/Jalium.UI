using System.Collections.ObjectModel;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSDynamicItemsAccessibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonvirtualItems_FollowInsertRemoveReplaceAndSourceRebind(bool externalSource)
    {
        var source = new ObservableCollection<string> { "原文件", "保留文件" };
        var template = new DataTemplate();
        template.SetVisualTree(() =>
        {
            var button = new Button { Height = 28 };
            button.SetBinding(ContentControl.ContentProperty, new Binding("."));
            return button;
        });
        var rows = new ItemsControl { ItemTemplate = template };
        if (externalSource) rows.ItemsSource = source;
        else foreach (var item in source) rows.Items.Add(item);
        var window = new DisplayedTestWindow { TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 300, Height = 300, Content = new ScrollViewer { Content = rows } };
        try
        {
            var tree = new MacOSAccessibilityTree(window);
            Layout(window);
            var peer = Assert.IsAssignableFrom<ItemsControlAutomationPeer>(rows.GetAutomationPeer());
            Assert.True(tree.TryGetId(peer, out ulong list));
            Assert.Equal(new[] { "原文件", "保留文件" }, ButtonNames(tree, list));
            Assert.Equal(2, peer.GetChildren().Count); // Warm the logical peer cache before mutation.

            if (externalSource) source.Insert(0, "新窗口");
            else rows.Items.Insert(0, "新窗口");
            Layout(window);
            Assert.Equal(3, rows.Items.Count);
            Assert.NotNull(rows.ItemContainerGenerator.ContainerFromItem("新窗口"));
            Assert.Equal(new[] { "新窗口", "原文件", "保留文件" }, ButtonNames(tree, list));
            Assert.Equal(3, peer.GetChildren().Count);

            if (externalSource) source.RemoveAt(1);
            else rows.Items.RemoveAt(1);
            Layout(window);
            Assert.Equal(new[] { "新窗口", "保留文件" }, ButtonNames(tree, list));

            if (externalSource) source[0] = "替换文件";
            else rows.Items[0] = "替换文件";
            Layout(window);
            Assert.Equal(new[] { "替换文件", "保留文件" }, ButtonNames(tree, list));

            if (!externalSource) rows.Items.Clear();
            rows.ItemsSource = new ObservableCollection<string> { "重绑定文件" };
            Layout(window);
            Assert.Equal(new[] { "重绑定文件" }, ButtonNames(tree, list));
            Assert.Single(peer.GetChildren());
        }
        finally { window.Close(); }
    }

    private static void Layout(Window window)
    {
        window.Measure(new Size(300, 300));
        window.Arrange(new Rect(0, 0, 300, 300));
        window.UpdateLayout();
    }

    private static List<string> ButtonNames(MacOSAccessibilityTree tree, ulong parent)
    {
        var result = new List<string>();
        Walk(parent);
        return result;
        void Walk(ulong id)
        {
            var info = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.Info };
            Assert.True(tree.Handle(ref info));
            if (info.Role == (int)AutomationControlType.Button)
            {
                char* buffer = stackalloc char[128];
                var text = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.String,
                    Index = (int)MacOSAXString.Name, Text = buffer, TextCapacity = 128 };
                Assert.True(tree.Handle(ref text));
                result.Add(new string(buffer, 0, text.TextCount));
            }
            for (int i = 0; i < info.ChildCount; i++)
            {
                var child = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.Child, Index = i };
                Assert.True(tree.Handle(ref child));
                Walk(child.ResultId);
            }
        }
    }
}
