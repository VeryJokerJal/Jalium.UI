using System.Linq;
using Jalium.UI.Controls.TextEffects;
using Jalium.UI.Controls.TextEffects.Effects;

namespace Jalium.UI.Tests.TextEffects;

public class TextEffectPresenterTests
{
    private static TextEffectPresenter CreateWithAnimation()
    {
        // Default constructor wires RiseSettleEffect. Animation is enabled but
        // nothing ticks until AdvanceFrameForTesting is called, so all phase
        // transitions are deterministic.
        return new TextEffectPresenter();
    }

    #region Grapheme splitting

    [Fact]
    public void AppendText_AsciiString_OneCellPerChar()
    {
        var p = CreateWithAnimation();
        p.AppendText("abc");

        Assert.Equal(3, p.Cells.Count);
        Assert.Equal("a", p.Cells[0].Text);
        Assert.Equal("b", p.Cells[1].Text);
        Assert.Equal("c", p.Cells[2].Text);
    }

    [Fact]
    public void AppendText_SurrogatePair_StaysAsOneCell()
    {
        var p = CreateWithAnimation();
        p.AppendText("a\U0001F600b"); // grinning face emoji

        Assert.Equal(3, p.Cells.Count);
        Assert.Equal("a", p.Cells[0].Text);
        Assert.Equal("\U0001F600", p.Cells[1].Text);
        Assert.Equal("b", p.Cells[2].Text);
    }

    [Fact]
    public void AppendText_CombiningMark_StaysAsOneCell()
    {
        var p = CreateWithAnimation();
        // 'e' + combining acute accent (U+0301) is one grapheme.
        p.AppendText("e\u0301");

        Assert.Single(p.Cells);
        Assert.Equal("e\u0301", p.Cells[0].Text);
    }

    #endregion

    #region State machine — new cells enter

    [Fact]
    public void AppendText_NewCells_StartInEntering()
    {
        var p = CreateWithAnimation();
        p.AppendText("ab");

        Assert.All(p.Cells, c => Assert.Equal(TextEffectCellPhase.Entering, c.Phase));
    }

    [Fact]
    public void AppendText_ExistingCells_StayVisible()
    {
        var p = CreateWithAnimation();
        p.AppendText("ab");
        // Finish the initial enter animation — advance past the default
        // enter duration (520ms) plus the stagger max (260ms) to be safe.
        p.AdvanceFrameForTesting(900);

        Assert.All(p.Cells, c => Assert.Equal(TextEffectCellPhase.Visible, c.Phase));

        p.AppendText("cd");

        Assert.Equal(TextEffectCellPhase.Visible, p.Cells[0].Phase);
        Assert.Equal(TextEffectCellPhase.Visible, p.Cells[1].Phase);
        Assert.Equal(TextEffectCellPhase.Entering, p.Cells[2].Phase);
        Assert.Equal(TextEffectCellPhase.Entering, p.Cells[3].Phase);
    }

    #endregion

    #region State machine — insert shifts the tail

    [Fact]
    public void InsertText_Middle_TailBecomesShifting()
    {
        var p = CreateWithAnimation();
        p.AppendText("abcd");
        p.AdvanceFrameForTesting(900);

        p.InsertText(2, "X");

        Assert.Equal(5, p.Cells.Count);
        Assert.Equal("a", p.Cells[0].Text);
        Assert.Equal("b", p.Cells[1].Text);
        Assert.Equal("X", p.Cells[2].Text);
        Assert.Equal("c", p.Cells[3].Text);
        Assert.Equal("d", p.Cells[4].Text);

        Assert.Equal(TextEffectCellPhase.Visible, p.Cells[0].Phase);
        Assert.Equal(TextEffectCellPhase.Visible, p.Cells[1].Phase);
        Assert.Equal(TextEffectCellPhase.Entering, p.Cells[2].Phase);
        Assert.Equal(TextEffectCellPhase.Shifting, p.Cells[3].Phase);
        Assert.Equal(TextEffectCellPhase.Shifting, p.Cells[4].Phase);
    }

    [Fact]
    public void InsertText_AtEnd_NoShift()
    {
        var p = CreateWithAnimation();
        p.AppendText("ab");
        p.AdvanceFrameForTesting(900);

        p.InsertText(2, "c");

        Assert.Equal(TextEffectCellPhase.Visible, p.Cells[0].Phase);
        Assert.Equal(TextEffectCellPhase.Visible, p.Cells[1].Phase);
        Assert.Equal(TextEffectCellPhase.Entering, p.Cells[2].Phase);
    }

    #endregion

    #region State machine — remove exits and shifts

