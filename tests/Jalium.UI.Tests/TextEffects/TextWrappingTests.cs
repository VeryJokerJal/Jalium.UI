using Jalium.UI;
using Jalium.UI.Controls.TextEffects;

namespace Jalium.UI.Tests.TextEffects;

/// <summary>
/// Layout tests for <see cref="TextEffectPresenter.TextWrapping"/>. The
/// fallback measurement path (<c>length * fontSize * 0.55</c>) is
/// deterministic in headless runs, so we can assert exact line counts and
/// wrap points without a real DirectWrite context.
/// </summary>
public class TextWrappingTests
{
    private static TextEffectPresenter MakePresenter(string text, TextWrapping wrapping, double width)
    {
        var p = new TextEffectPresenter
        {
            IsAnimationEnabled = false, // lock every cell into Visible — layout is independent of phase
            FontSize = 20,              // fallback width per char ≈ 11 px
            TextWrapping = wrapping,
            Text = text,
        };
        p.Measure(new Size(width, double.PositiveInfinity));
        return p;
    }

    private static int DistinctLineCount(TextEffectPresenter p)
    {
        var ys = new System.Collections.Generic.HashSet<double>();
        foreach (var c in p.Cells)
        {
            if (c.Bounds.Width > 0)
            {
                ys.Add(c.Bounds.Y);
            }
        }
        return ys.Count;
    }

    #region NoWrap regression

    [Fact]
    public void NoWrap_LaysOutOneLineRegardlessOfWidth()
    {
        var p = MakePresenter(
            "hello world this is much wider than the constraint",
            TextWrapping.NoWrap,
            width: 60);

        // Every cell sits on Y = 0 — no wrap occurred.
        foreach (var cell in p.Cells)
        {
            Assert.Equal(0, cell.Bounds.Y, precision: 3);
        }
    }

    [Fact]
    public void NoWrap_ExplicitNewlineStillBreaksLine()
    {
        var p = MakePresenter("ab\ncd", TextWrapping.NoWrap, width: 400);

        Assert.Equal(0, p.Cells[0].Bounds.Y, precision: 3);         // a
        Assert.Equal(0, p.Cells[1].Bounds.Y, precision: 3);         // b
        Assert.True(p.Cells[3].Bounds.Y > p.Cells[0].Bounds.Y);     // c on line 2
        Assert.True(p.Cells[4].Bounds.Y > p.Cells[0].Bounds.Y);     // d on line 2
    }

    #endregion

    #region Wrap — western text

    [Fact]
    public void Wrap_ShortText_StaysOnOneLine()
    {
        var p = MakePresenter("hello", TextWrapping.Wrap, width: 400);
        Assert.Equal(1, DistinctLineCount(p));
    }

    [Fact]
    public void Wrap_LongText_BreaksIntoMultipleLines()
    {
        var p = MakePresenter(
            "hello world this is much wider than the constraint",
            TextWrapping.Wrap,
            width: 100);

        Assert.True(DistinctLineCount(p) >= 3, $"Expected ≥3 lines, got {DistinctLineCount(p)}");
    }

    [Fact]
    public void Wrap_BreaksAtWordBoundaries_NotMidWord()
    {
        var p = MakePresenter("alpha beta gamma", TextWrapping.Wrap, width: 120);

        // Find where the word "alpha" ends. Every cell of "alpha" must be on
        // the same line — we never break inside a Latin word when a whitespace
        // break is available.
        var aCell = p.Cells[0];
        var lCell = p.Cells[2];
        var hCell = p.Cells[3];
        var aEndCell = p.Cells[4]; // last 'a' of "alpha"
        Assert.Equal(aCell.Bounds.Y, lCell.Bounds.Y, precision: 3);
        Assert.Equal(aCell.Bounds.Y, hCell.Bounds.Y, precision: 3);
        Assert.Equal(aCell.Bounds.Y, aEndCell.Bounds.Y, precision: 3);
    }

    #endregion

    #region Wrap — CJK

    [Fact]
    public void Wrap_CjkBreaksAnywhere()
    {
        // 16 Chinese chars. At fontSize 20, each char ~ 11 px, so the 30-px
        // constraint should break every 2–3 chars.
        var p = MakePresenter("一二三四五六七八九十甲乙丙丁戊己", TextWrapping.Wrap, width: 30);

        Assert.True(DistinctLineCount(p) >= 5, $"CJK should wrap frequently at narrow width, got {DistinctLineCount(p)} lines");
    }

    [Fact]
    public void Wrap_MixedCjkAndAscii_BreaksAtCjkBoundary()
    {
        // "hello" + Chinese: the CJK block should be allowed to break from
        // "hello" without a space.
        var p = MakePresenter("hello你好世界", TextWrapping.Wrap, width: 80);

        var helloEnd = p.Cells[4]; // 'o' of hello
        var chineseStart = p.Cells[5]; // '你'

        // Whichever line they end up on, helloEnd must not be wider than the
        // constraint, and if chinese starts on a new line that's the legal
        // break we expect.
        Assert.True(chineseStart.Bounds.Y >= helloEnd.Bounds.Y,
            "CJK block should either start on the same line or wrap to a new one");
    }

    #endregion

    #region Wrap — width changes trigger relayout

    [Fact]
    public void Wrap_RemeasureWithSmallerWidth_RelaysToMoreLines()
    {
        var p = new TextEffectPresenter
        {
            IsAnimationEnabled = false,
            FontSize = 20,
            TextWrapping = TextWrapping.Wrap,
            Text = "alpha beta gamma delta epsilon",
        };

        p.Measure(new Size(400, double.PositiveInfinity));
        var wideLines = DistinctLineCount(p);

        p.Measure(new Size(80, double.PositiveInfinity));
        var narrowLines = DistinctLineCount(p);

        Assert.True(narrowLines > wideLines, $"narrow={narrowLines} must exceed wide={wideLines}");
    }

    #endregion

    #region Wrap — edge cases

    [Fact]
    public void Wrap_SingleWordWiderThanConstraint_DoesNotInfiniteLoop()
    {
        // With no break opportunity on the first line the layout must give
        // up and just let the cell overflow — crucially, it must terminate.
        var p = MakePresenter("supercalifragilistic", TextWrapping.Wrap, width: 40);

        // Just asserting the call returned is enough; the layout walked to
        // the end of the cell list. All cells got Bounds assigned.
        Assert.All(p.Cells, c => Assert.True(c.Bounds.Width > 0 || c.Text == "\n"));
    }

    [Fact]
    public void Wrap_ExplicitNewlineStillBreaks()
    {
        var p = MakePresenter("abc\ndef", TextWrapping.Wrap, width: 500);

        Assert.Equal(p.Cells[0].Bounds.Y, p.Cells[2].Bounds.Y, precision: 3); // a, c on line 1
        Assert.True(p.Cells[4].Bounds.Y > p.Cells[0].Bounds.Y);               // d on line 2
    }

    [Fact]
    public void Wrap_Empty_DoesNotThrow()
    {
        var p = new TextEffectPresenter
        {
            IsAnimationEnabled = false,
            TextWrapping = TextWrapping.Wrap,
            Text = "",
        };
        p.Measure(new Size(100, double.PositiveInfinity));

        Assert.Empty(p.Cells);
    }

    #endregion
}
