using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Automation.MacOS;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSComboBoxSelectionTests
{
    [Theory]
    [InlineData(6, false)]
    [InlineData(650, false)]
    [InlineData(650, true)]
    public void OpenComboRetainsWindowAndSelectorInformation(int count, bool ownContainers)
    {
        var template = new ControlTemplate(typeof(ComboBox));
        template.SetVisualTree(static () =>
        {
            var popup = new Popup
            {
                Name = "PART_Popup", StaysOpen = false,
                Child = new Border
                {
                    Child = new ScrollViewer { MaxHeight = 200, Content = new ItemsPresenter() },
                },
            };
            return new Grid { Children = { new Border { Height = 32 }, popup } };
        });
        var combo = new ComboBox { Width = 220, Height = 32, Template = template };
        for (int i = 0; i < count; i++)
            combo.Items.Add(ownContainers ? new ComboBoxItem { Content = $"Process {i}" } : $"Process {i}");
        var window = new DisplayedTestWindow
        {
            TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 400, Height = 300,
            Content = new StackPanel { Children = { combo } },
        };
        try
        {
            window.Measure(new Size(400, 300));
            window.Arrange(new Rect(0, 0, 400, 300));
            window.UpdateLayout();
            var tree = new MacOSAccessibilityTree(window);
            Assert.True(tree.TryGetId(combo.GetAutomationPeer()!, out var selector));
            AssertInfo(tree, 1, AutomationControlType.Window);
            AssertInfo(tree, selector, AutomationControlType.ComboBox);
            var logical = combo.GetAutomationPeer()!.GetChildren()[0];
            // Registration can depend on mounting. A live read is the exposure contract.
            bool registered = tree.TryGetId(logical, out var hiddenId);
            var hidden = new MacOSAXRequest { NodeId = hiddenId, Operation = MacOSAXOperation.Info };
            Assert.False(registered && tree.Handle(ref hidden));
            combo.IsDropDownOpen = true;
            window.UpdateLayout();
            Assert.True(combo.IsDropDownOpen);
            AssertInfo(tree, 1, AutomationControlType.Window);
            AssertInfo(tree, selector, AutomationControlType.ComboBox);
            var row = Assert.IsType<ComboBoxItem>(combo.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.True(tree.TryGetId(row.GetAutomationPeer()!, out var rowId));
            combo.IsDropDownOpen = false;
            window.UpdateLayout();
            var retired = new MacOSAXRequest { NodeId = rowId, Operation = MacOSAXOperation.Info };
            Assert.False(tree.Handle(ref retired));
            AssertInfo(tree, 1, AutomationControlType.Window);
            AssertInfo(tree, selector, AutomationControlType.ComboBox);
        }
        finally { window.Close(); }
    }

    private static void AssertInfo(MacOSAccessibilityTree tree, ulong id, AutomationControlType role)
    {
        var request = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.Info };
        Assert.True(tree.Handle(ref request));
        Assert.Equal((int)role, request.Role);
        Assert.True(request.Width > 0);
        Assert.True(request.Height > 0);
    }
}
