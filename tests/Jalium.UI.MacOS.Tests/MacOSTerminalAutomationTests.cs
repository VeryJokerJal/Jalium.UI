using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSTerminalAutomationTests
{
    private static Terminal Create() => new()
    {
        AutoStartShell = false, AutoSize = false, TerminalColumns = 32, TerminalRows = 3
    };

    [Fact]
    public void TerminalHasStableDocumentRoleAndReadOnlyOutputWithoutStartingAShell()
    {
        var terminal = Create();
        AutomationProperties.SetName(terminal, "终端输出");
        var peer = Assert.IsType<TerminalAutomationPeer>(terminal.GetAutomationPeer());
        Assert.Equal(AutomationControlType.Document, peer.GetAutomationControlType());
        Assert.Equal("终端输出", peer.GetName());
        Assert.True(peer.IsKeyboardFocusable());
        var value = Assert.IsAssignableFrom<IValueProvider>(peer.GetPattern(PatternInterface.Value));
        Assert.True(value.IsReadOnly);
        var text = Assert.IsAssignableFrom<ITextProvider>(peer.GetPattern(PatternInterface.Text));
        Assert.Equal(value.Value, text.DocumentRange.GetText(-1));
    }

    [Fact]
    public void OutputReadsTheInterpretedBufferAndRefreshesAfterOverwrite()
    {
        var terminal = Create();
        terminal.Write("\u001b[31mfirst\u001b[0m");
        var value = Assert.IsAssignableFrom<IValueProvider>(terminal.GetAutomationPeer()!.GetPattern(PatternInterface.Value));
        var first = value.Value;
        Assert.StartsWith("first", first);
        Assert.DoesNotContain("\u001b", first, StringComparison.Ordinal);
        Assert.Same(first, value.Value);
        terminal.Write("\rOK\u001b[K");
        Assert.StartsWith("OK ", value.Value);
        Assert.DoesNotContain("first", value.Value);
    }

    [Fact]
    public void PublicTextSelectionReadsUnicodeWithoutChangingTheBuffer()
    {
        var terminal = Create(); terminal.Write("alpha 中文🙂 omega");
        var peer = terminal.GetAutomationPeer()!;
        var value = Assert.IsAssignableFrom<IValueProvider>(peer.GetPattern(PatternInterface.Value));
        var text = Assert.IsAssignableFrom<ITextProvider>(peer.GetPattern(PatternInterface.Text));
        var original = value.Value;
        var range = text.DocumentRange.FindText("中文🙂", false, false);
        Assert.NotNull(range);
        range!.Select();
        Assert.Equal("中文🙂", terminal.GetSelectedText());
        Assert.Equal("中文🙂", Assert.Single(text.GetSelection()).GetText(-1));
        _ = value.Value; _ = text.GetVisibleRanges();
        Assert.Equal(original, value.Value);
        Assert.Equal("中文🙂", terminal.GetSelectedText());
        Assert.Throws<InvalidOperationException>(() => value.SetValue("echo unsafe"));
        Assert.Equal(original, value.Value);
    }

    [Fact]
    public void HistoryTextCanBeScrolledIntoTheActualTextViewport()
    {
        ThemeLoader.Initialize();
        var terminal = Create();
        var theme = Assert.IsType<ResourceDictionary>(ThemeLoader.LoadGenericTheme());
        terminal.Resources.MergedDictionaries.Add(theme);
        Assert.True(theme.TryGetValue(typeof(Terminal), out var implicitStyle));
        terminal.Style = Assert.IsType<Style>(implicitStyle);
        terminal.ApplyTemplate();
        // A style may already have applied the template; verify the real result, not
        // whether this idempotent call happened to create a new template instance.
        Assert.NotNull(terminal.TemplateRootInternal);
        terminal.Measure(new Size(640, 120)); terminal.Arrange(new Rect(0, 0, 640, 120));
        Assert.False(terminal.AutomationTextViewport.IsEmpty);
        for (int i = 0; i < 12; i++) terminal.WriteLine($"line{i:00}");
        terminal.UpdateLayout();
        var text = Assert.IsAssignableFrom<ITextProvider>(terminal.GetAutomationPeer()!.GetPattern(PatternInterface.Text));
        var first = text.DocumentRange.FindText("line00", false, false);
        Assert.NotNull(first);
        Assert.DoesNotContain("line00", string.Concat(text.GetVisibleRanges().Select(range => range.GetText(-1))));
        first!.ScrollIntoView(true);
        Assert.Contains("line00", string.Concat(text.GetVisibleRanges().Select(range => range.GetText(-1))));
        var bounds = first.GetBoundingRectangles();
        Assert.NotEmpty(bounds);
        Assert.Equal(0, bounds.Length % 4);
        Assert.All(bounds, coordinate => Assert.True(double.IsFinite(coordinate)));
    }

    [Fact]
    public void ConcealedAnsiCellsAreNotRevealedByTheOutputProvider()
    {
        var terminal = Create();
        terminal.Write("shown \u001b[8mhidden\u001b[0m ok");
        var peer = terminal.GetAutomationPeer()!;
        var value = Assert.IsAssignableFrom<IValueProvider>(peer.GetPattern(PatternInterface.Value));
        var text = Assert.IsAssignableFrom<ITextProvider>(peer.GetPattern(PatternInterface.Text));
        Assert.StartsWith("shown        ok", value.Value);
        Assert.DoesNotContain("hidden", value.Value);
        Assert.Equal(value.Value, text.DocumentRange.GetText(-1));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void AnsiScreenEraseAndSavedLineEraseAffectOnlyTheirOwnBuffers(int mode)
    {
        var terminal = Create();
        for (int i = 0; i < 12; i++) terminal.WriteLine($"old{i:00}");
        terminal.Write("keep-current");
        var buffer = Assert.IsType<Jalium.UI.Controls.TerminalEmulator.TerminalBuffer>(typeof(Terminal)
            .GetField("_buffer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(terminal));
        var cursor = (buffer.CursorRow, buffer.CursorCol);
        var history = buffer.ScrollbackCount;
        Assert.True(history > 0);
        var screen = string.Join("\n", Enumerable.Range(0, buffer.Rows).Select(buffer.GetRowTextRaw));
        var value = Assert.IsAssignableFrom<IValueProvider>(terminal.GetAutomationPeer()!.GetPattern(PatternInterface.Value));
        Assert.Contains("old00", value.Value);
        Assert.Contains("keep-current", value.Value);
        terminal.Write($"\u001b[{mode}J");
        Assert.Equal(cursor, (buffer.CursorRow, buffer.CursorCol));
        if (mode == 2)
        {
            Assert.Equal(history, buffer.ScrollbackCount);
            Assert.Contains("old00", value.Value);
            Assert.DoesNotContain("keep-current", value.Value);
        }
        else
        {
            Assert.Equal(0, buffer.ScrollbackCount);
            Assert.DoesNotContain("old00", value.Value);
            Assert.Contains("keep-current", value.Value);
            Assert.Equal(screen, string.Join("\n", Enumerable.Range(0, buffer.Rows).Select(buffer.GetRowTextRaw)));
        }
    }

    [Fact]
    public void ClearRemovesScrollbackAsWellAsVisibleText()
    {
        var terminal = Create();
        for (int i = 0; i < 12; i++) terminal.WriteLine($"old{i:00}");
        var value = Assert.IsAssignableFrom<IValueProvider>(terminal.GetAutomationPeer()!.GetPattern(PatternInterface.Value));
        Assert.Contains("old00", value.Value);
        terminal.Clear();
        Assert.DoesNotContain("old", value.Value);
        Assert.Equal("", terminal.GetSelectedText());
    }
}
