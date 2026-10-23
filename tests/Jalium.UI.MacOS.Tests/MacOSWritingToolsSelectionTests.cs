using System.Reflection;
using System.Text.Json;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSWritingToolsSelectionTests
{
    public static IEnumerable<object[]> Editors()
    {
        for (int kind = 0; kind < 3; kind++)
            foreach (bool custom in new[] { false, true })
                yield return [kind, custom];
    }

    public static IEnumerable<object[]> ExactRanges()
    {
        foreach (var editor in Editors())
            foreach (var range in new[] { (0, 2), (2, 2), (4, 2), (6, 0), (0, 0) })
                foreach (bool readOnly in new[] { false, true })
                    yield return [editor[0], editor[1], range.Item1, range.Item2, readOnly];
    }

    public static IEnumerable<object[]> Retirements()
    {
        foreach (var editor in Editors())
            foreach (string change in new[] { "focus", "disabled", "detached", "text", "deactivated" })
                yield return [editor[0], editor[1], change];
    }

    [Theory, MemberData(nameof(ExactRanges))]
    public void ExactRangesRemainEligibleIncludingReadOnlyEditors(int kind, bool custom, int start, int length, bool readOnly)
    {
        using var h = new Host(kind, custom);
        h.ReadOnly(readOnly);
        Assert.True(h.Select(start, length));
        Assert.Equal(start, h.Source.SelectionStart);
        Assert.Equal(length, h.Source.SelectionLength);
        if (h.Editor is ObservedTextBox text) Assert.Equal(1, text.ScrollCalls);
    }

    [Theory, MemberData(nameof(Editors))]
    public void SelectionCallbackCannotAcknowledgeADifferentRange(int kind, bool custom)
    {
        using var h = new Host(kind, custom);
        int callbacks = 0;
        h.OnSelection(() => { if (callbacks++ == 0) h.Source.Select(4, 2); });
        Assert.False(h.Select(0, 2));
        Assert.Equal(4, h.Source.SelectionStart); Assert.Equal(2, h.Source.SelectionLength);
        if (h.Editor is ObservedTextBox text) Assert.Equal(0, text.ScrollCalls);
    }

    [Theory, MemberData(nameof(Editors))]
    public void ACallbackThatKeepsTheRequestedRangeIsAccepted(int kind, bool custom)
    {
        using var h = new Host(kind, custom);
        int callbacks = 0;
        h.OnSelection(() => callbacks++);
        Assert.True(h.Select(0, 2)); Assert.Equal(1, callbacks);
    }

    [Theory, MemberData(nameof(Retirements))]
    public void SelectionCallbackCannotScrollOrAcknowledgeARetiredContext(int kind, bool custom, string change)
    {
        using var h = new Host(kind, custom);
        bool changed = false;
        h.OnSelection(() =>
        {
            if (changed) return; changed = true;
            switch (change)
            {
                case "focus": Assert.True(h.Other.Focus()); break;
                case "disabled": h.Editor.IsEnabled = false; break;
                case "detached": h.Panel.Children.Remove(h.Editor); break;
                case "deactivated": h.Activate(false); break;
                case "text": h.SetText("replacement"); break;
            }
        });
        Assert.False(h.Select(0, 2)); Assert.True(changed);
        if (h.Editor is ObservedTextBox text) Assert.Equal(0, text.ScrollCalls);
    }

    [Theory]
    [InlineData(false, "selection")] [InlineData(true, "selection")]
    [InlineData(false, "text")] [InlineData(true, "text")]
    [InlineData(false, "focus")] [InlineData(true, "focus")]
    public void ScrollingCallbacksAreRevalidatedBeforeAcknowledgement(bool custom, string change)
    {
        using var h = new Host(0, custom);
        var text = (ObservedTextBox)h.Editor;
        text.OnScroll = () =>
        {
            if (change == "selection") h.Source.Select(4, 2);
            if (change == "text") h.SetText("replacement");
            if (change == "focus") Assert.True(h.Other.Focus());
        };
        Assert.False(h.Select(0, 2)); Assert.Equal(1, text.ScrollCalls);
        if (change == "selection") { Assert.Equal(4, h.Source.SelectionStart); Assert.Equal(2, h.Source.SelectionLength); }
    }

    [Theory]
    [InlineData(false, "a\u0301b", 1, 1)] [InlineData(true, "a\u0301b", 1, 1)]
    [InlineData(false, "👩‍💻!", 0, 2)] [InlineData(true, "👩‍💻!", 0, 2)]
    [InlineData(false, "🇨🇳!", 2, 2)] [InlineData(true, "🇨🇳!", 2, 2)]
    public void IncompleteGraphemesAreRejectedBeforeChangingSelection(bool custom, string text, int start, int length)
    {
        using var h = new Host(2, custom, text);
        Assert.False(h.Select(start, length));
        Assert.Equal(0, h.Source.SelectionStart); Assert.Equal(0, h.Source.SelectionLength);
    }

    [Theory, MemberData(nameof(Editors))]
    public void InvalidScalarBoundaryDoesNotChangeTheSelection(int kind, bool custom)
    {
        using var h = new Host(kind, custom);
        h.Source.Select(0, 2);
        Assert.False(h.Select(3, 0));
        Assert.Equal(0, h.Source.SelectionStart); Assert.Equal(2, h.Source.SelectionLength);
        if (h.Editor is ObservedTextBox text) Assert.Equal(0, text.ScrollCalls);
    }

    private sealed class ObservedTextBox : TextBox
    {
        internal Action? OnScroll;
        internal int ScrollCalls;
        protected override AutomationPeer? OnCreateAutomationPeer() => new Peer(this);
        private sealed class Peer(ObservedTextBox owner) : TextBoxAutomationPeer(owner), IAutomationTextProviderSource
        {
            void IAutomationTextProviderSource.ScrollIntoView(int start, int length)
            { owner.ScrollCalls++; owner.OnScroll?.Invoke(); }
        }
    }

    private sealed class Host : IDisposable
    {
        internal readonly DisplayedTestWindow Window;
        internal readonly StackPanel Panel = new();
        internal readonly TextBox Other = new() { Text = "other" };
        internal readonly Control Editor;
        internal readonly IAutomationTextProviderSource Source;
        private readonly string _editor;
        private readonly string _session = Guid.NewGuid().ToString("D");
        internal Host(int kind, bool custom, string text = "ab🙂中文")
        {
            Assert.True(OperatingSystem.IsMacOS(), "This suite requires macOS managed Window contracts.");
            Keyboard.Initialize(); Keyboard.ClearFocus();
            Editor = kind switch { 0 => new ObservedTextBox(), 1 => new RichTextBox(), _ => new EditControl() };
            SetText(text); Editor.Height = 100;
            Panel.Children.Add(Editor); Panel.Children.Add(Other);
            Window = new DisplayedTestWindow { Width = 500, Height = 300, Content = Panel,
                TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native };
            Window.Measure(new Size(500, 300)); Window.Arrange(new Rect(0, 0, 500, 300));
            Assert.True(Editor.Focus()); Activate(true);
            Source = ((AutomationTextProvider)UIElementAutomationPeer.CreatePeerForElement(Editor)!.GetPattern(PatternInterface.Text)!).Source;
            var query = Send(0, new { }); Assert.NotNull(query.Response);
            using var json = JsonDocument.Parse(query.Response);
            Assert.True(json.RootElement.GetProperty("canRead").GetBoolean());
            _editor = json.RootElement.GetProperty("editor").GetString()!;
            var context = Send(1, new { editor = _editor, session = _session }); Assert.NotNull(context.Response);
            if (Editor is ObservedTextBox observed) observed.ScrollCalls = 0;
        }
        internal void SetText(string text)
        {
            switch (Editor) { case TextBox box: box.Text = text; break; case RichTextBox rich: rich.Document = FlowDocument.FromText(text); break; case EditControl edit: edit.Text = text; break; }
        }
        internal void ReadOnly(bool value)
        { switch (Editor) { case TextBoxBase box: box.IsReadOnly = value; break; case EditControl edit: edit.IsReadOnly = value; break; } }
        internal void OnSelection(Action action)
        {
            if (Editor is TextBoxBase box) box.SelectionChanged += (_, _) => action();
            else ((EditControl)Editor).SelectionChanged += (_, _) => action();
        }
        internal void Activate(bool value) => typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Window, [new PlatformEvent { Type = value ? PlatformEventType.Activate : PlatformEventType.Deactivate }]);
        private PlatformTextServicesRequest Send(int kind, object payload)
        {
            var request = new PlatformTextServicesRequest(kind, 0, 0, 0, JsonSerializer.Serialize(payload));
            typeof(Window).GetMethod("HandleMacOSTextServicesRequest", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Window, [request]);
            return request;
        }
        internal bool Select(int start, int length)
        {
            var request = Send(3, new { editor = _editor, session = _session, ranges = new[] { new[] { start, length } } });
            if (request.Response == null) return false;
            using var json = JsonDocument.Parse(request.Response);
            return json.RootElement.GetProperty("applied").GetBoolean();
        }
        public void Dispose() { Keyboard.ClearFocus(); Window.Close(); }
    }
}
