using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Input;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssCounterTests
{
    [Fact]
    public void CountersUseDocumentOrderAcrossDescendantsAndBeforeAfter()
    {
        var root = Box("root"); var first = Box("row"); var nested = Box("row"); var last = Box("row");
        first.Children.Add(nested); root.Children.Add(first); root.Children.Add(last);
        Sheet(root, """
            .root { counter-reset: item; }
            .row { counter-increment: item; }
            .row::before { content: counter(item); }
            .row::after { content: '=' counter(item); }
            .root::after { content: 'total=' counter(item); }
            """);
        Layout(root);
        Assert.Equal("1", Before(first).Text); Assert.Equal("2", Before(nested).Text);
        Assert.Equal("=2", After(first).Text); Assert.Equal("3", Before(last).Text);
        Assert.Equal("total=3", After(root).Text);
        for (var i = 0; i < 5; i++) { Layout(root); Assert.Equal("total=3", After(root).Text); }
        Assert.False(CssEvaluationScheduler.HasPending(Dispatcher.CurrentDispatcher));
    }

    [Fact]
    public void NestedResetKeepsAncestorIdentityButSiblingResetReplacesItsPeer()
    {
        var root = Box("list"); var first = Box("item"); var second = Box("item"); var nested = Box("list");
        var sub1 = Box("item"); var sub2 = Box("item"); nested.Children.Add(sub1); nested.Children.Add(sub2);
        first.Children.Add(nested); root.Children.Add(first); root.Children.Add(second);
        Sheet(root, ".list { counter-reset: n; } .item { counter-increment: n; } .item::before { content: counters(n, '.'); }");
        Layout(root);
        Assert.Equal("1", Before(first).Text); Assert.Equal("1.1", Before(sub1).Text);
        Assert.Equal("1.2", Before(sub2).Text); Assert.Equal("2", Before(second).Text);
        var host = Box(); var a = Box("a"); var b = Box("b"); host.Children.Add(a); host.Children.Add(b);
        Sheet(host, ".a { counter-reset: n 5; } .b { counter-reset: n 9; } .a::before, .b::before { content: counters(n, '.'); }");
        Layout(host);
        Assert.Equal("5", Before(a).Text); Assert.Equal("9", Before(b).Text);
    }

    [Fact]
    public void MissingCountersAreCreatedAtThePointOfUseAndNamesAreCaseSensitive()
    {
        var root = Box("root"); var row = Box("row"); root.Children.Add(row);
        Sheet(root, """
            .root::before { content: counter(item) '/' counter(Item); }
            .row { counter-increment: item; counter-set: Item 8; }
            .row::before { content: counters(item, '.') '/' counter(Item); }
            .root::after { content: counter(item) '/' counter(Item); }
            """);
        Layout(root);
        Assert.Equal("0/0", Before(root).Text); Assert.Equal("1/8", Before(row).Text); Assert.Equal("1/8", After(root).Text);
    }

    [Fact]
    public void MovingToANewRootAndClearingTheCascadeDropsOldCounterIdentities()
    {
        var a = Box(); var b = Box(); var row = Box("row");
        Css.SetStyle(a, "counter-reset: n 4;");
        Sheet(row, ".row { counter-increment: n; } .row::before { content: counters(n, '.'); }");
        a.Children.Add(row); Layout(a); Assert.Equal("5", Before(row).Text);
        a.Children.Remove(row); b.Children.Add(row); Layout(b); Assert.Equal("1", Before(row).Text);
        Css.SetStyle(b, "counter-reset: n 7;"); Layout(b); Assert.Equal("8", Before(row).Text);
        Css.SetStyle(b, "all: initial;"); Layout(b); Assert.Equal("1", Before(row).Text);
        Assert.False(CssEvaluationScheduler.HasPending(Dispatcher.CurrentDispatcher));
    }

    [Fact]
    public void FlexVisualOrderDoesNotChangeCounterSourceOrder()
    {
        var root = Box("root"); var a = Box("a row"); var b = Box("b row");
        root.Children.Add(a); root.Children.Add(b);
        Sheet(root, """
            .root { display: flex; counter-reset: n; }
            .row { counter-increment: n; width: 80px; }
            .a { order: 2; } .b { order: 1; }
            .row::before { content: counter(n); }
            """);
        Layout(root);
        Assert.True(b.VisualBounds.X < a.VisualBounds.X);
        Assert.Equal("1", Before(a).Text); Assert.Equal("2", Before(b).Text);
    }

    [Fact]
    public void ResetIncrementAndSetApplyInOrderAndHandleDuplicates()
    {
        var root = Box("root");
        Sheet(root, """
            .root { counter-reset: n 1 n 4; counter-increment: n 2 n 3; counter-set: k 1 k 8; }
            .root::before { content: counters(n, '.') '/' counter(k); counter-increment: n -1; counter-set: n 12; }
            .root::after { content: counter(n); counter-increment: n; }
            """);
        Layout(root);
        Assert.Equal("12/8", Before(root).Text); Assert.Equal("13", After(root).Text);
    }

    [Fact]
    public void InsertRemoveReorderAndCollapseRecalculateWithoutAccumulating()
    {
        var root = Box("root"); var a = Box("row"); var b = Box("row"); var c = Box("row");
        root.Children.Add(a); root.Children.Add(b); root.Children.Add(c);
        Sheet(root, ".root { counter-reset: n; } .row { counter-increment: n; } .row::before { content: counter(n); } .row:first-child { width: 31px; }");
        Layout(root); Assert.Equal("3", Before(c).Text);
        b.Visibility = Visibility.Collapsed; Layout(root); Assert.Equal("2", Before(c).Text);
        b.Visibility = Visibility.Hidden; Layout(root); Assert.Equal("3", Before(c).Text);
        b.Visibility = Visibility.Visible; root.Children.Remove(a); Layout(root);
        Assert.Equal("1", Before(b).Text); Assert.Equal("2", Before(c).Text);
        root.Children.Insert(0, a); Layout(root); Assert.Equal("3", Before(c).Text);
        root.Children.Remove(c); root.Children.Insert(0, c); Layout(root);
        Assert.Equal("1", Before(c).Text); Assert.Equal("2", Before(a).Text); Assert.Equal("3", Before(b).Text);
        Assert.Equal(31, c.Width); Assert.True(double.IsNaN(a.Width));
    }

    [Fact]
    public void DisplayNoneAndSuppressedPseudosDoNotAffectValues()
    {
        var root = Box("root"); var a = Box("hidden"); var b = Box("row"); root.Children.Add(a); root.Children.Add(b);
        Sheet(root, """
            .root { counter-reset: n 10; }
            .hidden { display: none; counter-reset: n 100; }
            .hidden::before { content: counter(n); counter-increment: n; }
            .row { counter-increment: n; }
            .row::before { counter-increment: n 90; content: none; }
            .row::after { content: counter(n); }
            """);
        Layout(root); Assert.Equal("11", After(b).Text);
        Css.SetStyle(b, "visibility: hidden;"); Layout(root); Assert.Equal("11", After(b).Text);
        Css.SetStyle(a, "display: block;"); Layout(root); Assert.Equal("102", After(b).Text);
    }

    [Theory]
    [InlineData("reversed(n)", "n -1", "3", "2", "1")]
    [InlineData("reversed(n) 9", "n -2", "7", "5", "3")]
    [InlineData("reversed(n)", "n", "-3", "-2", "-1")]
    public void ReversedCountersCalculateStartsButRespectExplicitIncrements(string reset, string increment, string a, string b, string c)
    {
        var root = Box("root"); var rows = Enumerable.Range(0, 3).Select(_ => Box("row")).ToArray();
        foreach (var row in rows) root.Children.Add(row);
        Sheet(root, $".root {{ counter-reset: {reset}; }} .row {{ counter-increment: {increment}; }} .row::before {{ content: counter(n); }}");
        Layout(root); Assert.Equal(new[] { a, b, c }, rows.Select(row => Before(row).Text));
        rows[1].Visibility = Visibility.Collapsed; Layout(root);
        if (reset == "reversed(n)" && increment == "n -1")
        { Assert.Equal("2", Before(rows[0]).Text); Assert.Equal("1", Before(rows[2]).Text); }
    }

    [Fact]
    public void ReversedInitialValueStopsAtSetAndCombinesDuplicateIncrementsPerElement()
    {
        var root = Box("root"); var a = Box("a"); var b = Box("b"); var c = Box("c");
        root.Children.Add(a); root.Children.Add(b); root.Children.Add(c);
        Sheet(root, """
            .root { counter-reset: reversed(n); }
            .a { counter-increment: n -1 n -1; }
            .b { counter-increment: n -3; counter-set: n 10 n 8; }
            .c { counter-increment: n -1; }
            .a::before, .b::before, .c::before { content: counter(n); }
            """);
        Layout(root);
        Assert.Equal("11", Before(a).Text); Assert.Equal("8", Before(b).Text); Assert.Equal("7", Before(c).Text);
    }

    [Theory]
    [InlineData("n 2147483647", "n", "2147483647")]
    [InlineData("n -2147483648", "n -1", "-2147483648")]
    [InlineData("n 9999999999999999999999999999", "n", "2147483647")]
    [InlineData("n calc(1.5)", "n", "3")]
    [InlineData("n 1 /* comment */ n 3", "n 1 /* another */ n 2", "6")]
    public void IntegersClampAndCalculatedIntegersRound(string reset, string increment, string expected)
    {
        var root = Box("root");
        Sheet(root, $".root {{ counter-reset: {reset}; counter-increment: {increment}; }} .root::before {{ content: counter(n); }}");
        Layout(root); Assert.Equal(expected, Before(root).Text);
    }

    [Fact]
    public void VariablesAttributesLayersAndAlternativeTextShareTheCounterValue()
    {
        var root = Box("root"); var row = Box("row"); row.Tag = 4; root.Children.Add(row);
        Sheet(root, """
            @layer base { .root { counter-reset: n 100; } }
            .root { --step: 2; counter-reset: n; }
            .row { counter-increment: n var(--step); counter-set: n attr(Tag type(<integer>), 3); }
            .row::before { content: '[' counter(n, upper-roman) ']' / '第' counter(n) '项'; }
            """);
        Layout(root); Assert.Equal("[IV]", Before(row).Text);
        Assert.Equal("第4项", UIElementAutomationPeer.CreatePeerForElement(Before(row))!.GetName());
        row.Tag = 7; Layout(root); Assert.Equal("[VII]", Before(row).Text);
        Assert.Equal("第7项", Before(row).Alternative);
        Assert.False(CssEvaluationScheduler.HasPending(Dispatcher.CurrentDispatcher));
    }

    [Fact]
    public void CounterUpdatesPreserveKeyboardOrderAndFocus()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var root = Box("root"); var a = new Button { Content = "first" }; var b = new Button { Content = "second" };
        root.Children.Add(a); root.Children.Add(b);
        Sheet(root, ".root { counter-reset: n 9; } .root::before { content: counter(n); } .root::after { content: counter(n); }");
        var window = new DisplayedTestWindow { Width = 360, Height = 240, Content = root, TitleBarStyle = WindowTitleBarStyle.Native };
        try
        {
            Layout(window);
            Assert.True(a.Focus()); Assert.True(KeyboardNavigation.MoveFocus(a)); Assert.Same(b, Keyboard.FocusedElement);
            Css.SetStyle(root, "counter-reset: n 1000;"); Layout(root);
            Assert.Equal("1000", Before(root).Text); Assert.Same(b, Keyboard.FocusedElement);
            Assert.True(KeyboardNavigation.MoveFocus(b, reverse: true)); Assert.Same(a, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData("counter(n)", true)]
    [InlineData("counters(n, '.')", true)]
    [InlineData("counter(n, upper-roman)", true)]
    [InlineData("counter(\\6e )", true)]
    [InlineData("'item' / counter(n)", true)]
    [InlineData("counter(n, unknown-style)", true)]
    [InlineData("counter()", false)]
    [InlineData("counters(n)", false)]
    [InlineData("counter(n,)", false)]
    [InlineData("counter(n, none)", false)]
    [InlineData("counter(n, inherit)", false)]
    [InlineData("'ok' counter(n,)", false)]
    [InlineData("'ok' invalid", false)]
    [InlineData("'ok' url('x')", true)]
    [InlineData("'ok' / open-quote", false)]
    public void ContentGrammarRejectsInvalidCountersAndTrailingTokens(string value, bool valid)
        => Assert.Equal(valid, CssAuthoring.IsValueSupported("content", value));

    [Theory]
    [InlineData("counter-reset", "a 1 a 2 reversed(b) -4", true)]
    [InlineData("counter-set", "a calc(4 / 2)", true)]
    [InlineData("counter-increment", "none", true)]
    [InlineData("counter-increment", "none a", false)]
    [InlineData("counter-reset", "default", false)]
    [InlineData("counter-reset", "reversed(n n)", false)]
    [InlineData("counter-increment", "reversed(n)", false)]
    [InlineData("counter-set", "a 1.0", false)]
    [InlineData("counter-reset", "a 1e2", false)]
    [InlineData("quotes", "'(' ')' '[' ']'", true)]
    [InlineData("quotes", "match-parent", true)]
    [InlineData("quotes", "'only one'", false)]
    [InlineData("quotes", "none 'x' 'y'", false)]
    public void AuthoringValidatesCounterAndQuoteProperties(string property, string value, bool valid)
        => Assert.Equal(valid, CssAuthoring.IsValueSupported(property, value));

    [Fact]
    public void NestedQuotesUseGlobalDepthAndRepeatTheFinalPair()
    {
        var root = Box("root q"); var child = Box("q"); var grandchild = Box("q");
        child.Children.Add(grandchild); root.Children.Add(child);
        Sheet(root, ".root { quotes: '(' ')' '[' ']'; } .q::before { content: open-quote; } .q::after { content: close-quote; }");
        Layout(root);
        Assert.Equal("(", Before(root).Text); Assert.Equal("[", Before(child).Text); Assert.Equal("[", Before(grandchild).Text);
        Assert.Equal("]", After(grandchild).Text); Assert.Equal("]", After(child).Text); Assert.Equal(")", After(root).Text);
        grandchild.Visibility = Visibility.Collapsed; Layout(root); Assert.Equal("]", After(child).Text);
        Assert.False(CssEvaluationScheduler.HasPending(Dispatcher.CurrentDispatcher));
    }

    [Fact]
    public void SilentAndNoneQuotesChangeDepthAndExtraClosersAreIgnored()
    {
        var root = Box("root"); var a = Box("a"); var b = Box("b"); var c = Box("c");
        root.Children.Add(a); root.Children.Add(b); root.Children.Add(c);
        Sheet(root, """
            .root { quotes: '(' ')' '[' ']'; }
            .root::before { content: close-quote no-close-quote no-open-quote; }
            .a { quotes: none; } .a::before { content: open-quote; }
            .b::before { content: close-quote; }
            .c::before { content: close-quote open-quote no-close-quote close-quote; }
            """);
        Layout(root);
        Assert.Equal("", Before(root).Text); Assert.Equal("", Before(a).Text);
        Assert.Equal("]", Before(b).Text); Assert.Equal(")(", Before(c).Text);
    }

    [Fact]
    public void AutoAndMatchParentUseTheCorrectLanguageAndObserveLanguageChanges()
    {
        var root = Box("root"); root.Language = XmlLanguage.GetLanguage("ja-JP");
        var child = Box("child"); child.Language = XmlLanguage.GetLanguage("de-DE"); root.Children.Add(child);
        Sheet(root, """
            .root { quotes: auto; }
            .child::before { content: open-quote close-quote; }
            .child::after { quotes: match-parent; content: open-quote close-quote; }
            """);
        Layout(root);
        Assert.Equal("„“", Before(child).Text); Assert.Equal("「」", After(child).Text);
        child.Language = XmlLanguage.GetLanguage("fr-FR"); Layout(root);
        Assert.Equal("«»", Before(child).Text); Assert.Equal("「」", After(child).Text);
        root.Language = XmlLanguage.GetLanguage("en-US"); Layout(root); Assert.Equal("“”", After(child).Text);
    }

    [Theory]
    [InlineData(27, "lower-alpha", "aa")]
    [InlineData(52, "upper-latin", "AZ")]
    [InlineData(944, "lower-roman", "cmxliv")]
    [InlineData(3999, "upper-roman", "MMMCMXCIX")]
    [InlineData(4000, "upper-roman", "4000")]
    [InlineData(0, "lower-alpha", "0")]
    [InlineData(-1, "lower-roman", "-1")]
    [InlineData(-8, "decimal-leading-zero", "-8")]
    [InlineData(-2147483648, "decimal-leading-zero", "-2147483648")]
    [InlineData(25, "lower-greek", "αα")]
    [InlineData(12, "arabic-indic", "١٢")]
    [InlineData(203, "cjk-decimal", "二〇三")]
    [InlineData(1, "hiragana", "あ")]
    [InlineData(0, "disc", "•")]
    [InlineData(5, "unknown-style", "5")]
    public void CounterRepresentationsExcludeMarkerPunctuationAndUseFallbacks(int number, string style, string expected)
        => Assert.Equal(expected, CssCounterStyles.Format(number, style));

    private static StackPanel Box(string classes = "") { var box = new StackPanel(); Css.SetClass(box, classes); return box; }
    private static void Sheet(FrameworkElement host, string text)
    {
        var sheet = CssStyleSheet.Parse(text); Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(host).Add(sheet);
    }
    private static CssGeneratedText Before(Panel host) => Assert.IsType<CssGeneratedText>(CssGeneratedContent.Get(host)?.Before);
    private static CssGeneratedText After(Panel host) => Assert.IsType<CssGeneratedText>(CssGeneratedContent.Get(host)?.After);
    private static void Layout(FrameworkElement root)
    {
        for (var i = 0; i < 5; i++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new Size(360, 400)); root.Arrange(new Rect(0, 0, 360, 400));
        }
    }
}
