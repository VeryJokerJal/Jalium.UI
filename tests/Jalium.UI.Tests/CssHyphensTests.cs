using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssHyphensTests
{
    [Theory]
    [InlineData("none", true)]
    [InlineData("manual", true)]
    [InlineData("auto", true)]
    [InlineData("initial", true)]
    [InlineData("inherit", true)]
    [InlineData("always", false)]
    [InlineData("manual auto", false)]
    public void SupportsHyphensGrammar(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("hyphens", value));

    [Theory]
    [InlineData("auto", true)]
    [InlineData("\"=\"", true)]
    [InlineData("\"/-/\"", true)]
    [InlineData("\"\\1400\"", true)]
    [InlineData("\"\"", true)]
    [InlineData("normal", false)]
    [InlineData("manual", false)]
    [InlineData("\"-\" \"=\"", false)]
    [InlineData("1400", false)]
    public void SupportsHyphenateCharacterGrammar(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("hyphenate-character", value));

    [Theory]
    [InlineData("auto", true)]
    [InlineData("auto auto", true)]
    [InlineData("5", true)]
    [InlineData("5 2", true)]
    [InlineData("5 2 3", true)]
    [InlineData("auto 2 auto", true)]
    [InlineData("auto auto 2", true)]
    [InlineData("0 0 0", true)]
    [InlineData("normal", false)]
    [InlineData("1.2", false)]
    [InlineData("-1", false)]
    [InlineData("1 2 3 4", false)]
    public void SupportsHyphenateLimitCharsGrammar(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("hyphenate-limit-chars", value));

    [Theory]
    [InlineData("no-limit", true)]
    [InlineData("0", true)]
    [InlineData("1", true)]
    [InlineData("+1", true)]
    [InlineData("2147483647", true)]
    [InlineData("initial", true)]
    [InlineData("inherit", true)]
    [InlineData("none", false)]
    [InlineData("auto", false)]
    [InlineData("-1", false)]
    [InlineData("1.5", false)]
    [InlineData("1 2", false)]
    public void SupportsHyphenateLimitLinesGrammar(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("hyphenate-limit-lines", value));

    [Theory]
    [InlineData("0", true)]
    [InlineData("8px", true)]
    [InlineData("50%", true)]
    [InlineData("calc(100% - 1px)", true)]
    [InlineData("-1px", true)]
    [InlineData("initial", true)]
    [InlineData("inherit", true)]
    [InlineData("1", false)]
    [InlineData("auto", false)]
    [InlineData("10fr", false)]
    [InlineData("1px 2px", false)]
    public void SupportsHyphenateLimitZoneGrammar(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("hyphenate-limit-zone", value));

    [Theory]
    [InlineData("none", true)]
    [InlineData("always", true)]
    [InlineData("column", true)]
    [InlineData("page", true)]
    [InlineData("spread", true)]
    [InlineData("initial", true)]
    [InlineData("inherit", true)]
    [InlineData("auto", false)]
    [InlineData("all", false)]
    [InlineData("always page", false)]
    public void SupportsHyphenateLimitLastGrammar(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("hyphenate-limit-last", value));

    [Fact]
    public void HyphenateCharacterDecodesCssStringsAndKeepsQuotedAutoDistinct()
    {
        var block = Block("co\u00ADoperate");
        Css.SetStyle(block, "hyphenate-character:\"\\1400\"");
        Assert.Equal("\u1400", ((CssHyphenateCharacter)block.GetValue(
            CssFlowProperties.HyphenateCharacterProperty)!).Value);

        Css.SetStyle(block, "hyphenate-character:\"auto\"");
        Assert.Equal("auto", ((CssHyphenateCharacter)block.GetValue(
            CssFlowProperties.HyphenateCharacterProperty)!).Value);
        Css.SetStyle(block, "hyphenate-character:auto");
        Assert.Null(((CssHyphenateCharacter)block.GetValue(
            CssFlowProperties.HyphenateCharacterProperty)!).Value);
    }

    [Fact]
    public void SoftHyphenIsInvisibleWithoutBreakAndKeepsSourceOffsets()
    {
        var block = Block("co\u00ADoperate");
        Css.SetStyle(block, "hyphens:manual");

        var unwrapped = Render(block, 300);
        Assert.Equal("cooperate", Assert.Single(unwrapped.Texts).Text.Text);
        Assert.Equal("co\u00ADoperate", block.Text);
        block.Select(2, 1);
        Assert.Equal("\u00AD", block.SelectedText);
        Assert.InRange(Assert.Single(Render(block, 300).Rectangles).Width, 0, 1.1);
    }

    [Fact]
    public void ManualBreakDrawsHyphenWhileNoneKeepsTheWordTogether()
    {
        var block = Block("co\u00ADoperate");
        Css.SetStyle(block, "hyphens:manual");

        var manual = Render(block, 45);
        Assert.Equal("co", manual.Texts[0].Text.Text);
        var hyphen = Assert.Single(manual.Texts, item => item.Text.Text == "-");
        Assert.Contains(manual.Texts, item => item.Text.Text == "operate" && item.Y > hyphen.Y);
        Assert.True(hyphen.X > manual.Texts[0].X);

        Css.SetStyle(block, "hyphens:none");
        var none = Render(block, 45);
        Assert.Equal("cooperate", Assert.Single(none.Texts).Text.Text);

        Css.SetStyle(block, "hyphens:auto");
        Assert.Single(Render(block, 45).Texts, item => item.Text.Text == "-");
    }

    [Fact]
    public void DefaultManualWrapsAndNoneStillBreaksAfterVisibleHyphens()
    {
        var soft = Block("co\u00ADoperate");
        soft.TextWrapping = TextWrapping.Wrap;
        Assert.Single(Render(soft, 45).Texts, item => item.Text.Text == "-");

        var visible = Block("co-operate");
        Css.SetStyle(visible, "hyphens:none");
        var drawn = Render(visible, 45).Texts;
        Assert.Contains(drawn, item => item.Text.Text == "co-");
        Assert.True(drawn.Max(item => item.Y) > drawn.Min(item => item.Y));
    }

    [Fact]
    public void JustifiedSoftHyphenLineStillDrawsItsEndingGlyph()
    {
        var block = Block("A co\u00ADoperate");
        Css.SetStyle(block, "hyphens:manual; text-align:justify");

        var drawn = Render(block, 75).Texts;
        var hyphen = Assert.Single(drawn, item => item.Text.Text == "-");
        Assert.Contains(drawn, item => item.Text.Text == "operate" && item.Y > hyphen.Y);

        var leftAligned = Block("A co\u00ADoperate");
        Css.SetStyle(leftAligned, "hyphens:manual; text-align:left");
        var plainHyphen = Assert.Single(Render(leftAligned, 75).Texts,
            item => item.Text.Text == "-");
        Assert.True(hyphen.X > plainHyphen.X + 5);
    }

    [Fact]
    public void InheritedHyphensChangeRelayoutsTextBlock()
    {
        var parent = new StackPanel();
        var block = Block("co\u00ADoperate");
        parent.Children.Add(block);
        Css.SetStyle(block, "white-space:normal");
        Css.SetStyle(parent, "hyphens:none");
        Assert.DoesNotContain(Render(block, 45).Texts, item => item.Text.Text == "-");

        Css.SetStyle(parent, "hyphens:manual");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        Assert.Single(Render(block, 45).Texts, item => item.Text.Text == "-");
    }

    [Fact]
    public void HyphenUsesTheSoftHyphensRunStyling()
    {
        var first = new Run("co");
        var softHyphen = new Run("\u00AD") { Foreground = Brushes.Red, FontSize = 30 };
        var block = new TextBlock(first) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(softHyphen);
        block.Inlines.Add(new Run("operate"));
        Css.SetStyle(block, "hyphens:none");
        Css.SetStyle(softHyphen, "hyphens:manual");

        var wrapped = Render(block, 45);
        var hyphen = Assert.Single(wrapped.Texts, item => item.Text.Text == "-");
        Assert.Equal(30, hyphen.Text.FontSize);
        Assert.Equal(Colors.Red,
            Assert.IsType<SolidColorBrush>(hyphen.Text.Foreground).Color);

        Css.SetStyle(softHyphen, "hyphens:inherit");
        Assert.DoesNotContain(Render(block, 45).Texts, item => item.Text.Text == "-");
    }

    [Fact]
    public void CustomHyphenationStringAndEmptyStringAffectPaint()
    {
        var block = Block("co\u00ADoperate");
        Css.SetStyle(block, "hyphenate-character:\"/-/\"");
        Assert.Equal("/-/", ((CssHyphenateCharacter)block.GetValue(
            CssFlowProperties.HyphenateCharacterProperty)!).Value);
        Assert.Single(Render(block, 70).Texts, item => item.Text.Text == "/-/");

        Css.SetStyle(block, "hyphens:manual; hyphenate-character:\"\"");
        var withoutMarker = Render(block, 45).Texts;
        Assert.Equal("co", withoutMarker[0].Text.Text);
        Assert.Contains(withoutMarker,
            item => item.Text.Text == "operate" && item.Y > withoutMarker[0].Y);
        Assert.DoesNotContain(withoutMarker, item => item.Text.Text == "-");

        Css.SetStyle(block, "hyphens:manual; hyphenate-character:auto");
        Assert.Single(Render(block, 45).Texts, item => item.Text.Text == "-");
    }

    [Fact]
    public void CustomMarkerWidthCanSelectAnEarlierWordBreak()
    {
        var normal = Block("A co\u00ADoperate");
        Css.SetStyle(normal, "hyphens:manual");
        var normalText = Render(normal, 55).Texts;
        var normalA = Assert.Single(normalText, item => item.Text.Text.StartsWith("A"));
        var normalHyphen = Assert.Single(normalText, item => item.Text.Text == "-");
        Assert.Equal(normalA.Y, normalHyphen.Y);

        var custom = Block("A co\u00ADoperate");
        Css.SetStyle(custom, "hyphens:manual; hyphenate-character:\"==\"");
        var customText = Render(custom, 55).Texts;
        var customA = Assert.Single(customText, item => item.Text.Text.StartsWith("A"));
        var customMarker = Assert.Single(customText, item => item.Text.Text == "==");
        Assert.True(customMarker.Y > customA.Y);
    }

    [Fact]
    public void MarkerInheritsAndRunOverrideUsesTheSoftHyphensStyle()
    {
        var parent = new StackPanel();
        var softHyphen = new Run("\u00AD") { Foreground = Brushes.Red };
        var block = new TextBlock(new Run("co")) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(softHyphen);
        block.Inlines.Add(new Run("operate"));
        parent.Children.Add(block);
        Css.SetStyle(parent, "hyphenate-character:\"=\"");
        Css.SetStyle(block, "hyphens:manual");

        Assert.Single(Render(block, 45).Texts, item => item.Text.Text == "=");
        Css.SetStyle(softHyphen, "hyphenate-character:\"~\"");
        var marker = Assert.Single(Render(block, 45).Texts, item => item.Text.Text == "~");
        Assert.Equal(Colors.Red,
            Assert.IsType<SolidColorBrush>(marker.Text.Foreground).Color);

        Css.SetStyle(softHyphen, "hyphenate-character:inherit");
        Css.SetStyle(parent, "hyphenate-character:\"+\"");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        Assert.Single(Render(block, 45).Texts, item => item.Text.Text == "+");
    }

    [Fact]
    public void LimitCharsControlsManualSoftHyphenBreaksAndAutoDefaults()
    {
        var block = Block("co\u00ADoperate");
        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-chars:auto");
        Assert.Single(Render(block, 45).Texts, item => item.Text.Text == "-");

        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-chars:10");
        Assert.Equal("cooperate", Assert.Single(Render(block, 45).Texts).Text.Text);

        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-chars:5 3");
        Assert.Equal("cooperate", Assert.Single(Render(block, 45).Texts).Text.Text);

        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-chars:5 2 8");
        Assert.Equal("cooperate", Assert.Single(Render(block, 45).Texts).Text.Text);

        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-chars:5 2 7");
        Assert.Single(Render(block, 45).Texts, item => item.Text.Text == "-");
    }

    [Fact]
    public void ExplicitZeroLimitsPermitAnOtherwiseTooEarlySoftHyphen()
    {
        var block = Block("a\u00ADbcdef");
        Css.SetStyle(block, "hyphens:manual");
        Assert.DoesNotContain(Render(block, 40).Texts, item => item.Text.Text == "-");

        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-chars:0 0 0");
        Assert.Single(Render(block, 40).Texts, item => item.Text.Text == "-");
    }

    [Fact]
    public void TwoValueLimitRepeatsTheBeforeCountForTheAfterCount()
    {
        var block = Block("cooper\u00ADate");
        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-chars:5 4");
        Assert.DoesNotContain(Render(block, 75).Texts, item => item.Text.Text == "-");

        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-chars:5 3");
        Assert.Single(Render(block, 75).Texts, item => item.Text.Text == "-");
    }

    [Fact]
    public void LimitCharsExcludesNonspacingMarksAndIntraWordPunctuation()
    {
        var block = Block("co\u0301'\u00ADoperate");
        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-chars:5 3 2");
        Assert.DoesNotContain(Render(block, 50).Texts, item => item.Text.Text == "-");

        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-chars:5 2 2");
        Assert.Single(Render(block, 50).Texts, item => item.Text.Text == "-");
    }

    [Fact]
    public void LimitCharsCanBeOverriddenOnTheSoftHyphensRun()
    {
        var softHyphen = new Run("\u00AD");
        var block = new TextBlock(new Run("co")) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(softHyphen);
        block.Inlines.Add(new Run("operate"));
        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-chars:10");
        Assert.DoesNotContain(Render(block, 45).Texts, item => item.Text.Text == "-");

        Css.SetStyle(softHyphen, "hyphenate-limit-chars:5 2 2");
        Assert.Single(Render(block, 45).Texts, item => item.Text.Text == "-");

        Css.SetStyle(softHyphen, "hyphenate-limit-chars:inherit");
        Assert.DoesNotContain(Render(block, 45).Texts, item => item.Text.Text == "-");
    }

    [Fact]
    public void InheritedLimitCharsChangeRelayoutsTextBlock()
    {
        var parent = new StackPanel();
        var block = Block("co\u00ADoperate");
        parent.Children.Add(block);
        Css.SetStyle(block, "hyphens:manual");
        Css.SetStyle(parent, "hyphenate-limit-chars:10");
        Assert.DoesNotContain(Render(block, 45).Texts, item => item.Text.Text == "-");

        Css.SetStyle(parent, "hyphenate-limit-chars:5 2 2");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        Assert.Single(Render(block, 45).Texts, item => item.Text.Text == "-");
    }

    [Fact]
    public void LimitLinesCapsSuccessiveSoftHyphenatedLines()
    {
        var block = Block("co\u00ADop\u00ADer\u00ADative");
        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-lines:no-limit");
        Assert.Equal(3, Render(block, 35).Texts.Count(item => item.Text.Text == "-"));

        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-lines:2");
        Assert.Equal(2, Render(block, 35).Texts.Count(item => item.Text.Text == "-"));

        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-lines:1");
        Assert.Single(Render(block, 35).Texts, item => item.Text.Text == "-");

        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-lines:0");
        Assert.Equal("cooperative", Assert.Single(Render(block, 35).Texts).Text.Text);

        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-lines:initial");
        Assert.Equal(3, Render(block, 35).Texts.Count(item => item.Text.Text == "-"));
    }

    [Fact]
    public void LimitLinesInheritsAndResetsAfterAnUnhyphenatedLine()
    {
        var parent = new StackPanel();
        var block = Block("co\u00ADoperate co\u00ADoperate");
        parent.Children.Add(block);
        Css.SetStyle(block, "hyphens:manual");
        Css.SetStyle(parent, "hyphenate-limit-lines:1");
        Assert.Equal(2, Render(block, 45).Texts.Count(item => item.Text.Text == "-"));

        Css.SetStyle(parent, "hyphenate-limit-lines:0");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        Assert.DoesNotContain(Render(block, 45).Texts, item => item.Text.Text == "-");

        Css.SetStyle(parent, "hyphenate-limit-lines:no-limit");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        Assert.Equal(2, Render(block, 45).Texts.Count(item => item.Text.Text == "-"));
    }

    [Fact]
    public void ZeroLimitKeepsVisibleHyphenBreaksAndEmergencyWrapping()
    {
        var visible = Block("co-operate");
        Css.SetStyle(visible, "hyphenate-limit-lines:0");
        Assert.Contains(Render(visible, 45).Texts, item => item.Text.Text == "co-");

        var emergency = Block("co\u00ADoperative");
        Css.SetStyle(emergency, "hyphenate-limit-lines:0; overflow-wrap:anywhere");
        var drawn = Render(emergency, 35).Texts;
        Assert.DoesNotContain(drawn, item => item.Text.Text == "-");
        Assert.True(drawn.Max(item => item.Y) > drawn.Min(item => item.Y));
        Assert.Equal("cooperative", string.Concat(drawn.Select(item => item.Text.Text)));
    }

    [Fact]
    public void LimitZonePrefersANearbyPlainBreakButStillHyphenatesWithoutOne()
    {
        var block = Block("A co\u00ADoperate");
        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-zone:0");
        var initial = Render(block, 55).Texts;
        var initialA = Assert.Single(initial, item => item.Text.Text.StartsWith("A"));
        Assert.Equal(initialA.Y, Assert.Single(initial, item => item.Text.Text == "-").Y);

        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-zone:50%");
        var smallZone = Render(block, 55).Texts;
        var smallA = Assert.Single(smallZone, item => item.Text.Text.StartsWith("A"));
        Assert.Equal(smallA.Y, Assert.Single(smallZone, item => item.Text.Text == "-").Y);

        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-zone:100%");
        var largeZone = Render(block, 55).Texts;
        var largeA = Assert.Single(largeZone, item => item.Text.Text.StartsWith("A"));
        Assert.True(Assert.Single(largeZone, item => item.Text.Text == "-").Y > largeA.Y);

        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-zone:50px");
        var pixelZone = Render(block, 55).Texts;
        var pixelA = Assert.Single(pixelZone, item => item.Text.Text.StartsWith("A"));
        Assert.True(Assert.Single(pixelZone, item => item.Text.Text == "-").Y > pixelA.Y);

        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-zone:calc(100% - 1px)");
        var calculatedZone = Render(block, 55).Texts;
        var calculatedA = Assert.Single(calculatedZone, item => item.Text.Text.StartsWith("A"));
        Assert.True(Assert.Single(calculatedZone, item => item.Text.Text == "-").Y > calculatedA.Y);

        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-zone:initial");
        var resetZone = Render(block, 55).Texts;
        var resetA = Assert.Single(resetZone, item => item.Text.Text.StartsWith("A"));
        Assert.Equal(resetA.Y, Assert.Single(resetZone, item => item.Text.Text == "-").Y);

        Css.SetStyle(block, "hyphens:manual; hyphenate-limit-zone:-1px");
        var negativeZone = Render(block, 55).Texts;
        var negativeA = Assert.Single(negativeZone, item => item.Text.Text.StartsWith("A"));
        Assert.Equal(negativeA.Y, Assert.Single(negativeZone, item => item.Text.Text == "-").Y);

        var unbreakable = Block("co\u00ADoperate");
        Css.SetStyle(unbreakable, "hyphenate-limit-zone:100%");
        Assert.Single(Render(unbreakable, 45).Texts, item => item.Text.Text == "-");
    }

    [Fact]
    public void LimitZoneCanPreferAnEarlierVisibleHyphenBreak()
    {
        var block = Block("A-co\u00ADoperate");
        Css.SetStyle(block, "hyphenate-limit-zone:0");
        var noZone = Render(block, 70).Texts;
        var noZoneA = Assert.Single(noZone, item => item.Text.Text.StartsWith("A-"));
        Assert.Equal(noZoneA.Y, Assert.Single(noZone, item => item.Text.Text == "-").Y);

        Css.SetStyle(block, "hyphenate-limit-zone:100%");
        var largeZone = Render(block, 70).Texts;
        var largeA = Assert.Single(largeZone, item => item.Text.Text.StartsWith("A-"));
        Assert.True(Assert.Single(largeZone, item => item.Text.Text == "-").Y > largeA.Y);
    }

    [Fact]
    public void InheritedLimitZoneChangeRelayoutsTextBlock()
    {
        var parent = new StackPanel();
        var block = Block("A co\u00ADoperate");
        parent.Children.Add(block);
        Css.SetStyle(parent, "hyphenate-limit-zone:100%");
        var largeZone = Render(block, 55).Texts;
        var largeA = Assert.Single(largeZone, item => item.Text.Text.StartsWith("A"));
        Assert.True(Assert.Single(largeZone, item => item.Text.Text == "-").Y > largeA.Y);

        Css.SetStyle(parent, "hyphenate-limit-zone:0");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        var noZone = Render(block, 55).Texts;
        var noZoneA = Assert.Single(noZone, item => item.Text.Text.StartsWith("A"));
        Assert.Equal(noZoneA.Y, Assert.Single(noZone, item => item.Text.Text == "-").Y);
    }

    [Fact]
    public void LimitLastAlwaysKeepsTheLastFullLineUnhyphenated()
    {
        var block = Block("A co\u00ADoperate");
        Css.SetStyle(block, "hyphenate-limit-last:none");
        var normal = Render(block, 90).Texts;
        var normalA = Assert.Single(normal, item => item.Text.Text.StartsWith("A"));
        Assert.Equal(normalA.Y, Assert.Single(normal, item => item.Text.Text == "-").Y);

        Css.SetStyle(block, "hyphenate-limit-last:always");
        var protectedLast = Render(block, 90).Texts;
        var protectedA = Assert.Single(protectedLast, item => item.Text.Text.StartsWith("A"));
        Assert.DoesNotContain(protectedLast, item => item.Text.Text == "-");
        Assert.Contains(protectedLast,
            item => item.Text.Text == "cooperate" && item.Y > protectedA.Y);

        Css.SetStyle(block, "hyphenate-limit-last:always; text-wrap-style:balance");
        Assert.DoesNotContain(Render(block, 90).Texts, item => item.Text.Text == "-");

        Css.SetStyle(block, "hyphenate-limit-last:initial; text-wrap-style:auto");
        Assert.Single(Render(block, 90).Texts, item => item.Text.Text == "-");
    }

    [Fact]
    public void LimitLastOnlyProtectsTheFinalForcedBreakGroup()
    {
        var block = Block("A co\u00ADoperate\nA co\u00ADoperate");
        Css.SetStyle(block, "white-space:pre-wrap; hyphenate-limit-last:none");
        Assert.Equal(2, Render(block, 90).Texts.Count(item => item.Text.Text == "-"));

        Css.SetStyle(block, "white-space:pre-wrap; hyphenate-limit-last:always");
        Assert.Single(Render(block, 90).Texts, item => item.Text.Text == "-");
    }

    [Fact]
    public void LimitLastPreservesHyphenationOnEarlierLinesOfTheSameElement()
    {
        var block = Block("co\u00ADoperate hello");
        Css.SetStyle(block, "hyphenate-limit-last:always");
        Assert.Single(Render(block, 45).Texts, item => item.Text.Text == "-");
    }

    [Fact]
    public void LimitLastInheritsAndFragmentationValuesDoNotAffectUnfragmentedText()
    {
        var parent = new StackPanel();
        var block = Block("A co\u00ADoperate");
        parent.Children.Add(block);
        Css.SetStyle(parent, "hyphenate-limit-last:always");
        Assert.DoesNotContain(Render(block, 90).Texts, item => item.Text.Text == "-");

        Css.SetStyle(parent, "hyphenate-limit-last:column");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        Assert.Single(Render(block, 90).Texts, item => item.Text.Text == "-");

        Css.SetStyle(parent, "hyphenate-limit-last:page");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        Assert.Single(Render(block, 90).Texts, item => item.Text.Text == "-");

        Css.SetStyle(parent, "hyphenate-limit-last:spread");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        Assert.Single(Render(block, 90).Texts, item => item.Text.Text == "-");
    }

    private static TextBlock Block(string text) => new()
    {
        Text = text, FontSize = 20, Foreground = Brushes.Black,
    };

    private static TextDrawingContext Render(TextBlock block, double width)
    {
        block.Measure(new Size(width, 400));
        block.Arrange(new Rect(0, 0, width, 400));
        var context = new TextDrawingContext();
        block.Render(context);
        return context;
    }

    private sealed class TextDrawingContext : DrawingContextAdapter
    {
        public List<(FormattedText Text, double X, double Y)> Texts { get; } = [];
        public List<Rect> Rectangles { get; } = [];
        public override void DrawText(FormattedText formattedText, Point origin)
            => Texts.Add((formattedText, origin.X, origin.Y));
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
            => Rectangles.Add(rectangle);
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle,
            double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center,
            double radiusX, double radiusY) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect,
            CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
