using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("AutomationEventSink")]
public sealed class TitleBarButtonAutomationTests
{
    [Theory]
    [InlineData(TitleBarButtonKind.Close, "Close")]
    [InlineData(TitleBarButtonKind.Minimize, "Minimize")]
    [InlineData(TitleBarButtonKind.Maximize, "Maximize")]
    [InlineData(TitleBarButtonKind.Restore, "Restore")]
    public void IconCaption_NameDescribesItsAction_IdentifierRetainsTemplateName(TitleBarButtonKind kind, string name)
    {
        var button = new TitleBarButton { Name = "PART_WindowButton", Kind = kind };
        var peer = button.GetAutomationPeer()!;
        Assert.Equal(name, peer.GetName());
        Assert.Equal("PART_WindowButton", peer.GetAutomationId());
        Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
        Assert.IsAssignableFrom<Jalium.UI.Automation.Provider.IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke));
        AutomationProperties.SetName(button, "操作当前文档");
        Assert.Equal("操作当前文档", peer.GetName());
    }

    [Theory]
    [InlineData(TitleBarButtonKind.Close, "TitleBarCloseButtonName", "关闭窗口")]
    [InlineData(TitleBarButtonKind.Minimize, "TitleBarMinimizeButtonName", "最小化窗口")]
    [InlineData(TitleBarButtonKind.Maximize, "TitleBarMaximizeButtonName", "最大化窗口")]
    [InlineData(TitleBarButtonKind.Restore, "TitleBarRestoreButtonName", "还原窗口")]
    public void Caption_UsesScopedResources_AndCustomContentTakesPrecedence(TitleBarButtonKind kind, string key, string name)
    {
        var parent = new StackPanel();
        var button = new TitleBarButton { Name = "PART_WindowButton", Kind = kind };
        parent.Children.Add(button);
        parent.Resources[key] = name;
        var peer = button.GetAutomationPeer()!;
        Assert.Equal(name, peer.GetName());
        parent.Resources[key] = name + "🙂";
        Assert.Equal(name + "🙂", peer.GetName());
        button.Content = "自定义操作文字";
        Assert.Equal("自定义操作文字", peer.GetName());
        AutomationProperties.SetName(button, "显式读屏名称");
        Assert.Equal("显式读屏名称", peer.GetName());
    }

    [Fact]
    public void ZoomRestore_NameChangesNotifyTheSamePeer_ExplicitNameSuppressesAutomaticRename()
    {
        var button = new TitleBarButton { Kind = TitleBarButtonKind.Maximize };
        var peer = button.GetAutomationPeer()!;
        var saved = EventSinkRegistry.Sink;
        var sink = new NameSink(peer);
        try
        {
            EventSinkRegistry.Sink = sink;
            button.Kind = TitleBarButtonKind.Restore;
            Assert.Same(peer, button.GetAutomationPeer());
            Assert.Equal(("Maximize", "Restore"), Assert.Single(sink.Changes));
            sink.Changes.Clear();
            AutomationProperties.SetName(button, "切换窗口大小");
            sink.Changes.Clear();
            button.Kind = TitleBarButtonKind.Maximize;
            Assert.Empty(sink.Changes);
            Assert.Equal("切换窗口大小", peer.GetName());
        }
        finally { EventSinkRegistry.Sink = saved; }
    }

    private sealed class NameSink(AutomationPeer target) : IAutomationEventSink
    {
        internal List<(string Old, string New)> Changes { get; } = [];
        public void OnAutomationEventRaised(AutomationPeer peer, AutomationEvents eventId) { }
        public void OnFocusChanged(AutomationPeer peer) { }
        public void OnPropertyChangedRaised(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
        {
            if (ReferenceEquals(peer, target) && property == AutomationProperty.NameProperty)
                Changes.Add(((string)oldValue!, (string)newValue!));
        }
    }
}
