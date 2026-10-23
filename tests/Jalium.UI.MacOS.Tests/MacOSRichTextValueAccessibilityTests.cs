using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSRichTextValueAccessibilityTests
{
    [Fact]
    public void NativeWholeValueEditingDoesNotAddAUIAValuePattern()
    {
        using var h = new Host();
        Assert.Null(h.Editor.GetAutomationPeer()!.GetPattern(PatternInterface.Value));
        Assert.NotNull(h.Editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text));
        Assert.True(h.Info().Flags.HasFlag(MacOSAXFlags.Text | MacOSAXFlags.Value | MacOSAXFlags.Writable));
        Assert.True(h.Write("跨🙂 e\u0301 中文"));
        Assert.Equal("跨🙂 e\u0301 中文\n", h.Text);
        Assert.True(h.Write(""));
        Assert.Equal("\n", h.Text);
    }

    [Fact]
    public void OneNativeWriteUndoesAndRedoesTheDocumentFormattingAndSelection()
    {
        using var h = new Host();
        var document = h.Editor.Document;
        Assert.True(((IImeSupport)h.Editor).TrySetImeSelection(1, 2));
        int originalStart = h.Editor.Selection.Start.DocumentOffset;
        int originalEnd = h.Editor.Selection.End.DocumentOffset;
        Assert.True(h.Write("替换🙂\n第二行"));
        Assert.Equal("替换🙂\n第二行\n", h.Text);
        Assert.True(h.Editor.Undo());
        Assert.Same(document, h.Editor.Document);
        Assert.Equal("原文🙂 e\u0301\n", h.Text);
        Assert.Same(h.Run, Assert.Single(Assert.Single(document.Blocks.OfType<Paragraph>()).Inlines));
        Assert.Equal(FontWeights.Bold, h.Run.FontWeight);
        Assert.Equal(FontStyles.Italic, h.Run.FontStyle);
        Assert.Equal(originalStart, h.Editor.Selection.Start.DocumentOffset);
        Assert.Equal(originalEnd, h.Editor.Selection.End.DocumentOffset);
        Assert.False(h.Editor.CanUndo);
        Assert.True(h.Editor.Redo());
        Assert.Equal("替换🙂\n第二行\n", h.Text);
        Assert.False(h.Editor.CanRedo);
    }

    [Theory]
    [InlineData("readonly")] [InlineData("disabled")] [InlineData("window-disabled")]
    [InlineData("hidden")] [InlineData("detached")] [InlineData("closed")] [InlineData("disposed")]
    public void InvalidEditingContextsRejectWritesWithoutChangingTextOrHistory(string mode)
    {
        using var h = new Host();
        switch (mode)
        {
            case "readonly": h.Editor.IsReadOnly = true;
                Assert.False(h.Info().Flags.HasFlag(MacOSAXFlags.Writable)); break;
            case "disabled": h.Editor.IsEnabled = false; break;
            case "window-disabled": h.Window.IsEnabled = false; break;
            case "hidden": h.Editor.Visibility = Visibility.Collapsed; break;
            case "detached": h.Window.Content = null; break;
            case "closed": h.Window.Close(); break;
            case "disposed": h.Tree.Dispose(); break;
        }
        Assert.False(h.Write("禁止写入"));
        Assert.Equal("原文🙂 e\u0301\n", h.Text);
        Assert.False(h.Editor.CanUndo);
    }

    [Theory]
    [InlineData(-1, 4, false)] [InlineData(5, 4, false)]
    [InlineData(1, 4, true)] [InlineData(16 * 1024 * 1024 + 1, int.MaxValue, false)]
    public void MalformedNativeBuffersAreRejectedBeforeReading(int count, int capacity, bool nullBuffer)
    {
        using var h = new Host();
        char* text = stackalloc char[4];
        var request = new MacOSAXRequest { NodeId = h.Id, Operation = MacOSAXOperation.SetValue,
            Text = nullBuffer ? null : text, TextCount = count, TextCapacity = capacity };
        Assert.False(h.Tree.Handle(ref request));
        Assert.Equal("原文🙂 e\u0301\n", h.Text);
        Assert.False(h.Editor.CanUndo);
    }

    [Fact]
    public void TextChangedCallbackSeesTheCommittedWriteAndItsNewerEditWins()
    {
        using var h = new Host();
        var observed = new List<string>();
        h.Editor.TextChanged += (_, _) =>
        {
            observed.Add(h.Text);
            if (observed.Count == 1)
                h.Editor.Document = new FlowDocument(new Paragraph(new Run("回调编辑")));
        };
        Assert.True(h.Write("AX写入"));
        Assert.Equal("AX写入\n", observed[0]);
        Assert.Equal("回调编辑\n", h.Text);
    }

    private sealed class Host : IDisposable
    {
        internal readonly Run Run = new("原文🙂 e\u0301") { FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic };
        internal readonly RichTextBox Editor;
        internal readonly DisplayedTestWindow Window;
        internal readonly MacOSAccessibilityTree Tree;
        internal readonly ulong Id;
        internal string Text => Editor.Document.GetText();
        internal Host()
        {
            Editor = new RichTextBox(new FlowDocument(new Paragraph(Run))) { Height = 120 };
            Window = new DisplayedTestWindow { TitleBarStyle = WindowTitleBarStyle.Native, Width = 420, Height = 220, Content = Editor };
            Window.Measure(new Size(420, 220)); Window.Arrange(new Rect(0, 0, 420, 220));
            Tree = new(Window);
            var request = new MacOSAXRequest { NodeId = 1, Operation = MacOSAXOperation.Child };
            Assert.True(Tree.Handle(ref request)); Id = request.ResultId;
            Editor.IsUndoEnabled = false; Editor.IsUndoEnabled = true;
        }
        internal MacOSAXRequest Info()
        {
            var request = new MacOSAXRequest { NodeId = Id, Operation = MacOSAXOperation.Info };
            Assert.True(Tree.Handle(ref request)); return request;
        }
        internal bool Write(string value)
        {
            fixed (char* text = value)
            {
                var request = new MacOSAXRequest { NodeId = Id, Operation = MacOSAXOperation.SetValue,
                    Text = text, TextCount = value.Length, TextCapacity = value.Length };
                return Tree.Handle(ref request);
            }
        }
        public void Dispose() { Tree.Dispose(); Keyboard.Focus(null); Window.Close(); }
    }
}