    [Fact]
    public void RemoveText_Middle_RemovesAndShiftsTail()
    {
        var p = CreateWithAnimation();
        p.AppendText("abcd");
        p.AdvanceFrameForTesting(900);

        p.RemoveText(1, 2);

        // Visible text: a, d
        Assert.Equal(2, p.Cells.Count);
        Assert.Equal("a", p.Cells[0].Text);
        Assert.Equal("d", p.Cells[1].Text);

        Assert.Equal(TextEffectCellPhase.Visible, p.Cells[0].Phase);
        Assert.Equal(TextEffectCellPhase.Shifting, p.Cells[1].Phase);
    }

    [Fact]
    public void RemoveText_EndOfLine_NoShiftNeeded()
    {
        var p = CreateWithAnimation();
        p.AppendText("abcd");
        p.AdvanceFrameForTesting(900);

        p.RemoveText(2, 2);

        Assert.Equal(2, p.Cells.Count);
        Assert.Equal("a", p.Cells[0].Text);
        Assert.Equal("b", p.Cells[1].Text);
        Assert.All(p.Cells, c => Assert.Equal(TextEffectCellPhase.Visible, c.Phase));
    }

    #endregion

    #region State machine — clear

    [Fact]
    public void ClearText_AllCellsExit()
    {
        var p = CreateWithAnimation();
        p.AppendText("abc");
        p.AdvanceFrameForTesting(900);

        p.ClearText();

        Assert.Empty(p.Cells);
        // We can't read exiting cells directly — assert via AnimationIdle later.
    }

    [Fact]
    public void ClearText_WhenAlreadyEmpty_DoesNotThrow()
    {
        var p = CreateWithAnimation();
        p.ClearText();
        p.ClearText();
        Assert.Empty(p.Cells);
    }

    #endregion

    #region Text property — full replace with prefix diff

    [Fact]
    public void TextProperty_SharedPrefix_PreservesCellIdentity()
    {
        var p = CreateWithAnimation();
        p.Text = "hello";
        p.AdvanceFrameForTesting(900);

        var originalHId = p.Cells[0].Id;
        var originalEId = p.Cells[1].Id;

        p.Text = "help";

        // Shared prefix "hel" keeps its cells.
        Assert.Equal(originalHId, p.Cells[0].Id);
        Assert.Equal(originalEId, p.Cells[1].Id);
        Assert.Equal("h", p.Cells[0].Text);
        Assert.Equal("e", p.Cells[1].Text);
        Assert.Equal("l", p.Cells[2].Text);
        Assert.Equal("p", p.Cells[3].Text);
        Assert.Equal(TextEffectCellPhase.Entering, p.Cells[3].Phase);
    }

    [Fact]
    public void TextProperty_CompleteReplace_ExitsAllOld()
    {
        var p = CreateWithAnimation();
        p.Text = "abc";
        p.AdvanceFrameForTesting(900);
        var oldIds = p.Cells.Select(c => c.Id).ToArray();

        p.Text = "xyz";

        Assert.Equal(3, p.Cells.Count);
        Assert.All(p.Cells, c => Assert.DoesNotContain(c.Id, oldIds));
        Assert.All(p.Cells, c => Assert.Equal(TextEffectCellPhase.Entering, c.Phase));
    }

    #endregion

    #region IsAnimationEnabled = false → instant application

    [Fact]
    public void AppendText_AnimationDisabled_CellsGoStraightToVisible()
    {
        var p = new TextEffectPresenter { IsAnimationEnabled = false };
        p.AppendText("hi");

        Assert.All(p.Cells, c => Assert.Equal(TextEffectCellPhase.Visible, c.Phase));
    }

    [Fact]
    public void DisableAnimation_InFlight_JumpsAllToVisible()
    {
        var p = CreateWithAnimation();
        p.AppendText("abc");
        Assert.All(p.Cells, c => Assert.Equal(TextEffectCellPhase.Entering, c.Phase));

        p.IsAnimationEnabled = false;

        Assert.All(p.Cells, c => Assert.Equal(TextEffectCellPhase.Visible, c.Phase));
    }

    #endregion

    #region AnimationIdle fires once work is done

    [Fact]
    public void AnimationIdle_FiresAfterAllPhasesComplete()
    {
        var p = CreateWithAnimation();
        var idleCount = 0;
        p.AnimationIdle += (_, _) => idleCount++;

        p.AppendText("abc");
        // Not yet — cells are still Entering.
        p.AdvanceFrameForTesting(100);
        Assert.Equal(0, idleCount);

        // Advance past the entire enter window.
        p.AdvanceFrameForTesting(2000);
        Assert.Equal(1, idleCount);
    }

    #endregion

    #region Cell identity is monotonic

    [Fact]
    public void Cells_GetFreshIdOnEveryMutation()
    {
        var p = CreateWithAnimation();
        p.AppendText("a");
        var firstId = p.Cells[0].Id;
        p.AppendText("b");
        var secondId = p.Cells[1].Id;

        Assert.True(secondId > firstId);
    }

