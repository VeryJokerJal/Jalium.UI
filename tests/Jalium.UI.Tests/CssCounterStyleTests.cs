using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssCounterStyleTests
{
    [Theory]
    [InlineData("system: cyclic; symbols: A B C;", 1, "A")]
    [InlineData("system: cyclic; symbols: A B C;", 0, "C")]
    [InlineData("system: cyclic; symbols: A B C;", -1, "B")]
    [InlineData("system: cyclic; symbols: A B C;", -2147483648, "A")]
    [InlineData("system: fixed -2; symbols: A B C;", -1, "B")]
    [InlineData("system: fixed; symbols: A B;", 3, "3")]
    [InlineData("symbols: '*' '†';", 3, "**")]
    [InlineData("symbols: '*' '†';", 4, "††")]
    [InlineData("symbols: '*' '†';", 0, "0")]
    [InlineData("system: alphabetic; symbols: A B;", 3, "AA")]
    [InlineData("system: alphabetic; symbols: A B;", 6, "BB")]
    [InlineData("system: alphabetic; symbols: A B;", -1, "-1")]
    [InlineData("system: numeric; symbols: 〇 一;", 5, "一〇一")]
    [InlineData("system: numeric; symbols: 〇 一;", 0, "〇")]
    [InlineData("system: numeric; symbols: 〇 一;", -3, "-一一")]
    [InlineData("system: additive; additive-symbols: 10 X, V 5, 1 I, 0 零;", 16, "XVI")]
    [InlineData("system: additive; additive-symbols: 10 X, V 5, 1 I, 0 零;", 0, "零")]
    [InlineData("system: additive; additive-symbols: 5 V, 3 III; fallback: lower-roman;", 7, "vii")]
    [InlineData("system: extends decimal; negative: '(' ')'; pad: 6 '0';", -1, "(0001)")]
    [InlineData("system: extends decimal; pad: 3 '0';", -5, "-05")]
    [InlineData("system: extends decimal; pad: 'ab' 4;", 1, "ababab1")]
    [InlineData("system: numeric; symbols: '👩‍💻' '⚙️'; pad: 3 '.';", 1, "..⚙️")]
    [InlineData("system: cyclic; symbols: A B; negative: '(' ')';", -1, "A")]
    [InlineData("system: extends decimal; range: 1 2, 4 5; fallback: upper-alpha;", 3, "C")]
    [InlineData("system: extends lower-roman; range: -10 10; negative: '(' ')';", -4, "(iv)")]
    [InlineData("system: extends missing; pad: 3 '0';", 7, "007")]
    [InlineData("system: extends decimal;", -2147483648, "-2147483648")]
    [InlineData("symbols: '*';", 2147483647, "2147483647")]
    [InlineData("system: extends decimal; pad: 2147483647 '0';", 1, "1")]
    [InlineData("symbols: '';", 2147483647, "")]
    public void SystemsAndDescriptorsProduceTheirSpecifiedRepresentations(string descriptors, int value, string expected)
        => Assert.Equal(expected, Map("@counter-style custom { " + descriptors + " }").Format(value, "custom"));

    [Fact]
    public void ExtendsInheritsEveryUnspecifiedDescriptorAndCanResetAutoRange()
    {
        var map = Map("""
            @counter-style base { system: numeric; symbols: '0' '1'; negative: '(' ')'; pad: 6 '0'; prefix: '['; suffix: ']'; range: -4 4; speak-as: words; }
            @counter-style child { system: extends base; suffix: '!'; range: auto; }
            """);
        Assert.Equal("001000", map.Format(8, "child"));
        Assert.Equal("[001000!", map.FormatMarker(8, "child"));
        Assert.Equal("(0010)", map.Format(-2, "child"));
        Assert.Equal("words", map.Definition("child").SpeakAs);
        Assert.Equal("8", map.Format(8, "base"));
    }

    [Fact]
    public void ExtendsCyclesInheritDecimalRatherThanEachOthersDescriptors()
    {
        var map = Map("""
            @counter-style A { system: extends B; negative: '(' ')'; }
            @counter-style B { system: extends A; pad: 3 '0'; }
            @counter-style C { system: extends A; }
            """);
        Assert.Equal("(1)", map.Format(-1, "A")); Assert.Equal("001", map.Format(1, "B"));
        Assert.Equal("(2)", map.Format(-2, "C"));
    }

    [Fact]
    public void FallbackCyclesAreOnlyCutWhenNoParticipatingStyleCanRepresentTheValue()
    {
        var map = Map("""
            @counter-style A { system: cyclic; symbols: A; range: 1 1; fallback: B; }
            @counter-style B { system: cyclic; symbols: B; range: 2 2; fallback: A; }
            """);
        Assert.Equal("A", map.Format(1, "A")); Assert.Equal("B", map.Format(2, "A")); Assert.Equal("3", map.Format(3, "A"));
    }

    [Theory]
    [InlineData("system: numeric; symbols: '0' '1'; symbols: '8' '9' !important;", 5, "101")]
    [InlineData("system: numeric; symbols: '0' '1'; symbols: var(--x);", 5, "101")]
    [InlineData("system: numeric; symbols: '0' '1'; system: unknown;", 5, "101")]
    [InlineData("system: numeric; symbols: '〇' '一'; range: 1 3; range: 9 2;", 2, "一〇")]
    [InlineData("system: numeric; symbols: '〇' '一'; range: 1 3; range: 9 2;", 5, "5")]
    [InlineData("system: extends decimal; pad: 3 '0'; pad: -1 'x';", 5, "005")]
    [InlineData("system: additive; additive-symbols: 5 V, 1 I; additive-symbols: 1 X, 5 Y;", 5, "V")]
    public void InvalidAndImportantDescriptorsDoNotReplaceEarlierValidValues(string descriptors, int value, string expected)
        => Assert.Equal(expected, Map("@counter-style custom { " + descriptors + " }").Format(value, "custom"));

    [Theory]
    [InlineData("decimal", "symbols: A;")]
    [InlineData("NoNe", "symbols: A;")]
    [InlineData("inherit", "symbols: A;")]
    [InlineData("custom", "system: numeric; symbols: A;")]
    [InlineData("custom", "system: additive; symbols: A;")]
    [InlineData("custom", "system: extends decimal; symbols: A;")]
    [InlineData("custom", "system: extends decimal; additive-symbols: 1 A;")]
    public void InvalidDefinitionsAreDroppedWithoutDroppingFollowingStyleRules(string name, string descriptors)
    {
        var sheet = CssStyleSheet.Parse($"@counter-style {name} {{ {descriptors} }} .ok {{ width: 12px; }}");
        Assert.Empty(sheet.CounterStyles); Assert.Single(sheet.Rules); Assert.Contains(sheet.Diagnostics, d => d.Message.Contains("counter-style"));
    }

    [Theory]
    [InlineData("symbols('*' '†')", 3, "**")]
    [InlineData("symbols(cyclic '甲' '乙')", 0, "乙")]
    [InlineData("symbols(numeric '〇' '一')", 5, "一〇一")]
    [InlineData("symbols(alphabetic 'A' 'B')", 3, "AA")]
    [InlineData("symbols(fixed 'a' 'b')", 3, "3")]
    [InlineData("symbols(symbolic '')", 999999, "")]
    public void AnonymousStylesUseTheSameAlgorithms(string value, int number, string expected)
    {
        var reader = new CssTokenReader(value); Assert.True(CssCounterStyles.ReadReference(ref reader, out var reference)); Assert.True(reader.AtEnd);
        Assert.Equal(expected, CssCounterStyleMap.Empty.Format(number, reference));
    }

    [Theory]
    [InlineData("symbols()")]
    [InlineData("symbols(numeric 'one')")]
    [InlineData("symbols(additive 'a' 'b')")]
    [InlineData("symbols(fixed 3 'a')")]
    [InlineData("symbols(cyclic unquoted)")]
    public void AnonymousGrammarRejectsUnsupportedOrInvalidInputs(string value)
        => Assert.False(CssAuthoring.IsValueSupported("content", $"counter(n, {value})"));

    [Fact]
    public void AnonymousImageSymbolsPreserveTheirImageSyntax()
    {
        const string value = "symbols(cyclic url('x'))";
        Assert.True(CssAuthoring.IsValueSupported("content", $"counter(n, {value})"));
        var reader = new CssTokenReader(value);
        Assert.True(CssCounterStyles.ReadReference(ref reader, out var reference));
        Assert.True(reader.AtEnd);
        var definition = Assert.IsType<CssCounterStyleDefinition>(CssCounterStyles.Anonymous(reference[1..]));
        Assert.Equal("url('x')", Assert.Single(definition.ImageSyntaxes!).Value);
    }

    [Fact]
    public void EscapedNamesRemainCaseSensitiveAndCannotCollideWithAnonymousStyles()
    {
        var root = Box("host");
        Add(root, """
            @counter-style Fancy { symbols: A; }
            @counter-style fancy { symbols: B; }
            @counter-style UPPER-ROMAN { symbols: R; }
            @counter-style symbols\(cyclic\ \"C\"\) { symbols: D; }
            .host { counter-reset: n 1; }
            .host::before { content: counter(n, Fancy) counter(n, fancy) counter(n, symbols\(cyclic\ \"C\"\)) counter(n, symbols(cyclic "C")) counter(n, UpPeR-RoMaN); }
            """);
        Layout(root); Assert.Equal("ABDCR", Before(root).Text);
    }

    [Fact]
    public void HierarchicalCountersShareNamedAndAnonymousFormattingAndRefreshDefinitions()
    {
        var root = Box("host"); var child = Box("nested"); root.Children.Add(child);
        Add(root, """
            @counter-style custom { system: alphabetic; symbols: A B; }
            .host { counter-reset: n 1; } .nested { counter-reset: n 2; }
            .host::before, .nested::before { content: counters(n, '.', custom); }
            .nested::after { content: counters(n, '/', symbols(numeric '〇' '一')); }
            """);
        Layout(root);
        Assert.Equal("A", Before(root).Text); Assert.Equal("A.B", Before(child).Text);
        Assert.Equal("一/一〇", CssGeneratedContent.Get(child)!.After!.Text);
        Add(root, "@counter-style custom { system: numeric; symbols: '0' '1'; }"); Layout(root);
        Assert.Equal("1.10", Before(child).Text);
    }

    [Fact]
    public void LayersReplaceTheWholeDefinitionAndDefinitionsAreDocumentWide()
    {
        var root = Box("host"); var child = Box(); root.Children.Add(child);
        Add(root, """
            @layer one, two;
            @layer two { @counter-style custom { system: extends decimal; pad: 3 '0'; } }
            @layer one { @counter-style custom { symbols: A; } }
            .host { counter-reset: n 2; } .host::before { content: counter(n, custom); }
            """);
        Layout(root); Assert.Equal("002", Before(root).Text);
        var higher = Add(child, "@counter-style custom { symbols: B; }");
        Layout(root); Assert.Equal("BB", Before(root).Text);
        Css.GetStyleSheets(child).Remove(higher); Layout(root); Assert.Equal("002", Before(root).Text);
        var independent = Box("host"); Add(independent, ".host { counter-reset: n 2; } .host::before { content: counter(n, custom); }");
        Layout(independent); Assert.Equal("2", Before(independent).Text);
    }

    [Fact]
    public void RemovingAndReattachingDefinitionHostsRefreshesTheDocumentInventory()
    {
        var root = Box("host"); var holder = Box(); root.Children.Add(holder);
        Add(holder, "@counter-style custom { symbols: '*'; }");
        Add(root, ".host { counter-reset: n 3; } .host::before { content: counter(n, custom); }");
        Layout(root); Assert.Equal("***", Before(root).Text);
        root.Children.Remove(holder); Layout(root); Assert.Equal("3", Before(root).Text);
        root.Children.Add(holder); Layout(root); Assert.Equal("***", Before(root).Text);
    }

    [Fact]
    public void MediaConditionsRefreshWhenViewportOrPreferencesChange()
    {
        var root = Box("host"); var original = SystemParameters.PrefersDarkColorScheme;
        Add(root, """
            @counter-style custom { symbols: A; }
            @media (min-width: 500px) { @counter-style custom { symbols: B; } }
            @media (prefers-color-scheme: dark) { @counter-style custom { symbols: C; } }
            .host { counter-reset: n 1; } .host::before { content: counter(n, custom); }
            """);
        try
        {
            SystemParameters.RecordPreferredColorScheme(2); Layout(root, 360); Assert.Equal("A", Before(root).Text);
            Layout(root, 720); Assert.Equal("B", Before(root).Text);
            SystemParameters.RecordPreferredColorScheme(1); Layout(root, 720); Assert.Equal("C", Before(root).Text);
            Css.SetStyle(root, "opacity: .9;"); Layout(root, 720);
            SystemParameters.RecordPreferredColorScheme(2); Layout(root, 720); Assert.Equal("B", Before(root).Text);
        }
        finally { SystemParameters.RecordPreferredColorScheme(original ? 1u : 2u); }
    }

    [Fact]
    public async Task ImportsRetainDefinitionOrderLayersAndConditions()
    {
        var resolver = new Resolver(new Dictionary<string, string>
        {
            ["main.css"] = "@import 'base.css' layer(base); @counter-style custom { symbols: C; } .host::before { content: counter(n, custom); } .host { counter-reset: n 2; }",
            ["base.css"] = "@counter-style custom { symbols: A; } @media (min-width: 600px) { @counter-style custom { symbols: B; } }",
        });
        var sheet = await CssStyleSheet.LoadAsync(new Uri("https://css.test/main.css"), resolver);
        Assert.Empty(sheet.Diagnostics); Assert.Equal(3, sheet.CounterStyles.Length);
        var root = Box("host"); Css.GetStyleSheets(root).Add(sheet); Layout(root, 720); Assert.Equal("CC", Before(root).Text);
    }

    [Fact]
    public void CompiledBuilderRetainsDescriptorsAndCounterStylesAreReportedAsSupported()
    {
        var builder = new CssStyleSheetBuilder(1);
        var declaration = builder.Declaration("symbols", "'甲' '乙'", false);
        builder.CounterStyle("custom", [declaration], 0, -1, null);
        var sheet = builder.Build(); Assert.Single(sheet.CounterStyles);
        Assert.Equal("甲甲", new CssCounterStyleMap(sheet.CounterStyles.ToDictionary(r => r.Name)).Format(3, "custom"));
        Assert.True(CssParser.SupportsAtRule("counter-style"));
    }

    [Fact]
    public void DynamicAttributesVariablesAndAlternativeContentUseTheSelectedStyle()
    {
        var root = Box("host"); root.Tag = "甲";
        Add(root, """
            @counter-style custom { system: extends decimal; pad: 3 '0'; }
            .host { counter-reset: n 2; --zero: '〇'; --one: '一'; }
            .host::before { content: counter(n, custom) attr(Tag) counter(n, symbols(numeric var(--zero) var(--one))) / '编号' counter(n, custom); }
            """);
        Layout(root); Assert.Equal("002甲一〇", Before(root).Text); Assert.Equal("编号002", Before(root).Alternative);
        root.Tag = "乙"; Layout(root); Assert.Equal("002乙一〇", Before(root).Text);
        Assert.False(CssEvaluationScheduler.HasPending(Dispatcher.CurrentDispatcher));
    }

    private static CssCounterStyleMap Map(string css)
    {
        var sheet = CssStyleSheet.Parse(css); Assert.Empty(sheet.Diagnostics);
        return new(sheet.CounterStyles.ToDictionary(r => r.Name, StringComparer.Ordinal));
    }
    private static StackPanel Box(string classes = "") { var root = new StackPanel(); Css.SetClass(root, classes); return root; }
    private static CssStyleSheet Add(FrameworkElement host, string css)
    {
        var sheet = CssStyleSheet.Parse(css); Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(host).Add(sheet); return sheet;
    }
    private static CssGeneratedText Before(Panel host) => Assert.IsType<CssGeneratedText>(CssGeneratedContent.Get(host)?.Before);
    private static void Layout(FrameworkElement root, int width = 360)
    {
        var viewport = new Size(width, 400);
        Css.SetViewportMetrics(root, new CssViewportMetrics(viewport, viewport, viewport));
        for (var i = 0; i < 5; i++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new Size(width, 400)); root.Arrange(new Rect(0, 0, width, 400));
        }
    }
    private sealed class Resolver(Dictionary<string, string> sources) : ICssResourceResolver
    {
        public ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new CssResource(uri, new MemoryStream(Encoding.UTF8.GetBytes(sources[uri.Segments[^1]]))));
    }
}
