using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Documents;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSStaticTextValueAccessibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StableAccessibleNameDoesNotReplaceVisibleOrUpdatedText(bool inlineContent)
    {
        var text = new TextBlock();
        if (inlineContent)
        {
            text.Inlines.Add(new Run("当前 "));
            text.Inlines.Add(new Run("中文🙂"));
        }
        else text.Text = "当前 中文🙂";
        AutomationProperties.SetName(text, "查询状态");
        var window = new DisplayedTestWindow
        {
            TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 420,
            Height = 180,
            Content = text,
        };
        var tree = new MacOSAccessibilityTree(window);
        try
        {
            window.Measure(new Size(420, 180));
            window.Arrange(new Rect(0, 0, 420, 180));
            var child = new MacOSAXRequest { NodeId = 1, Operation = MacOSAXOperation.Child };
            Assert.True(tree.Handle(ref child));
            Assert.Equal("查询状态", Read(tree, child.ResultId, MacOSAXString.Name));
            Assert.Equal("当前 中文🙂", Read(tree, child.ResultId, MacOSAXString.Value));

            text.Text = "已完成 4 项🙂";
            Assert.Equal("查询状态", Read(tree, child.ResultId, MacOSAXString.Name));
            Assert.Equal("已完成 4 项🙂", Read(tree, child.ResultId, MacOSAXString.Value));
            Assert.Null(text.GetAutomationPeer()!.GetPattern(PatternInterface.Value));
        }
        finally { tree.Dispose(); window.Close(); }
    }

    private static string Read(MacOSAccessibilityTree tree, ulong id, MacOSAXString property)
    {
        char* buffer = stackalloc char[128];
        var request = new MacOSAXRequest
        {
            NodeId = id,
            Operation = MacOSAXOperation.String,
            Index = (int)property,
            Text = buffer,
            TextCapacity = 128,
        };
        Assert.True(tree.Handle(ref request));
        return new string(buffer, 0, request.TextCount);
    }
}
