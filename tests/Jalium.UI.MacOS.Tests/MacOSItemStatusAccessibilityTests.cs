using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSItemStatusAccessibilityTests
{
    [Theory]
    [InlineData("已选中")]
    [InlineData("未选中")]
    [InlineData("已开启")]
    [InlineData("已关闭 中文🙂")]
    public void CustomButtonKeepsItsNameAndHelpWhileExposingLiveLocalizedState(string status)
    {
        var button = new Button { Content = "手动" };
        AutomationProperties.SetName(button, "权限模式");
        AutomationProperties.SetHelpText(button, "所有工具调用都需要确认");
        AutomationProperties.SetItemStatus(button, status);
        using var fixture = new Fixture(button);
        Assert.True(fixture.Info().Flags.HasFlag(MacOSAXFlags.ValueDescription));
        Assert.Equal("权限模式", fixture.Read(MacOSAXString.Name));
        Assert.Equal("所有工具调用都需要确认", fixture.Read(MacOSAXString.Help));
        Assert.Equal(status, fixture.Read(MacOSAXString.ValueDescription));
        Assert.Null(button.GetAutomationPeer()!.GetPattern(PatternInterface.Value));

        button.IsEnabled = false;
        AutomationProperties.SetItemStatus(button, "状态已改变🙂");
        Assert.Equal("状态已改变🙂", fixture.Read(MacOSAXString.ValueDescription));
        AutomationProperties.SetItemStatus(button, string.Empty);
        Assert.False(fixture.Info().Flags.HasFlag(MacOSAXFlags.ValueDescription));
        Assert.Equal(string.Empty, fixture.Read(MacOSAXString.ValueDescription));
        Assert.Equal("权限模式", fixture.Read(MacOSAXString.Name));
    }

    [Fact]
    public void ProtectedContentDoesNotExposeStatusAsAValueDescription()
    {
        var password = new PasswordBox { Password = "never exposed" };
        AutomationProperties.SetItemStatus(password, "private status");
        using var fixture = new Fixture(password);
        Assert.False(fixture.Info().Flags.HasFlag(MacOSAXFlags.ValueDescription));
        Assert.Equal(string.Empty, fixture.Read(MacOSAXString.ValueDescription));
    }

    [Fact]
    public void DetachedButtonDoesNotKeepItsOldDescription()
    {
        var button = new Button();
        AutomationProperties.SetItemStatus(button, "已开启");
        using var fixture = new Fixture(button);
        Assert.Equal("已开启", fixture.Read(MacOSAXString.ValueDescription));
        fixture.Window.Content = null;
        var request = new MacOSAXRequest { NodeId = fixture.Id, Operation = MacOSAXOperation.String, Index = (int)MacOSAXString.ValueDescription };
        Assert.False(fixture.Tree.Handle(ref request));
    }

    [Fact]
    public void StatusChangesNotifyTheExistingPeerWithoutCreatingAnUnqueriedPeer()
    {
        var button = new Button();
        using var sink = new RecordingSink();
        AutomationProperties.SetItemStatus(button, "未选中");
        Assert.Null(button.GetExistingAutomationPeer());
        Assert.Empty(sink.Changes);
        var peer = button.GetAutomationPeer();
        AutomationProperties.SetItemStatus(button, "已选中");
        var change = Assert.Single(sink.Changes);
        Assert.Same(peer, change.Peer);
        Assert.Same(AutomationProperty.ItemStatusProperty, change.Property);
        Assert.Equal("未选中", change.Old);
        Assert.Equal("已选中", change.New);
        AutomationProperties.SetItemStatus(button, "已选中");
        Assert.Single(sink.Changes);
    }

    private sealed class RecordingSink : IAutomationEventSink, IDisposable
    {
        private readonly IAutomationEventSink? _previous = AutomationPeer.EventSink;
        internal readonly List<(AutomationPeer Peer, AutomationProperty Property, object? Old, object? New)> Changes = [];
        internal RecordingSink() => AutomationPeer.EventSink = this;
        public void OnPropertyChangedRaised(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
        {
            _previous?.OnPropertyChangedRaised(peer, property, oldValue, newValue);
            if (property == AutomationProperty.ItemStatusProperty) Changes.Add((peer, property, oldValue, newValue));
        }
        public void OnAutomationEventRaised(AutomationPeer peer, AutomationEvents eventId) => _previous?.OnAutomationEventRaised(peer, eventId);
        public void OnFocusChanged(AutomationPeer peer) => _previous?.OnFocusChanged(peer);
        public void Dispose() => AutomationPeer.EventSink = _previous;
    }

    private sealed class Fixture : IDisposable
    {
        internal DisplayedTestWindow Window { get; }
        internal MacOSAccessibilityTree Tree { get; }
        internal ulong Id { get; }
        internal Fixture(UIElement content)
        {
            Window = new DisplayedTestWindow { Width = 420, Height = 180, Content = content };
            Window.Measure(new Size(420, 180));
            Window.Arrange(new Rect(0, 0, 420, 180));
            Tree = new MacOSAccessibilityTree(Window);
            var child = new MacOSAXRequest { NodeId = 1, Operation = MacOSAXOperation.Child };
            Assert.True(Tree.Handle(ref child));
            Id = child.ResultId;
        }
        internal MacOSAXRequest Info()
        {
            var request = new MacOSAXRequest { NodeId = Id, Operation = MacOSAXOperation.Info, Index = -1 };
            Assert.True(Tree.Handle(ref request));
            return request;
        }
        internal string Read(MacOSAXString property)
        {
            char* buffer = stackalloc char[128];
            var request = new MacOSAXRequest { NodeId = Id, Operation = MacOSAXOperation.String, Index = (int)property, Text = buffer, TextCapacity = 128 };
            Assert.True(Tree.Handle(ref request));
            return new string(buffer, 0, request.TextCount);
        }
        public void Dispose() { Tree.Dispose(); Window.Close(); }
    }
}
