using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Data;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSEditControlAccessibilityTests
{
    [Theory]
    [InlineData("")]
    [InlineData("中文🙂")]
    [InlineData("一行\r\n第二行🙂")]
    public void EditorExposesValueAndTextAreaEvenWhenEmpty(string text)
    {
        using var fixture = new Fixture(text);
        var peer = fixture.Editor.GetAutomationPeer()!;
        Assert.Equal("EditControl", peer.GetClassName());
        Assert.Equal(AutomationControlType.Edit, peer.GetAutomationControlType());
        Assert.Equal(text, fixture.Value.Value);
        Assert.Equal(text, fixture.Text.DocumentRange.GetText(-1));
        var info = fixture.Request(MacOSAXOperation.Info);
        Assert.True(info.Flags.HasFlag(MacOSAXFlags.Multiline | MacOSAXFlags.Text | MacOSAXFlags.Value | MacOSAXFlags.Writable));
        Assert.Equal(text.Length, info.TextCount);
    }

    [Fact]
    public void ValueReplacementIsUndoableAndKeepsTextBinding()
    {
        using var fixture = new Fixture("original");
        var source = new TextBox { Text = "original" };
        var binding = new Binding("Text") { Source = source };
        fixture.Editor.SetBinding(EditControl.TextProperty, binding);
        fixture.Value.SetValue("中文🙂\nnew");
        Assert.Equal("中文🙂\nnew", fixture.Editor.Text);
        Assert.Equal(fixture.Editor.Text, fixture.Editor.Document.Text);
        Assert.Same(binding, BindingOperations.GetBindingBase(fixture.Editor, EditControl.TextProperty));
        Assert.True(fixture.Editor.CanUndo);
        fixture.Editor.Undo(); Assert.Equal("original", fixture.Value.Value);
        fixture.Editor.Redo(); Assert.Equal("中文🙂\nnew", fixture.Value.Value);
        source.Text = "source refresh";
        Assert.Equal("source refresh", fixture.Value.Value);
        Assert.Same(binding, BindingOperations.GetBindingBase(fixture.Editor, EditControl.TextProperty));
    }

    [Fact]
    public void ReadOnlySelectionRemainsReadableAndSnapsToGraphemes()
    {
        using var fixture = new Fixture("A🙂e\u0301中");
        fixture.Editor.IsReadOnly = true;
        Assert.True(fixture.Select(2, 1));
        Assert.Equal("🙂", Assert.Single(fixture.Text.GetSelection()).GetText(-1));
        Assert.Equal(1, fixture.Editor.SelectionStart); Assert.Equal(2, fixture.Editor.SelectionLength);
        Assert.True(fixture.Select(4, 1));
        Assert.Equal("e\u0301", fixture.Editor.SelectedText);
        Assert.False(fixture.Select(-1, 1)); Assert.False(fixture.Select(99, 1));
        Assert.Equal("e\u0301", fixture.Editor.SelectedText);
        Assert.Throws<InvalidOperationException>(() => fixture.Value.SetValue("forbidden"));
        Assert.Equal("A🙂e\u0301中", fixture.Value.Value);
    }

    [Fact]
    public void DisabledWindowBlocksDirectAndBridgeEditing()
    {
        using var fixture = new Fixture("disabled");
        fixture.Window.IsEnabled = false;
        Assert.Throws<InvalidOperationException>(() => fixture.Value.SetValue("forbidden"));
        Assert.False(fixture.Select(0, 1));
        Assert.Equal("disabled", fixture.Editor.Text);
        fixture.Window.IsEnabled = true;
        fixture.Value.SetValue("enabled");
        Assert.Equal("enabled", fixture.Value.Value);
    }

    [Fact]
    public void TextSelectionAndCaretChangesNotifyTheCachedPeerOnce()
    {
        using var fixture = new Fixture("original");
        _ = fixture.Text;
        var previous = AutomationPeer.EventSink;
        var sink = new RecordingSink(fixture.Editor); AutomationPeer.EventSink = sink;
        try
        {
            fixture.Editor.Text = "通知🙂";
            fixture.Editor.Select(2, 2); fixture.Editor.CaretOffset = 0;
            Assert.Contains(("original", "通知🙂"), sink.Values);
            Assert.Single(sink.Events, e => e == AutomationEvents.TextPatternOnTextChanged);
            Assert.Equal(2, sink.Events.Count(e => e == AutomationEvents.TextPatternOnTextSelectionChanged));
            fixture.Editor.Select(0, 0);
            Assert.Equal(2, sink.Events.Count(e => e == AutomationEvents.TextPatternOnTextSelectionChanged));
        }
        finally { AutomationPeer.EventSink = previous; }
    }

    [Fact]
    public void GeometryUsesVisibleRowsAndScrollsTheRequestedRangeWithoutChangingSelection()
    {
        using var fixture = new Fixture(string.Join('\n', Enumerable.Range(1, 100).Select(i => $"line{i}🙂")));
        var source = (IAutomationTextProviderSource)fixture.Editor.GetAutomationPeer()!;
        var top = Assert.Single(source.GetBoundingRectangles(0, 5));
        // Logical tests do not own a native font context. HostSmoke verifies shaped widths.
        Assert.True(top.Width > 0 && top.Height > 0 && top.X > 0,
            $"bounds={top}, fontSize={fixture.Editor.FontSize}, lineNumbers={fixture.Editor.ShowLineNumbers}, viewport={fixture.Editor.RenderSize}");
        int tail = fixture.Editor.Text.LastIndexOf("line100", StringComparison.Ordinal);
        Assert.Empty(source.GetBoundingRectangles(tail, 5));
        source.ScrollIntoView(tail, 5);
        Assert.Single(source.GetBoundingRectangles(tail, 5));
        Assert.Empty(source.GetBoundingRectangles(0, 5));
        Assert.Equal(0, fixture.Editor.SelectionStart); Assert.Equal(0, fixture.Editor.SelectionLength);
    }

    [Fact]
    public void ProgrammaticDocumentShorteningReportsCoercedSelection()
    {
        using var fixture = new Fixture("long document");
        _ = fixture.Text; fixture.Editor.Select(5, 8);
        var previous = AutomationPeer.EventSink;
        var sink = new RecordingSink(fixture.Editor); AutomationPeer.EventSink = sink;
        try
        {
            fixture.Editor.Text = "a";
            Assert.Equal(1, fixture.Editor.SelectionStart); Assert.Equal(0, fixture.Editor.SelectionLength);
            Assert.Single(sink.Events, e => e == AutomationEvents.TextPatternOnTextSelectionChanged);
        }
        finally { AutomationPeer.EventSink = previous; }
    }

    [Fact]
    public void ThrowingUserCallbackCannotSuppressTextNotifications()
    {
        using var fixture = new Fixture("before");
        fixture.Editor.TextChanged += (_, _) => throw new InvalidOperationException("expected callback failure");
        _ = fixture.Value;
        var previous = AutomationPeer.EventSink;
        var sink = new RecordingSink(fixture.Editor); AutomationPeer.EventSink = sink;
        try
        {
            Assert.Throws<InvalidOperationException>(() => fixture.Value.SetValue("after🙂"));
            Assert.Equal("after🙂", fixture.Value.Value);
            Assert.Contains(("before", "after🙂"), sink.Values);
            Assert.Single(sink.Events, e => e == AutomationEvents.TextPatternOnTextChanged);
        }
        finally { AutomationPeer.EventSink = previous; }
    }

    [Fact]
    public void FoldedLinesDoNotExposeVisibleGeometry()
    {
        using var fixture = new Fixture("header {\n hidden🙂\n}\nlast");
        var source = (IAutomationTextProviderSource)fixture.Editor.GetAutomationPeer()!;
        int hidden = fixture.Editor.Text.IndexOf("hidden", StringComparison.Ordinal);
        Assert.NotEmpty(source.GetBoundingRectangles(hidden, 2));
        Assert.True(fixture.Editor.ToggleFold(1));
        Assert.Empty(source.GetBoundingRectangles(hidden, 2));
        Assert.True(fixture.Editor.ToggleFold(1));
        Assert.NotEmpty(source.GetBoundingRectangles(hidden, 2));
    }

    private sealed class Fixture : IDisposable
    {
        internal DisplayedTestWindow Window { get; }
        internal EditControl Editor { get; }
        private readonly MacOSAccessibilityTree _tree;
        private readonly ulong _id;
        internal IValueProvider Value => Assert.IsAssignableFrom<IValueProvider>(Editor.GetAutomationPeer()!.GetPattern(PatternInterface.Value));
        internal ITextProvider Text => Assert.IsAssignableFrom<ITextProvider>(Editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text));
        internal Fixture(string text)
        {
            Editor = new EditControl { Text = text, Height = 120, IsScrollInertiaEnabled = false };
            Window = new DisplayedTestWindow { TitleBarStyle = WindowTitleBarStyle.Native, Width = 420, Height = 200, Content = Editor };
            Window.Measure(new Size(420, 200)); Window.Arrange(new Rect(0, 0, 420, 200));
            _tree = new(Window);
            var child = new MacOSAXRequest { NodeId = 1, Operation = MacOSAXOperation.Child, Index = 0 };
            Assert.True(_tree.Handle(ref child)); _id = child.ResultId;
        }
        internal MacOSAXRequest Request(MacOSAXOperation operation)
        {
            var request = new MacOSAXRequest { NodeId = _id, Operation = operation };
            Assert.True(_tree.Handle(ref request)); return request;
        }
        internal bool Select(int start, int length)
        {
            var request = new MacOSAXRequest { NodeId = _id, Operation = MacOSAXOperation.SetTextSelection, TextStart = start, TextLength = length };
            return _tree.Handle(ref request);
        }
        public void Dispose() { Keyboard.Focus(null); Window.Close(); }
    }

    private sealed class RecordingSink(EditControl owner) : IAutomationEventSink
    {
        internal List<AutomationEvents> Events { get; } = [];
        internal List<(string? Before, string? After)> Values { get; } = [];
        public void OnAutomationEventRaised(AutomationPeer peer, AutomationEvents eventId)
        { if (ReferenceEquals(peer.Owner, owner)) Events.Add(eventId); }
        public void OnPropertyChangedRaised(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
        { if (ReferenceEquals(peer.Owner, owner) && property == AutomationProperty.ValueProperty) Values.Add((oldValue as string, newValue as string)); }
        public void OnFocusChanged(AutomationPeer peer) { }
    }
}