    #endregion

    #region Default TextEffect is RiseSettleEffect

    [Fact]
    public void DefaultTextEffect_IsRiseSettleEffect()
    {
        var p = new TextEffectPresenter();
        Assert.IsType<RiseSettleEffect>(p.TextEffect);
    }

    #endregion

    #region Batched mutations still drive the animation clock

    [Fact]
    public void TextSetter_AfterBatchEdit_CellsCanStillAnimate()
    {
        // Regression. Text setter → ApplyFullTextReplace → BeginBatchEdit.
        // Every mutation inside that batch (RemoveText + InsertText) calls
        // EnsureRenderingSubscription, which early-returns because batchDepth>0.
        // Before the BatchEditToken.Dispose fix, nobody re-called
        // EnsureRenderingSubscription after the batch ended, so the animation
        // clock never started and cells sat permanently in Entering —
        // "太多时 / 添加过快时 不再播放动画".
        var p = CreateWithAnimation();

        p.Text = "abc";

        Assert.Equal(3, p.Cells.Count);
        Assert.All(p.Cells, c => Assert.Equal(TextEffectCellPhase.Entering, c.Phase));

        // Advancing the clock must carry every cell through its enter window.
        // If the batched-mutation path forgot to wire up the clock, cells
        // would remain Entering forever regardless of how long we advance.
        p.AdvanceFrameForTesting(2000);

        Assert.All(p.Cells, c => Assert.Equal(TextEffectCellPhase.Visible, c.Phase));
    }

    [Fact]
    public void ReplaceText_CellsCanStillAnimate()
    {
        // Same concern for the user-facing ReplaceText API — it explicitly
        // wraps Remove + Insert in BeginBatchEdit.
        var p = CreateWithAnimation();
        p.AppendText("abcd");
        p.AdvanceFrameForTesting(2000);

        p.ReplaceText(1, 2, "XY");

        Assert.Equal(TextEffectCellPhase.Visible, p.Cells[0].Phase);  // 'a' was outside the replaced span
        Assert.Equal(TextEffectCellPhase.Entering, p.Cells[1].Phase); // inserted 'X'
        Assert.Equal(TextEffectCellPhase.Entering, p.Cells[2].Phase); // inserted 'Y'

        // And the clock advances them — would fail before the fix.
        p.AdvanceFrameForTesting(2000);
        Assert.All(p.Cells, c => Assert.Equal(TextEffectCellPhase.Visible, c.Phase));
    }

    [Fact]
    public void AppendText_WhileEarlierBatchAnimating_DoesNotResetEarlierBatchProgress()
    {
        // Regression for "上一个文本的透明度是 0.6, 那它停在 0.6":
        // appending a new run while an earlier one is mid-enter must NOT
        // reset the earlier cells' phase clock. Previously, certain code
        // paths could reset PhaseStartTimeMs on cells not in the new batch,
        // causing their progress to snap back to zero and appear frozen at
        // their last rendered opacity until the full phase duration elapsed.
        var p = CreateWithAnimation();
        p.AppendText("abc");

        // Advance into the first batch's enter window but not past it.
        p.AdvanceFrameForTesting(100);
        var firstBatchMidPhaseStart = p.Cells[0].Id;  // capture identity

        // Second append while first is still animating.
        p.AppendText("xyz");

        // Cells 0..2 should still be Entering with an earlier clock start than
        // the fresh xyz cells, i.e. closer to Visible than the new ones.
        Assert.Equal(TextEffectCellPhase.Entering, p.Cells[0].Phase);
        Assert.Equal(TextEffectCellPhase.Entering, p.Cells[1].Phase);
        Assert.Equal(TextEffectCellPhase.Entering, p.Cells[2].Phase);
        Assert.Equal(TextEffectCellPhase.Entering, p.Cells[3].Phase);

        // A single small tick: the earlier batch must reach Visible long before
        // the new batch — if the Append2 had reset them, they'd both arrive
        // at the same time.
        p.AdvanceFrameForTesting(500);  // total elapsed for first batch ≈ 600 ms → past its 520 ms window
        Assert.Equal(TextEffectCellPhase.Visible, p.Cells[0].Phase);
        Assert.Equal(TextEffectCellPhase.Visible, p.Cells[1].Phase);
        Assert.Equal(TextEffectCellPhase.Visible, p.Cells[2].Phase);
        // Second batch total elapsed ≈ 500 ms < 520 ms window → still Entering.
        Assert.Equal(TextEffectCellPhase.Entering, p.Cells[3].Phase);

        // Identity untouched — the "abc" cells are the same objects.
        Assert.Equal(firstBatchMidPhaseStart, p.Cells[0].Id);
    }

    #endregion
}
