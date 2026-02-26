using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Editor;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class EditControlTests
{
    [Fact]
    public void Language_Default_ShouldBePlaintext()
    {
        var editor = new EditControl();
        Assert.Equal("plaintext", editor.Language);
    }

    [Fact]
    public void Language_CSharp_ShouldApplyCSharpHighlighter()
    {
        var editor = new EditControl();
        editor.Language = "csharp";
        Assert.IsType<RegexSyntaxHighlighter>(editor.SyntaxHighlighter);
    }

    [Fact]
    public void Language_Jalxaml_ShouldApplyJalxamlHighlighter()
    {
        var editor = new EditControl();
        editor.Language = "jalxaml";
        Assert.IsType<JalxamlSyntaxHighlighter>(editor.SyntaxHighlighter);
    }

    [Fact]
    public void ShowMinimap_Default_ShouldBeTrue()
    {
        var editor = new EditControl();
        Assert.True(editor.ShowMinimap);
    }

    [Fact]
    public void Language_Xml_ShouldUseXmlTagFolding()
    {
        var editor = new EditControl();
        editor.Language = "xml";
        editor.LoadText("<Root>\n  <Item />\n</Root>\n");

        var manager = GetPrivateFoldingManager(editor);
        var section = manager.GetFoldingAt(1);

        Assert.NotNull(section);
        Assert.Equal(3, section!.EndLine);
        Assert.Equal(1, section.GuideStartLine);
    }

    [Fact]
    public void Language_Jalxaml_ShouldUseXmlTagFolding()
    {
        var editor = new EditControl();
        editor.Language = "jalxaml";
        editor.LoadText("<Window>\n  <Grid>\n  </Grid>\n</Window>\n");

        var manager = GetPrivateFoldingManager(editor);

        Assert.NotNull(manager.GetFoldingAt(1));
        Assert.NotNull(manager.GetFoldingAt(2));
    }

    [Fact]
    public void ExternalTextSet_ShouldClampCaretAndSelection()
    {
        var editor = new EditControl();
        editor.LoadText("abcdef");
        editor.CaretOffset = 6;
        editor.SetSelection(2, 3);

        editor.Text = "x";

        Assert.InRange(editor.CaretOffset, 0, editor.Document.TextLength);
        Assert.InRange(editor.SelectionStart, 0, editor.Document.TextLength);
        Assert.InRange(editor.SelectionLength, 0, editor.Document.TextLength - editor.SelectionStart);
    }

    [Fact]
    public void DocumentChange_ShouldSyncTextAndRaiseTextChanged()
    {
        var editor = new EditControl();
        int raised = 0;
        editor.TextChanged += (_, _) => raised++;

        editor.Document.Insert(0, "abc");

        Assert.Equal("abc", editor.Text);
        Assert.True(raised > 0);
    }

    [Fact]
    public void TextInput_Backspace_Delete_ShouldKeepTextAndDocumentInSync()
    {
        var editor = new EditControl();
        editor.LoadText("ab");
        editor.CaretOffset = editor.Document.TextLength;

        editor.RaiseEvent(new TextCompositionEventArgs(UIElement.TextInputEvent, "c", 0));
        Assert.Equal(editor.Document.Text, editor.Text);
        Assert.Equal("abc", editor.Text);

        editor.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Back, ModifierKeys.None, true, false, 1));
        Assert.Equal(editor.Document.Text, editor.Text);
        Assert.Equal("ab", editor.Text);

        editor.CaretOffset = 0;
        editor.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Delete, ModifierKeys.None, true, false, 2));
        Assert.Equal(editor.Document.Text, editor.Text);
        Assert.Equal("b", editor.Text);
    }

    [Fact]
    public void CanUndoAndCanRedo_ShouldTrackUndoStackState()
    {
        var editor = new EditControl();
        editor.LoadText("a");

        Assert.False(editor.CanUndo);
        Assert.False(editor.CanRedo);

        editor.Document.Insert(editor.Document.TextLength, "b");
        Assert.True(editor.CanUndo);
        Assert.False(editor.CanRedo);

        editor.Undo();
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);

        editor.Redo();
        Assert.True(editor.CanUndo);
        Assert.False(editor.CanRedo);
    }

    [Fact]
    public void SetSelection_ShouldUpdateSelectionProperties()
    {
        var editor = new EditControl();
        editor.LoadText("hello world");
        editor.SetSelection(2, 5);

        Assert.Equal(2, editor.SelectionStart);
        Assert.Equal(5, editor.SelectionLength);
    }

    [Fact]
    public void SelectionAndCaretEvents_ShouldRaiseOnSetSelection()
    {
        var editor = new EditControl();
        editor.LoadText("hello");

        int selectionRaised = 0;
        int caretRaised = 0;
        editor.SelectionChanged += (_, _) => selectionRaised++;
        editor.CaretPositionChanged += (_, _) => caretRaised++;

        editor.SetSelection(1, 3);

        Assert.True(selectionRaised > 0);
        Assert.True(caretRaised > 0);
    }

    [Fact]
    public void PageUpAndPageDown_BeforeFirstRender_ShouldNotThrow()
    {
        var editor = new EditControl();
        editor.LoadText("a\nb\nc\nd\ne");
        editor.CaretOffset = 2;

        var pageUp = new KeyEventArgs(UIElement.KeyDownEvent, Key.PageUp, ModifierKeys.None, true, false, 0);
        editor.RaiseEvent(pageUp);

        var pageDown = new KeyEventArgs(UIElement.KeyDownEvent, Key.PageDown, ModifierKeys.None, true, false, 1);
        editor.RaiseEvent(pageDown);

        Assert.True(pageUp.Handled);
        Assert.True(pageDown.Handled);
        Assert.InRange(editor.CaretOffset, 0, editor.Document.TextLength);
    }

    [Fact]
    public void MultiLineIndentAndUnindent_ShouldPreserveSelectionSemantics()
    {
        var editor = new EditControl();
        editor.LoadText("a\nb\nc");
        editor.SetSelection(0, 3);

        Assert.True(editor.ExecuteEditorCommand(EditorCommands.IndentLine));
        Assert.Equal("    a\n    b\nc", editor.Text);
        Assert.Equal(4, editor.SelectionStart);
        Assert.Equal(7, editor.SelectionLength);

        Assert.True(editor.ExecuteEditorCommand(EditorCommands.UnindentLine));
        Assert.Equal("a\nb\nc", editor.Text);
        Assert.Equal(0, editor.SelectionStart);
        Assert.Equal(3, editor.SelectionLength);
    }

    [Fact]
    public void DuplicateLineOrSelection_WithoutSelection_ShouldDuplicateCurrentLine()
    {
        var editor = new EditControl();
        editor.LoadText("alpha\nbeta");
        editor.CaretOffset = 1;

        editor.DuplicateLineOrSelection();

        Assert.Equal("alpha\nalpha\nbeta", editor.Text);
    }

    [Fact]
    public void DeleteLineOrSelection_WithoutSelection_ShouldDeleteCurrentLine()
    {
        var editor = new EditControl();
        editor.LoadText("alpha\nbeta\ngamma");
        editor.CaretOffset = 2;

        editor.DeleteLineOrSelection();

        Assert.Equal("beta\ngamma", editor.Text);
    }

    [Fact]
    public void MoveLineUp_ShouldMoveCurrentLineAbove()
    {
        var editor = new EditControl();
        editor.LoadText("one\ntwo\nthree");
        editor.CaretOffset = 5; // inside "two"

        bool moved = editor.MoveLineUp();

        Assert.True(moved);
        Assert.Equal("two\none\nthree", editor.Text);
    }

    [Fact]
    public void MoveLineDown_ShouldMoveCurrentLineBelow()
    {
        var editor = new EditControl();
        editor.LoadText("one\ntwo\nthree");
        editor.CaretOffset = 1; // inside "one"

        bool moved = editor.MoveLineDown();

        Assert.True(moved);
        Assert.Equal("two\none\nthree", editor.Text);
    }

    [Fact]
    public void ToggleLineComment_CSharp_ShouldCommentAndUncomment()
    {
        var editor = new EditControl { Language = "csharp" };
        editor.LoadText("int x = 1;");
        editor.CaretOffset = 0;

        editor.ToggleLineComment();
        Assert.StartsWith("//", editor.Text);

        editor.ToggleLineComment();
        Assert.Equal("int x = 1;", editor.Text);
    }

    [Fact]
    public void FindAndReplace_ShouldUpdateMatches()
    {
        var editor = new EditControl();
        editor.LoadText("foo bar foo");

        var all = editor.FindAll("foo");
        Assert.Equal(2, all.Count);

        var first = editor.FindNext();
        Assert.True(first.HasValue);
        Assert.Equal(3, editor.SelectionLength);

        bool replaced = editor.ReplaceCurrent("baz");
        Assert.True(replaced);
        Assert.Equal("baz bar foo", editor.Text);
    }

    [Fact]
    public void ReplaceAll_ShouldReturnReplacementCount()
    {
        var editor = new EditControl();
        editor.LoadText("foo foo foo");
        editor.FindAll("foo");

        int count = editor.ReplaceAll("bar");

        Assert.Equal(3, count);
        Assert.Equal("bar bar bar", editor.Text);
    }

    [Fact]
    public void SearchResults_ShouldRefreshAfterDocumentMutation()
    {
        var editor = new EditControl();
        editor.LoadText("foo bar");

        int raised = 0;
        editor.SearchResultsChanged += (_, _) => raised++;

        var results = editor.FindAll("foo");
        Assert.Single(results);

        editor.Document.Insert(editor.Document.TextLength, " foo");

        var refreshed = editor.FindAll("foo");
        Assert.Equal(2, refreshed.Count);
        Assert.True(raised >= 2);
    }

    [Fact]
    public void FoldingApi_ShouldToggleAndRaiseEvent()
    {
        var editor = new EditControl { Language = "csharp" };
        editor.LoadText("{\n    x\n}\n");

        int raised = 0;
        editor.FoldingChanged += (_, _) => raised++;

        bool toggled = editor.ToggleFold(1);
        Assert.True(toggled);

        editor.FoldAll();
        editor.UnfoldAll();

        Assert.True(raised >= 3);
    }

    [Fact]
    public void FoldingApi_ShouldToggleRegionFold()
    {
        var editor = new EditControl { Language = "csharp" };
        editor.LoadText("#region Sample\nint x;\n#endregion\n");

        bool toggled = editor.ToggleFold(1);

        Assert.True(toggled);
    }

    [Fact]
    public void FoldAllAndUnfoldAll_ShouldSwitchFoldStates()
    {
        var editor = new EditControl { Language = "csharp" };
        editor.LoadText("{\n  if (a)\n  {\n    b\n  }\n}\n");

        var manager = GetPrivateFoldingManager(editor);
        Assert.NotEmpty(manager.Foldings);

        editor.FoldAll();
        Assert.All(manager.Foldings, section => Assert.True(section.IsFolded));

        editor.UnfoldAll();
        Assert.All(manager.Foldings, section => Assert.False(section.IsFolded));
    }

    [Fact]
    public void ToggleFold_WhenCaretInsideFoldedBody_ShouldMoveCaretToFoldHeaderLine()
    {
        var editor = new EditControl { Language = "csharp" };
        editor.LoadText("{\n  body\n}\n");

        int bodyOffset = editor.Text.IndexOf("body", StringComparison.Ordinal);
        Assert.True(bodyOffset >= 0);
        editor.CaretOffset = bodyOffset;

        Assert.True(editor.ToggleFold(1));

        var firstLine = editor.Document.GetLineByNumber(1);
        Assert.Equal(firstLine.Offset + firstLine.Length, editor.CaretOffset);
        Assert.Equal(0, editor.SelectionLength);
    }

    [Fact]
    public void CaretNearBracket_ShouldUpdateActiveBracketPair()
    {
        var editor = new EditControl();
        editor.LoadText("a(b)c");

        editor.CaretOffset = 2; // right after '('
        var pair = GetPrivateActiveBracketPair(editor);

        Assert.True(pair.HasValue);
        Assert.Equal(1, pair.Value.bracketOffset);
        Assert.Equal(3, pair.Value.matchOffset);

        editor.CaretOffset = 0;
        Assert.False(GetPrivateActiveBracketPair(editor).HasValue);
    }

    [Fact]
    public void ExecuteEditorCommand_ShouldHandleKnownAndUnknownCommands()
    {
        var editor = new EditControl();
        editor.LoadText("x");

        Assert.True(editor.ExecuteEditorCommand(EditorCommands.SelectAll));
        Assert.False(editor.ExecuteEditorCommand("Edit.DoesNotExist"));
        Assert.Equal("x", editor.Text);
    }

    [Fact]
    public void ImeCompositionState_ShouldToggleCorrectly()
    {
        var editor = new EditControl();
        editor.LoadText("abc");
        editor.CaretOffset = 1;

        editor.OnImeCompositionStart();
        Assert.True(editor.IsImeComposing);

        editor.OnImeCompositionUpdate("zhong", 1);
        Assert.True(editor.IsImeComposing);

        editor.OnImeCompositionEnd("zhong");
        Assert.False(editor.IsImeComposing);
    }

    [Fact]
    public void ScrollBars_ShouldShowVerticalForManyLines()
    {
        var editor = new EditControl { ShowMinimap = false };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 120).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 220, 120));
        editor.UpdateScrollBarsForTesting(new Size(220, 120));

        Assert.True(editor.IsVerticalScrollBarVisibleForTesting);
        Assert.False(editor.IsHorizontalScrollBarVisibleForTesting);
    }

    [Fact]
    public void ScrollBars_ShouldShowHorizontalForLongLine()
    {
        var editor = new EditControl { ShowMinimap = false };
        editor.LoadText(new string('x', 400));
        editor.Arrange(new Rect(0, 0, 220, 120));
        editor.UpdateScrollBarsForTesting(new Size(220, 120));

        Assert.True(editor.IsHorizontalScrollBarVisibleForTesting);
    }

    [Fact]
    public void VerticalScrollBarThumb_Drag_ShouldChangeVerticalOffset()
    {
        var editor = new EditControl { ShowMinimap = false };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 200).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 240, 120));
        editor.UpdateScrollBarsForTesting(new Size(240, 120));
        var thumb = editor.VerticalScrollBarThumbRectForTesting;
        Assert.False(thumb.IsEmpty);

        var start = new Point(thumb.X + thumb.Width / 2, thumb.Y + thumb.Height / 2);
        editor.RaiseEvent(CreateMouseDown(start));
        editor.RaiseEvent(CreateMouseMove(new Point(start.X, start.Y + 40), MouseButtonState.Pressed));
        editor.RaiseEvent(CreateMouseUp(new Point(start.X, start.Y + 40)));

        Assert.True(editor.VerticalOffsetForTesting > 0);
    }

    [Fact]
    public void ShiftMouseWheel_ShouldScrollHorizontally()
    {
        var editor = new EditControl { ShowMinimap = false, IsScrollInertiaEnabled = false };
        editor.LoadText(new string('x', 500));
        editor.Arrange(new Rect(0, 0, 240, 120));
        editor.UpdateScrollBarsForTesting(new Size(240, 120));
        Assert.True(editor.IsHorizontalScrollBarVisibleForTesting);

        editor.RaiseEvent(CreateMouseWheel(new Point(40, 40), -120, ModifierKeys.Shift));

        Assert.True(editor.HorizontalOffsetForTesting > 0);
    }

    [Fact]
    public void ShowMinimap_False_ShouldDisableMinimapHitZone()
    {
        var editor = new EditControl { ShowMinimap = true };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 300).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 260, 160));
        editor.UpdateScrollBarsForTesting(new Size(260, 160));

        var minimapRect = editor.MinimapRectForTesting;
        Assert.False(minimapRect.IsEmpty);

        editor.ShowMinimap = false;
        editor.UpdateScrollBarsForTesting(new Size(260, 160));
        Assert.True(editor.MinimapRectForTesting.IsEmpty);

        var clickPoint = new Point(minimapRect.X + 4, minimapRect.Y + minimapRect.Height * 0.8);
        double before = editor.VerticalOffsetForTesting;
        editor.RaiseEvent(CreateMouseDown(clickPoint));
        editor.RaiseEvent(CreateMouseUp(clickPoint));

        Assert.Equal(before, editor.VerticalOffsetForTesting, precision: 3);
    }

    [Fact]
    public void ShowMinimap_True_ShouldUseIntegratedMinimapWithoutStandaloneVerticalThumb()
    {
        var editor = new EditControl { ShowMinimap = true };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 500).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 320, 180));
        editor.UpdateScrollBarsForTesting(new Size(320, 180));

        var minimapRect = editor.MinimapRectForTesting;
        var viewportRect = editor.MinimapViewportRectForTesting;
        var thumb = editor.VerticalScrollBarThumbRectForTesting;

        Assert.True(editor.IsVerticalScrollBarVisibleForTesting);
        Assert.False(minimapRect.IsEmpty);
        Assert.False(viewportRect.IsEmpty);
        Assert.True(thumb.IsEmpty);
    }

    [Fact]
    public void Minimap_Click_ShouldMoveVerticalOffsetTowardTarget()
    {
        var editor = new EditControl { ShowMinimap = true };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 500).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 320, 180));
        editor.UpdateScrollBarsForTesting(new Size(320, 180));

        var minimapRect = editor.MinimapRectForTesting;
        Assert.False(minimapRect.IsEmpty);

        var clickPoint = new Point(minimapRect.X + minimapRect.Width * 0.5, minimapRect.Y + minimapRect.Height * 0.9);
        editor.RaiseEvent(CreateMouseDown(clickPoint));
        editor.RaiseEvent(CreateMouseUp(clickPoint));

        Assert.True(editor.VerticalOffsetForTesting > 0);
    }

    [Fact]
    public void MinimapTrack_Click_ShouldNavigateWithAnimation()
    {
        var editor = new EditControl { ShowMinimap = true, IsScrollInertiaEnabled = true, ScrollInertiaDurationMs = 3000 };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 500).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 320, 180));
        editor.UpdateScrollBarsForTesting(new Size(320, 180));

        var minimapRect = editor.MinimapRectForTesting;
        var viewport = editor.MinimapViewportRectForTesting;
        Assert.False(minimapRect.IsEmpty);
        Assert.False(viewport.IsEmpty);

        double clickY = viewport.Bottom + 8;
        if (clickY >= minimapRect.Bottom - 2)
            clickY = Math.Max(minimapRect.Y + 2, viewport.Y - 8);
        Assert.True(clickY < viewport.Y || clickY > viewport.Bottom);

        var clickPoint = new Point(minimapRect.X + minimapRect.Width * 0.5, clickY);
        editor.RaiseEvent(CreateMouseDown(clickPoint));
        editor.RaiseEvent(CreateMouseUp(clickPoint));

        double target = GetPrivateField<double>(editor, "_scrollAnimationTargetVerticalOffset");
        Assert.True(editor.IsScrollAnimatingForTesting);
        Assert.True(target > 0);
    }

    [Fact]
    public void MinimapViewport_ClickWithoutDrag_ShouldNavigateWithAnimation()
    {
        var editor = new EditControl { ShowMinimap = true, IsScrollInertiaEnabled = true, ScrollInertiaDurationMs = 3000 };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 500).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 320, 180));
        editor.UpdateScrollBarsForTesting(new Size(320, 180));

        var minimapRect = editor.MinimapRectForTesting;
        var viewport = editor.MinimapViewportRectForTesting;
        Assert.False(minimapRect.IsEmpty);
        Assert.False(viewport.IsEmpty);

        var clickPoint = new Point(
            minimapRect.X + minimapRect.Width * 0.5,
            Math.Min(viewport.Bottom - 2, viewport.Y + viewport.Height * 0.85));

        editor.RaiseEvent(CreateMouseDown(clickPoint));
        editor.RaiseEvent(CreateMouseUp(clickPoint));

        double target = GetPrivateField<double>(editor, "_scrollAnimationTargetVerticalOffset");
        Assert.True(editor.IsScrollAnimatingForTesting);
        Assert.True(target > 0);
    }

    [Fact]
    public void MinimapTooltip_MouseMoveWithinMinimap_ShouldFollowPointerLine()
    {
        var editor = new EditControl { ShowMinimap = true };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 500).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 320, 180));
        editor.UpdateScrollBarsForTesting(new Size(320, 180));

        var minimapRect = editor.MinimapRectForTesting;
        Assert.False(minimapRect.IsEmpty);

        var topPoint = new Point(minimapRect.X + minimapRect.Width * 0.5, minimapRect.Y + 6);
        var bottomPoint = new Point(minimapRect.X + minimapRect.Width * 0.5, minimapRect.Bottom - 6);

        editor.RaiseEvent(CreateMouseMove(topPoint, MouseButtonState.Released));
        int topLine = editor.MinimapTooltipLineForTesting;
        Assert.True(editor.IsMinimapTooltipVisibleForTesting);
        Assert.InRange(topLine, 1, editor.Document.LineCount);

        editor.RaiseEvent(CreateMouseMove(bottomPoint, MouseButtonState.Released));
        int bottomLine = editor.MinimapTooltipLineForTesting;
        Assert.True(editor.IsMinimapTooltipVisibleForTesting);
        Assert.True(bottomLine > topLine);
    }

    [Fact]
    public void MinimapTooltip_ShouldRenderAtLeast11Lines_WithHoveredLineCentered()
    {
        var editor = new EditControl { ShowMinimap = true };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 500).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 320, 180));
        editor.UpdateScrollBarsForTesting(new Size(320, 180));

        var minimapRect = editor.MinimapRectForTesting;
        Assert.False(minimapRect.IsEmpty);

        var middlePoint = new Point(minimapRect.X + minimapRect.Width * 0.5, minimapRect.Y + minimapRect.Height * 0.5);
        editor.RaiseEvent(CreateMouseMove(middlePoint, MouseButtonState.Released));

        int hoveredLine = editor.MinimapTooltipLineForTesting;
        Assert.True(editor.IsMinimapTooltipVisibleForTesting);
        Assert.InRange(hoveredLine, 1, editor.Document.LineCount);

        string tooltip = editor.MinimapTooltipTextForTesting;
        string[] lines = tooltip.Split('\n');
        Assert.True(lines.Length >= 11);

        int centerIndex = 11 / 2;
        string centerLine = lines[centerIndex];
        Assert.StartsWith(">", centerLine);
        Assert.Contains(hoveredLine.ToString(), centerLine);
    }

    [Fact]
    public void Minimap_ViewportDrag_ShouldContinuouslyUpdateVerticalOffset()
    {
        var editor = new EditControl { ShowMinimap = true };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 500).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 320, 180));
        editor.UpdateScrollBarsForTesting(new Size(320, 180));

        var viewport = editor.MinimapViewportRectForTesting;
        Assert.False(viewport.IsEmpty);

        var start = new Point(viewport.X + viewport.Width / 2, viewport.Y + viewport.Height / 2);
        var end = new Point(start.X, start.Y + 36);

        editor.RaiseEvent(CreateMouseDown(start));
        editor.RaiseEvent(CreateMouseMove(end, MouseButtonState.Pressed));
        editor.RaiseEvent(CreateMouseUp(end));

        Assert.True(editor.VerticalOffsetForTesting > 0);
    }

    [Fact]
    public void ScrollAnimation_Wheel_ShouldEnterAnimationState()
    {
        var editor = new EditControl { ShowMinimap = false, IsScrollInertiaEnabled = true, ScrollInertiaDurationMs = 3000 };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 500).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 260, 140));
        editor.UpdateScrollBarsForTesting(new Size(260, 140));

        editor.RaiseEvent(CreateMouseWheel(new Point(40, 40), -120, ModifierKeys.None));

        Assert.True(editor.IsScrollAnimatingForTesting);
        Assert.True(editor.VerticalOffsetForTesting > 0);
    }

    [Fact]
    public void ScrollAnimation_ScrollPageDown_ShouldUpdateAnimatedTarget()
    {
        var editor = new EditControl { ShowMinimap = false, IsScrollInertiaEnabled = true, ScrollInertiaDurationMs = 3000 };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 500).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 260, 140));
        editor.UpdateScrollBarsForTesting(new Size(260, 140));

        editor.ScrollPageDown();

        double target = GetPrivateField<double>(editor, "_scrollAnimationTargetVerticalOffset");
        Assert.True(editor.IsScrollAnimatingForTesting);
        Assert.True(target > editor.VerticalOffsetForTesting);
    }

    [Fact]
    public void ScrollAnimation_ThumbDrag_ShouldInterruptAnimation()
    {
        var editor = new EditControl { ShowMinimap = false, IsScrollInertiaEnabled = true, ScrollInertiaDurationMs = 3000 };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 500).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 260, 140));
        editor.UpdateScrollBarsForTesting(new Size(260, 140));

        editor.RaiseEvent(CreateMouseWheel(new Point(40, 40), -120, ModifierKeys.None));
        Assert.True(editor.IsScrollAnimatingForTesting);

        var thumb = editor.VerticalScrollBarThumbRectForTesting;
        var start = new Point(thumb.X + thumb.Width / 2, thumb.Y + thumb.Height / 2);
        var end = new Point(start.X, start.Y + 40);

        editor.RaiseEvent(CreateMouseDown(start));
        editor.RaiseEvent(CreateMouseMove(end, MouseButtonState.Pressed));
        editor.RaiseEvent(CreateMouseUp(end));

        Assert.False(editor.IsScrollAnimatingForTesting);
        Assert.True(editor.VerticalOffsetForTesting > 0);
    }

    [Fact]
    public void AutoFollowBottom_WhenFollowing_ShouldStayAtBottomAfterAppend()
    {
        var editor = new EditControl { ShowMinimap = false, IsScrollInertiaEnabled = false, AutoFollowBottom = true };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 120).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 260, 140));
        editor.UpdateScrollBarsForTesting(new Size(260, 140));
        editor.ScrollToLine(editor.Document.LineCount);
        Assert.True(editor.IsFollowingBottom);

        editor.AppendText("tail\n");

        Assert.True(editor.IsFollowingBottom);
        AssertAtBottom(editor);
    }

    [Fact]
    public void AutoFollowBottom_WhenUserScrollsUp_ShouldStopFollowingOnAppend()
    {
        var editor = new EditControl { ShowMinimap = false, IsScrollInertiaEnabled = false, AutoFollowBottom = true };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 160).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 260, 140));
        editor.UpdateScrollBarsForTesting(new Size(260, 140));
        editor.ScrollToLine(editor.Document.LineCount);
        Assert.True(editor.IsFollowingBottom);

        editor.RaiseEvent(CreateMouseWheel(new Point(40, 40), 120, ModifierKeys.None));
        double offsetAfterManualScroll = editor.VerticalOffsetForTesting;
        Assert.False(editor.IsFollowingBottom);

        editor.AppendText("tail\n");

        Assert.Equal(offsetAfterManualScroll, editor.VerticalOffsetForTesting, precision: 3);
        Assert.False(editor.IsFollowingBottom);
    }

    [Fact]
    public void AutoFollowBottom_WhenScrolledBackToBottom_ShouldResumeFollowing()
    {
        var editor = new EditControl { ShowMinimap = false, IsScrollInertiaEnabled = false, AutoFollowBottom = true };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 180).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 260, 140));
        editor.UpdateScrollBarsForTesting(new Size(260, 140));
        editor.ScrollToLine(editor.Document.LineCount);

        editor.RaiseEvent(CreateMouseWheel(new Point(40, 40), 120, ModifierKeys.None));
        Assert.False(editor.IsFollowingBottom);

        int guard = 0;
        while (!editor.IsFollowingBottom && guard++ < 400)
            editor.RaiseEvent(CreateMouseWheel(new Point(40, 40), -120, ModifierKeys.None));

        Assert.True(editor.IsFollowingBottom);
        editor.AppendText("tail\n");
        AssertAtBottom(editor);
    }

    [Theory]
    [InlineData(EditorCommands.CaretLeft)]
    [InlineData(EditorCommands.CaretRight)]
    [InlineData(EditorCommands.CaretUp)]
    [InlineData(EditorCommands.CaretDown)]
    [InlineData(EditorCommands.WordLeft)]
    [InlineData(EditorCommands.WordRight)]
    [InlineData(EditorCommands.LineStart)]
    [InlineData(EditorCommands.LineEnd)]
    [InlineData(EditorCommands.DocumentStart)]
    [InlineData(EditorCommands.DocumentEnd)]
    [InlineData(EditorCommands.PageUp)]
    [InlineData(EditorCommands.PageDown)]
    [InlineData(EditorCommands.SelectLeft)]
    [InlineData(EditorCommands.SelectRight)]
    [InlineData(EditorCommands.SelectUp)]
    [InlineData(EditorCommands.SelectDown)]
    [InlineData(EditorCommands.SelectWordLeft)]
    [InlineData(EditorCommands.SelectWordRight)]
    [InlineData(EditorCommands.SelectLineStart)]
    [InlineData(EditorCommands.SelectLineEnd)]
    [InlineData(EditorCommands.SelectDocumentStart)]
    [InlineData(EditorCommands.SelectDocumentEnd)]
    [InlineData(EditorCommands.SelectPageUp)]
    [InlineData(EditorCommands.SelectPageDown)]
    [InlineData(EditorCommands.SelectCurrentWord)]
    [InlineData(EditorCommands.SelectCurrentLine)]
    [InlineData(EditorCommands.ClearSelection)]
    [InlineData(EditorCommands.DeleteLeft)]
    [InlineData(EditorCommands.DeleteRight)]
    [InlineData(EditorCommands.DeleteWordLeft)]
    [InlineData(EditorCommands.DeleteWordRight)]
    [InlineData(EditorCommands.InsertNewLine)]
    [InlineData(EditorCommands.InsertTab)]
    [InlineData(EditorCommands.Unindent)]
    [InlineData(EditorCommands.ScrollLineUp)]
    [InlineData(EditorCommands.ScrollLineDown)]
    [InlineData(EditorCommands.ScrollPageUp)]
    [InlineData(EditorCommands.ScrollPageDown)]
    public void ExecuteEditorCommand_ExtendedCommands_ShouldReturnTrue(string commandId)
    {
        var editor = new EditControl();
        editor.LoadText("alpha beta\ngamma delta\n" + new string('x', 220));
        editor.Arrange(new Rect(0, 0, 240, 120));
        editor.UpdateScrollBarsForTesting(new Size(240, 120));
        editor.CaretOffset = Math.Min(8, editor.Document.TextLength);
        editor.SetSelection(2, 4);

        bool handled = editor.ExecuteEditorCommand(commandId);

        Assert.True(handled);
    }

    [Fact]
    public void MoveCaretWordLeftAndRight_ShouldUseWordBoundaries()
    {
        var editor = new EditControl();
        editor.LoadText("one two");
        editor.CaretOffset = editor.Document.TextLength;

        editor.MoveCaretWordLeft();
        Assert.Equal(4, editor.CaretOffset);

        editor.MoveCaretWordRight();
        Assert.Equal(7, editor.CaretOffset);
    }

    [Fact]
    public void SelectCurrentLine_ThenClearSelection_ShouldUpdateSelection()
    {
        var editor = new EditControl();
        editor.LoadText("aa\nbb\ncc");
        editor.CaretOffset = 4; // line 2

        editor.SelectCurrentLine();
        Assert.Equal(3, editor.SelectionStart);
        Assert.Equal(2, editor.SelectionLength);

        editor.ClearSelection();
        Assert.Equal(0, editor.SelectionLength);
    }

    [Fact]
    public void DeleteWordRight_ShouldDeleteWordAndFollowingWhitespace()
    {
        var editor = new EditControl();
        editor.LoadText("one two");
        editor.CaretOffset = 0;

        editor.DeleteWordRight();

        Assert.Equal("two", editor.Text);
    }

    [Fact]
    public void InsertTabAndUnindent_ShouldRoundTripOnCurrentLine()
    {
        var editor = new EditControl();
        editor.LoadText("x");
        editor.CaretOffset = 0;

        editor.InsertTab();
        Assert.StartsWith("    ", editor.Text);

        editor.Unindent();
        Assert.Equal("x", editor.Text);
    }

    [Fact]
    public void ScrollPageDown_Command_ShouldMoveVerticalOffset()
    {
        var editor = new EditControl { ShowMinimap = false, IsScrollInertiaEnabled = false };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 200).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 240, 120));
        editor.UpdateScrollBarsForTesting(new Size(240, 120));

        editor.ExecuteEditorCommand(EditorCommands.ScrollPageDown);

        Assert.True(editor.VerticalOffsetForTesting > 0);
    }

    [Fact]
    public void ScrollPageDown_WhenRepeated_ShouldAllowLastLineAtTopButNotBeyond()
    {
        var editor = new EditControl { ShowMinimap = false, IsScrollInertiaEnabled = false };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 1500).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 260, 140));
        editor.UpdateScrollBarsForTesting(new Size(260, 140));

        for (int i = 0; i < 2000; i++)
            editor.ExecuteEditorCommand(EditorCommands.ScrollPageDown);

        var view = GetPrivateView(editor);
        int topLine = view.GetLineNumberFromY(0);
        Assert.Equal(editor.Document.LineCount, topLine);

        double maxExpected = view.GetAbsoluteLineTop(editor.Document.LineCount);
        Assert.True(editor.VerticalOffsetForTesting <= maxExpected + 0.001);
    }

    [Fact]
    public void MinimapViewport_WhenOverscrolledToLastLineTop_ShouldNotStickAtBottomEarly()
    {
        var editor = new EditControl { ShowMinimap = true, IsScrollInertiaEnabled = false };
        editor.LoadText(string.Join("\n", Enumerable.Range(1, 1500).Select(i => $"line{i}")));
        editor.Arrange(new Rect(0, 0, 320, 180));
        editor.UpdateScrollBarsForTesting(new Size(320, 180));

        var view = GetPrivateView(editor);
        double contentHeight = Math.Max(0, view.TotalContentHeight);
        double viewportHeight = Math.Max(0, view.ViewportHeight);
        double lineHeight = Math.Max(1, view.LineHeight);
        double defaultBottom = Math.Max(0, contentHeight - viewportHeight);
        double maxOffset = Math.Max(defaultBottom, Math.Max(0, contentHeight - lineHeight));

        InvokePrivateMethod(editor, "SetVerticalOffsetImmediate", defaultBottom, false, true);
        editor.UpdateScrollBarsForTesting(new Size(320, 180));
        var minimapRect = editor.MinimapRectForTesting;
        var viewportAtDefaultBottom = editor.MinimapViewportRectForTesting;

        InvokePrivateMethod(editor, "SetVerticalOffsetImmediate", maxOffset, false, true);
        editor.UpdateScrollBarsForTesting(new Size(320, 180));
        var viewportAtMaxOffset = editor.MinimapViewportRectForTesting;

        Assert.False(minimapRect.IsEmpty);
        Assert.False(viewportAtDefaultBottom.IsEmpty);
        Assert.False(viewportAtMaxOffset.IsEmpty);
        Assert.True(viewportAtDefaultBottom.Bottom < minimapRect.Bottom - 0.5);
        Assert.InRange(Math.Abs(viewportAtMaxOffset.Bottom - minimapRect.Bottom), 0, 0.5);
    }

    [Fact]
    public void CaretSetBeforeFirstViewport_ShouldNotCauseGlobalHorizontalShift()
    {
        var editor = new EditControl();
        editor.LoadText(new string('x', 500));

        // Simulate caret operations before first valid viewport is available.
        editor.CaretOffset = 0;

        editor.Arrange(new Rect(0, 0, 260, 140));
        editor.UpdateScrollBarsForTesting(new Size(260, 140));

        Assert.Equal(0, editor.HorizontalOffsetForTesting);
        Assert.Equal(0, editor.VerticalOffsetForTesting);
    }

    [Fact]
    public void ExecuteEditorCommand_ReadOnlyMutatingCommand_ShouldReturnFalse()
    {
        var editor = new EditControl();
        editor.LoadText("alpha");
        editor.IsReadOnly = true;

        bool handled = editor.ExecuteEditorCommand(EditorCommands.DeleteLeft);

        Assert.False(handled);
        Assert.Equal("alpha", editor.Text);
    }

    [Fact]
    public void CanExecuteEditorCommand_ShouldReflectContext()
    {
        var editor = new EditControl();
        editor.LoadText("alpha");
        editor.SetSelection(0, 0);

        Assert.False(editor.CanExecuteEditorCommand(EditorCommands.Copy));
        Assert.False(editor.CanExecuteEditorCommand(EditorCommands.Cut));
        Assert.True(editor.CanExecuteEditorCommand(EditorCommands.DeleteLeft));

        editor.IsReadOnly = true;
        Assert.False(editor.CanExecuteEditorCommand(EditorCommands.DeleteLeft));
    }

    [Fact]
    public void ExecuteEditorCommand_SelectAllLines_ShouldSelectWholeDocument()
    {
        var editor = new EditControl();
        editor.LoadText("a\nb\nc");
        editor.CaretOffset = 2;

        bool handled = editor.ExecuteEditorCommand(EditorCommands.SelectAllLines);

        Assert.True(handled);
        Assert.Equal(0, editor.SelectionStart);
        Assert.Equal(editor.Document.TextLength, editor.SelectionLength);
    }

    [Fact]
    public void FeatureFlags_ShouldToggle()
    {
        var editor = new EditControl();

        Assert.False(editor.IsFeatureEnabled(EditFeature.MultiCaret));
        editor.SetFeatureEnabled(EditFeature.MultiCaret, true);
        Assert.True(editor.IsFeatureEnabled(EditFeature.MultiCaret));
        editor.SetFeatureEnabled(EditFeature.MultiCaret, false);
        Assert.False(editor.IsFeatureEnabled(EditFeature.MultiCaret));
    }

    [Fact]
    public void BehaviorOptions_ShouldApplyUndoMergeWindowToDocument()
    {
        var editor = new EditControl();
        editor.BehaviorOptions = new EditControlBehaviorOptions { UndoMergeWindowMs = 123 };

        Assert.Equal(123, editor.Document.UndoStack.MergeTypingWindowMs);
    }

    [Fact]
    public void UserKeyBindings_ShouldOverrideDefaultBinding()
    {
        var editor = new EditControl();
        editor.LoadText("line1\nline2");
        editor.CaretOffset = 1;
        editor.SetUserKeyBindings(
        [
            new EditorKeyBinding(Key.D, ModifierKeys.Control, EditorCommands.SelectCurrentLine)
        ]);

        var keyEvent = new KeyEventArgs(UIElement.KeyDownEvent, Key.D, ModifierKeys.Control, true, false, 7);
        editor.RaiseEvent(keyEvent);

        Assert.True(keyEvent.Handled);
        Assert.Equal(0, editor.SelectionStart);
        Assert.Equal(5, editor.SelectionLength);
    }

    [Fact]
    public void ImeComposition_WhenSuppressingShortcuts_ShouldSkipCtrlCommandHandling()
    {
        var editor = new EditControl();
        editor.LoadText("line");
        editor.BehaviorOptions = new EditControlBehaviorOptions { SuppressShortcutsDuringIme = true };
        editor.OnImeCompositionStart();

        var keyEvent = new KeyEventArgs(UIElement.KeyDownEvent, Key.D, ModifierKeys.Control, true, false, 9);
        editor.RaiseEvent(keyEvent);

        Assert.False(keyEvent.Handled);
        Assert.Equal("line", editor.Text);
        editor.OnImeCompositionEnd(null);
    }

    private static (int bracketOffset, int matchOffset)? GetPrivateActiveBracketPair(EditControl editor)
    {
        var field = typeof(EditControl).GetField("_activeBracketPair", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var value = field!.GetValue(editor);
        if (value is ValueTuple<int, int> pair)
            return (pair.Item1, pair.Item2);

        return null;
    }

    private static FoldingManager GetPrivateFoldingManager(EditControl editor)
    {
        var field = typeof(EditControl).GetField("_foldingManager", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var value = field!.GetValue(editor);
        var manager = value as FoldingManager;
        Assert.NotNull(manager);
        return manager!;
    }

    private static EditorView GetPrivateView(EditControl editor)
    {
        var field = typeof(EditControl).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var value = field!.GetValue(editor);
        var view = value as EditorView;
        Assert.NotNull(view);
        return view!;
    }

    private static T GetPrivateField<T>(EditControl editor, string fieldName)
    {
        var field = typeof(EditControl).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(editor);
        Assert.NotNull(value);
        return (T)value;
    }

    private static void InvokePrivateMethod(EditControl editor, string methodName, params object[] args)
    {
        var method = typeof(EditControl).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(editor, args);
    }

    private static void AssertAtBottom(EditControl editor)
    {
        var view = GetPrivateView(editor);
        double expectedBottom = view.GetAbsoluteLineTop(editor.Document.LineCount);
        double tolerance = Math.Max(1, view.LineHeight * 0.5) + 0.001;
        Assert.InRange(Math.Abs(expectedBottom - editor.VerticalOffsetForTesting), 0, tolerance);
    }

    private static MouseButtonEventArgs CreateMouseDown(Point position)
    {
        return new MouseButtonEventArgs(
            UIElement.MouseDownEvent,
            position,
            MouseButton.Left,
            MouseButtonState.Pressed,
            clickCount: 1,
            leftButton: MouseButtonState.Pressed,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 0);
    }

    private static MouseButtonEventArgs CreateMouseUp(Point position)
    {
        return new MouseButtonEventArgs(
            UIElement.MouseUpEvent,
            position,
            MouseButton.Left,
            MouseButtonState.Released,
            clickCount: 1,
            leftButton: MouseButtonState.Released,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 1);
    }

    private static MouseEventArgs CreateMouseMove(Point position, MouseButtonState leftButton)
    {
        return new MouseEventArgs(
            UIElement.MouseMoveEvent,
            position,
            leftButton,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 2);
    }

    private static MouseWheelEventArgs CreateMouseWheel(Point position, int delta, ModifierKeys modifiers)
    {
        return new MouseWheelEventArgs(
            UIElement.MouseWheelEvent,
            position,
            delta,
            leftButton: MouseButtonState.Released,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers,
            timestamp: 3);
    }
}
