using System.Reflection;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Controls.DevTools;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSDevToolsWorkspaceAccessibilityTests
{
    [Fact]
    public void ActualLayoutPageKeepsItsNativeRecordingAction()
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        _ = Application.Current ?? new Application();
        var window = new DevToolsWindow(new Window { Title = "Host" });
        MacOSAccessibilityTree? tree = null;
        try
        {
            typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [Visibility.Visible]);
            Layout(window);
            var workspace = Assert.IsType<DevToolsWorkspace>(window.Content);
            tree = new MacOSAccessibilityTree(window);
            _ = Walk(tree, 1);
            workspace.SelectedPage = DevToolsPage.Layout;
            _ = Walk(tree, 1);
            Layout(window);
            typeof(Jalium.UI.Threading.Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window.Dispatcher, null);
            Assert.Contains(Walk(tree, 1), entry => entry.Name == "Start recording" && entry.Info.Role == (int)AutomationControlType.Button);
        }
        finally { tree?.Dispose(); window.CloseDevTools(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SwitchingPagesKeepsNativeActionsAndConsistentParentLinks(bool anonymousLayoutRoot)
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        _ = Application.Current ?? new Application();
        var workspace = new DevToolsWorkspace
        {
            InspectorContent = new DevToolsActionButton { Label = "Refresh" },
            LayoutContent = new DevToolsSurface
            {
                Kind = DevToolsSurfaceKind.TabShell,
                Content = new DevToolsSurface
                {
                    Kind = DevToolsSurfaceKind.Toolbar,
                    Content = new StackPanel { Children = { new DevToolsActionButton { Label = "Start recording" } } },
                },
            },
        };
        if (anonymousLayoutRoot)
            workspace.LayoutContent = new Border { Child = new Grid { Children = { (UIElement)workspace.LayoutContent! } } };
        var window = new DisplayedTestWindow { TitleBarStyle = WindowTitleBarStyle.Native, Width = 1040, Height = 860, Content = workspace };
        MacOSAccessibilityTree? tree = null;
        try
        {
            Layout(window);
            tree = new MacOSAccessibilityTree(window);
            Assert.Contains(Walk(tree, 1), entry => entry.Name == "Refresh" && entry.Info.Role == (int)AutomationControlType.Button);
            var layoutTab = Assert.Single(Walk(tree, 1), entry => entry.Name == "Layout" && entry.Info.Role == (int)AutomationControlType.TabItem);
            var press = new MacOSAXRequest { NodeId = layoutTab.Info.NodeId, Operation = MacOSAXOperation.Action, Index = (int)MacOSAXAction.Press };
            Assert.True(tree.Handle(ref press));
            Assert.Equal(DevToolsPage.Layout, workspace.SelectedPage);
            _ = Walk(tree, 1).ToArray(); // Query during the pending layout, as a native bridge can.
            Layout(window);
            typeof(Jalium.UI.Threading.Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window.Dispatcher, null);
            var entries = Walk(tree, 1).ToArray();
            Assert.Contains(entries, entry => entry.Name == "Start recording" && entry.Info.Role == (int)AutomationControlType.Button);
            Assert.DoesNotContain(entries, entry => entry.Name == "Refresh" && entry.Info.Role == (int)AutomationControlType.Button);
            foreach (var entry in entries.Where(entry => entry.ListedParent != 0))
                Assert.Equal(entry.ListedParent, entry.Info.ParentId);
        }
        finally { tree?.Dispose(); window.Close(); }
    }

    private static void Layout(Window window)
    { window.Measure(new Size(1040, 860)); window.Arrange(new Rect(0, 0, 1040, 860)); window.UpdateLayout(); }

    private static List<(ulong ListedParent, MacOSAXRequest Info, string Name)> Walk(MacOSAccessibilityTree tree, ulong root)
    {
        var result = new List<(ulong, MacOSAXRequest, string)>();
        var seen = new HashSet<ulong>();
        void Visit(ulong id, ulong listedParent)
        {
            if (!seen.Add(id)) return;
            var info = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.Info };
            Assert.True(tree.Handle(ref info));
            char* buffer = stackalloc char[1024];
            var name = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.String, Index = (int)MacOSAXString.Name, Text = buffer, TextCapacity = 1024 };
            Assert.True(tree.Handle(ref name));
            result.Add((listedParent, info, new string(buffer, 0, name.TextCount)));
            for (int index = 0; index < info.ChildCount; index++)
            {
                var child = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.Child, Index = index };
                Assert.True(tree.Handle(ref child));
                Visit(child.ResultId, id);
            }
        }
        Visit(root, 0);
        return result;
    }
}
